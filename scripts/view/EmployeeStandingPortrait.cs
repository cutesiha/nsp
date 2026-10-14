using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 직원 2D 스탠딩 원화 한 자리. **스토리 전용이 아니다** — 스탠딩을 띄우는 화면은 전부 이걸 쓴다.
//
//   StandingPortraitLayout        (배치 계산)
//           ↓
//   EmployeeStandingPortrait      (이 클래스 — 원화 · 표정 · 입 · 발광 · 명암)
//           ↓
//   ┌─────────────────┬──────────────────┐
//   StoryCutinHud                    PhoneCallHud
//
// 두 화면은 서로를 모른다. 이 컴포넌트만 공유하는 형제다.
//
// 배치 계산은 인터뷰 화면과 같은 StandingPortraitLayout 을 쓴다 — 여섯 명이 하나의
// 공통 배율을 쓰고 발끝이 아래에 붙는 그 방식 그대로다.
// 톤도 인터뷰 화면과 같은 스탠딩 CRT 셰이더(Config.StandingShaderPath)를 쓴다.
// 밝고 화려한 비주얼 노벨 프레임을 만들지 않는다(문서 §24.1).
//
// 입 모양은 EmployeeMouthAnimator 가 전역으로 쥐고 있고, 그 화자가 '나' 일 때만 움직인다.
// 즉 2인 화면에서 듣는 쪽은 자기 표정의 닫은 입으로 서 있는다.
public partial class EmployeeStandingPortrait : Control
{
    // 원화 위쪽 여백 — 제일 큰 캐릭터의 머리가 영역 위선에 닿지 않게 한다.
    public float TopMargin { get; set; } = 12f;
    // 전원에게 똑같이 걸리는 확대. 공통 배율 위에 곱해지므로 여섯 명의 키 비율은 유지된다.
    public float Zoom { get; set; } = 1f;

    // 발끝을 바닥에서 이만큼(표시 영역 높이 비율) 띄운다. 양수면 그림이 위로 올라간다.
    // 크기는 그대로 두고 자리만 올리는 값이다.
    public float LiftFraction { get; set; }

    // 말하지 않는 쪽은 살짝 눌러 둔다 — 누가 말하는지 한눈에 보이게.
    private const float ListenDim = 0.55f;
    private const float SpeakDim = 1f;

    // 혼자 뜨는 화면(전화)에서는 명암을 나누지 않는다 — 나눌 상대가 없다.
    public bool AlwaysLit { get; set; }

    // 표정을 고정하지 않고 EmployeeMouthAnimator 가 대사마다 정한 표정을 따라간다.
    // 전화가 이 모드를 쓴다(대사 분위기에 따라 smile ↔ bad 가 저절로 바뀐다).
    public bool FollowSpeakerExpression { get; set; }

    private TextureRect _portrait;
    private ShaderMaterial _mat;
    private string _employeeId = "";
    private string _expression = "";
    private Texture2D _lastTex;
    // 마지막으로 배치를 계산했을 때의 표시 영역 크기. 레이아웃이 아직 잡히지 않은 첫 프레임
    // (Size = 0)에 계산한 자리가 그대로 굳지 않게 한다.
    private Vector2 _lastBox = Vector2.Zero;
    private float _dim = ListenDim;
    private Tween _fade;
    // 자리 이동 트윈. **반드시 하나만** 살아 있어야 한다 — 앞 묶음에서 돌던 이동이 남아
    // 있으면 다음 묶음에서 방금 잡아 준 자리를 계속 덮어쓴다(실제로 그 버그가 있었다).
    private Tween _move;

    public string EmployeeId => _employeeId;

    // 지금 실제로 그려지고 있는 표정.
    public string Expression => FollowSpeakerExpression
        ? (EmployeeMouthAnimator.Speaker == _employeeId && !string.IsNullOrEmpty(EmployeeMouthAnimator.Expression)
            ? EmployeeMouthAnimator.Expression
            : EmployeeArt.DefaultExpression(_employeeId))
        : _expression;

    // 검사용 — 지금 이 자리에 걸린 원화.
    public Texture2D CurrentTexture => _portrait?.Texture;

    public bool IsSpeaking => !string.IsNullOrEmpty(_employeeId)
                              && EmployeeMouthAnimator.Speaker == _employeeId
                              && EmployeeMouthAnimator.Talking;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;

        // 크기는 StandingPortraitLayout 이 직접 계산한다. 여기서 화면에 맞춰 늘리면
        // (KeepAspect) 원화마다 잘라낸 여백이 달라서 키가 제각각으로 보인다.
        _portrait = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_portrait);
        BuildMaterial();
        Modulate = new Color(1, 1, 1, 0);
    }

    // 인터뷰 화면과 같은 스탠딩 CRT 셰이더. 경로가 비어 있거나 셰이더가 없으면
    // 원화를 그대로 그린다(연출만 빠지고 화면은 정상).
    private void BuildMaterial()
    {
        var cfg = Config.Instance?.Data;
        string path = cfg?.StandingShaderPath ?? "";
        if (string.IsNullOrEmpty(path)) return;
        var shader = ResourceLoader.Exists(path) ? GD.Load<Shader>(path) : null;
        if (shader == null) return;

        _mat = new ShaderMaterial { Shader = shader };
        // 스캔라인 · 그레인은 화면 쪽에서 이미 그린다 — 일러에서는 끈다.
        _mat.SetShaderParameter("scan_amt", cfg?.StandingScanAmt ?? 0f);
        _mat.SetShaderParameter("grain_amt", cfg?.StandingGrainAmt ?? 0f);
        _portrait.Material = _mat;
    }

    // 이 자리에 설 직원. 같은 직원이면 표정만 갈아 끼운다.
    public void SetEmployee(string employeeId, string expression)
    {
        _employeeId = employeeId ?? "";
        _expression = EmployeeArt.ResolveExpression(_employeeId, expression);

        var def = FacilitySimulation.Instance?.GetEmployeeDef(_employeeId);
        if (_mat != null && def != null)
        {
            // 직원별 발광 값. 흰 옷처럼 밝은 원화는 EmployeeDef 에서 낮춰 둔다.
            _mat.SetShaderParameter("glow_amt", def.StandingGlowAmt);
            _mat.SetShaderParameter("glow_center", def.StandingGlowCenter);
        }
        Refresh(force: true);
    }

    public void SetExpression(string expression)
    {
        _expression = EmployeeArt.ResolveExpression(_employeeId, expression);
        Refresh(force: true);
    }

    // --- 등장 · 퇴장 (전부 페이드) -----------------------------------------

    public bool IsVisibleOnScreen => Modulate.A > 0.01f;

    public void FadeIn(float seconds)
    {
        KillFade();
        if (Modulate.A >= 0.99f) return;
        _fade = CreateTween();
        _fade.TweenProperty(this, "modulate:a", 1f, seconds);
    }

    // 사라진 뒤 자리를 비운다(다음 등장이 깨끗하게 시작되도록).
    public void FadeOut(float seconds, bool clearAfter = true)
    {
        KillFade();
        if (Modulate.A <= 0.01f)
        {
            if (clearAfter) SetEmployee("", "");
            return;
        }
        _fade = CreateTween();
        _fade.TweenProperty(this, "modulate:a", 0f, seconds);
        if (clearAfter) _fade.TweenCallback(Callable.From(() => SetEmployee("", "")));
    }

    // --- 자리 이동 --------------------------------------------------------

    // 부드럽게 옮긴다(혼자 ↔ 둘 구도가 바뀔 때).
    public void MoveTo(Vector2 target, float seconds)
    {
        KillMove();
        if (Position.IsEqualApprox(target)) return;
        _move = CreateTween();
        _move.SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _move.TweenProperty(this, "position", target, seconds);
    }

    // 트윈 없이 그 자리에 둔다(등장 직전 · 창 크기 변경).
    public void SnapTo(Vector2 target)
    {
        KillMove();
        Position = target;
    }

    // 연출 없이 즉시 걷는다(페일세이프 경로).
    public void ClearNow()
    {
        KillFade();
        KillMove();
        Modulate = new Color(1, 1, 1, 0);
        SetEmployee("", "");
    }

    private void KillFade()
    {
        if (_fade != null && _fade.IsValid()) _fade.Kill();
        _fade = null;
    }

    private void KillMove()
    {
        if (_move != null && _move.IsValid()) _move.Kill();
        _move = null;
    }

    // 매 프레임 — 입 모양이 바뀌었으면 원화를 갈아 끼우고, 화자/청자 밝기를 따라간다.
    public override void _Process(double delta)
    {
        Refresh(force: false);

        float want = AlwaysLit || EmployeeMouthAnimator.Speaker == _employeeId ? SpeakDim : ListenDim;
        _dim = Mathf.MoveToward(_dim, want, (float)delta * 2.6f);
        _portrait.Modulate = new Color(_dim, _dim, _dim, 1f);
    }

    private void Refresh(bool force)
    {
        if (string.IsNullOrEmpty(_employeeId)) { _portrait.Texture = null; return; }

        // 원화가 없는 직원(standing_v2 폴더가 없는 경우)은 EmployeeDef 의 기본 스탠딩으로.
        var tex = (FollowSpeakerExpression
                      ? EmployeeMouthAnimator.PortraitFor(_employeeId)
                      : EmployeeMouthAnimator.PortraitFor(_employeeId, _expression))
                  ?? FacilitySimulation.Instance?.GetEmployeeDef(_employeeId)?.StandingImage;
        if (tex == null) { _portrait.Texture = null; return; }
        if (!force && tex == _lastTex && Size == _lastBox) return;

        _lastTex = tex;
        _lastBox = Size;
        _portrait.Texture = tex;
        var (size, pos) = StandingPortraitLayout.Place(tex, Size, TopMargin, Zoom, Size.Y * LiftFraction);
        _portrait.Size = size;
        _portrait.Position = pos;
    }
}
