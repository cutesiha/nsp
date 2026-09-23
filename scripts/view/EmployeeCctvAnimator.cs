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
}

// 직원 3D 캐릭터 한 명의 애니메이션만 담당하는 작은 도우미. AI 는 없다.
//   · 상태가 바뀐 프레임에만 Play 를 부른다(매 프레임 재시작하면 애니메이션이 멈춰 보인다)
//   · 전환은 AnimationPlayer 의 기본 블렌드(0.15초)에 맡긴다
//   · 반복 안 하는 동작(inspect/suspicious/handoff)은 끝나면 스스로 idle 로 돌아간다
//   · 6명이 한 프레임처럼 겹쳐 보이지 않게 시작 위상을 조금씩 어긋나게 둔다(표현 전용)
public sealed class EmployeeCctvAnimator
{
    // 표현 상태 → 공용 라이브러리의 애니메이션 이름.
    private static readonly string[] ClipOf =
    {
        "idle", "walk", "talk", "work", "repair", "inspect", "suspicious", "handoff",
        "isolated_struggle",
    };

    private static readonly bool[] LoopOf =
    {
        true, true, true, true, true, false, false, false, true,
    };

    private readonly AnimationPlayer _anim;
    private readonly float _phase;        // 0~1 시작 위상(직원마다 조금씩 다르게)
    private readonly float _speedJitter;  // 0.95~1.05, 완전히 같은 박자로 움직이지 않게

    private CctvEmployeeAction _action = CctvEmployeeAction.Idle;
    private bool _started;
    private float _walkSpeed = 1f;

    public CctvEmployeeAction Action => _action;

    public EmployeeCctvAnimator(AnimationPlayer anim, int seed)
    {
        _anim = anim;
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 2654435761L + 1) };
        _phase = rng.Randf();
        _speedJitter = rng.RandfRange(0.95f, 1.05f);
        if (_anim != null) _anim.PlaybackDefaultBlendTime = 0.15;
    }

    // 게임 이동 속도에 맞춰 walk 재생 속도를 조절한다(1 = 기본).
    public void SetWalkSpeedScale(float scale) => _walkSpeed = Mathf.Clamp(scale, 0.25f, 3f);

    public void SetAction(CctvEmployeeAction action)
    {
        if (_started && action == _action && _clip == ClipOf[(int)action]) return;
        _action = action;
        _started = true;
        _clip = ClipOf[(int)action];
        PlayCurrent(seekPhase: LoopOf[(int)action]);
    }

    // 캐릭터가 화면에서 사라졌다 다시 나타날 때 — 같은 동작이어도 한 번 다시 걸어 준다.
    public void Restart() { _started = false; _clip = ""; SetAction(_action); }

    // WorkSpot 이 지정한 클립을 직접 튼다(sit_typing / hammer_work / lying_idle 등).
    // 표현 상태 enum 으로는 담을 수 없는 자리 전용 동작에만 쓴다.
    public void PlayClip(string clip)
    {
        if (_anim == null || string.IsNullOrEmpty(clip) || clip == _clip) return;
        if (!_anim.HasAnimation(clip)) return;
        _clip = clip;
        _started = true;
        _anim.SpeedScale = _speedJitter;
        _anim.Play(clip);
        var a = _anim.GetAnimation(clip);
        if (a.LoopMode != Animation.LoopModeEnum.None) _anim.Seek(a.Length * _phase, true);
    }

    private string _clip = "";

    public void Tick()
    {
        if (_anim == null) return;
        // 자리 전용 클립(PlayClip)을 돌리는 중에는 손대지 않는다 — 여기서 idle 로 되돌리면
        // 작업 모션이 매 프레임 끊긴다.
        if (_clip != ClipOf[(int)_action]) return;
        // 반복하지 않는 동작이 끝났으면 조용히 idle 로 내려온다.
        if (!LoopOf[(int)_action] && !_anim.IsPlaying())
            SetAction(CctvEmployeeAction.Idle);
    }

    private void PlayCurrent(bool seekPhase)
    {
        if (_anim == null) return;
        string clip = ClipOf[(int)_action];
        if (!_anim.HasAnimation(clip)) return;

        _anim.SpeedScale = (_action == CctvEmployeeAction.Walking ? _walkSpeed : 1f) * _speedJitter;
        _anim.Play(clip);
        // 반복 동작은 시작 지점을 직원마다 어긋나게 — 6명이 복제인간처럼 보이지 않게 한다.
        if (seekPhase) _anim.Seek(_anim.GetAnimation(clip).Length * _phase, true);
    }
}
