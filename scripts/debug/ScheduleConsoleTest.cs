using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;
using NSP.View;

namespace NSP.Debug;

// Phase 0 — 근무 배치 콘솔(ScheduleMapView / ScheduleStaffView) · 환경 설정 홀로그램 창 검증.
//
//   godot --headless --path . res://scenes/debug/ScheduleConsoleTest.tscn
//
// 실제 입력 이벤트(누름 → 끌기 → 뗌)를 뷰포트에 밀어 넣어 배치가 FacilitySimulation 에
// 종이 배치표와 똑같이 반영되는지 본다. 게임 흐름에서는 로드되지 않는다.
public partial class ScheduleConsoleTest : Node
{
    private FacilitySimulation _sim;
    private SubViewport _vp;
    private ScheduleMapView _map;
    private int _pass, _fail;
    private int _startPressed;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        _ = RunAll();
    }

    private async System.Threading.Tasks.Task RunAll()
    {
        GD.Print("################ Phase 0 — 배치 콘솔 · 설정 창 검증 ################");
        NewGame(1);

        _vp = new SubViewport
        {
            Size = new Vector2I(800, 600), HandleInputLocally = true, GuiDisableInput = false, Disable3D = true,
        };
        AddChild(_vp);
        _map = new ScheduleMapView();
        _vp.AddChild(_map);
        _vp.AddChild(new ScheduleStaffView());
        _map.StartPressed += () => _startPressed++;
        await Frames(3);

        Rules();
        await Drag();
        await ClickAssign();
        await RightClick();
        await Refused();
        await StartButton();
        await AllSix();
        await Settings();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 규칙 ─────────────────────────────────────────────────────────────
    private void Rules()
    {
        Head("A", "배치 대상 작업실 = 종이 배치표와 같은 조건");
        var expected = _sim.GetRoomIds().Where(id =>
        {
            var d = _sim.GetRoomDef(id);
            return d != null && !d.IsRestricted && _sim.IsRoomActive(id) && _sim.GetRoomTasksInPriorityOrder(id).Count > 0;
        }).OrderBy(x => x).ToList();
        var got = _sim.GetRoomIds().Where(id => ScheduleMapView.IsAssignable(_sim, id)).OrderBy(x => x).ToList();
        GD.Print($"   DAY1 배치 가능: {string.Join(", ", got)}");
        Check(expected.SequenceEqual(got), "배치 가능 작업실 목록이 같다");
        Check(!got.Contains("vent_room") && !got.Contains("medical_room"), "DAY1 잠긴 방(환기실·의무실)은 배치 불가");
        Check(!got.Contains("central_office") && !got.Contains("isolation_room"), "제한 구역은 배치 불가");

        _map.ComputeLayout(_sim);
        bool allOnMap = _sim.GetRoomIds().Where(id => _sim.GetRoomDef(id)?.MapPosition != Vector2.Zero)
            .All(id => _map.CellOf(id).Size != Vector2.Zero);
        Check(allOnMap, "MapPosition 이 있는 방은 모두 지도에 칸이 있다(잠긴 방 포함)");
        bool inside = _sim.GetRoomIds().Select(_map.CellOf).Where(r => r.Size != Vector2.Zero)
            .All(r => r.Position.X >= 0 && r.Position.Y >= 90 && r.End.X <= 580 && r.End.Y <= 500);
        Check(inside, "방 칸이 지도 영역 안에 있다");
        var cells = _sim.GetRoomIds().Select(_map.CellOf).Where(r => r.Size != Vector2.Zero).ToList();
        bool overlap = false;
        for (int i = 0; i < cells.Count; i++)
            for (int j = i + 1; j < cells.Count; j++)
                if (cells[i].Intersects(cells[j])) overlap = true;
        Check(!overlap, "방 칸끼리 겹치지 않는다");
        Check(_map.RelationSlotOf("core_room").Size != Vector2.Zero, "Phase 1 관계 아이콘 자리가 방 칸 안에 비어 있다");
    }

    // ── 끌어다 놓기 ──────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Drag()
    {
        Head("B", "끌어다 놓기 — 대기 인원 → 정비실, 정비실 → 대기 인원(해제)");
        await DragFromTo(_map.RosterCardOf("rabbit").GetCenter(), _map.CellOf("maintenance_room").GetCenter());
        Check(Assigned("rabbit") == "maintenance_room", $"토끼 → 정비실 ({Assigned("rabbit")})");

        await Frames(1);
        var chip = _map.ChipOf("rabbit");
        Check(chip.Size != Vector2.Zero && _map.CellOf("maintenance_room").Encloses(chip), "정비실 칸 안에 토끼 칩이 생긴다");
        await DragFromTo(chip.GetCenter(), _map.RosterArea.GetCenter());
        Check(Assigned("rabbit") == "", "칩을 대기 인원으로 끌면 배치 해제");

        await DragFromTo(_map.RosterCardOf("cat").GetCenter(), _map.CellOf("storage_room").GetCenter());
        await Frames(1);
        await DragFromTo(_map.ChipOf("cat").GetCenter(), _map.CellOf("core_room").GetCenter());
        Check(Assigned("cat") == "core_room", "방에서 방으로 바로 옮긴다(저장고 → 코어실)");
        Check(AssignedTo("storage_room").Count == 0, "옮긴 뒤 이전 방에는 남지 않는다");
    }

    // ── 클릭으로 배치 ─────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task ClickAssign()
    {
        Head("C", "클릭 배치 — 직원 선택 → 작업실 클릭");
        await Click(_map.RosterCardOf("dog").GetCenter());
        Check(_map.SelectedEmployeeId == "dog" && _map.FocusEmployeeId == "dog", "강아지 선택 + 오른쪽 모니터 대상");
        await Click(_map.CellOf("guard_room").GetCenter());
        Check(Assigned("dog") == "guard_room", $"선택한 뒤 경비실 클릭 → 배치 ({Assigned("dog")})");
        Check(_map.SelectedEmployeeId == "" && _map.FocusRoomId == "guard_room", "배치 후 선택이 풀리고 경비실 정보가 뜬다");

        await Click(_map.CellOf("power_room").GetCenter());
        Check(_map.FocusRoomId == "power_room" && Assigned("dog") == "guard_room", "선택 없이 작업실 클릭 = 정보만(배치 변화 없음)");
    }

    private async System.Threading.Tasks.Task RightClick()
    {
        Head("D", "우클릭 — 배치 해제");
        await Frames(1);
        await Click(_map.ChipOf("dog").GetCenter(), MouseButton.Right);
        Check(Assigned("dog") == "", "강아지 칩 우클릭 → 해제");
    }

    // ── 놓을 수 없는 곳 ───────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Refused()
    {
        Head("E", "잠긴 방 · 제한 구역에는 놓이지 않는다");
        await DragFromTo(_map.RosterCardOf("fox").GetCenter(), _map.CellOf("vent_room").GetCenter());
        Check(Assigned("fox") == "", "환기실(DAY1 잠김)에 놓아도 배치 안 됨");
        await DragFromTo(_map.RosterCardOf("fox").GetCenter(), _map.CellOf("central_office").GetCenter());
        Check(Assigned("fox") == "", "중앙 제어실(관리자)에 놓아도 배치 안 됨");
        await DragFromTo(_map.RosterCardOf("fox").GetCenter(), new Vector2(300f, 560f));
        Check(Assigned("fox") == "", "빈 곳에 놓으면 그대로");
    }

    // ── 근무 시작 ────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task StartButton()
    {
        Head("F", "근무 시작 — 코어실에 1명 이상일 때만");
        await DragFromTo(_map.ChipOf("cat").GetCenter(), _map.RosterArea.GetCenter());
        await Frames(2);
        Check(!_map.StartEnabled, "코어실이 비면 근무 시작 비활성");
        await Click(_map.StartButtonRect.GetCenter());
        Check(_startPressed == 0, "비활성일 때 눌러도 시작하지 않는다");

        await DragFromTo(_map.RosterCardOf("wolf").GetCenter(), _map.CellOf("core_room").GetCenter());
        await Frames(2);
        Check(_map.StartEnabled, "코어실에 늑대 배치 → 근무 시작 활성");
        await Click(_map.StartButtonRect.GetCenter());
        Check(_startPressed == 1, "근무 시작 버튼 → StartPressed 한 번");
    }

    private async System.Threading.Tasks.Task AllSix()
    {
        Head("G", "6명 전원 배치");
        var plan = new Dictionary<string, string>
        {
            ["rabbit"] = "maintenance_room", ["cat"] = "power_room", ["fox"] = "storage_room",
            ["sheep"] = "storage_room", ["wolf"] = "core_room", ["dog"] = "core_room",
        };
        foreach (var (emp, room) in plan)
        {
            await Frames(1);
            await DragFromTo(_map.RosterCardOf(emp).GetCenter(), _map.CellOf(room).GetCenter());
        }
        var roster = _sim.GetActiveEmployeeIds();
        Check(roster.Count == 6 && roster.All(id => Assigned(id) == plan[id]),
            "6명 모두 지정한 방에 배치됨 — " + string.Join(", ", roster.Select(id => $"{id}:{Assigned(id)}")));
    }

    // ── 환경 설정 ────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Settings()
    {
        Head("H", "환경 설정 — 홀로그램 창 · 값 그대로");
        float master = GameSettings.MasterVolume, bgm = GameSettings.BgmVolume, sfx = GameSettings.SfxVolume;
        bool fs = GameSettings.Fullscreen;
        var q = GameSettings.GraphicsQuality;

        var panel = new SettingsPanel();
        AddChild(panel);
        await Frames(1);
        panel.Open();
        await Frames(1);
        Check(panel.Visible, "설정 창이 열린다");
        var nodes = All(panel).ToList();
        Check(!nodes.Any(n => n is DocumentPaperTexture), "종이 질감(DocumentPaperTexture)이 없다");
        Check(nodes.Any(n => n is HologramFrame), "홀로그램 프레임(로그 창과 같은 HologramFrame)이 있다");
        Check(nodes.OfType<HSlider>().Count() == 3, "음량 슬라이더 3개(MASTER · BGM · SFX) 그대로");
        Check(nodes.OfType<Button>().Any(b => b.Text == "닫기") && nodes.OfType<Button>().Any(b => b.Text == "✕"),
            "하단 닫기 + 오른쪽 위 ✕");
        panel.Close();
        Check(!panel.Visible, "닫기");
        Check(Mathf.IsEqualApprox(master, GameSettings.MasterVolume) && Mathf.IsEqualApprox(bgm, GameSettings.BgmVolume)
              && Mathf.IsEqualApprox(sfx, GameSettings.SfxVolume) && fs == GameSettings.Fullscreen
              && q == GameSettings.GraphicsQuality, "열고 닫아도 설정 값이 바뀌지 않는다");
        panel.QueueFree();
    }

    // ── 도우미 ───────────────────────────────────────────────────────────
    private void NewGame(int day)
    {
        EventLog.Instance?.ClearAll();
        GameState.Instance.ResetRun(day);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        _sim.RollDailyMoods();
    }

    private string Assigned(string id) => _sim.GetEmployeeState(id)?.AssignedRoomId ?? "";
    private List<string> AssignedTo(string room) => ScheduleMapView.AssignedTo(_sim, room);

    private async System.Threading.Tasks.Task DragFromTo(Vector2 from, Vector2 to)
    {
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = from, GlobalPosition = from });
        await Frames(1);
        for (int i = 1; i <= 6; i++)
        {
            var p = from.Lerp(to, i / 6f);
            Push(new InputEventMouseMotion { Position = p, GlobalPosition = p, ButtonMask = MouseButtonMask.Left });
        }
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = to, GlobalPosition = to });
        await Frames(2);
    }

    private async System.Threading.Tasks.Task Click(Vector2 at, MouseButton button = MouseButton.Left)
    {
        Push(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at, GlobalPosition = at });
        await Frames(1);
        Push(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at, GlobalPosition = at });
        await Frames(2);
    }

    private void Push(InputEvent e) => _vp.PushInput(e, true);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static IEnumerable<Node> All(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            yield return c;
            foreach (var d in All(c)) yield return d;
        }
    }

    private static void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
