using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Prologue;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 포스터 변형 한 장 — 중앙제어실 조명을 켜고, 모니터1 에 GUIDE-0.exe(smile) 얼굴,
// 모니터2 에 "근무를 시작하시겠습니까?" 를 크게 띄운 상태로 찍는다.
// 창 모드로 실행한다(--headless 불가).
//
//   godot --path . res://scenes/debug/PosterShotLit.tscn -- <저장 폴더> [가로] [세로] [천장광] [노출배수]
//
// 예) godot --path . res://scenes/debug/PosterShotLit.tscn -- C:/poster
//     godot --path . res://scenes/debug/PosterShotLit.tscn -- C:/poster 5000 7000 2.9 1.0
//
// 게임 코드는 하나도 건드리지 않는다 — 전부 이 스크립트가 런타임에 그 상태로 만들어 두고
// PosterCamera 로 찍을 뿐이다.
public partial class PosterShotLit : Node
{
    private string _dir = "";
    private int _width = 5000;
    private int _height = 7000;
    private float _ceilEnergy = 2.9f;     // 씬의 CeilingLight 원래 밝기 = 완전히 켠 상태
    private float _fillEnergy = 0.34f;    // 씬의 FillLight 원래 밝기
    private float _exposure = 1f;

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://poster");
        if (args.Length > 2 && int.TryParse(args[1], out int w) && int.TryParse(args[2], out int h))
        {
            _width = w;
            _height = h;
        }
        if (args.Length > 3 && float.TryParse(args[3], out float ce)) _ceilEnergy = ce;
        if (args.Length > 4 && float.TryParse(args[4], out float ex)) _exposure = ex;

        // 타이틀 · 프롤로그를 건너뛰고 DAY1 배치 화면으로 바로 들어간다.
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Seconds(1.0);

        // 배치를 채우고 근무를 시작한다 — PosterShot 과 같은 경로다.
        var sim = FacilitySimulation.Instance;
        var ctl = ControlRoom3DController.Instance;
        var flow = DebugEntryPoint.FindFlow(GetTree());
        DebugEntryPoint.CallStatic("StartNewRun", 1);
        GameState.Instance.GoToNextDay();
        DebugEntryPoint.SetStage(flow, "DayTransition");
        DebugEntryPoint.Call(flow, "EnterSchedule");
        DebugEntryPoint.SuppressStressHint(flow);
        await Seconds(1.0);

        var map = ScheduleMapView.Instance;
        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "core_room", "maintenance_room", "guard_room", "storage_room", "power_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Seconds(0.5);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var svp = ctl.ScheduleMapViewport;
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        svp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(1.2);

        var root = GetNodeOrNull<Node3D>("MainScene3D_Test");

        // ── 1. 중앙제어실 조명 켜기 ─────────────────────────────────────────
        // 평소에는 Lights 노드가 통째로 꺼져 있고(모니터 CRT 빛만 남는다), 근무 중에는
        // ControlRoom3DHorror 가 매 프레임 천장등 밝기를 제 목표값으로 되돌린다.
        // 그래서 끄기 스위치(ExternalLightingOverride)를 먼저 올리고 밝기를 넣는다.
        ControlRoom3DHorror.ExternalLightingOverride = true;
        var lights = root?.GetNodeOrNull<Node3D>("ControlRoom/Lights");
        if (lights != null) lights.Visible = true;
        var ceiling = root?.GetNodeOrNull<OmniLight3D>("ControlRoom/Lights/CeilingLight");
        if (ceiling != null) { ceiling.Visible = true; ceiling.LightEnergy = _ceilEnergy; }
        var fill = root?.GetNodeOrNull<OmniLight3D>("ControlRoom/Lights/FillLight");
        if (fill != null) { fill.Visible = true; fill.LightEnergy = _fillEnergy; }
        GD.Print($"[PosterShotLit] 조명 ON — 천장등 {_ceilEnergy}, 보조등 {_fillEnergy}");

        // ── 2. 모니터 1 — GUIDE-0.exe 얼굴(smile) ──────────────────────────
        ctl.SetLeftScreen(ctl.GuideFaceViewport);
        var face = GuideFaceView.Instance;
        if (face != null)
        {
            face.SetShown(true);
            // guide0_smile.png 는 입까지 그려진 완성 얼굴이라 입 Overlay 가 필요 없다.
            var smile = GD.Load<Texture2D>("res://assets/ui/guide0/guide0_smile.png");
            face.SetPortrait("smile", smile, false);
            GD.Print($"[PosterShotLit] 모니터1 — GUIDE-0 smile (텍스처 {(smile != null ? "로드됨" : "없음")})");
        }
        else GD.PushWarning("[PosterShotLit] GuideFaceView 를 못 찾았다.");

        // ── 3. 모니터 2 — "근무를 시작하시겠습니까?" ────────────────────────
        var promptVp = new SubViewport
        {
            Name = "PosterPromptViewport",
            Size = ctl.GuideFaceViewport.Size,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            Disable3D = true,
            GuiDisableInput = true,
            TransparentBg = false,
        };
        ctl.AddChild(promptVp);
        ControlRoom3DController.AddScaledView(promptVp, new ShiftPromptView(), new Vector2I(800, 600));
        ctl.SetRightScreen(promptVp);
        // 두 화면 모두 완전히 켜 둔다(UpdateActiveViewports 도 여기서 같이 돈다).
        ctl.SetScreenBrightness(1.0f);
        // SetScreenBrightness 가 '아는 뷰포트'만 Always 로 올리므로, 내 뷰포트는 직접 못박는다.
        promptVp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        GD.Print("[PosterShotLit] 모니터2 — 근무 시작 안내 문구");

        await Seconds(1.5);

        var cam = GetTree().Root.FindChild("PosterCamera", true, false) as PosterCamera;
        if (cam == null)
        {
            GD.PushError("[PosterShotLit] 메인 씬에서 PosterCamera 를 못 찾았다.");
            GetTree().Quit(1);
            return;
        }
        cam.CaptureResolution = new Vector2I(_width, _height);
        cam.OutputDirectory = _dir;
        cam.ExposureBoost = _exposure;
        string saved = await cam.CaptureAsync();
        GD.Print(saved != null ? "saved → " + saved : "[PosterShotLit] 저장 실패");
        GetTree().Quit(saved != null ? 0 : 1);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}

// 모니터 2 전용 — 근무 시작 안내 한 줄을 화면 정중앙에 크게. 다른 CRT 프로그램과
// 같은 논리 캔버스(800x600)에 그린다.
public partial class ShiftPromptView : Control
{
    private static readonly Vector2 Canvas = new(800f, 600f);
    // 게임 CRT 화면의 기본 본문색 — CCTVMonitorView 가 쓰는 인광 흰색 그대로.
    private static readonly Color Ink = new(0.85f, 0.90f, 0.85f);
    private const string Text = "근무를 시작하시겠습니까?";

    private Font _font;

    public override void _Ready()
    {
        _font = ViewFont.Default;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Size = Canvas;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Canvas), new Color(0.02f, 0.02f, 0.025f));

        // 화면 폭에 맞는 가장 큰 글자 크기를 찾는다(글자가 칸 밖으로 넘치지 않게).
        const float maxWidth = 716f;
        int size = ViewFont.S(60);
        while (size > 12 && _font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X > maxWidth) size -= 2;

        // 세로 정중앙 — 베이스라인은 어센더/디센더를 빼고 잡는다.
        float baseline = Canvas.Y * 0.5f + (_font.GetAscent(size) - _font.GetDescent(size)) * 0.5f;

        // CRT 잔광처럼 한 겹 번지게 깔고 그 위에 본문을 얹는다.
        DrawString(_font, new Vector2(0f, baseline), Text, HorizontalAlignment.Center,
            Canvas.X, size, Ink with { A = 0.22f });
        DrawString(_font, new Vector2(0f, baseline), Text, HorizontalAlignment.Center,
            Canvas.X, size, Ink);

        // 문구 아래 가는 밑줄 — 글자만 떠 있지 않게 받쳐 준다.
        float w = _font.GetStringSize(Text, HorizontalAlignment.Left, -1, size).X;
        DrawRect(new Rect2((Canvas.X - w) * 0.5f, baseline + _font.GetDescent(size) + 18f, w, 2f),
            Ink with { A = 0.5f });

        // 다른 CRT 화면과 같은 주사선.
        for (float y = 0f; y < Canvas.Y; y += 4f)
            DrawRect(new Rect2(0f, y, Canvas.X, 1f), new Color(0f, 0f, 0f, 0.16f));
    }
}
