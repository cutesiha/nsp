using System.Collections.Generic;
using Godot;

namespace NSP.View;

// CCTV 에 보이는 직원이 "지금 무엇을 하는 것처럼 보일지". 표현 전용이다.
//
// 이 값은 게임 판정에 쓰이지 않는다. 기존 시뮬레이션(이동/업무/방해공작)이 먼저 상태를
// 정하고, CctvActionResolver 가 그것을 읽어 이 enum 으로 번역할 뿐이다.
// 반대 방향(애니메이션 → 게임 판정)은 절대 만들지 않는다.
public enum CctvEmployeeAction
{
    Idle,
    Walking,
    Talking,
    Working,
    Repairing,
    Inspecting,
    Suspicious,
    Handoff,
    // 격리 — 다른 모든 행동보다 우선한다(구속 침대 위 표현).
    Isolated,
    // 동료의 죽음을 보고 무너졌다 — 그날 내내 이어지는 상태다(sim.IsPanicked).
    // 괴물을 마주친 순간의 반응(아래 Ghost*)과는 다른 것이다.
    Panicked,
    // 같은 방에 괴물이 있다 — 직원마다 성격에 맞는 전용 반응을 보인다.
    // 동작의 세부(주저앉기·숨기·대치·계속 일하기)는 RoomWorkVisualController 가 GhostReactionProfiles 로 푼다.
    GhostSheepCower,     // 양   — 다리가 풀려 주저앉아 머리를 감싸고 운다
    GhostRabbitScream,   // 토끼 — 온몸으로 튀어 오르며 비명, 긴장한 채 계속 확인한다
    GhostCatHide,        // 고양이 — 짧게 움찔하고 곧장 같은 방 구석으로 빠진다
    GhostDogHide,        // 강아지 — 크게 놀라 허둥대며 구석으로 도망쳐 웅크린다
    GhostWolfConfront,   // 늑대 — 짧게 움찔하고 괴물 쪽으로 싸울 자세를 잡는다
    GhostFoxWorking,     // 여우 — 잠깐 손이 멈추지만 하던 일을 계속하며 가끔 쳐다본다
    // 정의되지 않은 직원용 예비 반응(기존 ghost_startle).
    GhostStartled,
}

// 직원 3D 캐릭터 한 명의 애니메이션만 담당하는 작은 도우미. AI 는 없다.
//   · 상태가 바뀐 프레임에만 Play 를 부른다(매 프레임 재시작하면 애니메이션이 멈춰 보인다)
//   · 전환은 AnimationPlayer 의 기본 블렌드(0.15초)에 맡긴다 — 놀람 반응만 더 짧게 끊는다
//   · 반복 안 하는 동작(inspect/suspicious/handoff)은 끝나면 스스로 idle 로 돌아간다
//   · 6명이 한 프레임처럼 겹쳐 보이지 않게 시작 위상을 조금씩 어긋나게 둔다(표현 전용)
//   · 지금 클립이 쓰지 않는 관절은 기본 자세로 서서히 되돌린다(아래 RelaxOrphans)
//   · 시선(Neck)은 어떤 클립도 쓰지 않으므로 여기서 따로 돌린다(SetLook)
public sealed class EmployeeCctvAnimator
{
    public const double DefaultBlendSeconds = 0.15;

    // 표현 상태 → 공용 라이브러리의 애니메이션 이름.
    // 괴물 반응은 여러 클립을 순서대로 잇는다 — 여기 있는 것은 반복 구간(예비)이다.
    public static string ClipOf(CctvEmployeeAction a) => a switch
    {
        CctvEmployeeAction.Idle => "idle",
        CctvEmployeeAction.Walking => "walk",
        CctvEmployeeAction.Talking => "talk",
        CctvEmployeeAction.Working => "work",
        CctvEmployeeAction.Repairing => "repair",
        CctvEmployeeAction.Inspecting => "inspect",
        CctvEmployeeAction.Suspicious => "suspicious",
        CctvEmployeeAction.Handoff => "handoff",
        CctvEmployeeAction.Isolated => "isolated_struggle",
        CctvEmployeeAction.Panicked => "ghost_panic",
        CctvEmployeeAction.GhostSheepCower => "ghost_sheep_cower_loop",
        CctvEmployeeAction.GhostRabbitScream => "ghost_rabbit_scream_loop",
        CctvEmployeeAction.GhostCatHide => "ghost_cat_hide_loop",
        CctvEmployeeAction.GhostDogHide => "ghost_dog_hide_loop",
        CctvEmployeeAction.GhostWolfConfront => "ghost_wolf_guard_loop",
        CctvEmployeeAction.GhostFoxWorking => "work",
        CctvEmployeeAction.GhostStartled => "ghost_startle",
        _ => "idle",
    };

    private static bool LoopOf(CctvEmployeeAction a) =>
        a is not (CctvEmployeeAction.Inspecting or CctvEmployeeAction.Suspicious or CctvEmployeeAction.Handoff);

    private readonly AnimationPlayer _anim;
    private readonly float _phase;        // 0~1 시작 위상(직원마다 조금씩 다르게)
    private readonly float _speedJitter;  // 0.95~1.05, 완전히 같은 박자로 움직이지 않게

    private CctvEmployeeAction _action = CctvEmployeeAction.Idle;
    private bool _started;
    private float _walkSpeed = 1f;
    private float _baseSpeed = 1f;
    private float _speedFactor = 1f;

    // 시선 — Neck 을 좌우로 돌린다(라디안). 목표값과 현재값을 따로 둬서 부드럽게 돌린다.
    private readonly Node3D _neck;
    private float _lookTarget, _lookNow;
    private float _lookRate = 14f;

    // 라이브러리 전체에서 한 번이라도 움직이는 관절과, 그 관절의 기본 자세.
    private sealed class Joint
    {
        public string Path;
        public Node3D Node;
        public bool Position;   // false = rotation
        public Vector3 Rest;
    }
    private readonly List<Joint> _joints = new();
    private readonly HashSet<string> _clipTracks = new();

    public CctvEmployeeAction Action => _action;
    public string CurrentClip => _clip;

    public EmployeeCctvAnimator(AnimationPlayer anim, int seed)
    {
        _anim = anim;
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 2654435761L + 1) };
        _phase = rng.Randf();
        _speedJitter = rng.RandfRange(0.95f, 1.05f);
        if (_anim == null) return;
        _anim.PlaybackDefaultBlendTime = DefaultBlendSeconds;

        var root = _anim.GetParent() as Node3D;
        _neck = root?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest/Neck");

        // 가슴 배지는 원래 캐릭터 루트에 붙어 있어서, 몸을 숙이거나 주저앉으면 공중에 남는다.
        // 가슴 관절 밑으로 옮겨 몸을 따라가게 한다(보이는 위치는 그대로 — 표현 보정).
        var badge = root?.GetNodeOrNull<Node3D>("AccessoryAnchor");
        var chest = root?.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips/Torso/Chest");
        if (badge != null && chest != null && badge.IsInsideTree()) badge.Reparent(chest, true);

        CollectJoints(root);
    }

    private void CollectJoints(Node3D root)
    {
        if (root == null) return;
        var seen = new HashSet<string>();
        foreach (string name in _anim.GetAnimationList())
        {
            var a = _anim.GetAnimation(name);
            for (int t = 0; t < a.GetTrackCount(); t++)
            {
                string path = a.TrackGetPath(t).ToString();
                if (!seen.Add(path)) continue;
                int colon = path.LastIndexOf(':');
                if (colon < 0) continue;
                string prop = path[(colon + 1)..];
                if (prop != "rotation" && prop != "position") continue;
                var node = root.GetNodeOrNull<Node3D>(path[..colon]);
                if (node == null) continue;
                bool pos = prop == "position";
                _joints.Add(new Joint { Path = path, Node = node, Position = pos,
                                        Rest = pos ? node.Position : node.Rotation });
            }
        }
    }

    // 게임 이동 속도에 맞춰 walk 재생 속도를 조절한다(1 = 기본).
    public void SetWalkSpeedScale(float scale) => _walkSpeed = Mathf.Clamp(scale, 0.25f, 3f);

    // 재생 속도 배율(표현 전용). 여우가 괴물을 보는 순간 손을 멈추는 데 쓴다. 매 프레임 1 로 돌려놓는다.
    public void SetSpeedFactor(float f)
    {
        _speedFactor = Mathf.Max(0f, f);
        if (_anim != null) _anim.SpeedScale = _baseSpeed * _speedFactor;
    }

    // 고개를 몸 기준으로 yaw(라디안)만큼 돌린다. weight 0 이면 정면. rate = 초당 따라가는 빠르기.
    public void SetLook(float yaw, float weight, float rate = 14f)
    {
        _lookTarget = Mathf.Clamp(yaw, -1.3f, 1.3f) * Mathf.Clamp(weight, 0f, 1f);
        _lookRate = rate;
    }

    public void SetAction(CctvEmployeeAction action)
    {
        string clip = ClipOf(action);
        if (_started && action == _action && _clip == clip) return;
        _action = action;
        _started = true;
        if (_anim == null || !_anim.HasAnimation(clip)) { _clip = clip; return; }
        _baseSpeed = (action == CctvEmployeeAction.Walking ? _walkSpeed : 1f) * _speedJitter;
        Play(clip, -1, LoopOf(action) ? -2f : -1f);
    }

    // 캐릭터가 화면에서 사라졌다 다시 나타날 때 — 같은 동작이어도 한 번 다시 걸어 준다.
    public void Restart() { _started = false; _clip = ""; SetAction(_action); }

    // 이름으로 클립을 직접 튼다(sit_typing / hammer_work / lying_idle, 괴물 반응 단계 등).
    //   blend   : 음수면 기본 블렌드. 놀람 반응은 짧게 줘야 순간이 죽지 않는다.
    //   startAt : 0 이상이면 그 시점부터(CCTV 를 돌렸다 와도 처음부터 다시 하지 않게),
    //             음수면 반복 클립은 직원마다 다른 위상에서 시작한다.
    public void PlayClip(string clip, double blend = -1, float startAt = -1f)
    {
        if (_anim == null || string.IsNullOrEmpty(clip) || clip == _clip) return;
        if (!_anim.HasAnimation(clip)) return;
        _started = true;
        _baseSpeed = _speedJitter;
        Play(clip, blend, startAt >= 0f ? startAt : -2f);
    }

    private string _clip = "";

    // startAt : 0 이상 = 그 시점 · -2 = 반복 클립이면 위상 맞춤 · -1 = 처음부터
    private void Play(string clip, double blend, float startAt)
    {
        _clip = clip;
        _anim.SpeedScale = _baseSpeed * _speedFactor;
        _anim.Play(clip, blend);
        var a = _anim.GetAnimation(clip);
        if (startAt >= 0f) _anim.Seek(Mathf.Min(startAt, (float)a.Length), true);
        else if (startAt < -1.5f && a.LoopMode != Animation.LoopModeEnum.None) _anim.Seek(a.Length * _phase, true);

        _clipTracks.Clear();
        for (int t = 0; t < a.GetTrackCount(); t++) _clipTracks.Add(a.TrackGetPath(t).ToString());
    }

    public void Tick()
    {
        if (_anim == null) return;
        // 자리 전용 클립(PlayClip)을 돌리는 중에는 손대지 않는다 — 여기서 idle 로 되돌리면
        // 작업 모션이 매 프레임 끊긴다.
        if (_clip != ClipOf(_action)) return;
        // 반복하지 않는 동작이 끝났으면 조용히 idle 로 내려온다.
        if (!LoopOf(_action) && !_anim.IsPlaying())
            SetAction(CctvEmployeeAction.Idle);
    }

    // 매 프레임 한 번(보이는 동안). 위치·클립을 정한 뒤에 부른다.
    public void Update(float delta)
    {
        if (_anim == null) return;
        RelaxOrphans(delta);
        if (_neck != null)
        {
            _lookNow = Mathf.Lerp(_lookNow, _lookTarget, 1f - Mathf.Exp(-_lookRate * delta));
            var r = _neck.Rotation;
            if (!Mathf.IsEqualApprox(r.Y, _lookNow)) _neck.Rotation = r with { Y = _lookNow };
        }
    }

    // AnimationPlayer 는 지금 클립에 없는 관절을 건드리지 않는다 — 앞 클립의 마지막 자세가
    // 그대로 남는다(주저앉았던 다리가 일할 때도 접혀 있는 식). 블렌드가 끝난 뒤에는
    // 그런 관절을 기본 자세로 부드럽게 되돌린다. 블렌드 중에는 믹서가 뒤에 덮어쓰므로 해가 없다.
    // VisualRoot 높이(Y)는 앉기 보정을 하는 컨트롤러 몫이라 건드리지 않는다.
    private void RelaxOrphans(float delta)
    {
        float k = 1f - Mathf.Exp(-10f * delta);
        foreach (var j in _joints)
        {
            if (_clipTracks.Contains(j.Path)) continue;
            if (j.Position)
            {
                var p = j.Node.Position;
                var want = new Vector3(j.Rest.X, p.Y, j.Rest.Z);
                if (!p.IsEqualApprox(want)) j.Node.Position = p.Lerp(want, k);
            }
            else
            {
                var r = j.Node.Rotation;
                if (!r.IsEqualApprox(j.Rest)) j.Node.Rotation = r.Lerp(j.Rest, k);
            }
        }
    }
}
