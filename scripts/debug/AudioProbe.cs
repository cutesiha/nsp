using Godot;
using NSP.Core;
using NSP.Ui;

namespace NSP.Debug;

// 헤드폰으로 직접 들어 보는 도구(지시서 §23 · §24).
//
//   godot --path . res://scenes/debug/AudioProbe.tscn
//
// 창 모드로 띄워 숫자키를 누르면 그 소리가 즉시 난다. 좌우가 실제로 구분되는지,
// 너무 큰지, 전화벨 · 경고음과 헷갈리는지를 귀로 비교하려고 만든 것이다.
// 대형 디버그 시스템이 아니다 — 목록 하나와 키 입력뿐이다.
public partial class AudioProbe : Node
{
    private sealed class Entry
    {
        public string Label = "";
        public string Key = "";
        public float Pan;
        public float Db = -20f;
        public bool Compare;   // 게임 정보음 — 공포음과 헷갈리지 않는지 견주는 용도
    }

    private static readonly Entry[] List =
    {
        new() { Label = "속삭임 — 오른쪽",      Key = "whisper_short",   Pan = 0.82f, Db = -26f },
        new() { Label = "속삭임 — 왼쪽",        Key = "whisper_short",   Pan = -0.82f, Db = -26f },
        new() { Label = "속삭임 — 긴 것(왼쪽)", Key = "whisper_long",    Pan = -0.72f, Db = -27f },
        new() { Label = "속삭임 — 부르는 억양", Key = "whisper_call",    Pan = 0.7f,  Db = -25f },
        new() { Label = "속삭임 — 거꾸로",      Key = "whisper_reverse", Pan = -0.7f, Db = -27f },
        new() { Label = "환풍구 — 기어가는 소리", Key = "duct_crawl",    Pan = -0.4f, Db = -24f },
        new() { Label = "환풍구 — 긁힘",        Key = "duct_scrape",     Pan = 0.4f,  Db = -26f },
        new() { Label = "문 — 노크",            Key = "door_knock_soft", Pan = 0f,    Db = -20f },
        new() { Label = "문 — 긁힘",            Key = "door_scratch",    Pan = 0.1f,  Db = -24f },
        new() { Label = "문 — 손잡이",          Key = "door_handle",     Pan = -0.1f, Db = -24f },
        new() { Label = "가까이 — 들이마심(오른쪽)", Key = "breath_close", Pan = 0.85f, Db = -23f },
        new() { Label = "가까이 — 옷 스침(왼쪽)",   Key = "cloth_rustle", Pan = -0.85f, Db = -25f },
        new() { Label = "관리자 — 날숨",        Key = "admin_exhale",    Pan = 0f,    Db = -22f },
        new() { Label = "관리자 — 숨 멎었다 들이마심", Key = "admin_gasp", Pan = 0f,  Db = -17f },
        new() { Label = "휴게실 — 웅성거림",    Key = "breakroom_murmur", Pan = 0f,   Db = -27f },
        new() { Label = "휴게실 — 의자 끌기",   Key = "chair_pull",      Pan = 0f,    Db = -22f },
        new() { Label = "휴게실 — 컵 내려놓기", Key = "cup_set",         Pan = 0f,    Db = -25f },
        // 아래 둘은 공포음이 아니다. 헷갈리지 않는지 바로 견주려고 같은 목록에 둔다(§13).
        new() { Label = "[게임음] 전화벨",      Key = "call_ring",       Pan = 0f,    Db = -8f, Compare = true },
        new() { Label = "[게임음] 경고",        Key = "alert_beep3",     Pan = 0f,    Db = -8f, Compare = true },
    };

    private Label _log;

    public override void _Ready()
    {
        // 진단 모드 — 실제 근무를 띄우고 환경음이 **왜 안 들리는지** 숫자로 적는다.
        //   godot --path . res://scenes/debug/AudioProbe.tscn -- diag
        var args = OS.GetCmdlineUserArgs();
        if (args.Length > 0 && args[0] == "diag") { _ = Diagnose(); return; }

        var layer = new CanvasLayer();
        AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.03f, 0.04f, 0.05f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        box.Position = new Vector2(40f, 28f);
        box.AddThemeConstantOverride("separation", 4);
        layer.AddChild(box);

        Add(box, "─── 공포 · 스토리 사운드 확인 (헤드폰 권장) ───", 26, new Color(0.55f, 0.95f, 1f));
        Add(box, "숫자 · 알파벳 키를 누르면 그 소리가 납니다.", 18, new Color(0.6f, 0.7f, 0.74f));
        Add(box, "", 14, Colors.White);

        for (int i = 0; i < List.Length; i++)
        {
            var e = List[i];
            string side = Mathf.Abs(e.Pan) < 0.1f ? "중앙" : e.Pan > 0 ? $"오른쪽 {e.Pan:0.00}" : $"왼쪽 {e.Pan:0.00}";
            var color = e.Compare ? new Color(1f, 0.8f, 0.36f) : new Color(0.86f, 0.92f, 0.94f);
            Add(box, $"  [{KeyLabel(i)}]  {e.Label,-28}  {side,-14}  {e.Db:0}dB", 19, color);
        }

        Add(box, "", 14, Colors.White);
        Add(box, "  [,]  스토리 전환 — 눈 감기 · 날숨 · 생활음 · 눈 뜨기", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "  [.]  BGM 더킹 한 번 (-4dB)", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "  [/]  시설 상시음 침묵 1.2초 (§18)", 19, new Color(0.7f, 0.95f, 0.8f));
        Add(box, "", 14, Colors.White);
        _log = Add(box, "준비됨.", 20, new Color(1f, 0.9f, 0.5f));
    }

    private static string KeyLabel(int i) => i < 9 ? (i + 1).ToString()
        : i == 9 ? "0" : ((char)('A' + i - 10)).ToString();

    private static Label Add(Node parent, string text, int size, Color color)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        parent.AddChild(l);
        return l;
    }

    public override void _Input(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true, Echo: false } k) return;

        int idx = IndexOf(k.Keycode);
        if (idx >= 0 && idx < List.Length)
        {
            var e = List[idx];
            if (e.Compare) Sfx.Instance?.Play(e.Key, e.Db);
            else Sfx.Instance?.PlayHorror(e.Key, e.Db, e.Pan);
            _log.Text = $"▶ {e.Label}  ({e.Key} · pan {e.Pan:0.00} · {e.Db:0}dB)";
            return;
        }

        switch (k.Keycode)
        {
            case Key.Comma:
                _log.Text = "▶ 스토리 전환 (모니터2 가 없는 씬이라 소리와 암전만)";
                _ = NSP.View.StoryTransition.Enter(this);
                break;
            case Key.Period:
                Sfx.Instance?.DuckMusic(-4f, 0.7f);
                _log.Text = "▶ BGM 더킹 -4dB";
                break;
            case Key.Slash:
                NSP.View.ControlRoomAtmosphere.Instance?.Hush(1.2f);
                _log.Text = NSP.View.ControlRoomAtmosphere.Instance == null
                    ? "시설 환경음이 없는 씬입니다(중앙제어실에서만 들립니다)."
                    : "▶ 상시음 침묵 1.2초";
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
    }

    // ── 진단 : 근무 중 환경음이 실제로 귀에 닿는가 ──────────────────────
    //
    // "안 들린다" 를 눈으로 확인하려면 셋을 같이 봐야 한다.
    //   ① 플레이어가 돌고 있는가(Playing) · 루프인가
    //   ② 설정된 볼륨(VolumeDb)이 목표까지 올라왔는가
    //   ③ **듣는 자리(카메라)에서 얼마나 떨어져 있고, 그 거리에서 몇 dB 가 되는가**
    // 3D 소리는 ③ 때문에 조용해지는 경우가 대부분인데 코드만 봐서는 안 보인다.
    private async System.Threading.Tasks.Task Diagnose()
    {
        typeof(NSP.View.ShiftFlowController)
            .GetField("_skipToDay1Pending", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.SetValue(null, true);
        AddChild(GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate());

        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != NSP.Data.GamePhase.Schedule; i++) await Frame();
        await Frames(60);

        var sim = NSP.Facility.FacilitySimulation.Instance;
        var map = NSP.Ui.ScheduleMapView.Instance;
        var ctl = NSP.View.ControlRoom3DController.Instance;
        if (sim == null || map == null || ctl == null) { GD.PrintErr("씬이 뜨지 않았다"); GetTree().Quit(1); return; }

        var roster = new System.Collections.Generic.List<string>(sim.GetActiveEmployeeIds());
        string[] rooms = { "core_room", "core_room", "power_room", "maintenance_room" };
        for (int i = 0; i < rooms.Length && i < roster.Count; i++) sim.AssignToRoom(roster[i], rooms[i]);
        await Frames(5);

        var at = map.GetGlobalTransformWithCanvas() * map.StartButtonRect.GetCenter();
        var vp = ctl.ScheduleMapViewport;
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at }, true);
        await Frame();
        vp.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at }, true);
        for (int i = 0; i < 1200 && GameState.Instance?.CurrentPhase != NSP.Data.GamePhase.Live; i++) await Frame();
        // 볼륨이 목표까지 올라올 시간을 준다(초당 24dB 로 기어오른다).
        await Seconds(6.0);

        GD.Print("\n################ 근무 중 환경음 진단 ################");
        GD.Print($"단계={GameState.Instance?.CurrentPhase}  전력={GameState.Instance?.PowerCapacity}");
        foreach (string bus in new[] { "Master", NSP.Core.GameSettings.BusBgm,
                                       NSP.Core.GameSettings.BusSfx, NSP.Core.GameSettings.BusAmbience })
        {
            int idx = AudioServer.GetBusIndex(bus);
            GD.Print(idx < 0
                ? $"  버스 {bus,-7} 없음(!)"
                : $"  버스 {bus,-7} {AudioServer.GetBusVolumeDb(idx),6:0.0} dB  음소거={AudioServer.IsBusMute(idx)}");
        }

        var cam = GetViewport().GetCamera3D();
        GD.Print($"  듣는 자리(카메라) {(cam == null ? "없음(!)" : cam.GlobalPosition.ToString())}");
        GD.Print($"\n{"소리",-22}{"재생",5}{"루프",5}{"볼륨",8}{"단위",6}{"최대",6}{"거리",7}{"귀에 닿는 값",14}");

        // 환경음 플레이어는 소리가 나는 **물건 밑**에 붙는다(환풍구 · 벽 · 모니터).
        // Atmosphere 노드 밑만 보면 하나도 안 보이므로 씬 전체를 훑는다.
        if (NSP.View.ControlRoomAtmosphere.Instance == null)
        { GD.PrintErr("ControlRoomAtmosphere 가 씬에 없다"); GetTree().Quit(1); return; }
        Dump(GetTree().Root, cam);

        // ── 단계별 베드 음량 ────────────────────────────────────────────
        // 기계음이 늘 송풍보다 커야 하고, 실시간 근무에서 제일 커야 한다.
        GD.Print("\n[단계별 시설 베드]");
        await ReportBed("스토리/지금");
        // DAY 시작 스토리는 플레이어가 넘겨야 끝난다 — 검사에서는 바로 걷고 재 본다.
        // 그 DAY 의 비트가 여러 개 줄 서 있어서, 하나를 걷으면 다음 비트가 바로 시작한다.
        // 큐가 빌 때까지 계속 걷어 낸다.
        for (int i = 0; i < 600; i++)
        {
            NSP.View.StoryCutinDirector.Instance?.Abort();
            await Frame();
        }
        await Seconds(5.0);
        await ReportBed("실시간 근무");

        // 정전 — 기계가 멎으면 환경음도 내려가야 한다.
        GameState.Instance?.TriggerPowerAccident(99);   // 정전(용량 0)
        await Seconds(7.0);
        await ReportBed("정전");
        GameState.Instance?.RepairPowerAccident();
        await Seconds(7.0);
        await ReportBed("전력 복구");

        GD.Print("####################################################");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task ReportBed(string label)
    {
        await Frame();
        var s = NSP.Core.Sfx.Instance;
        GD.Print($"   {label,-12} 기계 {s?.BedMachineDb,7:0.0} dB   송풍 {s?.BedFanDb,7:0.0} dB   " +
                 $"돌고 있음={(s?.BedPlaying == true ? "O" : "X")}   " +
                 $"[단계={GameState.Instance?.CurrentPhase} 전력={GameState.Instance?.PowerCapacity} " +
                 $"컷인멈춤={NSP.View.StoryCutinDirector.PausesGameplay} " +
                 $"전환={NSP.View.StoryTransition.Active} " +
                 $"가림={NSP.View.ControlRoom3DController.WorldCovered}]");
    }

    private static void Dump(Node n, Camera3D cam)
    {
        foreach (var c in n.GetChildren())
        {
            if (c is AudioStreamPlayer3D p3)
            {
                float dist = cam == null ? -1f : cam.GlobalPosition.DistanceTo(p3.GlobalPosition);
                // 역거리 감쇠에서 실제로 귀에 닿는 값. 최대 거리를 넘으면 아예 안 들린다.
                float heard = dist < 0f ? 0f
                    : dist > p3.MaxDistance && p3.MaxDistance > 0f ? float.NegativeInfinity
                    : p3.VolumeDb + 20f * Mathf.Log(Mathf.Min(1f, p3.UnitSize / Mathf.Max(0.01f, dist))) / Mathf.Log(10f);
                GD.Print($"{Label(p3),-22}{(p3.Playing ? "O" : "X"),5}{(Looping(p3.Stream) ? "O" : "X"),5}" +
                         $"{p3.VolumeDb,8:0.0}{p3.UnitSize,6:0.0}{p3.MaxDistance,6:0.0}{dist,7:0.00}" +
                         $"{(float.IsNegativeInfinity(heard) ? "   거리밖(무음)" : $"{heard,10:0.0} dB"),14}");
            }
            else if (c is AudioStreamPlayer p2 && p2.Stream != null)
            {
                GD.Print($"{Label(p2),-22}{(p2.Playing ? "O" : "X"),5}{(Looping(p2.Stream) ? "O" : "X"),5}" +
                         $"{p2.VolumeDb,8:0.0}{"-",6}{"-",6}{"2D",7}{$"{p2.VolumeDb,10:0.0} dB",14}");
            }
            Dump(c, cam);
        }
    }

    private static string Label(Node p)
    {
        string path = (p as AudioStreamPlayer3D)?.Stream?.ResourcePath
                      ?? (p as AudioStreamPlayer)?.Stream?.ResourcePath ?? "";
        string file = path.Length > 0 ? path[(path.LastIndexOf('/') + 1)..] : "(생성음)";
        return $"{p.GetParent()?.Name}/{file}";
    }

    private static bool Looping(AudioStream s) => s switch
    {
        AudioStreamWav w => w.LoopMode != AudioStreamWav.LoopModeEnum.Disabled,
        AudioStreamOggVorbis o => o.Loop,
        AudioStreamMP3 m => m.Loop,
        _ => false,
    };

    private async System.Threading.Tasks.Task Frame()
        => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private async System.Threading.Tasks.Task Seconds(double s)
        => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

    private static int IndexOf(Key code)
    {
        if (code >= Key.Key1 && code <= Key.Key9) return (int)(code - Key.Key1);
        if (code == Key.Key0) return 9;
        if (code >= Key.A && code <= Key.Z) return 10 + (int)(code - Key.A);
        return -1;
    }
}
