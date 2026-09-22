using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 관계 시스템 Phase 2 — CCTV 로 엿듣는 같은 방 두 사람의 대화 검증.
//
//   godot --headless --path . res://scenes/debug/RelationshipPhase2Test.tscn
//
// A~D 는 OverheardDialogue(고르기 · 상황 판정)와 CctvOverheardCaption(자막 진행)을 직접 돌린다.
// E 는 실제 메인 씬(개발용 DAY1 바로 시작)으로 근무에 들어가 오른쪽 CRT 의 CCTV 에 자막이 뜨는지 본다.
public partial class RelationshipPhase2Test : Node
{
    private const float Step = 0.25f;
    private FacilitySimulation _sim;
    private int _pass, _fail;

    public override void _Ready()
    {
        _sim = FacilitySimulation.Instance;
        if (_sim == null) { GD.PrintErr("FacilitySimulation 없음"); return; }
        _ = RunAll();
    }

    private async System.Threading.Tasks.Task RunAll()
    {
        GD.Print("################ 관계 시스템 Phase 2 검증 ################");
        Data();
        Picking();
        Situations();
        await Caption();
        await MainScene();
        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── 데이터 ───────────────────────────────────────────────────────────
    private void Data()
    {
        Head("A", "대사 테이블 (config OverheardLinesPath)");
        Check(OverheardDialogue.Table != null && OverheardDialogue.Count > 0,
            $"overheard_lines.tres 로드 — {OverheardDialogue.Count}줄");
        foreach (var key in new[] { "Close", "Friendly", "Neutral", "Uneasy" })
            foreach (var sit in new[] { OverheardSituation.Normal, OverheardSituation.Incident, OverheardSituation.Sabotage })
            {
                int n = OverheardDialogue.CountOf(key, sit);
                if (n < 2) Check(false, $"{key} × {sit} 줄이 2개 이상 — {n}");
            }
        Check(true, "밴드 4 × 상황 3 칸마다 2줄 이상");

        var bad = OverheardDialogue.AllTemplates()
            .SelectMany(t => new[] { t.A, t.B })
            .Select(KoreanParticle.Lint).Where(s => s.Length > 0).ToList();
        Check(bad.Count == 0, "변수 뒤 조사는 슬래시 표기 — " + (bad.FirstOrDefault() ?? "문제 없음"));
        var ids = new[] { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };
        var names = ids.Select(id => _sim.GetEmployeeDef(id)?.Codename ?? id).ToList();
        var leak = OverheardDialogue.AllTemplates().Where(t => t.Situation == OverheardSituation.Sabotage)
            .SelectMany(t => new[] { t.A, t.B }).Where(l => names.Any(l.Contains)).ToList();
        Check(leak.Count == 0, "방해 정황 대사에 특정 직원 이름이 박혀 있지 않다(방해자를 가리키지 않음)");
    }

    // ── 고르기 ───────────────────────────────────────────────────────────
    private void Picking()
    {
        Head("B", "(밴드, 상황) → 대화");
        Expect("cat", "fox", OverheardSituation.Normal, "Uneasy", "동실 거부 쌍(근무 중 재배치)은 불편 줄");
        Expect("fox", "wolf", OverheardSituation.Sabotage, "Uneasy", "불편 × 방해 정황 = 서로 의심");
        Expect("dog", "fox", OverheardSituation.Incident, "Neutral", "중립 × 사고 직후 = 짧은 확인");
        Expect("rabbit", "dog", OverheardSituation.Sabotage, "Friendly", "우호 × 방해 정황 = 수상함 공유");

        // 밀접(고양이–강아지)은 Close 줄 또는 연인 전용 줄.
        var keys = Many("cat", "dog", OverheardSituation.Normal).Select(e => e.Key).Distinct().ToList();
        Check(keys.All(k => k == "Close" || k == "type:Lover") && keys.Contains("type:Lover") && keys.Contains("Close"),
            "고양이–강아지 = 밀접 줄 + 연인 전용 줄 — " + string.Join(", ", keys));

        // 짝사랑(토끼 → 여우)은 토끼가 먼저 말한다.
        var crush = Many("fox", "rabbit", OverheardSituation.Normal).Where(e => e.Key == "type:Crush").ToList();
        Check(crush.Count > 0 && crush.All(e => e.A == "rabbit" && e.B == "fox"), $"짝사랑 줄은 토끼가 A({crush.Count}회)");

        var sample = Many("fox", "sheep", OverheardSituation.Normal).Concat(Many("sheep", "wolf", OverheardSituation.Incident)).ToList();
        Check(sample.All(e => !e.LineA.Contains('{') && !e.LineB.Contains('{')), "{a}/{b} 가 코드네임으로 채워진다");
        var sheepLines = sample.SelectMany(e => new[] { (e.A, e.LineA), (e.B, e.LineB) }).Where(x => x.Item1 == "sheep").ToList();
        // 첫 낱말이 한 음절이면 더듬지 않고, 말끝 흐리기는 확률이라 전부는 아니다 — 대부분이면 된다.
        int ticced = sheepLines.Count(x => DialogueVoiceTics.HasStutter(x.Item2) || x.Item2.Contains("..."));
        Check(sheepLines.Count > 0 && ticced >= sheepLines.Count * 0.7f,
            $"말버릇 — 양은 더듬거나 말끝을 흐린다({ticced}/{sheepLines.Count}): " + sheepLines.First().Item2);

        // 같은 방에서 방금 나온 줄은 바로 반복되지 않는다.
        var seq = Enumerable.Range(0, 3).Select(_ => Pick("guard_room_x", "dog", "fox", OverheardSituation.Normal)?.LineA).ToList();
        Check(seq.Distinct().Count() == 3, "같은 방에서 연달아 같은 줄이 나오지 않는다(중립 평상 3줄 순환)");

        foreach (var e in Many("fox", "wolf", OverheardSituation.Sabotage).Take(2))
            GD.Print($"      예) {Nm(e.A)}: {e.LineA}  /  {Nm(e.B)}: {e.LineB}");
        foreach (var e in Many("cat", "dog", OverheardSituation.Incident).Take(2))
            GD.Print($"      예) {Nm(e.A)}: {e.LineA}  /  {Nm(e.B)}: {e.LineB}");
    }

    // ── 상황 판정 ─────────────────────────────────────────────────────────
    private void Situations()
    {
        Head("C", "상황 판정 — 평상 / 사고 직후 / 방해 정황");
        LiveSetup(new() { ["fox"] = "guard_room", ["wolf"] = "guard_room", ["dog"] = "core_room" });
        Run(3f);
        Check(OverheardDialogue.SituationOf("guard_room") == OverheardSituation.Normal, "기록이 없으면 평상");

        EventLog.Instance.LogEvent(LogEventType.TaskFailed, "", "guard_room", "경비실 — 테스트 사고");
        Check(OverheardDialogue.SituationOf("guard_room") == OverheardSituation.Incident, "그 방 사고 기록 → 사고 직후");
        Check(OverheardDialogue.SituationOf("core_room") == OverheardSituation.Normal, "다른 방은 영향 없음");

        EventLog.Instance.LogEvent(LogEventType.Sabotage, "fox", "guard_room", "경비실 — 테스트 방해");
        Check(OverheardDialogue.SituationOf("guard_room") == OverheardSituation.Sabotage, "방해 기록 → 방해 정황(사고보다 우선)");

        float win = OverheardDialogue.Table.SabotageWindowSeconds;
        Run(win + 1f);
        var after = OverheardDialogue.SituationOf("guard_room");
        Check(after != OverheardSituation.Sabotage, $"{win:0}초가 지나면 방해 정황은 끝난다 → {after}");

        Check(OverheardDialogue.TryPick("guard_room", out var ex) && ex.RoomId == "guard_room"
              && new[] { ex.A, ex.B }.OrderBy(x => x).SequenceEqual(new[] { "fox", "wolf" }), "경비실 근무 두 사람으로 대화를 고른다");
        Check(!OverheardDialogue.TryPick("core_room", out _), "혼자 있는 방(코어실)은 대화가 없다");
    }

    // ── 자막 진행 ────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task Caption()
    {
        Head("D", "CCTV 자막 — 대기 → A → B → 쉼, 신호 끊김 · 방 전환");
        var cap = new CctvOverheardCaption { Size = new Vector2(736f, 50f) };
        AddChild(cap);
        await Frames(1);
        var t = OverheardDialogue.Table;

        cap.Tick(0.1f, "guard_room", true);
        Check(!cap.Visible && cap.CurrentLine == "", "방을 켠 직후에는 조용하다(첫 대기)");
        Advance(cap, "guard_room", true, t.FirstDelaySeconds + 0.2f);
        Check(cap.Visible && cap.Current != null && cap.CurrentSpeaker == cap.Current.A, $"첫 대기 뒤 A 가 말한다 — {Nm(cap.CurrentSpeaker)}: {cap.CurrentLine}");
        Advance(cap, "guard_room", true, cap.CurrentLine.Length / t.CharsPerSecond + t.LineHoldSeconds + 0.1f);
        Check(cap.Visible && cap.CurrentSpeaker == cap.Current.B, $"이어서 B 가 받는다 — {Nm(cap.CurrentSpeaker)}: {cap.CurrentLine}");
        Advance(cap, "guard_room", true, cap.CurrentLine.Length / t.CharsPerSecond + t.LineHoldSeconds + 0.1f);
        Check(!cap.Visible && cap.ExchangesPlayed == 1, "대화가 끝나면 쉰다");
        Advance(cap, "guard_room", true, t.CooldownMaxSeconds + 0.5f);
        Check(cap.ExchangesPlayed == 2, "쉼이 끝나면 다음 대화");

        Advance(cap, "guard_room", false, 0.5f);
        Check(!cap.Visible && cap.CurrentLine == "", "신호가 끊기면 들리지 않는다");
        int before = cap.ExchangesPlayed;
        Advance(cap, "core_room", true, t.FirstDelaySeconds + 3f);
        Check(cap.ExchangesPlayed == before && !cap.Visible, "혼자 있는 방으로 바꾸면 대화가 없다");

        // 말하는 중에 한 사람이 자리를 뜨면 거기서 끊긴다.
        Advance(cap, "guard_room", true, t.FirstDelaySeconds + 0.2f);
        string a = cap.Current.A, b = cap.Current.B;
        _sim.AssignToRoom(b, "storage_room");
        for (float s = 0f; s < 30f && _sim.OnDutyEmployeeIds("guard_room").Contains(b); s += Step) Tick();
        Advance(cap, "guard_room", true, 8f);
        Check(!cap.Visible || cap.CurrentSpeaker != b, KoreanParticle.Subject(Nm(b)) + " 나가면 대답 없이 끊긴다");
        cap.QueueFree();
        await Frames(1);
    }

    // ── 메인 씬 ──────────────────────────────────────────────────────────
    private async System.Threading.Tasks.Task MainScene()
    {
        Head("E", "실제 메인 씬 — 근무 중 오른쪽 CRT CCTV 에 대화 자막");
        typeof(ShiftFlowController).GetField("_skipToDay1Pending", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, true);
        var scene = GD.Load<PackedScene>("res://scenes/main/MainScene3D_Test.tscn").Instantiate();
        AddChild(scene);
        for (int i = 0; i < 240 && GameState.Instance?.CurrentPhase != GamePhase.Schedule; i++) await Frame();
        Check(GameState.Instance?.CurrentPhase == GamePhase.Schedule, "배치 단계");

        _sim.AssignToRoom("wolf", "core_room");
        _sim.AssignToRoom("rabbit", "core_room");
        var map = NSP.Ui.ScheduleMapView.Instance;
        await Frames(3);
        var ctl = ControlRoom3DController.Instance;
        var local = map.StartButtonRect.GetCenter();
        var vpPos = map.GetGlobalTransformWithCanvas() * local;
        ctl.ScheduleMapViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = vpPos, GlobalPosition = vpPos }, true);
        await Frame();
        ctl.ScheduleMapViewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = vpPos, GlobalPosition = vpPos }, true);
        for (int i = 0; i < 400 && GameState.Instance?.CurrentPhase != GamePhase.Live; i++) await Frame();
        Check(GameState.Instance?.CurrentPhase == GamePhase.Live, "근무 시작");

        _sim.SetSurveillanceTarget("core_room");
        var cap = FindCaption(scene);
        Check(cap != null, "CCTV 화면에 자막 노드가 있다");
        bool heard = false;
        string line = "";
        for (int i = 0; i < 2400 && !heard; i++)
        {
            await Frame();
            if (cap != null && cap.Visible && cap.CurrentLine != "") { heard = true; line = $"{Nm(cap.CurrentSpeaker)}: {cap.CurrentLine}"; }
        }
        var cctv = CCTVMonitorView.Instance;
        // 창 모드로 "-- <폴더>" 를 주면 그 순간의 CCTV 화면을 저장한다(헤드리스는 그림이 없다).
        var args = OS.GetCmdlineUserArgs();
        if (heard && args.Length > 0 && DisplayServer.GetName() != "headless")
        {
            await Frames(20);
            ctl.CctvViewport.GetTexture().GetImage().SavePng(args[0] + "/cctv_overheard.png");
            GD.Print("saved → " + args[0] + "/cctv_overheard.png");
        }
        Check(heard, $"코어실(늑대+토끼) CCTV 에서 대화가 들린다 — {line} (피드 {(cctv?.FeedVisible == true ? "정상" : "?")})");
        scene.QueueFree();
        await Frames(2);
    }

    // ── 도우미 ───────────────────────────────────────────────────────────
    private void Expect(string x, string y, OverheardSituation sit, string key, string what)
    {
        var list = Many(x, y, sit);
        Check(list.Count > 0 && list.All(e => e.Key == key && e.Situation == sit), $"{what} — {Nm(x)}·{Nm(y)} → {key}");
    }

    private List<OverheardExchange> Many(string x, string y, OverheardSituation sit) =>
        Enumerable.Range(0, 40).Select(i => Pick("test_" + i % 7, x, y, sit)).Where(e => e != null).ToList();

    private static OverheardExchange Pick(string room, string x, string y, OverheardSituation sit) =>
        OverheardDialogue.TryPickFor(room, x, y, sit, out var e) ? e : null;

    private static CctvOverheardCaption FindCaption(Node n)
    {
        if (n is CctvOverheardCaption c) return c;
        foreach (Node ch in n.GetChildren())
        {
            var f = FindCaption(ch);
            if (f != null) return f;
        }
        return null;
    }

    private void Advance(CctvOverheardCaption cap, string room, bool audible, float seconds)
    {
        for (float s = 0f; s < seconds; s += 0.05f) cap.Tick(0.05f, room, audible);
    }

    private void LiveSetup(Dictionary<string, string> plan)
    {
        EventLog.Instance?.ClearAll();
        GameState.Instance.ResetRun(1);
        _sim.ResetRun();
        GameState.Instance.SetPhase(GamePhase.Schedule);
        foreach (var (emp, room) in plan) _sim.AssignToRoom(emp, room);
        _sim.ResetForNewShift();
        GameState.Instance.SetPhase(GamePhase.Live);
        for (float t = 0f; t < 60f && (_sim.OnDutyEmployeeIds("guard_room").Count < 2 || _sim.OnDutyEmployeeIds("core_room").Count < 1); t += Step) Tick();
    }

    private void Run(float seconds)
    {
        for (float t = 0f; t < seconds; t += Step) Tick();
    }

    private void Tick()
    {
        GameState.Instance.AdvanceDayTime(Step);
        _sim.Tick(Step);
    }

    private string Nm(string id) => _sim.GetEmployeeDef(id)?.Codename ?? id;

    private async System.Threading.Tasks.Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await Frame();
    }

    private static void Head(string id, string title) => GD.Print($"\n===== [{id}] {title} =====");

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   {(ok ? "PASS" : "FAIL")}  {what}");
    }
}
