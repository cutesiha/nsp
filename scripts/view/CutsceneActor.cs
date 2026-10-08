using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 컷씬 무대 위의 직원 한 명.
//
// 공용 클립 라이브러리(employee_common.tres)에는 **달리기가 없다.** 걷기를 빠르게 돌리면
// "빨리 걷는 사람"이 되고, 다리는 가만히 둔 채 몸만 밀면 미끄러진다. 그래서 걷기 · 달리기는
// 클립을 쓰지 않고 뼈대를 직접 돌린다. 서 있기 · 장비 조작은 기존 클립을 그대로 쓴다.
//
// ── 이 리그의 관절 규약(EmployeeCctvBase.tscn 에서 실측) ──────────────────
//   · 팔다리는 쉴 때 -Y 로 늘어져 있고, 모든 관절의 기본 회전은 0 이다.
//   · +X 회전 → 끝이 -Z(캐릭터 앞쪽)로 간다  = 팔다리를 **앞으로** 든다.
//   · +Z 회전 → 끝이 +X(캐릭터 오른쪽)로 간다 = 오른팔은 바깥, 왼팔은 **몸 안쪽**.
//        그래서 바깥으로 벌리려면 L 은 음수, R 은 양수다. 반대로 쓰면 팔이 몸통을 뚫는다.
//   · 팔꿈치는 **+X** 가 정상 굽힘(손이 앞으로 올라온다). 음수면 반대로 꺾인다.
//   · 무릎은 **-X** 가 정상 굽힘(발뒤꿈치가 뒤로 올라온다). 팔꿈치와 반대 방향이다.
public partial class CutsceneActor : Node3D
{
    public enum Gait { Clip, Walk, Run }

    private const string Rig = "VisualRoot/RigRoot";
    private const string Hips = Rig + "/Hips";
    private const string Chest = Hips + "/Torso/Chest";

    private static readonly Dictionary<string, string> Scenes = new()
    {
        ["fox"] = "res://scenes/cctv_characters/employees/FoxEmployee3D.tscn",
        ["dog"] = "res://scenes/cctv_characters/employees/DogEmployee3D.tscn",
        ["cat"] = "res://scenes/cctv_characters/employees/CatEmployee3D.tscn",
        ["sheep"] = "res://scenes/cctv_characters/employees/SheepEmployee3D.tscn",
        ["rabbit"] = "res://scenes/cctv_characters/employees/RabbitEmployee3D.tscn",
        ["wolf"] = "res://scenes/cctv_characters/employees/WolfEmployee3D.tscn",
    };

    // 성격 차이는 아주 조금만 준다 — 뼈대와 기본 러닝은 여섯 명이 완전히 같아야 한다(§9 · §10).
    //   Cadence : 보폭 배율(작을수록 종종걸음)  Arm : 팔 흔듦  Lean : 상체 기울기 가감  Panic : 흐트러짐
    private readonly record struct Trait(float Cadence, float Arm, float Lean, float Panic);

    private static readonly Dictionary<string, Trait> Traits = new()
    {
        ["rabbit"] = new(0.88f, 1.06f, 2.0f, 0.85f),   // 가장 다급 — 종종걸음에 상체가 더 숙여진다
        ["sheep"] = new(0.92f, 1.00f, 1.0f, 1.00f),    // 패닉 — 어깨가 움츠러들고 보폭이 고르지 않다
        ["cat"] = new(0.90f, 0.94f, 0.5f, 0.45f),      // 날렵하고 짧은 스텝
        ["dog"] = new(1.00f, 1.08f, 0.5f, 0.40f),      // 적극적이고 자연스럽다
        ["wolf"] = new(1.08f, 0.88f, -0.5f, 0.15f),    // 가장 안정적 — 팔을 절제한다
        ["fox"] = new(1.05f, 0.84f, -1.0f, 0.10f),     // 가장 침착하다
    };

    // ── 달리기 변형 ──────────────────────────────────────────────────────
    //
    // 같은 러닝을 여러 명에게 복붙하면 **군무**가 된다. 급히 도망치는 장면에서 그것만큼
    // 거짓말처럼 보이는 것이 없다. 그래서 성격(Trait) 위에 한 겹 더 얹는다.
    //
    //   Cadence   보폭(다리를 흔드는 각) — 작을수록 종종걸음
    //   Arm       팔 흔듦의 크기
    //   ArmBias   좌우 팔의 비대칭 — 한쪽을 더 크게 휘두른다
    //   Lean      상체를 숙이는 각
    //   Bounce    상하 움직임
    //   Irregular 속도의 흔들림. 위상이 **이동 거리**에서 나오므로(§8), 속도가 흔들리면
    //             발이 미끄러지지 않으면서 걸음 리듬만 불규칙해진다. 이게 핵심이다.
    //   Sway      달리는 동안 좌우로 흔들리는 머리 · 어깨
    public enum RunStyle { Normal, HeadDown, BigArms, Panicked, Tired, Loping }

    private readonly record struct Style(float Cadence, float Arm, float ArmBias, float Lean,
        float Bounce, float Irregular, float Sway);

    private static readonly Dictionary<RunStyle, Style> Styles = new()
    {
        [RunStyle.Normal] = new(1.00f, 1.00f, 0.00f, 0f, 1.00f, 0.04f, 0.3f),
        [RunStyle.HeadDown] = new(1.15f, 0.78f, 0.05f, 14f, 0.78f, 0.03f, 0.2f),   // 상체를 숙이고 전력질주
        [RunStyle.BigArms] = new(1.06f, 1.48f, 0.10f, -3f, 1.18f, 0.05f, 0.5f),    // 팔을 크게 휘두르며 직선으로
        [RunStyle.Panicked] = new(0.82f, 1.22f, 0.28f, 7f, 1.32f, 0.22f, 1.0f),    // 보폭이 고르지 않다
        [RunStyle.Tired] = new(0.89f, 0.70f, 0.15f, 10f, 0.68f, 0.13f, 0.7f),      // 조금 느리고 처진다
        [RunStyle.Loping] = new(1.24f, 0.95f, 0.06f, 2f, 1.26f, 0.06f, 0.4f),      // 성큼성큼 보폭이 크다
    };

    private Style _style = new(1f, 1f, 0f, 0f, 1f, 0.04f, 0.3f);
    private float _styleT;

    // 러닝 변형 + **걸음 위상**을 정한다. phase01 을 서로 다르게 주면 같은 프레임에
    // 같은 발이 나가지 않는다(§ "모두 같은 cycle offset 금지").
    public void SetRunStyle(RunStyle style, float phase01 = 0f)
    {
        _style = Styles.GetValueOrDefault(style, Styles[RunStyle.Normal]);
        _phase = Mathf.PosMod(phase01, 1f);
        _styleT = phase01 * 7.3f;        // 속도 흔들림의 위상도 어긋나게 둔다
        // 보폭 상수는 **자세에서 재서** 얻는다. cadence 를 건드렸으면 반드시 다시 재야
        // 발이 바닥을 미끄러지지 않는다(§8).
        if (_bonesReady) CalibrateStride();
    }

    private Node3D _actor, _hips, _torso, _chest, _head, _neck;
    private Node3D _shoulderL, _shoulderR;
    private Node3D _upLegL, _upLegR, _loLegL, _loLegR, _footL, _footR;
    private Node3D _upArmL, _upArmR, _loArmL, _loArmR;
    private AnimationPlayer _player;

    private Gait _gait = Gait.Clip;
    private Trait _trait = new(1f, 1f, 0f, 0.4f);
    private float _phase;            // 0~1 = 두 걸음
    private float _hipsRestY;
    private bool _bonesReady;

    // 이동.
    private Vector3 _target;
    private float _speedWant;        // 지시받은 속도
    private float _speedNow;         // 실제 속도 — 출발 · 정지에서 이쪽이 따라간다
    private bool _moving;
    private float _yawNow, _yawWant;
    private float _turnLean;         // 방향을 틀 때 몸이 기우는 정도
    private float _settle;           // 멈춘 직후 중심을 회복하는 동안 0~1
    private bool _restIdle;          // 회복이 끝나면 기본 자세로 넘길지

    public bool Moving => _moving;
    public Node3D Actor => _actor;

    // 더 놀란 직원(0~1). 팔 휘두름 · 상체 흔들림이 커지고 자세가 흐트러진다(§3-3 패닉 러닝).
    public float Panic;

    // 뒤로 물러나는 움직임(§3-3 (3)). 다리 위상이 뒤집히고 몸은 앞을 본 채로 물러난다.
    public bool Backpedal;

    public float Stumble;            // 균형을 잃고 휘청인다
    private float _lookOver;         // 뒤를 흘끗 보는 각도

    // ── 보폭 ────────────────────────────────────────────────────────────
    // 발이 미끄러지지 않으려면 "한 주기에 몸이 나아가는 거리" 와 "자세가 실제로 만들어
    // 내는 두 걸음 거리" 가 같아야 한다(§8). 그 거리를 손으로 계산하면 무릎 굽힘 ·
    // 몸체별 다리 길이 때문에 반드시 어긋나므로, **생성 직후 리그를 한 바퀴 돌려 직접
    // 잰다.** 자세 공식을 고쳐도 보폭이 저절로 따라오므로 다시 틀어지지 않는다.
    private float _strideRun, _strideWalk;

    // 성격별 cadence 는 보폭 상수가 아니라 **다리를 흔드는 각도**에 건다. 상수만 바꾸면
    // 자세는 그대로인데 거리만 달라져서 그 차이가 고스란히 미끄러짐으로 나온다.
    private float SwingDeg => (_gait == Gait.Run ? 44f : 25f) * _trait.Cadence * _style.Cadence;

    private float Stride
    {
        get
        {
            float v = _gait == Gait.Run ? _strideRun : _strideWalk;
            return v > 0.05f ? v : 1.6f;
        }
    }

    // 걸음 주기를 한 바퀴 돌려 두 발이 가장 벌어지는 간격을 잰다 = 한 걸음.
    // 한 주기는 두 걸음이므로 주기 거리는 그 두 배다.
    private void CalibrateStride()
    {
        if (_footL == null || _footR == null) return;
        var keepGait = _gait;
        float keepPhase = _phase, keepSettle = _settle;
        bool keepMoving = _moving;
        _moving = false;
        _settle = 1f;

        foreach (var g in new[] { Gait.Walk, Gait.Run })
        {
            _gait = g;
            float max = 0f;
            for (int i = 0; i < 48; i++)
            {
                _phase = i / 48f;
                ApplyGait();
                Vector3 a = _footL.GlobalPosition, b = _footR.GlobalPosition;
                max = Mathf.Max(max, new Vector2(a.X - b.X, a.Z - b.Z).Length());
            }
            if (g == Gait.Run) _strideRun = max * 2f;
            else _strideWalk = max * 2f;
        }

        _gait = keepGait;
        _phase = keepPhase;
        _moving = keepMoving;
        _settle = keepSettle;
    }

    public void Spawn(string employeeId, Vector3 pos, float yawDeg = 0f)
    {
        string id = employeeId ?? "";
        string path = Scenes.GetValueOrDefault(id, "");
        if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path))
            foreach (var p in Scenes.Values) { if (ResourceLoader.Exists(p)) { path = p; break; } }
        if (string.IsNullOrEmpty(path)) return;
        if (Traits.TryGetValue(id, out var t)) _trait = t;

        _actor = GD.Load<PackedScene>(path)?.Instantiate<Node3D>();
        if (_actor == null) return;
        AddChild(_actor);
        Position = pos;
        _yawNow = _yawWant = yawDeg;
        RotationDegrees = new Vector3(0f, yawDeg, 0f);

        _player = _actor.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        _hips = _actor.GetNodeOrNull<Node3D>(Hips);
        _torso = _actor.GetNodeOrNull<Node3D>(Hips + "/Torso");
        _chest = _actor.GetNodeOrNull<Node3D>(Chest);
        _neck = _actor.GetNodeOrNull<Node3D>(Chest + "/Neck");
        _head = _actor.GetNodeOrNull<Node3D>(Chest + "/Neck/Head");
        _shoulderL = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderL");
        _shoulderR = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderR");
        _upLegL = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegL");
        _upLegR = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegR");
        _loLegL = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegL/LowerLegL");
        _loLegR = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegR/LowerLegR");
        _footL = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegL/LowerLegL/FootL");
        _footR = _actor.GetNodeOrNull<Node3D>(Hips + "/UpperLegR/LowerLegR/FootR");
        _upArmL = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderL/UpperArmL");
        _upArmR = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderR/UpperArmR");
        _loArmL = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderL/UpperArmL/LowerArmL");
        _loArmR = _actor.GetNodeOrNull<Node3D>(Chest + "/ShoulderR/UpperArmR/LowerArmR");
        _hipsRestY = _hips?.Position.Y ?? 0.86f;
        _bonesReady = _hips != null && _upLegL != null && _upArmL != null;
        if (_bonesReady) CalibrateStride();
        SetProcess(true);
    }

    public void PlayClip(string name, float speed = 1f, double from = 0)
    {
        _gait = Gait.Clip;
        _moving = false;
        if (_player == null || !_player.HasAnimation(name)) return;
        _player.Play(name, -1, speed);
        if (from > 0) _player.Seek(from, true);
    }

    public void SetGait(Gait gait)
    {
        if (_gait == gait) return;
        _gait = gait;
        // 뼈대를 직접 돌리는 동안 클립이 매 프레임 덮어쓰면 안 된다.
        if (gait != Gait.Clip) _player?.Stop();
    }

    // 목표 지점까지 걸어/뛰어간다. 멈춰 있었다면 한두 걸음에 걸쳐 가속하고, 도착할 때는
    // 감속해서 선다 — 속도를 즉시 박으면 빙판 위를 미끄러지는 것처럼 보인다(§11 · §12).
    public void MoveTo(Vector3 to, float speed, Gait gait)
    {
        SetGait(gait);
        _path.Clear();
        _target = to with { Y = Position.Y };
        _speedWant = speed;
        _moving = true;
        _settle = 0f;
        FaceTo(_target);
    }

    public void Stop(Gait rest = Gait.Clip)
    {
        _path.Clear();
        _moving = false;
        _speedNow = 0f;
        SetGait(rest);
    }

    // 캐릭터는 자기 -Z 를 본다. 몸은 **서서히** 돌아간다 — 좌표만 90도 꺾으면
    // 방향만 바뀐 것처럼 보인다(§13).
    public void FaceTo(Vector3 worldPoint)
    {
        Vector3 d = worldPoint - GlobalPosition;
        d.Y = 0f;
        if (d.LengthSquared() < 0.0001f) return;
        _yawWant = Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)) + 180f;
    }

    // 장면 시작 시 배치용 — 돌아가는 과정 없이 즉시 그 방향을 본다.
    public void SnapFaceTo(Vector3 worldPoint)
    {
        FaceTo(worldPoint);
        _yawNow = _yawWant;
        RotationDegrees = new Vector3(0f, _yawNow, 0f);
    }

    public void LookOverShoulder(float deg) => _lookOver = deg;

    // ── 경유점 ───────────────────────────────────────────────────────────
    // 앞사람을 피해 진로를 살짝 트는 데 쓴다. 도착해도 멈추지 않고 다음 점으로 이어 간다.
    private readonly Queue<Vector3> _path = new();

    public void MoveVia(Vector3 first, Vector3[] rest, float speed, Gait gait)
    {
        MoveTo(first, speed, gait);          // 여기서 큐가 비워진다
        foreach (var p in rest) _path.Enqueue(p);
    }

    // 달리다 한 번 뒤를 흘끗 본다. 연출기가 await 로 재는 것보다 타이밍을 흩기 쉽다.
    private float _glanceIn = -1f, _glanceHold, _glanceDeg;

    public void GlanceBack(float after, float hold, float deg = 145f)
    {
        _glanceIn = after;
        _glanceHold = hold;
        _glanceDeg = deg;
    }

    // 출발 직후 균형을 잃었다가 급히 되잡는다.
    private float _stumbleIn = -1f, _stumblePower;

    public void StumbleIn(float after, float power = 1f)
    {
        _stumbleIn = after;
        _stumblePower = power;
    }

    public override void _Process(double delta)
    {
        float d = (float)delta;
        _styleT += d;

        // ── 예약해 둔 동작 ──────────────────────────────────────────────
        if (_glanceIn >= 0f)
        {
            _glanceIn -= d;
            if (_glanceIn < 0f) { _lookOver = _glanceDeg; _glanceHold = Mathf.Max(0.05f, _glanceHold); }
        }
        else if (_glanceHold > 0f)
        {
            _glanceHold -= d;
            if (_glanceHold <= 0f) _lookOver = 0f;
        }
        if (_stumbleIn >= 0f)
        {
            _stumbleIn -= d;
            if (_stumbleIn < 0f) { Stumble = _stumblePower; _stumbleIn = -1f; }
        }

        // ── 방향 전환 : 몸이 기울면서 한두 걸음에 걸쳐 돌아간다 ──────────
        float dy = Mathf.Wrap(_yawWant - _yawNow, -180f, 180f);
        if (Mathf.Abs(dy) > 0.05f)
        {
            float turn = Mathf.Sign(dy) * Mathf.Min(Mathf.Abs(dy), 300f * d);
            _yawNow = Mathf.Wrap(_yawNow + turn, -180f, 180f);
            _turnLean = Mathf.Lerp(_turnLean, Mathf.Clamp(dy * 0.09f, -11f, 11f), 1f - Mathf.Exp(-9f * d));
        }
        else _turnLean = Mathf.Lerp(_turnLean, 0f, 1f - Mathf.Exp(-7f * d));
        RotationDegrees = new Vector3(0f, _yawNow, 0f);

        if (_moving)
        {
            Vector3 to = _target - Position;
            to.Y = 0f;
            float left = to.Length();

            // 속도가 **조금씩 흔들린다.** 위상은 이동 거리에서 나오므로(§8) 발은 그대로
            // 바닥을 물고, 걸음 리듬만 불규칙해진다 — 급히 도망치는 사람의 리듬이다.
            float irr = _style.Irregular;
            float wobble = irr <= 0.001f ? 1f
                : 1f + irr * (Mathf.Sin(_styleT * 3.1f + _phase * 6.3f) * 0.68f
                            + Mathf.Sin(_styleT * 5.7f + 1.3f) * 0.32f);
            // 휘청이는 동안에는 속도가 확 죽었다가 돌아온다.
            wobble *= 1f - 0.45f * Mathf.Clamp(Stumble, 0f, 1f);
            float cruise = _speedWant * Mathf.Max(0.25f, wobble);

            // 도착 직전에는 한두 걸음에 걸쳐 감속한다. 경유점이 남아 있으면 감속하지 않는다.
            float brake = _path.Count > 0 ? 0f : Mathf.Max(0.6f, _speedWant * 0.34f);
            float want = left < brake ? cruise * Mathf.Max(0.12f, left / brake) : cruise;
            // 출발은 0.28초 정도에 걸쳐 붙는다.
            _speedNow = Mathf.MoveToward(_speedNow, want, _speedWant * d / 0.28f);

            float step = _speedNow * d;
            if (left <= step + 0.02f)
            {
                Position = _target;
                if (_path.Count > 0)
                {
                    // 경유점이 남았다 — 멈추지 않고 다음 점으로 이어 간다.
                    _target = _path.Dequeue() with { Y = Position.Y };
                    FaceTo(_target);
                }
                else
                {
                    _moving = false;
                    _speedNow = 0f;
                    _settle = 1f;
                    _restIdle = true;
                }
            }
            else Position += to / left * step;

            // **위상은 이동 거리에서 나온다** — 발이 바닥을 미끄러지지 않는 유일한 방법이다.
            _phase += step / Mathf.Max(0.25f, Stride);
        }
        else if (_gait != Gait.Clip && !_restIdle)
        {
            // 이동 지시 없이 걸음걸이만 켜 둔 경우(디버그 등) — 자세는 유지한다.
            _settle = 1f;
            _phase += d * 0.4f;
        }

        if (Stumble > 0f) Stumble = Mathf.Max(0f, Stumble - d * 1.6f);
        if (_settle > 0f) _settle = Mathf.Max(0f, _settle - d * 2.2f);
        if (_gait != Gait.Clip) ApplyGait();

        // 중심을 회복하고 나면 기본 자세로 넘긴다 — run 자세로 굳어 있으면 안 된다(§12).
        if (!_moving && _gait != Gait.Clip && _settle <= 0f && _restIdle)
        {
            _restIdle = false;
            PlayClip("idle");
        }
    }

    // 한 주기 = 두 걸음. 다리 · 팔 · 상체 · 골반을 한꺼번에 세운다.
    private void ApplyGait()
    {
        if (!_bonesReady) return;
        bool run = _gait == Gait.Run;
        float p = Mathf.PosMod(_phase, 1f) * Mathf.Tau;
        float st = Stumble;
        float panic = Mathf.Clamp(Panic * _trait.Panic, 0f, 1f);

        // 멈춘 직후에는 동작 폭이 사그라든다 — run 프레임에서 idle 로 딱 끊기지 않게(§12).
        // 멈추면 **모든** 각도가 같이 줄어야 한다. 팔꿈치 · 상체 기울기를 빼놓으면
        // 달리던 자세 그대로 얼어붙은 사람이 된다.
        float amp = _moving ? 1f : _settle;

        float swing = SwingDeg * amp;                       // 허벅지 앞뒤
        float knee = (run ? 76f : 36f) * amp;               // 무릎 굽힘 최대
        float armSw = (run ? 46f : 18f) * _trait.Arm * _style.Arm * (1f + 0.25f * panic) * amp;
        float lean = ((run ? 17f : 3f) + _trait.Lean + _style.Lean + 4f * panic) * amp;
        float bob = (run ? 0.055f : 0.022f) * _style.Bounce * amp;
        // 달리는 동안 몸이 좌우로 흔들린다. 전력으로 뛰는 사람은 축이 가만히 있지 않는다.
        float sway = run ? _style.Sway * amp : 0f;
        float swayT = Mathf.Sin(_styleT * 2.7f + _phase * 5.1f);

        float sL = Mathf.Sin(p), sR = Mathf.Sin(p + Mathf.Pi);
        float back = Backpedal ? -1f : 1f;

        // ── 다리 ─────────────────────────────────────────────────────────
        // 달릴 때는 접지 순간에도 무릎이 조금 굽어 있어야 한다 — 완전히 펴지면 죽마 같다.
        float fwdBend = (run ? 24f : 4f) * amp;     // 앞으로 내민 다리의 무릎
        float kL = (run ? 13f : 5f) * amp + Mathf.Max(0f, -Mathf.Cos(p - 0.7f)) * knee
                 + Mathf.Max(0f, sL) * fwdBend;
        float kR = (run ? 13f : 5f) * amp + Mathf.Max(0f, -Mathf.Cos(p + Mathf.Pi - 0.7f)) * knee
                 + Mathf.Max(0f, sR) * fwdBend;
        float legLift = (run ? 8f : 2f) * amp;

        Set(_upLegL, (swing * sL * back) + legLift, 0f, 2.5f);
        Set(_upLegR, (swing * sR * back) + legLift, 0f, -2.5f);
        Set(_loLegL, -kL, 0f, 0f);      // 무릎은 -X 가 정상 굽힘
        Set(_loLegR, -kR, 0f, 0f);
        // 발목 — 딛는 쪽은 바닥과 수평에 가깝고, 떠 있는 쪽은 발끝이 내려간다.
        Set(_footL, Mathf.Clamp(kL * 0.55f - swing * sL * back * 0.45f, -28f, 30f), 0f, 0f);
        Set(_footR, Mathf.Clamp(kR * 0.55f - swing * sR * back * 0.45f, -28f, 30f), 0f, 0f);

        // ── 팔 ───────────────────────────────────────────────────────────
        // 다리와 반대 위상. 뒤로 가는 쪽은 덜 보내 손이 등 뒤로 넘어가지 않게 한다(§3 · §15).
        // 앞으로 가는 쪽을 덜 올린다 — 그대로 두면 손이 턱 높이까지 올라온다.
        // 뒤로 가는 쪽은 거의 그대로 두되, 손이 등 뒤로 넘어갈 만큼은 아니다(§3 · §15).
        // 좌우 팔의 크기를 다르게 — 양팔이 완벽히 대칭으로 흔들리면 기계처럼 보인다.
        float aL = -armSw * (1f + _style.ArmBias) * sL, aR = -armSw * (1f - _style.ArmBias) * sR;
        aL = aL > 0f ? aL * 0.74f : aL * 0.92f;
        aR = aR > 0f ? aR * 0.74f : aR * 0.92f;
        // 어깨를 바깥으로 벌리는 각 : L 은 음수, R 은 양수여야 팔이 몸통 밖으로 지난다.
        float outL = (-(run ? 15f : 8f) - 4f * panic) * amp;
        float outR = -outL;
        // 뒤로 흔들 때 조금 더 벌려 팔꿈치가 등으로 파고들지 않게 한다(§5).
        outL -= Mathf.Max(0f, sL) * 4f;
        outR += Mathf.Max(0f, sR) * 4f;

        Set(_upArmL, aL, 0f, outL);
        Set(_upArmR, aR, 0f, outR);
        // 팔꿈치는 +X 가 정상 굽힘. 달릴 때 70~100도 사이에서, 앞으로 올 때 더 접힌다.
        float elbow = (run ? 74f : 24f) * amp;
        float elbowSw = (run ? 20f : 6f) * amp;
        Set(_loArmL, Mathf.Clamp(elbow - elbowSw * sL, 18f, 105f), 0f, 0f);
        Set(_loArmR, Mathf.Clamp(elbow - elbowSw * sR, 18f, 105f), 0f, 0f);
        // 어깨는 팔을 따라 아주 조금만 — 여기서 크게 돌리면 어깨가 뒤집혀 보인다(§4).
        Set(_shoulderL, aL * 0.12f, 0f, -2f - 5f * panic);
        Set(_shoulderR, aR * 0.12f, 0f, 2f + 5f * panic);

        // ── 몸통 ─────────────────────────────────────────────────────────
        // 골반은 앞으로 나가는 다리를 따라 돌고, 가슴은 그 반대로 돌아간다(§6).
        float twist = (run ? 6f : 2.5f) * amp;
        Set(_torso, lean + st * 16f + Mathf.Abs(_turnLean) * 0.2f,
                    -sL * twist, st * 11f + _turnLean * 0.5f + sway * swayT * 4.5f);
        Set(_chest, ((run ? 3f : 1f) + 3f * panic) * amp, -sL * twist * 0.5f,
                    _turnLean * 0.35f + sway * swayT * 2.2f);
        // 숙인 만큼 목을 들어 앞을 본다 — 안 그러면 바닥만 보고 달린다.
        Set(_neck, ((run ? -5f : -1.5f) - _style.Lean * 0.55f) * amp, 0f, 0f);
        // 뒤를 흘끗 보는 동작은 목이 맡는다 — 가슴까지 돌리면 달리기가 무너진다.
        if (_head != null)
            _head.RotationDegrees = new Vector3(
                (run ? -3f : 0f) - _style.Lean * 0.35f * amp
                    + Mathf.Sin(p * 2f) * (run ? 2.5f : 0.8f) * (1f + panic),
                _lookOver + sway * Mathf.Sin(_styleT * 1.9f + _phase * 3.7f) * 5f,
                -st * 7f - _turnLean * 0.25f - sway * swayT * 3f);

        if (_hips != null)
        {
            // 두 걸음에 한 번씩 몸이 떴다 가라앉는다.
            _hips.Position = _hips.Position with
            {
                Y = _hipsRestY - bob * (0.5f - 0.5f * Mathf.Cos(p * 2f)) - st * 0.07f,
            };
            _hips.RotationDegrees = new Vector3(0f, sL * twist * 0.9f, 0f);
        }
    }

    // 디버그 촬영용 — 몸 방향을 즉시 박는다. _Process 가 매 프레임 yaw 를 다시 쓰므로
    // 밖에서 RotationDegrees 를 건드려 봐야 바로 되돌아간다.
    public void DebugYaw(float deg)
    {
        _yawNow = _yawWant = deg;
        _turnLean = 0f;
        RotationDegrees = new Vector3(0f, deg, 0f);
    }

    // 디버그 촬영용 — 걸음 위상을 직접 박아 넣고 즉시 자세를 세운다(§14 프레임별 검수).
    public void DebugPose(Gait gait, float phase)
    {
        SetGait(gait);
        _moving = false;
        _settle = 1f;
        _phase = phase;
        ApplyGait();
    }

    // 디버그 — 이 자세에서 두 발의 수평 간격. 실제 보폭이 맞는지 보는 데 쓴다(§8).
    public float DebugFootGap()
    {
        if (_footL == null || _footR == null) return 0f;
        Vector3 a = _footL.GlobalPosition, b = _footR.GlobalPosition;
        return new Vector2(a.X - b.X, a.Z - b.Z).Length();
    }

    public float DebugStride => Stride;

    private static void Set(Node3D n, float x, float y, float z)
    {
        if (n != null) n.RotationDegrees = new Vector3(x, y, z);
    }
}
