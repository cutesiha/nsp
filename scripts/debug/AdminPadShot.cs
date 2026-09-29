using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 관리자 패드 캡처 · 계측. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/AdminPadShot.tscn -- <저장 폴더> [모드]
//
//   (없음)  DAY1 근무를 띄우고 Tab 과 같은 경로로 패드를 꺼내 거치 · 드는 중 · 든 상태 ·
//           전원 차단 / 복구까지 PNG 로 남긴다.
//   pose    파지 포즈 검사 — 플레이어 시점 · 좌측 45° 렌더 + 손 메시가 본체 · 화면을
//           얼마나 뚫고 가리는지 꼭짓점 단위로 재서 수치로 찍는다.
//   grips   그립 후보를 차례로 적용해 pose 계측 + 렌더를 반복한다(값 고르기용).
//   clicks  든 상태에서 버튼을 5번 눌러 패드 네 모서리 픽셀 · 왼팔 뼈 · 카메라가
//           클릭에 반응하지 않는지(변화 0) 잰다.
public partial class AdminPadShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");

        var pad = await BootToShift();
        if (pad == null) { GetTree().Quit(1); return; }
        // showhand: 든 동안에도 왼팔을 보이게 해서 파지 자체를 검사한다(플랜 B 를 끈 상태).
        if (args.Contains("showhand")) pad.HideLeftArmWhileHolding = false;

        bool ok = true;
        if (args.Contains("pose")) ok = await PoseCheck(pad, "pose");
        else if (args.Contains("grips")) await CompareGrips(pad);
        else if (args.Contains("clicks")) await ClickStability(pad);
        else await FullSequence(pad);

        GD.Print("saved → " + _dir);
        GetTree().Quit(ok ? 0 : 1);
    }

    // DAY1 근무 시작까지 — 실제 게임 경로 그대로(배치 → 시작 버튼).
    private async System.Threading.Tasks.Task<AdminPad3D> BootToShift()
    {
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); return null; }

        var roster = sim.GetActiveEmployeeIds().ToList();
        string[] rooms = { "core_room", "power_room", "maintenance_room", "guard_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Frames(5);
        var vpPos = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        await Seconds(2.5);
        // 근무 시작 때 저절로 뜨는 「오늘의 업무」 창을 닫는다.
        Day1HistoryOverlay.Instance?.CloseWindow();
        await Seconds(0.4);

        var pad = AdminPad3D.Instance;
        if (pad == null) GD.PrintErr("AdminPad3D 가 씬에 없다");
        return pad;
    }

    private async System.Threading.Tasks.Task<AdminPad3D> HoldPad(AdminPad3D pad)
    {
        if (!pad.IsHeld)
        {
            ClueHud.Instance?._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
            for (int i = 0; i < 600 && !pad.IsHeld; i++) await Frame();
            await Seconds(0.8);
        }
        return pad;
    }

    // ── 기본 시퀀스 ──────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task FullSequence(AdminPad3D pad)
    {
        var ctl = ControlRoom3DController.Instance;

        // ① 거치 상태.
        Report("거치 상태", pad);
        Save("pad_1_cradle.png");

        // 전력 패널 확대 — PAD 명판(숫자키 4 = 전력 기기 확대).
        ctl._Input(new InputEventKey { Keycode = GameSettings.GetKey(GameSettings.ZoomTarget.PowerPanel), Pressed = true });
        await Seconds(0.8);
        Save("pad_2_power_panel.png");
        ctl._Input(new InputEventKey { Keycode = GameSettings.GetKey(GameSettings.ZoomTarget.PowerPanel), Pressed = true });
        await Seconds(0.6);

        // ② 드는 중 · ③ 든 상태.
        ClueHud.Instance?._Input(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        await Seconds(0.78);
        Report("드는 중", pad);
        Save("pad_3_lifting.png");
        await Seconds(1.2);
        Report("든 상태", pad);
        Save("pad_4_held.png");

        pad.View?.SwitchTab(PadView.Tab.Staff, fade: false);
        await Seconds(0.3);
        Save("pad_5_staff.png");

        // 전원 차단 — 들고 있던 패드는 글리치 뒤 내려놓는다.
        GameState.Instance.TryTogglePower(PowerConsumer.Sensor);
        for (int k = 0; k < 8; k++)
        {
            await Frame();
            GD.Print($"   전원 차단 +{k + 1}프레임: 화면={pad.IsScreenOn} 들고있음={pad.IsOpen} 패드전원={AdminPad3D.PadPowered} 글리치={pad.View?.IsGlitching}");
            if (k is 2 or 5) Save($"pad_6_power_cut_glitch_{k + 1}.png");
        }
        await Seconds(1.2);
        Report("전원 차단 뒤", pad);
        Save("pad_7_power_off.png");

        // 전원 복구 — 부팅 화면.
        GameState.Instance.TryTogglePower(PowerConsumer.Sensor);
        await Seconds(0.35);
        Save("pad_8_boot.png");
        await Seconds(1.0);
        Report("전원 복구 뒤", pad);
    }

    // ── ① 파지 포즈 검사 ─────────────────────────────────────────────────

    // 플레이어 시점 + 좌측 45° 두 장 + 파지 규칙 검사. 규칙을 어기면 false.
    private async System.Threading.Tasks.Task<bool> PoseCheck(AdminPad3D pad, string tag)
    {
        await HoldPad(pad);
        Report("든 상태", pad);
        bool rule = CheckBehindScreen(pad);
        Save($"{tag}_front.png");
        GD.Print("   " + await CountHandPixelsOverScreen(pad));
        await SideShot(pad, 45f, $"{tag}_left45.png");
        await SideShot(pad, 88f, $"{tag}_left90.png");
        await ReportTiming(pad);

        // 든 동안 왼팔을 숨기면(플랜 B) 화면을 가릴 손 자체가 없다.
        // 위의 파지 규칙 결과는 "숨기지 않았다면 어땠을까" 를 기록해 두는 용도다.
        if (pad.HideLeftArmWhileHolding)
        {
            GD.Print("   [파지] HideLeftArmWhileHolding = ON — 든 동안 왼팔 메시를 숨긴다."
                     + " 화면 위에 손 픽셀이 0 인 것은 이 스위치로 보장된다.");
            return true;
        }
        return rule;
    }

    // ── 파지 규칙 — 왼손은 통째로 화면 평면 뒤에 있어야 한다 ─────────────
    //
    //   화면 법선 N = 화면 쿼드가 바라보는 방향(카메라 쪽).
    //   규칙: 왼손의 모든 본(손목 · 엄지 · 네 손가락 전부)의 월드 위치 p 에 대해
    //         dot(p - 화면중심, N) < -0.005  (= 화면 평면보다 최소 5mm 뒤)
    //
    // 본만 보면 뼈는 뒤에 있는데 살(메시)이 앞으로 부풀 수 있으므로, 같은 잣대로
    // 왼손 메시 꼭짓점 전부도 잰다. 둘 다 통과해야 합격이다.
    private const float BehindMarginM = 0.005f;

    private static readonly string[] LeftHandBones =
    {
        "Hand_L",
        "Thumb_L_1", "Thumb_L_2",
        "Index_L_1", "Index_L_2", "Index_L_3",
        "Middle_L_1", "Middle_L_2", "Middle_L_3",
        "Ring_L_1", "Ring_L_2", "Ring_L_3",
        "Pinky_L_1", "Pinky_L_2", "Pinky_L_3",
    };

    private bool CheckBehindScreen(AdminPad3D pad)
    {
        var screen = pad.GetNodeOrNull<Node3D>("PadBody/Screen");
        var skel = GetTree().Root.FindChild("Rig", true, false) as Skeleton3D;
        if (screen == null || skel == null) { GD.PrintErr("파지 규칙 검사: 화면/리그를 찾지 못함"); return false; }

        Vector3 center = screen.GlobalPosition;
        Vector3 n = screen.GlobalTransform.Basis.Z.Normalized();   // 화면이 바라보는 방향
        Transform3D rig = skel.GlobalTransform;

        var bad = new List<(string Name, float D)>();
        float worstName = float.MinValue;
        string worst = "—";
        foreach (string b in LeftHandBones)
        {
            int idx = skel.FindBone(b);
            if (idx < 0) { GD.PrintErr($"파지 규칙 검사: 본 {b} 없음"); return false; }
            Vector3 p = rig * skel.GetBoneGlobalPose(idx).Origin;
            float d = (p - center).Dot(n);
            if (d > worstName) { worstName = d; worst = b; }
            if (d >= -BehindMarginM) bad.Add((b, d));
        }

        // 메시 꼭짓점 — 같은 평면 잣대(본보다 엄격하다).
        float meshWorst = float.MinValue;
        string meshWho = "—";
        int meshBad = 0;
        foreach (var node in skel.GetChildren())
        {
            foreach (var m in node.GetChildren())
            {
                if (m is not MeshInstance3D mi || mi.Mesh == null) continue;
                string name = mi.Name.ToString();
                if (!name.Contains("_L") || PartOf(name) == "팔") continue;   // 손만(팔뚝은 패드 아래)
                var world = mi.GlobalTransform;
                foreach (var v in mi.Mesh.GetFaces())
                {
                    float d = ((world * v) - center).Dot(n);
                    if (d >= -BehindMarginM) meshBad++;
                    if (d > meshWorst) { meshWorst = d; meshWho = PartOf(name); }
                }
            }
        }

        // 본은 지시받은 대로 5mm 여유까지 요구하고, 살(메시)은 "평면보다 앞이 아닐 것"만 요구한다.
        bool ok = bad.Count == 0 && meshWorst < 0f;
        GD.Print($"   [파지 규칙] 본 {LeftHandBones.Length}개 중 위반 {bad.Count}개(기준 −5.0mm)"
                 + $" · 손 메시 여유 {meshWorst * 1000f:+0.0;-0.0}mm (기준 0 미만, −5mm 미달 꼭짓점 {meshBad}개)"
                 + $" — 가장 앞선 본 {worst} {worstName * 1000f:+0.0;-0.0}mm · 가장 앞선 메시 {meshWho}");
        foreach (var (name, d) in bad)
            GD.Print($"      ✖ {name} 이(가) 화면 평면 {(d + BehindMarginM) * 1000f:0.0}mm 만큼 침범 (dot={d * 1000f:+0.0;-0.0}mm)");
        GD.Print(ok ? "   [파지 규칙] PASS — 왼손 전체가 화면 평면 뒤에 있다"
                    : "   [파지 규칙] FAIL — 왼손이 화면 평면 앞으로 넘어왔다");
        return ok;
    }

    // 패드 앞면 법선에서 왼쪽으로 deg 만큼 돌아간 자리에서 본 모습.
    // 손가락 · 손바닥이 앞면 쪽으로 튀어나왔는지가 이 각도에서 가장 잘 보인다.
    private async System.Threading.Tasks.Task SideShot(AdminPad3D pad, float deg, string file)
    {
        var body = pad.GetNodeOrNull<Node3D>("PadBody");
        if (body == null) return;
        var b = body.GlobalTransform;
        var cam = new Camera3D { Fov = 48f };
        AddChild(cam);
        float r = Mathf.DegToRad(deg);
        Vector3 dir = b.Basis.Y.Normalized() * Mathf.Cos(r) - b.Basis.X.Normalized() * Mathf.Sin(r);
        cam.GlobalPosition = b.Origin + dir * 0.52f;
        cam.LookAt(b.Origin, -b.Basis.Z.Normalized());
        var main = GetViewport().GetCamera3D();
        cam.MakeCurrent();
        await Frames(3);
        Save(file);
        main?.MakeCurrent();
        cam.QueueFree();
        await Frames(2);
    }

    // 최종 확인 — 왼팔을 켠 화면과 끈 화면을 픽셀로 비교해, 달라진 픽셀 중 "패드 화면 위"인 것을 센다.
    // 꼭짓점 계측이 놓칠 수 있는 면(삼각형 안쪽)까지 잡는 검사다. 0 이면 손이 화면을 1픽셀도 가리지 않는다.
    private async System.Threading.Tasks.Task<string> CountHandPixelsOverScreen(AdminPad3D pad)
    {
        var cam = GetViewport().GetCamera3D();
        var skel = GetTree().Root.FindChild("Rig", true, false) as Skeleton3D;
        if (cam == null || skel == null) return "시점 픽셀 검사: 씬을 찾지 못함";

        // 글로우(블룸)는 화면 밖의 밝은 손에서 빛을 번지게 해 화면 픽셀까지 바꾼다 —
        // "가렸는가"만 보려면 잠시 끈다. CRT 셰이더의 TIME 노이즈 · 플리커 · 가로 왜곡도 멈춘다.
        var env = (GetTree().Root.FindChild("WorldEnvironment", true, false) as WorldEnvironment)?.Environment;
        bool glow = env?.GlowEnabled ?? false;
        if (env != null) env.GlowEnabled = false;
        var mat = pad.GetNodeOrNull<MeshInstance3D>("PadBody/Screen")?.MaterialOverride as ShaderMaterial;
        string[] live = { "noise_strength", "flicker_strength", "h_distortion" };
        var saved = new Dictionary<string, Variant>();
        foreach (string k in live)
        {
            if (mat == null) break;
            saved[k] = mat.GetShaderParameter(k);
            mat.SetShaderParameter(k, 0f);
        }

        await Frames(2);
        var withHand = GetViewport().GetTexture().GetImage();
        var hidden = new List<Node3D>();
        foreach (var n in skel.GetChildren())
            if (n is Node3D n3 && n3.Name.ToString().Contains("_L") && n3.Visible) { n3.Visible = false; hidden.Add(n3); }
        await Frames(3);
        var noHand = GetViewport().GetTexture().GetImage();
        foreach (var n in hidden) n.Visible = true;
        foreach (var (k, val) in saved) mat.SetShaderParameter(k, val);
        if (env != null) env.GlowEnabled = glow;
        await Frames(2);

        var size = withHand.GetSize();
        int onScreen = 0, changed = 0;
        float u0 = 9f, u1 = -9f, v0 = 9f, v1 = -9f;
        var mark = (Image)withHand.Duplicate();
        for (int y = 0; y < size.Y; y += 2)
        for (int x = 0; x < size.X; x += 2)
        {
            Color a = withHand.GetPixel(x, y), b = noHand.GetPixel(x, y);
            if (Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < 0.18f) continue;
            changed++;
            var p = new Vector2(x, y);
            if (!pad.TryProjectRay(cam.ProjectRayOrigin(p), cam.ProjectRayNormal(p), false, out var cp)) continue;
            onScreen++;
            mark.SetPixel(x, y, Colors.Red);
            var uv = cp / (Vector2)pad.TargetViewport.Size;
            u0 = Mathf.Min(u0, uv.X); u1 = Mathf.Max(u1, uv.X);
            v0 = Mathf.Min(v0, uv.Y); v1 = Mathf.Max(v1, uv.Y);
        }
        mark.SavePng(_dir + "/pose_diff.png");
        noHand.SavePng(_dir + "/pose_nohand.png");
        return $"시점 픽셀 검사: 손이 바뀐 픽셀 {changed * 4}개 중 패드 화면 위 {onScreen * 4}개 (0 이어야 한다)"
               + (onScreen > 0 ? $" · 가려진 자리 u {u0:0.000}~{u1:0.000} v {v0:0.000}~{v1:0.000}" : "");
    }

    // 왼손 메시가 패드 본체 · 화면과 겹치는 양(꼭짓점 단위, 패드 본체 로컬 좌표).
    private sealed class HandFit
    {
        public int Verts;
        public int Pierce;              // 본체 속으로 들어간 꼭짓점
        public float PierceMm;          // 가장 깊이 들어간 양(mm)
        public string PierceWho = "—";
        public int OverScreen;          // 화면 표시 영역을 앞에서 가린 꼭짓점
        public float ScreenIntrudeMm;   // 화면 왼쪽 변에서 안쪽으로 들어온 최대 깊이(mm)
        public string ScreenWho = "—";
        public int FrontFace;           // 앞면(베젤 포함) 위로 올라온 꼭짓점 — 엄지만 허용
        public string FrontWho = "—";
        public float ThumbEdgeMm;       // 엄지가 화면 왼쪽 변에서 얼마나 떨어져 있나(mm, +면 안전)
        public int EyeBlock;            // 플레이어 시점에서 화면 표시 영역을 실제로 가린 꼭짓점
        public string EyeWho = "—";
        public float EyeU0 = 9f, EyeU1 = -9f, EyeV0 = 9f, EyeV1 = -9f;   // 가려진 화면 범위(0~1)
        public float TxMin = 9e9f, TxMax = -9e9f, TyMin = 9e9f, TyMax = -9e9f;   // 엄지 범위(본체 로컬, mm)

        public bool Ok => Pierce == 0 && OverScreen == 0 && EyeBlock == 0;

        public string Line() =>
            $"침투 {Pierce}점({PierceMm:0.0}mm, {PierceWho}) · 화면가림 {OverScreen}점({ScreenIntrudeMm:0.0}mm, {ScreenWho})"
            + $" · 시점가림 {EyeBlock}점({EyeWho}"
            + (EyeBlock > 0 ? $", u {EyeU0:0.00}~{EyeU1:0.00} v {EyeV0:0.00}~{EyeV1:0.00}" : "") + ")"
            + $" · 앞면 {FrontFace}점({FrontWho}) · 엄지여유 {ThumbEdgeMm:0.0}mm"
            + $" · 엄지 x[{TxMin:0} ~ {TxMax:0}] y[{TyMin:0} ~ {TyMax:0}]mm · 꼭짓점 {Verts}";
    }

    private static string PartOf(string meshName) =>
        meshName.Contains("Thumb") ? "엄지"
        : meshName.Contains("Index") || meshName.Contains("Middle") || meshName.Contains("Ring") || meshName.Contains("Pinky") ? "손가락"
        : meshName.Contains("Hand") ? "손바닥"
        : meshName.Contains("Cuff") || meshName.Contains("Forearm") || meshName.Contains("UpperArm") ? "팔" : meshName;

    private HandFit MeasureHand(AdminPad3D pad)
    {
        var fit = new HandFit();
        var body = pad.GetNodeOrNull<Node3D>("PadBody");
        var screen = body?.GetNodeOrNull<Node3D>("Screen");
        var skel = GetTree().Root.FindChild("Rig", true, false) as Skeleton3D;
        if (body == null || screen == null || skel == null) { GD.PrintErr("계측: 패드/리그를 찾지 못함"); return fit; }

        Transform3D bodyInv = body.GlobalTransform.AffineInverse();
        Transform3D screenInv = screen.GlobalTransform.AffineInverse();
        Vector3 half = pad.BodySize * 0.5f;
        float thumbEdge = float.MaxValue;
        Vector3 eye = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Zero;

        foreach (var n in skel.GetChildren())
        {
            foreach (var m in n.GetChildren())
            {
                if (m is not MeshInstance3D mi || mi.Mesh == null) continue;
                string name = mi.Name.ToString();
                if (!name.Contains("_L")) continue;
                string part = PartOf(name);
                var world = mi.GlobalTransform;
                foreach (var v in mi.Mesh.GetFaces())
                {
                    Vector3 w = world * v;
                    Vector3 b = bodyInv * w;
                    fit.Verts++;

                    // 팔뚝은 패드 아래로 빠지므로 침투 · 앞면 판정에서 뺀다(시점 가림만 본다).
                    float dx = half.X - Mathf.Abs(b.X), dy = half.Y - Mathf.Abs(b.Y), dz = half.Z - Mathf.Abs(b.Z);
                    if (part != "팔" && dx > 0f && dy > 0f && dz > 0f)
                    {
                        fit.Pierce++;
                        float d = Mathf.Min(dx, Mathf.Min(dy, dz)) * 1000f;
                        if (d > fit.PierceMm) { fit.PierceMm = d; fit.PierceWho = part; }
                    }
                    if (part != "팔" && b.Y > half.Y && Mathf.Abs(b.X) <= half.X && Mathf.Abs(b.Z) <= half.Z)
                    {
                        fit.FrontFace++;
                        fit.FrontWho = part;
                    }

                    Vector3 s = screenInv * w;
                    bool onScreen = Mathf.Abs(s.X) <= 0.5f && Mathf.Abs(s.Y) <= 0.5f;
                    if (s.Z > 0f && onScreen)
                    {
                        fit.OverScreen++;
                        float into = (s.X + 0.5f) * pad.ScreenSize.X * 1000f;
                        if (into > fit.ScreenIntrudeMm) { fit.ScreenIntrudeMm = into; fit.ScreenWho = part; }
                    }
                    // 플레이어 시점 가림 — 눈에서 이 꼭짓점을 지나 쏜 선이 화면 표시 영역에 닿고,
                    // 그 닿는 곳이 꼭짓점보다 멀면(= 꼭짓점이 앞을 가로막으면) 화면이 가려진 것이다.
                    Vector3 ray = w - eye;
                    Vector3 lo = screenInv * eye, ld = screenInv.Basis * ray;
                    if (Mathf.Abs(ld.Z) > 1e-9f)
                    {
                        float t = -lo.Z / ld.Z;
                        Vector3 hitL = lo + ld * t;
                        if (t > 1.0005f && Mathf.Abs(hitL.X) <= 0.5f && Mathf.Abs(hitL.Y) <= 0.5f)
                        {
                            fit.EyeBlock++;
                            fit.EyeWho = part;
                            float hu = hitL.X + 0.5f, hv = 0.5f - hitL.Y;
                            fit.EyeU0 = Mathf.Min(fit.EyeU0, hu); fit.EyeU1 = Mathf.Max(fit.EyeU1, hu);
                            fit.EyeV0 = Mathf.Min(fit.EyeV0, hv); fit.EyeV1 = Mathf.Max(fit.EyeV1, hv);
                        }
                    }

                    if (part == "엄지")
                    {
                        fit.TxMin = Mathf.Min(fit.TxMin, b.X * 1000f); fit.TxMax = Mathf.Max(fit.TxMax, b.X * 1000f);
                        fit.TyMin = Mathf.Min(fit.TyMin, b.Y * 1000f); fit.TyMax = Mathf.Max(fit.TyMax, b.Y * 1000f);
                        // 화면 왼쪽 변까지 남긴 여유(앞쪽에 있는 점만 본다).
                        if (s.Z > -0.004f && Mathf.Abs(s.Y) <= 0.5f)
                            thumbEdge = Mathf.Min(thumbEdge, -(s.X + 0.5f) * pad.ScreenSize.X * 1000f);
                    }
                }
            }
        }
        fit.ThumbEdgeMm = thumbEdge == float.MaxValue ? 0f : thumbEdge;
        return fit;
    }

    // ── 그립 후보 비교 ───────────────────────────────────────────────────

    // 후보는 "패드 기준 손 자세"로 쓴다.
    //   PalmTilt : 손바닥이 뒷면에서 뒤로 눕는 각(크면 손가락이 패드에서 멀어진다)
    //   Yaw      : 손가락이 화면 위쪽(-Z)에서 오른쪽(+X)으로 도는 각
    //   Roll     손목을 손가락 축으로 비트는 각(음수 = 엄지 쪽이 패드에 붙고 새끼 쪽이 뒤로 빠진다)
    private readonly record struct Grip(string Name, float PalmTilt, float Yaw, float Roll, Vector3 Pos,
                                        float FingerCurl, float FingerStep, float ThumbOpp, float ThumbLift, float ThumbCurl);

    // 손바닥이 뒷면(+Y)을 보고 손가락이 -Z 로 가는 기준 자세 = X 로 (90 - PalmTilt)° 회전.
    // Roll 은 그 자세에서 손가락 축(그립 로컬 Y)으로 한 번 더 비튼다.
    private static Vector3 GripEuler(float palmTiltDeg, float yawDeg, float rollDeg)
    {
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg))
                    * new Basis(Vector3.Right, Mathf.DegToRad(90f - palmTiltDeg))
                    * new Basis(Vector3.Up, Mathf.DegToRad(rollDeg));
        return basis.GetEuler() * (180f / Mathf.Pi);
    }

    private async System.Threading.Tasks.Task CompareGrips(AdminPad3D pad)
    {
        await HoldPad(pad);
        var player = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
        if (player == null) { GD.PrintErr("PlayerCharacter 를 찾지 못함"); return; }

        var cands = new List<Grip>
        {
            // 「뒷면 받침 파지」 후보. 숫자를 바꿔 가며 돌리고 "파지 규칙 PASS" 를 고른다.
            // 손바닥 눕힘(PalmTilt)이 클수록 패드가 덜 끌려오지만 손목이 앞으로 들린다.
            // 그래서 눕힘을 키울 때마다 깊이(-Y)도 같이 준다. 컬 누적(Step)은 0.
            new("A", 14f, 0f, 0f, new(-0.101f, -0.050f, 0.092f), 6f, 0f, 40f, 0f, 20f),
            new("B", 17f, 0f, 0f, new(-0.101f, -0.056f, 0.092f), 6f, 0f, 40f, 0f, 20f),
            new("C", 20f, 0f, 0f, new(-0.101f, -0.058f, 0.092f), 6f, 0f, 40f, 0f, 20f),
            new("D", 14f, 0f, 0f, new(-0.101f, -0.050f, 0.100f), 6f, 0f, 40f, 0f, 20f),
        };

        foreach (var c in cands)
        {
            Apply(pad, player, c);
            await Regrip(pad);
            GD.Print($"[그립 {c.Name}] rot={pad.GripRotDeg.Round()} pos={pad.GripLocal}");
            Report(c.Name, pad);          // 팔이 그 자리까지 실제로 닿는지(손소켓↔그립)도 같이 본다
            CheckBehindScreen(pad);
            GD.Print("   " + await CountHandPixelsOverScreen(pad));
            Save($"grip_{c.Name}_front.png");
            await SideShot(pad, 45f, $"grip_{c.Name}_left45.png");
        }
    }

    private static void Apply(AdminPad3D pad, PlayerCharacter player, Grip c)
    {
        pad.GripLocal = c.Pos;
        pad.GripRotDeg = GripEuler(c.PalmTilt, c.Yaw, c.Roll);
        player.PadFingerCurl = c.FingerCurl;
        player.PadFingerCurlStep = c.FingerStep;
        player.PadThumbOpp = c.ThumbOpp;
        player.PadThumbLift = c.ThumbLift;
        player.PadThumbCurl = c.ThumbCurl;
    }

    // 내려놓기 · 집어 들기에 실제로 걸리는 시간(초).
    private async System.Threading.Tasks.Task ReportTiming(AdminPad3D pad)
    {
        ulong t0 = Time.GetTicksMsec();
        pad.Close();
        for (int i = 0; i < 900 && pad.IsOpen; i++) await Frame();
        ulong down = Time.GetTicksMsec() - t0;

        await Seconds(0.3);
        t0 = Time.GetTicksMsec();
        pad.Open();
        for (int i = 0; i < 900 && !pad.IsHeld; i++) await Frame();
        ulong up = Time.GetTicksMsec() - t0;
        await Seconds(0.9);
        GD.Print($"   [속도] 집어 들기 {up / 1000f:0.00}초 · 내려놓기 {down / 1000f:0.00}초 (LiftSeconds={pad.LiftSeconds:0.00})");
    }

    // 값을 바꾼 뒤에는 반드시 내려놨다가 다시 집어 든다.
    // 든 채로 포즈만 다시 잡으면(예전 RefreshHandPose) 팔 IK 가 게임과 다른 해로 수렴해서,
    // 계측은 통과하는데 실제 플레이 화면은 틀린 상황이 나온다(3차에서 실제로 그랬다).
    private async System.Threading.Tasks.Task Regrip(AdminPad3D pad)
    {
        pad.Close();
        for (int i = 0; i < 600 && pad.IsOpen; i++) await Frame();
        await Seconds(0.3);
        pad.Open();
        for (int i = 0; i < 600 && !pad.IsHeld; i++) await Frame();
        await Seconds(0.9);
    }

    // ── ⑤ 클릭 안정화 계측 ───────────────────────────────────────────────

    // 든 상태에서 화면 버튼을 5번 누르는 동안 패드 · 왼팔 · 카메라가 정말 가만히 있는가.
    private async System.Threading.Tasks.Task ClickStability(AdminPad3D pad)
    {
        await HoldPad(pad);
        var ctl = ControlRoom3DController.Instance;
        var cam = GetViewport().GetCamera3D();
        var skel = GetTree().Root.FindChild("Rig", true, false) as Skeleton3D;
        if (ctl == null || cam == null || skel == null) { GD.PrintErr("계측: 씬을 찾지 못함"); return; }

        // 눌러도 화면이 바뀌지 않는 버튼 — 단서 앱 도구줄의 「정렬」 토글.
        pad.View.OpenApp(PadView.Tab.Clues, fade: false);
        await Seconds(2.0);          // 들어 올리는 트윈 · 손 IK 가 완전히 멎은 뒤부터 잰다
        float vs = pad.TargetViewport.Size.X / PadView.Layout.X;
        Vector2 target = new Vector2(606f, 75f) * vs;
        Vector2 mouse = cam.UnprojectPosition(pad.ScreenPointWorld(target));

        var corners0 = Corners(pad, cam);
        var bones0 = LeftBones(skel);
        var cam0 = cam.GlobalTransform;
        var cornersP = corners0; var bonesP = bones0; var camP = cam0;
        Save("click_0_before.png");
        GD.Print($"[클릭 기준] 모서리 px = {string.Join(" ", corners0.Select(p => $"({p.X:0.0},{p.Y:0.0})"))}");

        float maxCorner = 0f, maxBonePos = 0f, maxBoneRot = 0f, maxCamPos = 0f, maxCamRot = 0f;
        for (int i = 1; i <= 5; i++)
        {
            ctl._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = mouse, GlobalPosition = mouse });
            await Frames(2);
            if (i == 1) Save("click_1_pressed.png");
            ctl._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = mouse, GlobalPosition = mouse });
            await Seconds(0.35);

            var c = Corners(pad, cam);
            var bones = LeftBones(skel);
            var cx = cam.GlobalTransform;

            var (dc, dp, dr, dcp, dcr) = Diff(c, bones, cx, cornersP, bonesP, camP);   // 직전 클릭 대비
            var (ec, ep, er, ecp, ecr) = Diff(c, bones, cx, corners0, bones0, cam0);   // 첫 기준 대비
            maxCorner = Mathf.Max(maxCorner, dc); maxBonePos = Mathf.Max(maxBonePos, dp); maxBoneRot = Mathf.Max(maxBoneRot, dr);
            maxCamPos = Mathf.Max(maxCamPos, dcp); maxCamRot = Mathf.Max(maxCamRot, dcr);
            cornersP = c; bonesP = bones; camP = cx;
            GD.Print($"[클릭 {i}] 직전 대비 모서리 {dc:0.0000}px · 왼팔뼈 {dp * 1000f:0.0000}mm / 회전Δ {dr:E2} · 카메라 {dcp * 1000f:0.0000}mm / 회전Δ {dcr:E2}"
                     + $"  ‖ 기준 대비 모서리 {ec:0.0000}px · 왼팔뼈 {ep * 1000f:0.0000}mm / 회전Δ {er:E2} · 카메라 {ecp * 1000f:0.0000}mm / 회전Δ {ecr:E2}");
        }
        Save("click_2_after.png");
        GD.Print($"[클릭 5회 최대 · 클릭 사이 변화] 모서리 {maxCorner:0.0000}px · 왼팔뼈 {maxBonePos * 1000f:0.0000}mm / 회전Δ {maxBoneRot:E2}"
                 + $" · 카메라 {maxCamPos * 1000f:0.0000}mm / 회전Δ {maxCamRot:E2}");
    }

    private static (float, float, float, float, float) Diff(
        Vector2[] c, List<(string Name, Transform3D X)> bones, Transform3D cam,
        Vector2[] c0, List<(string Name, Transform3D X)> bones0, Transform3D cam0)
    {
        float dc = 0f;
        for (int k = 0; k < c.Length; k++) dc = Mathf.Max(dc, (c[k] - c0[k]).Length());
        float dp = 0f, dr = 0f;
        foreach (var (name, x) in bones)
        {
            var b0 = bones0.First(t => t.Name == name).X;
            dp = Mathf.Max(dp, (x.Origin - b0.Origin).Length());
            dr = Mathf.Max(dr, QuatDelta(x, b0));
        }
        return (dc, dp, dr, (cam.Origin - cam0.Origin).Length(), QuatDelta(cam, cam0));
    }

    // 패드 화면 네 모서리의 화면 픽셀 좌표.
    private static Vector2[] Corners(AdminPad3D pad, Camera3D cam)
    {
        var vp = pad.TargetViewport.Size;
        var uv = new[] { Vector2.Zero, new Vector2(vp.X, 0), new Vector2(vp.X, vp.Y), new Vector2(0, vp.Y) };
        return uv.Select(u => cam.UnprojectPosition(pad.ScreenPointWorld(u))).ToArray();
    }

    // 두 자세의 회전 차이 — 쿼터니언 성분의 최대 절대차(무차원). 0 이면 완전히 같다.
    // 각도(acos)는 1 근처에서 부동소수 잡음이 0.05° 수준으로 증폭돼 "변화 없음"을 증명하지 못한다.
    private static float QuatDelta(Transform3D a, Transform3D b)
    {
        var qa = a.Basis.GetRotationQuaternion().Normalized();
        var qb = b.Basis.GetRotationQuaternion().Normalized();
        if (qa.Dot(qb) < 0f) qb = new Quaternion(-qb.X, -qb.Y, -qb.Z, -qb.W);
        return Mathf.Max(Mathf.Max(Mathf.Abs(qa.X - qb.X), Mathf.Abs(qa.Y - qb.Y)),
                         Mathf.Max(Mathf.Abs(qa.Z - qb.Z), Mathf.Abs(qa.W - qb.W)));
    }

    private static List<(string Name, Transform3D X)> LeftBones(Skeleton3D skel)
    {
        var list = new List<(string, Transform3D)>();
        for (int i = 0; i < skel.GetBoneCount(); i++)
        {
            string n = skel.GetBoneName(i);
            if (n.Contains("_L")) list.Add((n, skel.GetBoneGlobalPose(i)));
        }
        return list;
    }

    // ── 공통 ─────────────────────────────────────────────────────────────

    private void Report(string label, AdminPad3D pad)
    {
        var cam = GetViewport().GetCamera3D();
        var body = pad.GetNodeOrNull<Node3D>("PadBody");
        var grip = body?.GetNodeOrNull<Node3D>("GripPoint");
        var player = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
        string hand = "";
        if (player?.HandSocketL != null && grip != null && player.IsLeftArmActive)
        {
            var s = player.HandSocketL.GlobalTransform;
            float dist = s.Origin.DistanceTo(grip.GlobalPosition);
            float ang = Mathf.RadToDeg(s.Basis.Orthonormalized().GetRotationQuaternion()
                .AngleTo(grip.GlobalTransform.Basis.Orthonormalized().GetRotationQuaternion()));
            hand = $" 손소켓↔그립 {dist * 100f:0.0}cm / {ang:0}°";
        }
        string width = "";
        if (cam != null && body != null)
        {
            float w = GetViewport().GetVisibleRect().Size.X;
            var half = pad.BodySize * 0.5f;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                var p = body.GlobalTransform * new Vector3(half.X * sx, 0f, half.Z * sz);
                if (cam.IsPositionBehind(p)) continue;
                float x = cam.UnprojectPosition(p).X;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
            }
            if (maxX > minX)
            {
                float px = Mathf.Clamp(maxX, 0, w) - Mathf.Clamp(minX, 0, w);
                width = $" 화면가로 {px:0}px / {w:0}px = {px / w * 100f:0}%";
            }
            // 화면 안에서 패드가 얼마나 갸웃한가 — 본체 좌우 변 중점을 화면에 찍어 잰다.
            // 양수 = 오른쪽이 아래로(시계 방향).
            var lm = body.GlobalTransform * new Vector3(-half.X, 0f, 0f);
            var rm = body.GlobalTransform * new Vector3(half.X, 0f, 0f);
            if (!cam.IsPositionBehind(lm) && !cam.IsPositionBehind(rm))
            {
                Vector2 pl = cam.UnprojectPosition(lm), pr = cam.UnprojectPosition(rm);
                Vector2 d = pr - pl;
                width += $" 기울기 {Mathf.RadToDeg(Mathf.Atan2(d.Y, d.X)):+0.0;-0.0}°"
                       + $"(좌변중점 y={pl.Y:0} · 우변중점 y={pr.Y:0} — y 가 클수록 아래)";
            }
        }
        GD.Print($"[{label}] open={pad.IsOpen} held={pad.IsHeld} pause={AdminPad3D.PausesGame} screen={pad.IsScreenOn}"
                 + $" power={AdminPad3D.PadPowered}{width}{hand} 패드밀림 {pad.HoldDriftMeters * 100f:0.0}cm");
    }

    private void Save(string file)
    {
        var img = GetViewport().GetTexture().GetImage();
        img?.SavePng(_dir + "/" + file);
    }

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
