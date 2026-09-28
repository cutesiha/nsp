using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// 관리자 패드 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/AdminPadShot.tscn -- <저장 폴더> [그립 후보 비교: grips]
//
// DAY1 근무를 띄우고 Tab 과 같은 경로로 패드를 꺼내
//   거치 상태 · 드는 중 · 든 상태(플레이어 시점) · 탭 화면 · 전력 패널(PAD 명판) ·
//   전원 차단(글리치 → 내려놓기) · 전원 복구(부팅) 를 PNG 로 남긴다.
// 그때마다 패드 상태 · 화면 가로 점유율 · 손바닥과 그립 마커의 거리를 출력한다.
public partial class AdminPadShot : Node
{
    private string _dir = "";

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        _dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        bool grips = args.Contains("grips");

        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 900 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        await Frames(30);

        var sim = FacilitySimulation.Instance;
        var map = ScheduleMapView.Instance;
        var ctl = ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); GetTree().Quit(); return; }

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
        if (pad == null) { GD.PrintErr("AdminPad3D 가 씬에 없다"); GetTree().Quit(); return; }

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

        if (grips)
        {
            await CompareGrips(pad);
            GetTree().Quit();
            return;
        }

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

        GD.Print("saved → " + _dir);
        GetTree().Quit();
    }

    // 그립 후보 비교 — 각 후보마다 플레이어 시점 + 패드 왼쪽 옆에서 본 사진.
    //   손바닥 기준 축(패드 로컬): palm = 손바닥이 향하는 쪽, fingers = 손가락이 뻗는 쪽.
    private async System.Threading.Tasks.Task CompareGrips(AdminPad3D pad)
    {
        var player = GetTree().Root.FindChild("PlayerCharacter", true, false) as PlayerCharacter;
        // 손바닥은 왼쪽 변(+X)을 향한 채, 손가락 방향을 뒤(-Y)에서 위(-Z)로 θ 만큼 돌린다 — 엄지 쪽이 앞으로 온다.
        static Vector3 Fingers(float deg) => new(0f, -Mathf.Cos(Mathf.DegToRad(deg)), -Mathf.Sin(Mathf.DegToRad(deg)));
        var cands = new (string Name, Vector3 Palm, Vector3 Fingers, Vector3 Pos, float Curl)[]
        {
            ("web_th35", Vector3.Right, Fingers(35), new(-0.168f, -0.012f, 0.03f), 32f),
            ("web_th55", Vector3.Right, Fingers(55), new(-0.168f, -0.012f, 0.03f), 32f),
            ("web_th75", Vector3.Right, Fingers(75), new(-0.168f, -0.014f, 0.03f), 32f),
            ("web_th55_c45", Vector3.Right, Fingers(55), new(-0.168f, -0.014f, 0.03f), 45f),
        };
        var side = new Camera3D { Fov = 50f };
        AddChild(side);
        var main = GetViewport().GetCamera3D();
        for (int i = 0; i < cands.Length; i++)
        {
            var c = cands[i];
            // 손 축 → 기저: 손 -Z = palm, 손 -Y = fingers.
            Vector3 z = -c.Palm.Normalized();
            Vector3 y = -(c.Fingers - c.Fingers.Dot(z) * z).Normalized();
            Vector3 x = y.Cross(z).Normalized();
            var basis = new Basis(x, y, z);
            pad.GripLocal = c.Pos;
            pad.GripRotDeg = basis.GetEuler() * (180f / Mathf.Pi);
            if (player != null) player.PadFingerCurl = c.Curl;
            pad.RefreshHandPose();
            await Seconds(0.8);
            Report($"그립 {c.Name} rot={pad.GripRotDeg}", pad);
            Save($"grip_{i}_{c.Name}.png");

            // 패드 왼쪽 앞에서 본 모습 — 손가락이 앞면에 있는지 뒷면에 있는지 보인다.
            var body = pad.GetNodeOrNull<Node3D>("PadBody");
            if (body == null) continue;
            var b = body.GlobalTransform;
            // 패드 윗변 위에서 내려다본다 — 사진 위쪽 = 화면(앞면), 아래쪽 = 뒷면.
            Vector3 left = b.Origin - b.Basis.Z.Normalized() * 0.40f - b.Basis.X.Normalized() * 0.06f;
            side.GlobalPosition = left;
            side.LookAt(b.Origin - b.Basis.X.Normalized() * 0.08f, b.Basis.Y.Normalized());
            side.MakeCurrent();
            await Frames(3);
            Save($"grip_{i}_{c.Name}_side.png");
            main?.MakeCurrent();
            await Frames(2);
        }
    }

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
            if (maxX > minX) width = $" 화면가로 {(Mathf.Clamp(maxX, 0, w) - Mathf.Clamp(minX, 0, w)) / w * 100f:0}%";
        }
        GD.Print($"[{label}] open={pad.IsOpen} held={pad.IsHeld} pause={AdminPad3D.PausesGame} screen={pad.IsScreenOn}"
                 + $" power={AdminPad3D.PadPowered}{width}{hand}");
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
