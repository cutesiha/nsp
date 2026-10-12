using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 괴물 하나를 3D 로 보여 준다. **판정은 전혀 하지 않는다** —
// MonsterThreatSystem 이 정한 단계와 거리(Progress)를 읽어 세우고 돌리고 재생할 뿐이다.
//
// 모델마다 가진 것이 다르다(MonsterModelProbe 로 실측):
//   결번자(MonsterPSX)  뼈 44 · 클립 6개 — 걷기 · 뛰기 · 문 공격 · 비명까지 다 있다
//   아기(CreepyDoll)    뼈 17 · 클립 2개 — Idle 과 Walk 뿐. 나머지는 코드가 만든다
//   거미(Huntsman)      뼈 없음 · 클립 없음 — 전부 코드가 만든다
// 그래서 "없으면 코드가 대신한다" 를 기본값으로 깔고, 있는 클립만 골라 쓴다.
// 클립 이름은 **데이터(MonsterDef)에서 온다** — 여기에 박아 두지 않는다.
public sealed class MonsterActor
{
    private readonly Node3D _root = new() { Name = "Monster", Visible = false };
    private Node3D _model;
    private AnimationPlayer _anim;
    private MonsterDef _def;
    private string _playing = "";
    private float _bob;

    // 클립이 없을 때 몸을 흔들어 '움직이는 중' 을 만드는 보조 노드.
    private Node3D _sway;

    public Node3D Root => _root;
    public string MonsterId => _def?.MonsterId ?? "";

    public void Attach(Node parent) => parent.AddChild(_root);

    // ── 모델 ────────────────────────────────────────────────────────

    // 한 번 읽은 모델은 **버리지 않고 숨겨 둔다.**
    //
    // 전에는 괴물이 바뀔 때마다 QueueFree 하고 다음에 다시 읽었다. 그 "다시 읽기"가
    // 하필 점프스케어가 터지는 순간에 걸리면 한 프레임이 통째로 멈춘다 —
    // 실측 88.8 ms(PERFORMANCE_BASELINE.md D). 모델 노드는 메시·텍스처를 **공유**하므로
    // (PackedScene 인스턴스는 리소스를 복제하지 않는다) 숨겨 두는 비용은 노드 몇 개뿐이고
    // VRAM 은 늘지 않는다.
    private readonly System.Collections.Generic.Dictionary<string, Node3D> _loaded = new();

    // 필요한 모델로 바꿔 끼운다. 같은 괴물이면 아무 일도 하지 않는다.
    public bool Use(MonsterDef def)
    {
        using var _perf = NSP.Debug.PerfStats.Scope(NSP.Debug.PerfStats.Span.MonsterLoad);
        if (def == null) return false;
        if (_def?.MonsterId == def.MonsterId) return _model != null;

        // 쓰던 것은 재워 둔다(지우지 않는다).
        if (_sway != null) Sleep(_sway);
        _sway = null;
        _anim = null;
        _playing = "";
        _def = def;

        // 전에 읽어 둔 것이 있으면 그것을 다시 쓴다 — 여기가 0 ms 가 되는 자리다.
        if (_loaded.TryGetValue(def.MonsterId, out var cached) && IsValid(cached))
        {
            _sway = cached;
            Wake(_sway);
            _model = _sway.GetChildCount() > 0 ? _sway.GetChild<Node3D>(0) : null;
            _anim = _model == null ? null : FindAnim(_model);
            return _model != null;
        }

        _model = Build(def, out _sway);
        if (_model == null) { _sway = null; return false; }
        _loaded[def.MonsterId] = _sway;
        return true;
    }

    // 모델을 **미리** 읽어 둔다(연출 전에 부른다). 지금 쓰는 모델은 바뀌지 않는다.
    //
    // 점프스케어 직전에 처음 읽지 않기 위한 입구다. 이미 읽어 둔 것이면 아무 일도 없다.
    // 이 배우가 아직 살아 있는가(씬이 바뀌며 노드가 해제되지 않았는가).
    public bool IsAlive => GodotObject.IsInstanceValid(_root);

    public bool Preload(MonsterDef def)
    {
        if (def == null || string.IsNullOrEmpty(def.MonsterId) || !IsAlive) return false;
        if (_loaded.TryGetValue(def.MonsterId, out var had) && IsValid(had)) return true;

        var model = Build(def, out var sway);
        if (model == null) return false;
        Sleep(sway);                          // 준비만 해 두고 화면에도 처리에도 올리지 않는다
        _loaded[def.MonsterId] = sway;
        return true;
    }

    // 쓰지 않는 모델을 **완전히 재운다.**
    //
    // Visible=false 만으로는 모자란다. 그리지는 않지만 AnimationPlayer 는 계속 돌고
    // 그때마다 스켈레톤이 갱신된다 — 미리 읽어 둔 모델이 열다섯이면 그 비용이 그대로
    // 상시 부담이 된다(실제로 준비를 넣자마자 평상시 FPS 가 25 → 18 로 떨어졌다).
    // ProcessMode.Disabled 가 그 처리를 통째로 끊는다.
    private static void Sleep(Node3D sway)
    {
        if (!IsValid(sway)) return;
        sway.Visible = false;
        sway.ProcessMode = Node.ProcessModeEnum.Disabled;
    }

    private static void Wake(Node3D sway)
    {
        if (!IsValid(sway)) return;
        sway.ProcessMode = Node.ProcessModeEnum.Inherit;
        sway.Visible = true;
    }

    public bool IsLoaded(MonsterDef def) =>
        def != null && _loaded.TryGetValue(def.MonsterId, out var n) && IsValid(n);

    private static bool IsValid(Node n) => n != null && GodotObject.IsInstanceValid(n);

    // 실제로 읽고 세우는 부분. Use 와 Preload 가 같은 길을 쓴다.
    private Node3D Build(MonsterDef def, out Node3D sway)
    {
        sway = new Node3D { Name = "Sway_" + def.MonsterId };
        _root.AddChild(sway);

        if (string.IsNullOrEmpty(def.ModelPath) || !ResourceLoader.Exists(def.ModelPath))
        {
            GD.PushWarning($"MonsterActor: 모델을 찾지 못했습니다 — {def.ModelPath}. 임시 형상으로 대신합니다.");
            var ph = Placeholder(def);
            sway.AddChild(ph);
            return ph;
        }

        var ps = GD.Load<PackedScene>(def.ModelPath);
        var inst = ps?.Instantiate<Node3D>();
        if (inst == null) { sway.QueueFree(); sway = null; return null; }
        sway.AddChild(inst);

        HideRigWidgets(inst);
        var anim = FindAnim(inst);
        // 임포트된 클립은 전부 LoopMode.None 으로 들어온다. 걷기 · 대기처럼 이어져야 하는
        // 것만 루프로 바꿔 둔다(한 번만 재생되고 멈추면 얼어붙은 것처럼 보인다).
        // 클립 리소스는 공유되므로 한 번만 고치면 된다.
        LoopClipOn(anim, def.AnimIdle);
        LoopClipOn(anim, def.AnimWalk);
        LoopClipOn(anim, def.AnimRun);
        if (_def?.MonsterId == def.MonsterId) _anim = anim;

        FitToHeight(inst, def);
        return inst;
    }

    // Rigify 가 같이 내보내는 조작용 위젯 메시(WGT_*). 화면에 나올 것이 아니다.
    private static void HideRigWidgets(Node n)
    {
        if (n is MeshInstance3D mi && mi.Name.ToString().StartsWith("WGT_")) mi.Visible = false;
        foreach (var c in n.GetChildren()) HideRigWidgets(c);
    }

    private static AnimationPlayer FindAnim(Node n)
    {
        if (n is AnimationPlayer ap) return ap;
        foreach (var c in n.GetChildren())
            if (FindAnim(c) is { } found) return found;
        return null;
    }

    private static void LoopClipOn(AnimationPlayer anim, string clip)
    {
        if (anim == null || string.IsNullOrEmpty(clip) || !anim.HasAnimation(clip)) return;
        var a = anim.GetAnimation(clip);
        if (a != null) a.LoopMode = Animation.LoopModeEnum.Linear;
    }

    // 실제로 그려지는 크기를 재서 원하는 키에 맞춘다.
    // FBX 마다 단위가 달라 배율을 적어 두면 모델을 바꿀 때마다 다시 재야 한다 —
    // 재서 맞추면 교체해도 그대로 선다.
    private static void FitToHeight(Node3D inst, MonsterDef def)
    {
        if (def.TargetHeight <= 0f) return;
        var box = VisualBounds(inst, inst);
        if (box.Size.Y <= 0.0001f) return;
        float k = def.TargetHeight / box.Size.Y;
        inst.Scale = Vector3.One * k;
        // 발이 바닥(y=0)에 닿게 내린다.
        inst.Position = new Vector3(0f, -box.Position.Y * k + def.FloorOffset, 0f);
        inst.RotationDegrees = new Vector3(0f, def.YawOffsetDegrees, 0f);
    }

    private static Aabb VisualBounds(Node3D root, Node n, bool first = true)
    {
        var box = new Aabb();
        bool got = false;
        void Walk(Node x)
        {
            if (x is MeshInstance3D mi && mi.Visible && mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0)
            {
                var a = (root.GlobalTransform.AffineInverse() * mi.GlobalTransform) * mi.GetAabb();
                box = got ? box.Merge(a) : a;
                got = true;
            }
            foreach (var c in x.GetChildren()) Walk(c);
        }
        Walk(n);
        return box;
    }

    // 모델이 없을 때 쓰는 임시 형상 — 크기와 움직임만 맞는 검은 덩어리.
    private static Node3D Placeholder(MonsterDef def)
    {
        var host = new Node3D { Name = "Placeholder" };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.06f), Roughness = 0.95f,
        };
        float h = Mathf.Max(0.4f, def.TargetHeight);
        host.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = h * 0.17f, Height = h },
            Position = new Vector3(0f, h * 0.5f, 0f),
            MaterialOverride = mat,
        });
        return host;
    }

    // ── 매 프레임 ───────────────────────────────────────────────────

    // 화면에 낼 때.
    //
    // **_root 의 ProcessMode 는 건드리지 않는다.** 한때 여기서 재웠다가, 바깥에서
    // Root.Visible 을 직접 켜는 호출부(HorrorDirector 의 얼굴 점프스케어)가 있다는 것을
    // 발견하고 되돌렸다 — 그 경로로는 깨우지 못해 애니메이션이 멈춘 채 떴을 것이다.
    // 재우는 것은 **쓰지 않는 모델(sway)** 쪽만으로 충분하다.
    private void ShowRoot() => _root.Visible = true;

    public void Hide()
    {
        _root.Visible = false;
        if (_anim != null && _anim.IsPlaying()) _anim.Stop();
        _playing = "";
    }

    // 복도 안의 위치와 자세를 상태에 맞춘다.
    //   vis       그 괴물이 지금 들어와 있는 복도
    //   faceYaw   중앙제어실 쪽을 보는 각도
    // 복도 CCTV 영상에서만 쓰는 확대 배율. 침입(사망 연출)은 이 값을 쓰지 않는다.
    // 다만 복도 천장을 뚫으면 머리가 잘려 무엇인지 더 안 보인다 — 천장 아래로 제한한다.
    // 그래서 작은 개체(애기 · 거미)가 많이 커지고, 큰 개체는 조금만 커진다.
    private const float CctvScale = 2.1f;
    private const float CctvHeadroom = 2.25f;

    public void Place(MonsterThreat t, CorridorVisual vis, float delta)
    {
        if (_def == null || vis == null) { Hide(); return; }
        ShowRoot();

        float z = vis.ApproachZ(t.Progress);
        // 거미는 벽에 붙어 온다(§6) — 사람형은 복도 가운데로.
        float x = _def.MonsterId == "spider" ? vis.WallHugX : 0f;
        // 문 앞에 붙는 순간에는 관찰 슬릿 정면으로 모인다. 벽에 붙은 채로 멈춰 서면
        // 닫힌 문짝 뒤에 완전히 가려 CCTV 로 아무것도 확인할 수 없게 된다(§4).
        if (t.Phase is ThreatPhase.AtDoor or ThreatPhase.Pounding or ThreatPhase.Breach) x = 0f;
        else if (t.Phase == ThreatPhase.Approach) x *= 1f - Mathf.SmoothStep(0.78f, 1f, t.Progress);
        _root.Position = vis.Root.Position + new Vector3(x, 0f, z);
        // CCTV 화면은 모니터 안의 작은 사각형이다. 실제 키 그대로 세우면 복도 저편에서
        // 점처럼 보여 "뭔가 있다" 조차 읽히지 않는다 — 영상에서만 크게 키운다.
        // 발이 원점이라 위로 자라므로 바닥에 붙은 채 커진다.
        _root.Scale = Vector3.One * Mathf.Min(CctvScale,
            CctvHeadroom / Mathf.Max(0.2f, _def.TargetHeight));

        // 언제나 중앙제어실 쪽(= 카메라 쪽)을 본다.
        _root.RotationDegrees = new Vector3(0f, vis.FacingControlYawDegrees, 0f);

        PlayFor(t, delta);
    }

    private void PlayFor(MonsterThreat t, float delta)
    {
        bool moving = t.Phase == ThreatPhase.Approach && !t.Paused
                      || t.Phase == ThreatPhase.Retreat;
        string clip = t.Phase switch
        {
            ThreatPhase.Pounding => Pick(_def.AnimAttackDoor, _def.AnimRun, _def.AnimWalk),
            ThreatPhase.Breach => Pick(_def.AnimJumpscare, _def.AnimRun, _def.AnimWalk),
            ThreatPhase.Retreat => Pick(_def.AnimRetreat, _def.AnimWalk),
            _ => moving ? Pick(_def.AnimWalk, _def.AnimIdle) : Pick(_def.AnimIdle, _def.AnimWalk),
        };
        Play(clip, t.Phase is ThreatPhase.Pounding or ThreatPhase.Breach ? 1.25f : 1f);

        // 클립이 없거나 모자란 몫은 코드가 메운다.
        //   걷는 중   — 좌우로 아주 조금 흔들리고 위아래로 들썩인다
        //   두드리는 중 — 문 쪽으로 짧게 들이받는다
        if (_sway == null) return;
        _bob += delta;
        bool hasClip = _anim != null && !string.IsNullOrEmpty(clip);
        float sway = hasClip ? 0.012f : 0.05f;
        float lift = hasClip ? 0.004f : 0.035f;

        float ram = 0f;
        if (t.Phase == ThreatPhase.Pounding)
            ram = Mathf.Max(0f, Mathf.Sin(_bob * 7.5f)) * (string.IsNullOrEmpty(_def.AnimAttackDoor) ? 0.22f : 0.06f);

        _sway.Position = new Vector3(
            Mathf.Sin(_bob * (moving ? 6.5f : 1.3f)) * sway,
            Mathf.Abs(Mathf.Sin(_bob * (moving ? 6.5f : 1.1f))) * lift,
            -ram);
        _sway.RotationDegrees = new Vector3(0f, 0f, Mathf.Sin(_bob * (moving ? 3.2f : 0.8f)) * (hasClip ? 0.6f : 3.2f));
    }

    private string Pick(params string[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && _anim != null && _anim.HasAnimation(c)) ?? "";

    private void Play(string clip, float speed, double blend = 0.25)
    {
        if (_anim == null || string.IsNullOrEmpty(clip)) return;
        if (_playing == clip && _anim.IsPlaying()) { _anim.SpeedScale = speed; return; }
        _playing = clip;
        _anim.SpeedScale = speed;
        _anim.Play(clip, blend);
    }

    // 점프스케어 — 카메라 앞으로 돌진하는 연출에서 쓰는 직접 제어.
    public void SetPose(Vector3 position, float yawDegrees, float scale = 1f)
    {
        ShowRoot();
        _root.Position = position;
        _root.RotationDegrees = new Vector3(0f, yawDegrees, 0f);
        _root.Scale = Vector3.One * scale;
    }

    // 돌진 연출 전용 — **월드 좌표로** 세우고 viewer 쪽을 똑바로 보게 한다.
    //
    // 각도를 직접 계산하지 않는다. 이 배우는 중앙제어실 노드 밑에 붙는데 그 노드가
    // 어떻게 놓여 있느냐에 따라 "로컬 yaw 몇 도" 의 답이 달라져, 같은 식으로도 앞뒤가
    // 뒤집힌다(실제로 뒷모습으로 오는 버그가 여기서 났다). 부모가 무엇이든 결과가
    // 같도록 LookAt 으로 맞춘다.
    //   괴물 모델 셋 다 **로컬 +Z 가 앞**이다(DeathShot 의 facing 모드로 확인).
    //   LookAt 은 -Z 를 목표로 돌리므로 **반대편**을 보게 해야 얼굴이 이쪽을 본다.
    public void FaceViewer(Vector3 globalPosition, Vector3 viewer, float scale = 1f,
                           float yawWobbleDegrees = 0f)
    {
        ShowRoot();

        Vector3 toViewer = viewer - globalPosition;
        toViewer.Y = 0f;                  // 고개를 숙이거나 들지 않는다 — 수평으로만 돈다
        _root.GlobalPosition = globalPosition;
        if (toViewer.LengthSquared() > 0.0001f)
        {
            // +Z 가 보는 사람 쪽을 향하는 각도. 축을 직접 맞추므로 LookAt 의
            // 기저 재계산을 거치지 않는다(거기서 배율이 흐트러진 적이 있다).
            float yaw = Mathf.RadToDeg(Mathf.Atan2(toViewer.X, toViewer.Z));
            _root.GlobalRotationDegrees = new Vector3(0f, yaw + yawWobbleDegrees, 0f);
        }
        _root.Scale = Vector3.One * scale;
    }

    // blend 를 0 으로 주면 **그 자리에서 바로** 갈아탄다. 0.06초짜리 들이밀기에
    // 기본 0.25초 블렌드를 쓰면 바뀐 자세가 화면에 닿기 전에 연출이 끝난다.
    public void PlayRole(string clip, float speed = 1f, double blend = 0.25) => Play(clip, speed, blend);

    // 조사용 — 지금 트는 클립의 특정 시점으로 건너뛴다. 클립 안에서 몸이 도는지
    // (뒷모습으로 서 있는 구간이 있는지) 눈으로 확인할 때 쓴다.
    public void SeekRole(double seconds) => _anim?.Seek(seconds, true);

    public MonsterDef Def => _def;
}
