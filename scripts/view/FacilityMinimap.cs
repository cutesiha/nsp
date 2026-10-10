using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;
using NSP.Facility;
using NSP.Ui;

namespace NSP.View;

// 왼쪽 CRT 안의 시설 미니맵. 방 = 네모 노드, 직원 = 점. 게임 상태를 읽기만 하고
// FacilitySimulation 에 배치 명령만 전달한다. 방 좌표를 SetRoomVisualCenter 로 등록해
// 기존 직원 이동 시뮬레이션이 이 미니맵 좌표계로 돌게 한다.
public partial class FacilityMinimap : Control
{
    public Action<string> OnRoomSelected;
    public Action<string> OnEmployeeSelected;

    public string SelectedRoomId = "";
    public string SelectedEmployeeId = "";
    // 마우스가 올라간 작업실. 테두리가 하늘색으로 바뀌고 한 번 소리가 난다 —
    // 배치 지도(ScheduleMapView) · 시작 화면 신원 카드와 같은 규칙.
    private string _hoverRoom = "";
    private static readonly Color HoverCyan = NSP.View.GuideTextMarkup.ChoiceCyan;

    // 직원 아이콘 반지름. 클릭 판정(EmployeeAt)도 이 값을 따라간다.
    private const float EmpDotRadius = 10f;

    // 방 배치 (미니맵 정규화 좌표). 사용자 스케치의 구조.
    private static readonly Dictionary<string, Vector2> Layout = new()
    {
        ["core_room"] = new(0.50f, 0.13f),
        ["power_room"] = new(0.17f, 0.32f),
        ["storage_room"] = new(0.83f, 0.32f),
        ["central_office"] = new(0.50f, 0.50f),
        ["guard_room"] = new(0.17f, 0.70f),
        ["maintenance_room"] = new(0.83f, 0.70f),
        ["isolation_room"] = new(0.50f, 0.88f),
        // 환기실 · 의무실 — 배치 지도(RoomDef.MapPosition)와 같은 쪽(왼쪽 아래 / 오른쪽 아래)에 둔다.
        ["vent_room"] = new(0.17f, 0.88f),
        ["medical_room"] = new(0.83f, 0.88f),
    };

    // 방 이름(위) / 직원 아이콘(가운데) / 직원 코드네임(아래)이 서로 안 겹치도록 잡은 크기.
    private static readonly Vector2 BoxSize = new(92f, 56f);

    private Font _font;
    private readonly HashSet<string> _seenCorridors = new();

    // 작업 완료 팝업 — 방 상자 위에 "코어 복구 +1%" / "자재 +3" 같은 결과가 톡 떴다가 올라가며 사라진다.
    private const float PopupSeconds = 1.6f;
    private const float PopupRise = 18f;
    private readonly List<(string RoomId, string Text, float Age)> _popups = new();

    // 방 효과 점멸 — 그 방이 실제로 일을 해낸 순간 상자가 방 색으로 한 번 밝아진다.
    // 한 방에 초당 한 번까지만 튄다(순찰이 12초 주기라 이 제한에 걸릴 일은 거의 없지만,
    // 자재 생산과 코어 복구가 같은 초에 겹치면 화면이 번쩍이는 것을 막는다).
    private const float FlashSeconds = 0.55f;
    private const float FlashMinGap = 1.0f;
    private readonly Dictionary<string, float> _flashAge = new();
    private readonly Dictionary<string, Color> _flashInk = new();
    private readonly Dictionary<string, float> _flashLastAt = new();

    // 그 방 상자를 한 번 밝힌다. ink 를 비워 두면 그 방의 지도 색을 쓴다.
    public void FlashRoom(string roomId, Color? ink = null)
    {
        if (string.IsNullOrEmpty(roomId) || !Layout.ContainsKey(roomId)) return;
        float now = Time.GetTicksMsec() / 1000f;
        if (now - _flashLastAt.GetValueOrDefault(roomId, -99f) < FlashMinGap) return;
        _flashLastAt[roomId] = now;
        _flashAge[roomId] = 0f;
        _flashInk[roomId] = ink ?? FacilitySimulation.Instance?.GetRoomDef(roomId)?.MapColor
            ?? new Color(0.6f, 0.8f, 0.75f);
    }

    // 환기 재개처럼 시설 전체에 걸리는 효과 — 근무 중인 방들을 한꺼번에 물들인다.
    public void FlashAllRooms(Color ink)
    {
        foreach (var roomId in Layout.Keys) FlashRoom(roomId, ink);
    }

    // 기절 경고 — 쓰러진 순간 그 자리에서 빨갛게 여러 번 깜빡이고, 그 뒤에는 계속 빨갛다.
    private const float FaintBlinkSeconds = 1.6f;
    private const int FaintBlinkCount = 4;
    private readonly Dictionary<string, float> _faintBlink = new();
    private bool _rescueWired;

    // 스트레스 구간이 올라간 순간 — 그 직원 아이콘이 잠깐 점멸하고 위에 붉은 ! 가 뜬다.
    // 숫자를 읽으러 들어가지 않아도 "누가 지금 올라갔다"가 지도에서 바로 보여야 한다.
    private const float StressBlinkSeconds = 2.4f;
    private const int StressBlinkCount = 6;
    private readonly Dictionary<string, float> _stressBlink = new();

    public override void _Ready()
    {
        _font = ViewFont.Default;
        MouseFilter = MouseFilterEnum.Stop;
        SetProcess(true);
        // 끌어다 놓기는 _GuiInput 밖(루트 _Input)에서 이동/뗌을 받아야 한다.
        SetProcessInput(true);
        if (NSP.Core.EventLog.Instance != null) NSP.Core.EventLog.Instance.EntryLogged += OnLogEntry;
        RoomEffectStats.RoomWorked += OnRoomWorked;
        WireRescue();
        RoomEffectStats.VentilationRestored += OnVentilationRestored;
        FacilitySimulation.StressBandRaised += OnStressBandRaised;
    }

    private void OnStressBandRaised(string employeeId, string band) => _stressBlink[employeeId] = 0f;

    // 그 작업실이 방금 제 일을 해냈다. 로그가 아니라 이 신호로 받는 이유는,
    // 로그 줄은 10초씩 모았다 나가지만 점멸은 그 순간에 보여야 하기 때문이다.
    private void OnRoomWorked(string roomId) => FlashRoom(roomId);

    // 시뮬레이션이 오토로드라 이 화면보다 늦게 살아날 수 있다 — 붙을 때까지 매 프레임 본다.
    private void WireRescue()
    {
        if (_rescueWired || FacilitySimulation.Instance?.Rescue == null) return;
        FacilitySimulation.Instance.Rescue.Fainted += OnEmployeeFainted;
        _rescueWired = true;
    }

    // 쓰러졌다 — 그 직원 아이콘이 제자리에서 빨갛게 깜빡이고 짧은 경고음이 난다(§4).
    // **아이콘은 움직이지 않는다.** 기절자는 쓰러진 그 작업실에 그대로 있다(§5).
    private void OnEmployeeFainted(string employeeId)
    {
        _faintBlink[employeeId] = 0f;
        _ = BeepThrice();
    }

    private async System.Threading.Tasks.Task BeepThrice()
    {
        for (int i = 0; i < 3; i++)
        {
            NSP.Core.Sfx.Instance?.Play("alert_beep3", -4f, 1.25f);
            await ToSignal(GetTree().CreateTimer(0.16), SceneTreeTimer.SignalName.Timeout);
            if (!IsInstanceValid(this)) return;
        }
    }

    // 지금 이 직원 아이콘을 무슨 색으로 그릴 것인가. 기절 상태면 빨강(깜빡이는 동안은 번갈아).
    private Color? FaintTint(string employeeId, FacilitySimulation sim)
    {
        var st = sim?.GetEmployeeState(employeeId);
        if (st == null || !st.Incapacitated) { _faintBlink.Remove(employeeId); return null; }

        var red = new Color(1f, 0.16f, 0.14f);
        if (!_faintBlink.TryGetValue(employeeId, out float age)) return red;
        if (age >= FaintBlinkSeconds) return red;
        // 깜빡임 — 원래 색과 빨강을 빠르게 오간다.
        return (int)(age / FaintBlinkSeconds * FaintBlinkCount * 2f) % 2 == 0 ? red : (Color?)null;
    }

    // 위험 · 기절 구간의 직원은 고유색 대신 구간 색으로 그린다(정상 · 주의는 그대로).
    // 기절은 FaintTint 가 따로 맡으므로 여기서는 손대지 않는다.
    private static Color? StressTint(NSP.Facility.EmployeeState st, FacilitySimulation sim)
    {
        if (st == null || sim == null || !NSP.Core.DayFeatures.StressEnabled) return null;
        if (!st.Alive || st.Incapacitated) return null;
        string band = sim.StressBandName(st);
        return band == "위험" ? FacilitySimulation.StressBandColor(band) : null;
    }

    // 환기는 한 방이 아니라 시설 전체에 걸린다 — 방들이 한꺼번에 잠깐 푸르러진다.
    private void OnVentilationRestored() => FlashAllRooms(new Color(0.42f, 0.82f, 0.92f));

    public override void _ExitTree()
    {
        if (NSP.Core.EventLog.Instance != null) NSP.Core.EventLog.Instance.EntryLogged -= OnLogEntry;
        RoomEffectStats.RoomWorked -= OnRoomWorked;
        if (_rescueWired && FacilitySimulation.Instance?.Rescue != null)
            FacilitySimulation.Instance.Rescue.Fainted -= OnEmployeeFainted;
        RoomEffectStats.VentilationRestored -= OnVentilationRestored;
        FacilitySimulation.StressBandRaised -= OnStressBandRaised;
    }

    private void OnLogEntry()
    {
        var e = NSP.Core.EventLog.Instance?.GetAllEntries().LastOrDefault();
        if (e == null || e.EventType != NSP.Data.LogEventType.TaskComplete || !Layout.ContainsKey(e.RoomId)) return;
        string text = PopupText(e.Description);
        if (!string.IsNullOrEmpty(text)) _popups.Add((e.RoomId, text, 0f));
    }

    // 완료 기록("✓ 코어 수리 완료 · 코어 +1% · 📦 자재 -2")에서 결과 부분만 짧게 뽑는다.
    // 결과가 적혀 있지 않은 업무는 "완료"만 띄운다. (미니맵 글꼴에 없는 그림 문자는 뺀다.)
    private static string PopupText(string desc)
    {
        if (string.IsNullOrEmpty(desc)) return "";
        var parts = desc.Split(" · ");
        if (parts.Length < 2) return desc.Contains("수리") ? "수리 완료" : "완료";
        var outp = new List<string>();
        foreach (var raw in parts.Skip(1))
        {
            string p = raw.Replace("📦", "").Replace("⚡", "").Replace("⚠", "").Trim();
            if (p.StartsWith("코어 +")) p = "코어 복구 +" + p.Substring("코어 +".Length);
            if (p.Length > 0) outp.Add(p);
        }
        return string.Join("\n", outp);
    }

    public override void _Process(double delta)
    {
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            var p = _popups[i];
            p.Age += (float)delta;
            if (p.Age >= PopupSeconds) _popups.RemoveAt(i); else _popups[i] = p;
        }
        WireRescue();
        if (_faintBlink.Count > 0)
            foreach (string id in _faintBlink.Keys.ToList())
            {
                float age = _faintBlink[id] + (float)delta;
                if (age >= FaintBlinkSeconds + 0.5f) _faintBlink[id] = FaintBlinkSeconds;
                else _faintBlink[id] = age;
            }
        if (_stressBlink.Count > 0)
            foreach (string id in _stressBlink.Keys.ToList())
            {
                float age = _stressBlink[id] + (float)delta;
                if (age >= StressBlinkSeconds) _stressBlink.Remove(id);
                else _stressBlink[id] = age;
            }
        if (_flashAge.Count > 0)
            foreach (var roomId in _flashAge.Keys.ToList())
            {
                float age = _flashAge[roomId] + (float)delta;
                if (age >= FlashSeconds) { _flashAge.Remove(roomId); _flashInk.Remove(roomId); }
                else _flashAge[roomId] = age;
            }
        var sim = FacilitySimulation.Instance;
        if (sim != null)
        {
            foreach (var (roomId, _) in Layout)
                sim.SetRoomVisualCenter(roomId, CenterOf(roomId));
        }
        QueueRedraw();
    }

    private Vector2 CenterOf(string roomId) =>
        Layout.TryGetValue(roomId, out var n) ? n * Size : Size * 0.5f;

    private Rect2 BoxOf(string roomId) =>
        new(CenterOf(roomId) - BoxSize * 0.5f, BoxSize);

    // --- draw ---------------------------------------------------------

    public override void _Draw()
    {
        var sim = FacilitySimulation.Instance;
        if (sim == null || _font == null) return;

        DrawCorridors(sim);

        foreach (var roomId in Layout.Keys)
            DrawRoom(sim, roomId);

        // 차폐문 마커와 상태 표는 **방 상자 뒤에** 올린다 — 중앙제어실 바로 앞 복도라
        // 문 자리가 방 상자와 가깝고, 먼저 그리면 상자에 글자가 잘려 나간다.
        DrawBarrierOverlay(sim);

        DrawDragHint(sim);

        BuildIconPositions(sim);
        foreach (var id in sim.GetEmployeeIds())
            DrawEmployee(sim, id);

        DrawPopups();
    }

    private void DrawPopups()
    {
        // 같은 방에 여러 개가 겹치면 위로 한 줄씩 쌓는다.
        var stack = new Dictionary<string, int>();
        foreach (var (roomId, text, age) in _popups)
        {
            int n = stack.GetValueOrDefault(roomId);
            stack[roomId] = n + 1;
            float k = age / PopupSeconds;
            // 처음 0.12초는 살짝 튀어 오르고(또잉), 이후 천천히 올라가며 옅어진다.
            float pop = age < 0.12f ? Mathf.Sin(age / 0.12f * Mathf.Pi) * 4f : 0f;
            float alpha = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            var box = BoxOf(roomId);
            int lines = text.Split('\n').Length;
            float fs = ViewFont.S(13);
            float y = box.Position.Y - 6f - PopupRise * k - pop - n * (fs + 4f) * lines - (lines - 1) * (fs + 2f);
            var col = new Color(0.62f, 1f, 0.78f, alpha);
            DrawMultilineStringOutline(_font, new Vector2(box.Position.X - 30f, y), text, HorizontalAlignment.Center,
                box.Size.X + 60f, (int)fs, -1, 4, new Color(0f, 0f, 0f, 0.85f * alpha));
            DrawMultilineString(_font, new Vector2(box.Position.X - 30f, y), text, HorizontalAlignment.Center,
                box.Size.X + 60f, (int)fs, -1, col);
        }
    }

    // ── 복도 ────────────────────────────────────────────────────────
    //
    // 예전에는 3px 짜리 선 한 줄이었다 — 통로가 아니라 배선처럼 보였다.
    // 지금은 **폭이 있는 바닥 띠 + 양쪽 벽선** 으로 그린다. 굵기와 색은 구간의 성격을 따른다.
    //   일반 통로      가는 띠 · 어두운 녹청
    //   주요 연결통로   굵은 띠 · 밝은 녹청 + 가운데 점선(물류 동선)
    //   차폐 가능 구간  굵은 띠 + 문 마커(▮) + OPEN/SEALED 글자 + 카메라 표시
    // 닫히는 중에는 문 마커가 가운데로 모이고, 닫히면 구간 전체가 붉게 죽는다.
    //
    // 꺾임은 직원 이동과 **같은 규칙(CorridorElbow)** 을 쓴다 — 그래야 걷는 길과 그린 길이 겹친다.
    private const float LaneMinor = 8f;
    private const float LaneTrunk = 15f;
    private const float LaneAccess = 12f;

    private static readonly Color LaneFloor = new(0.115f, 0.175f, 0.165f);
    private static readonly Color LaneEdge = new(0.42f, 0.55f, 0.50f);
    private static readonly Color LaneTrunkFloor = new(0.12f, 0.21f, 0.20f);
    private static readonly Color LaneTrunkEdge = new(0.45f, 0.64f, 0.58f);
    // 중앙제어실 접근 통로 — 다른 통로와 **색으로도** 갈린다(관리자 쪽으로 들어오는 길).
    // 네온이 아니라 낡은 황동/올리브 쪽으로 낮게 깐다.
    private static readonly Color LaneAccessFloor = new(0.16f, 0.16f, 0.11f);
    private static readonly Color LaneAccessEdge = new(0.58f, 0.56f, 0.38f);
    private static readonly Color LaneSealed = new(0.84f, 0.26f, 0.21f);
    private static readonly Color LaneSealedFloor = new(0.21f, 0.07f, 0.06f);
    private static readonly Color DoorInk = new(0.80f, 0.86f, 0.80f);

    private void DrawCorridors(FacilitySimulation sim)
    {
        // 성능: 매 프레임 도는 _Draw 라 HashSet 을 새로 만들지 않고 재사용한다.
        _seenCorridors.Clear();
        var seen = _seenCorridors;
        var net = sim.Corridors;
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);

        foreach (var roomId in Layout.Keys)
        {
            var def = sim.GetRoomDef(roomId);
            if (def == null) continue;
            foreach (var other in def.ConnectedRoomIds)
            {
                if (!Layout.ContainsKey(other)) continue;
                string key = string.CompareOrdinal(roomId, other) < 0 ? roomId + "|" + other : other + "|" + roomId;
                if (!seen.Add(key)) continue;

                var seg = net?.Between(roomId, other);
                Vector2 pa = CenterOf(roomId), pb = CenterOf(other);
                var elbow = CorridorElbow.Compute(pa, pb, hub);
                DrawLane(seg, pa, elbow, pb);
            }
        }

    }

    // 차폐문 마커 · 상태 표 · 남측 영구 봉쇄. 방 상자까지 다 그린 뒤에 올린다.
    private void DrawBarrierOverlay(FacilitySimulation sim)
    {
        var net = sim.Corridors;
        if (net == null) return;
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);
        foreach (var seg in net.Blockable)
        {
            if (!Layout.ContainsKey(seg.RoomA) || !Layout.ContainsKey(seg.RoomB)) continue;
            Vector2 pa = CenterOf(seg.RoomA), pb = CenterOf(seg.RoomB);
            DrawBarrierMarker(seg, pa, CorridorElbow.Compute(pa, pb, hub), pb,
                net.SelectedId == seg.Id);
        }
        foreach (var seg in net.Segments)
            if (seg.PermanentSeal) DrawPermanentSeal();
        DrawThreatZones();
    }

    // ── 외곽 서비스 구역 표기 ───────────────────────────────────────
    //
    // 괴물이 나오는 곳이다. **작업실이 아니다** — 배치할 수도, 누를 수도, 볼 수도 없다.
    // 그래서 방 상자가 아니라 지도 바깥 가장자리에 작은 약호만 찍는다.
    // 괴물이 지금 거기 있는지는 **알려주지 않는다**(알려주면 CCTV 를 돌릴 이유가 사라진다).
    private static readonly Color ZoneInk = new(0.40f, 0.42f, 0.36f);

    private void DrawThreatZones()
    {
        foreach (var z in NSP.Facility.MonsterThreatSystem.Zones)
        {
            Vector2 p = z.MapAnchor * Size;
            // 가장자리 밖으로 나가도 지도 안쪽으로 끌어당긴다(작은 CRT 에서 잘리지 않게).
            p.X = Mathf.Clamp(p.X, 30f, Size.X - 30f);
            p.Y = Mathf.Clamp(p.Y, 9f, Size.Y - 9f);
            // 점선 테두리 — 통행 구역이 아니라는 표시.
            var r = new Rect2(p - new Vector2(44f, 7f), new Vector2(88f, 14f));
            for (float x = r.Position.X; x < r.End.X; x += 7f)
            {
                DrawLine(new Vector2(x, r.Position.Y), new Vector2(Mathf.Min(x + 4f, r.End.X), r.Position.Y),
                    ZoneInk with { A = 0.55f }, 1f);
                DrawLine(new Vector2(x, r.End.Y), new Vector2(Mathf.Min(x + 4f, r.End.X), r.End.Y),
                    ZoneInk with { A = 0.55f }, 1f);
            }
            DrawString(_font, new Vector2(r.Position.X, r.End.Y - 3f), z.CodeName,
                HorizontalAlignment.Center, r.Size.X, 9, ZoneInk);
        }
    }

    // ── 남측 영구 봉쇄 격벽 ─────────────────────────────────────────
    //
    // 차폐문과 **다른 물건**이다. 조작할 수 없고 전력과도 무관하며, 애초에 길이 없다
    // (방 그래프에서 중앙제어실↔격리실 간선을 지웠다). 여기서는 "예전에는 길이었고
    // 지금은 용접해 막았다"는 사실만 보여 준다.
    //
    // 자리는 중앙제어실 바로 아래 — 경비실과 정비실 사이의 빈 공간이되,
    // **그 둘을 잇는 가로 복도(y 가 같다)보다 위**라 정상 통행을 가리지 않는다.
    private static readonly Color SealPanel = new(0.26f, 0.085f, 0.075f);
    private static readonly Color SealEdge = new(0.82f, 0.22f, 0.17f);
    private static readonly Color SealHatch = new(0.52f, 0.15f, 0.12f);
    private const float SealWidth = 116f;
    private const float SealHeight = 17f;
    private const float SealGapBelowRoom = 19f;

    // 검사 전용 — 두 방을 잇는 띠의 한가운데(미니맵 로컬 좌표).
    public Vector2 LaneMidForTest(string a, string b)
    {
        Vector2 pa = CenterOf(a), pb = CenterOf(b);
        var elbow = CorridorElbow.Compute(pa, pb, CenterOf(FacilitySimulation.DeployOriginRoomId));
        return elbow == null ? (pa + pb) * 0.5f : (pa + elbow.Value) * 0.5f;
    }

    private Rect2 SealRect()
    {
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);
        float y = hub.Y + BoxSize.Y * 0.5f + SealGapBelowRoom;
        return new Rect2(hub.X - SealWidth * 0.5f, y - SealHeight * 0.5f, SealWidth, SealHeight);
    }

    private void DrawPermanentSeal()
    {
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);
        var r = SealRect();

        // 중앙제어실 바닥에서 격벽까지 짧은 토막 — "여기가 길이었다".
        DrawLine(hub + new Vector2(0f, BoxSize.Y * 0.5f), r.GetCenter(), SealHatch with { A = 0.75f }, 6f);

        DrawRect(r, SealPanel);
        DrawRect(r, SealEdge, false, 1.6f);
        // 금속 패널 — 사선 경고 무늬.
        for (float x = r.Position.X + 6f; x < r.End.X - 2f; x += 9f)
            DrawLine(new Vector2(x, r.End.Y - 2f),
                new Vector2(Mathf.Min(x + 7f, r.End.X - 2f), r.Position.Y + 2f), SealHatch, 2.2f);
        // 양 끝 리벳 자리.
        DrawRect(new Rect2(r.Position.X + 2f, r.Position.Y + 2f, 3f, r.Size.Y - 4f), SealEdge with { A = 0.7f });
        DrawRect(new Rect2(r.End.X - 5f, r.Position.Y + 2f, 3f, r.Size.Y - 4f), SealEdge with { A = 0.7f });

        DrawString(_font, new Vector2(r.Position.X - 20f, r.End.Y + 11f), "⨯ PERMANENT SEAL",
            HorizontalAlignment.Center, r.Size.X + 40f, 9, SealEdge with { A = 0.92f });
    }

    // 바닥 띠 + 양쪽 벽선. 꺾임이 있으면 두 토막으로 나눠 그리고 모서리를 메운다.
    private void DrawLane(CorridorSegment seg, Vector2 a, Vector2? elbow, Vector2 b)
    {
        var kind = seg?.Kind ?? CorridorKind.Minor;
        float w = kind switch
        {
            CorridorKind.Trunk => LaneTrunk,
            CorridorKind.ControlAccess => LaneAccess,
            _ => LaneMinor,
        };
        bool shut = seg is { Sealed: true };
        var floor = shut ? LaneSealedFloor
            : kind == CorridorKind.Trunk ? LaneTrunkFloor
            : kind == CorridorKind.ControlAccess ? LaneAccessFloor
            : LaneFloor;
        var edge = shut ? LaneSealed
            : kind == CorridorKind.Trunk ? LaneTrunkEdge
            : kind == CorridorKind.ControlAccess ? LaneAccessEdge
            : LaneEdge;

        if (elbow == null)
        {
            Strip(a, b, w, floor, edge);
        }
        else
        {
            Strip(a, elbow.Value, w, floor, edge);
            Strip(elbow.Value, b, w, floor, edge);
            // 꺾이는 모서리의 빈 사각형을 메워 'ㄱ' 자가 끊겨 보이지 않게 한다.
            DrawRect(new Rect2(elbow.Value - new Vector2(w, w) * 0.5f, new Vector2(w, w)), floor);
        }

        // 주요 연결통로는 가운데 점선으로 한 번 더 구분한다(물류 동선).
        if (seg?.Kind == CorridorKind.Trunk && !shut)
            DashedSpine(a, elbow, b, edge with { A = 0.55f });
    }

    private void Strip(Vector2 a, Vector2 b, float w, Color floor, Color edge)
    {
        Vector2 d = b - a;
        if (d.LengthSquared() < 0.01f) return;
        Vector2 n = d.Normalized().Orthogonal() * (w * 0.5f);
        // 바닥 — 선 굵기로 칠하면 끝이 둥글게 뭉개져서 사각형으로 그린다.
        DrawColoredPolygon(new[] { a + n, b + n, b - n, a - n }, floor);
        // 양쪽 벽.
        DrawLine(a + n, b + n, edge, 1.2f);
        DrawLine(a - n, b - n, edge, 1.2f);
    }

    private void DashedSpine(Vector2 a, Vector2? elbow, Vector2 b, Color col)
    {
        if (elbow == null) { Dashes(a, b, col); return; }
        Dashes(a, elbow.Value, col);
        Dashes(elbow.Value, b, col);
    }

    private void Dashes(Vector2 a, Vector2 b, Color col)
    {
        const float dash = 6f, gap = 6f;
        Vector2 dir = (b - a).Normalized();
        float len = a.DistanceTo(b);
        for (float t = 4f; t + dash < len - 4f; t += dash + gap)
            DrawLine(a + dir * t, a + dir * (t + dash), col, 1.1f);
    }

    // 차폐문 — 통로 한가운데의 작은 셔터. 상태 글자와 카메라 표시가 함께 붙는다.
    private void DrawBarrierMarker(CorridorSegment seg, Vector2 a, Vector2? elbow, Vector2 b, bool selected)
    {
        // 문은 꺾임이 있으면 긴 쪽 토막의 한가운데에 둔다 — 모서리에 걸치면 모양이 깨진다.
        Vector2 p0 = a, p1 = elbow ?? b;
        if (elbow != null && elbow.Value.DistanceSquaredTo(b) > a.DistanceSquaredTo(elbow.Value))
        {
            p0 = elbow.Value;
            p1 = b;
        }
        Vector2 mid = DoorPoint(a, elbow, b);
        Vector2 along = (p1 - p0).Normalized();
        Vector2 across = along.Orthogonal();

        float w = seg.Kind == CorridorKind.Trunk ? LaneTrunk : LaneAccess;
        var ink = seg.Sealed ? LaneSealed : DoorInk;

        // 문짝 두 장이 양쪽에서 가운데로 내려온다 — Shut 이 그 진행도다.
        float half = w * 0.5f;
        float leaf = half * Mathf.Clamp(seg.Shut, 0f, 1f);
        float thick = 3.2f;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 outer = mid + across * (half * side);
            Vector2 inner = mid + across * ((half - leaf) * side);
            DrawLine(outer - along * thick, outer + along * thick, ink, 2.2f);
            if (leaf > 0.5f) DrawLine(outer, inner, ink, 2.6f);
        }
        // 닫혔으면 통로를 가로질러 한 줄로 막는다.
        if (seg.Sealed)
            DrawLine(mid - across * half, mid + across * half, LaneSealed, 3.2f);

        // 고른 구간은 가는 사각 테두리로 표시한다(레버가 무엇을 제어하는지).
        if (selected)
            DrawRect(new Rect2(mid - new Vector2(13f, 13f), new Vector2(26f, 26f)),
                HoverCyan with { A = 0.85f }, false, 1.2f);

        // 카메라가 붙은 구간이라는 작은 점 하나는 늘 찍는다. 글자는 아래 조건에서만.
        if (!string.IsNullOrEmpty(seg.CctvCameraId))
            DrawCircle(mid - across * (half + 5f), 2.2f, new Color(0.45f, 0.62f, 0.58f));

        // 상태 **글자**는 꼭 필요할 때만 띄운다 — 작은 CRT 에 세 구간의 문구가 늘 떠 있으면
        // 방 이름과 직원 코드네임 위에 겹쳐 지도가 읽히지 않는다.
        // 지금 고른 구간이거나, 열려 있지 않은 구간만.
        if (!selected && seg.State == BarrierState.Open) return;

        string text = seg.StatusText;
        if (selected && !string.IsNullOrEmpty(seg.CctvCameraId) && seg.State == BarrierState.Open)
            text = seg.CctvCameraId;
        var tone = seg.Sealed ? LaneSealed
            : seg.Moving ? new Color(0.98f, 0.78f, 0.36f)
            : HoverCyan;

        // 허브(중앙 제어실) 반대쪽에 붙인다 — 지도 가운데는 방이 빽빽하다.
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);
        float sign = (mid + across - hub).LengthSquared() >= (mid - across - hub).LengthSquared() ? 1f : -1f;
        var size = _font.GetStringSize(text, HorizontalAlignment.Left, -1, 10);
        Vector2 tl = mid + across * sign * (half + 4f) - new Vector2(size.X * 0.5f, 0f);
        tl.Y -= 6f;
        var plate = new Rect2(tl - new Vector2(4f, 2f), size + new Vector2(8f, 5f));
        // 어두운 받침 — 통로 띠 위에 그대로 쓰면 글자가 묻힌다.
        DrawRect(plate, new Color(0.02f, 0.04f, 0.04f, 0.88f));
        DrawRect(plate, tone with { A = 0.55f }, false, 1f);
        DrawString(_font, tl + new Vector2(0f, size.Y - 2f), text, HorizontalAlignment.Left, -1, 10, tone);
    }

    private void DrawRoom(FacilitySimulation sim, string roomId)
    {
        var def = sim.GetRoomDef(roomId);
        var state = sim.GetRoomState(roomId);
        if (def == null || state == null) return;

        Rect2 box = BoxOf(roomId);
        // 오늘 잠긴 작업실(환기실/의무실)은 배치도 업무도 사고도 없다 — 격리실처럼 어둡게만 둔다.
        bool inactive = !sim.IsRoomActive(roomId);
        bool dormant = def.IsRestricted || inactive;
        var tier = dormant ? RoomDangerTier.None : RoomStatusText.GetDangerTier(roomId);

        // 색은 두 단계뿐이다 — 주황(경고) / 빨강(사고 발생).
        // 단계 판정만 경고 단말기와 같은 IncidentBoard 를 쓴다.
        //
        // 주황은 **사고 직전**에만 켠다. 주의(Caution)까지 칠하면 제한시간이 걸린 업무가
        // 도는 방은 전부 근무 시작부터 주황이 되어, 정작 급한 방이 묻힌다.
        // 주의 단계는 방 상자의 진행 막대(DrawWarningBar / 아래 업무 바)가 이미 보여 준다.
        var incident = dormant ? null : NSP.Core.IncidentBoard.ForRoom(roomId);
        Color fill = incident?.State switch
        {
            NSP.Core.IncidentState.Active => new Color(0.55f, 0.09f, 0.09f)
                .Lerp(new Color(0.8f, 0.15f, 0.15f), 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 90f)),
            NSP.Core.IncidentState.Warning => new Color(0.5f, 0.32f, 0.08f),
            _ => tier switch
            {
                RoomDangerTier.Failure => new Color(0.55f, 0.09f, 0.09f),
                RoomDangerTier.Unstable => new Color(0.5f, 0.32f, 0.08f),
                _ => dormant ? new Color(0.10f, 0.11f, 0.13f) : new Color(0.11f, 0.17f, 0.16f),
            },
        };
        DrawRect(box, fill);

        // 방 효과 점멸 — 방 색이 상자 위에 잠깐 덮였다가 빠진다. 사고 색(빨강/주황)을
        // 지우지 않도록 알파로만 얹는다.
        if (_flashAge.TryGetValue(roomId, out float flashAge))
        {
            float k = 1f - flashAge / FlashSeconds;
            var ink = _flashInk.GetValueOrDefault(roomId, def.MapColor);
            DrawRect(box, new Color(ink.R, ink.G, ink.B, 0.55f * k));
            DrawRect(box.Grow(1.5f), new Color(ink.R, ink.G, ink.B, k), false, 2f);
        }

        bool selected = roomId == SelectedRoomId;
        bool hot = roomId == _hoverRoom && !inactive;
        Color border = hot ? HoverCyan : selected ? new Color(0.5f, 1f, 0.85f) : new Color(0.3f, 0.4f, 0.38f);
        DrawRect(box, border, false, selected || hot ? 2.5f : 1.2f);
        if (state.Locked)
            DrawRect(box.Grow(3f), new Color(0.9f, 0.5f, 0.2f), false, 1.5f);

        // 방 이름은 상자 위쪽 — 가운데는 직원 아이콘 자리로 비워둔다.
        // (인원수 "● n" 표기는 아이콘이 곧 인원이라 지웠다. 아이콘과 겹쳐 읽기 힘들었다.)
        string name = def.DisplayName;
        DrawString(_font, box.Position + new Vector2(0f, 14f), name, HorizontalAlignment.Center,
            box.Size.X, ViewFont.S(12), inactive ? new Color(0.45f, 0.48f, 0.47f) : new Color(0.85f, 0.92f, 0.88f));
        if (inactive)
        {
            DrawString(_font, box.Position + new Vector2(0f, 32f), "비활성", HorizontalAlignment.Center,
                box.Size.X, ViewFont.S(11), new Color(0.42f, 0.45f, 0.44f));
            return;
        }

        // 발생 업무: 남은 시간 + 게이지.
        // 평소 작업은 방 '아래' 파란 게이지, 사고 수리는 방 '위' 빨간 게이지로 완전히
        // 갈라 놓는다 — 한 눈에 "지금 고치는 중인 방"을 찾을 수 있어야 한다.
        var st = sim.GetPrimarySpawnedTask(roomId);
        if (st is { Status: SpawnedTaskStatus.Active })
        {
            if (st.IsRepair) DrawRepairBar(box, st);
            else
            {
                float y = box.Position.Y + box.Size.Y + 4f;
                if (!st.Recurring)
                {
                    DrawString(_font, new Vector2(box.Position.X, y + 10f),
                        $"⏱ {Clock(st.Remaining)}", HorizontalAlignment.Center, box.Size.X, ViewFont.S(10),
                        st.Remaining < 8f ? new Color(1f, 0.4f, 0.3f) : new Color(0.9f, 0.8f, 0.4f));
                    y += 13f;
                }
                var barBg = new Rect2(box.Position.X + 6f, y, box.Size.X - 12f, 4f);
                DrawRect(barBg, new Color(0.1f, 0.1f, 0.1f));
                DrawRect(new Rect2(barBg.Position, new Vector2(barBg.Size.X * Mathf.Clamp(st.Ratio, 0f, 1f), 4f)),
                    new Color(0.4f, 0.75f, 0.92f));
            }
        }

        // 아직 사고가 아닌 경고 — 남은 시간과 필요한 인원을 방 위에 띄운다.
        // 수리 막대가 이미 그 자리를 쓰고 있으면 그쪽이 우선이다(이미 고장 난 방이다).
        if (st is not { Status: SpawnedTaskStatus.Active, IsRepair: true })
        {
            var risk = NSP.Core.IncidentBoard.ForRoom(roomId);
            if (risk is { State: NSP.Core.IncidentState.Warning } && risk.WarningRemainingSeconds >= 0f)
                DrawWarningBar(box, sim, roomId, risk);
        }

        if (TabooRuleSystemAtRisk(roomId))
            DrawString(_font, box.Position + new Vector2(0f, -4f), "⚠", HorizontalAlignment.Center, box.Size.X, ViewFont.S(14),
                new Color(1f, 0.75f, 0.2f));

        if (def.IsCoreRoom)
            DrawString(_font, new Vector2(box.Position.X, box.Position.Y - 14f),
                $"CORE {sim.CoreProgressPreview():0.0}%", HorizontalAlignment.Center, box.Size.X, ViewFont.S(11),
                new Color(0.5f, 0.8f, 1f));

        // 지금 이 인원이면 무엇이 달라지는가. 사람을 옮기는 즉시 이 줄이 바뀐다.
        string effect = sim.StaffingEffectLine(roomId);
        if (!string.IsNullOrEmpty(effect))
            DrawString(_font, box.Position + new Vector2(0f, box.Size.Y - 4f), effect,
                HorizontalAlignment.Center, box.Size.X, ViewFont.S(10),
                effect.Contains("불안정") || effect.Contains("정지")
                    ? new Color(1f, 0.72f, 0.35f)
                    : new Color(0.62f, 0.78f, 0.72f));
    }

    // 사고 수리 표시 — 방 바로 위에 붉은 진행 막대 한 줄. 글자는 막대 안에 넣는다.
    // (위아래 방 사이가 12px 뿐이라 막대와 글자를 따로 쌓으면 윗방을 덮는다.)
    // 아무도 붙어 있지 않아 게이지가 멈춰 있으면 "수리 필요"로 바꿔 부른다.
    private void DrawRepairBar(Rect2 box, SpawnedTask st)
    {
        const float BarH = 15f;
        bool working = st.Progressing;
        // 맨 윗줄(코어실)은 위쪽 여백이 13px 뿐이라 그대로 두면 화면 밖으로 나간다.
        float top = Mathf.Max(box.Position.Y - BarH - 2f, 1f);

        var bar = new Rect2(box.Position.X + 4f, top, box.Size.X - 8f, BarH);
        DrawRect(bar, new Color(0.17f, 0.05f, 0.05f, 0.96f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * Mathf.Clamp(st.Ratio, 0f, 1f), BarH)),
            new Color(0.84f, 0.17f, 0.14f));
        DrawRect(bar, new Color(1f, 0.48f, 0.40f, 0.8f), false, 1f);

        // 수리 중일 때만 글자가 맥동한다 — 멈춰 있으면 가만히 떠 있다.
        float a = working ? 0.75f + 0.25f * Mathf.Sin(Time.GetTicksMsec() / 170f) : 1f;
        // 둘 이상이 붙어야 고쳐지는 설비(코어실 · 발전실)는 "몇 명이 필요한지"를 막대가
        // 직접 말한다. 혼자 보내 놓고 왜 게이지가 안 차는지 몰라 서 있던 자리다(문서 §17).
        var sim = FacilitySimulation.Instance;
        int need = st.MinWorkersOverride > 0
            ? st.MinWorkersOverride
            : RoomStaffing.RepairMinWorkers(st.RoomId, sim?.GetRoomDef(st.RoomId));
        string label = working ? "수 리 중" : "수리 필요";
        if (need >= 2) label += $"  {sim?.OnDutyCount(st.RoomId) ?? 0}/{need}";
        DrawString(_font, new Vector2(bar.Position.X, bar.Position.Y + 12f),
            label, HorizontalAlignment.Center, bar.Size.X,
            ViewFont.S(10), new Color(1f, 0.94f, 0.92f, a));
    }

    // 경고 표시 — 방 바로 위에 남은 시간 막대와 "몇 초 · 현재/필요 인원".
    // 이 막대가 차 있는 동안에는 아직 사고가 아니다. 인원을 채우면 그대로 사라진다.
    private void DrawWarningBar(Rect2 box, FacilitySimulation sim, string roomId,
        NSP.Core.IncidentDisplayData risk)
    {
        const float BarH = 15f;
        float top = Mathf.Max(box.Position.Y - BarH - 2f, 1f);
        var bar = new Rect2(box.Position.X + 4f, top, box.Size.X - 8f, BarH);

        // 남은 시간이 줄어드는 만큼 막대가 빈다.
        float total = risk.WarningTotalSeconds > 0f ? risk.WarningTotalSeconds : 20f;
        float ratio = Mathf.Clamp(risk.WarningRemainingSeconds / total, 0f, 1f);
        DrawRect(bar, new Color(0.18f, 0.12f, 0.02f, 0.96f));
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * ratio, BarH)),
            new Color(0.95f, 0.62f, 0.12f));
        DrawRect(bar, new Color(1f, 0.78f, 0.35f, 0.85f), false, 1f);

        int here = sim.OnDutyCount(roomId);
        int need = Mathf.Max(1, risk.RepairWorkers);
        float a = 0.7f + 0.3f * Mathf.Sin(Time.GetTicksMsec() / 150f);

        // 인원이 채워졌으면 막대가 "남은 시간"이 아니라 "안정화 진행도"로 바뀐다.
        // 사람을 보낸 뒤에도 뭔가 돌아가고 있다는 것이 보여야 자리를 지킨다.
        if (here >= need && risk.StabilizeNeedSeconds > 0f)
        {
            float wr = Mathf.Clamp(risk.StabilizeDoneSeconds / risk.StabilizeNeedSeconds, 0f, 1f);
            DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * wr, BarH)),
                new Color(0.42f, 0.82f, 0.55f));
        }

        // 글자가 막대의 채워진 쪽과 빈 쪽에 걸쳐 놓이므로, 어두운 그림자를 먼저 깔고
        // 밝은 글자를 얹어 양쪽 배경에서 모두 읽히게 한다.
        string text = here >= need && risk.StabilizeNeedSeconds > 0f
            ? $"안정화 {risk.StabilizeDoneSeconds:0.#}/{risk.StabilizeNeedSeconds:0}s"
            : $"⚠ {risk.WarningRemainingSeconds:0}s · {here}/{need}";
        var at = new Vector2(bar.Position.X, bar.Position.Y + 12f);
        DrawString(_font, at + new Vector2(1f, 1f), text, HorizontalAlignment.Center,
            bar.Size.X, ViewFont.S(10), new Color(0.08f, 0.05f, 0f, 0.9f));
        DrawString(_font, at, text, HorizontalAlignment.Center,
            bar.Size.X, ViewFont.S(10), new Color(1f, 0.96f, 0.86f, a));
    }

    // 끌고 있는 동안 대상 작업실을 밝히고, 커서 자리에 직원 색 점을 따라 그린다.
    private void DrawDragHint(FacilitySimulation sim)
    {
        if (!_dragging || string.IsNullOrEmpty(_dragEmp)) return;

        string hover = RoomAt(_lastDragPos);
        if (hover != null)
        {
            bool ok = sim.CanAssignToRoom(hover)
                      && sim.GetEmployeeState(_dragEmp)?.AssignedRoomId != hover;
            DrawRect(BoxOf(hover).Grow(3f),
                ok ? new Color(0.45f, 1f, 0.8f, 0.95f) : new Color(1f, 0.4f, 0.3f, 0.9f), false, 2.4f);
        }

        var def = sim.GetEmployeeDef(_dragEmp);
        Color c = def?.IconColor ?? Colors.White;
        DrawCircle(_lastDragPos, EmpDotRadius, new Color(c.R, c.G, c.B, 0.85f));
        DrawCircle(_lastDragPos, EmpDotRadius, new Color(1f, 1f, 1f, 0.9f), false, 1.6f);
    }

    private static bool TabooRuleSystemAtRisk(string roomId) =>
        NSP.Taboo.TabooRuleSystem.Instance?.IsRoomAtTabooRisk(roomId) ?? false;

    // ⑧ 한 방에 서 있는 직원들의 아이콘 자리. 시뮬레이션 좌표(EmployeeState.Position)는
    // 그대로 두고, **그리는 자리만** 좌우로 벌린다. 걷는 중인 직원은 통로 위 실제 위치 그대로다.
    private readonly Dictionary<string, Vector2> _iconPos = new();
    private const float IconSpread = 23f;

    private void BuildIconPositions(FacilitySimulation sim)
    {
        _iconPos.Clear();
        var perRoom = new Dictionary<string, List<string>>();
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null) continue;
            if (st.IsMoving || string.IsNullOrEmpty(st.CurrentRoomId))
            {
                _iconPos[id] = st.Position;      // 이동 중 — 통로 위 실제 위치
                continue;
            }
            if (!perRoom.TryGetValue(st.CurrentRoomId, out var list))
                perRoom[st.CurrentRoomId] = list = new List<string>();
            list.Add(id);
        }

        foreach (var (roomId, ids) in perRoom)
        {
            // 방 안에서 순서가 매 프레임 흔들리지 않게 ID 로 정렬한다.
            ids.Sort(System.StringComparer.Ordinal);
            // 상자를 넘지 않는 선에서 벌린다(3명까지는 여유, 그 이상은 간격을 좁힌다).
            float half = BoxSize.X * 0.5f - EmpDotRadius - 4f;
            float step = ids.Count <= 1 ? 0f : Mathf.Min(IconSpread, half * 2f / (ids.Count - 1));
            for (int i = 0; i < ids.Count; i++)
            {
                var st = sim.GetEmployeeState(ids[i]);
                float dx = (i - (ids.Count - 1) * 0.5f) * step;
                _iconPos[ids[i]] = st.Position + new Vector2(dx, 0f);
            }
        }
    }

    // 화면에서 이 직원의 아이콘이 실제로 그려지는 자리(클릭 판정도 같은 값을 쓴다).
    private Vector2 IconPos(FacilitySimulation sim, string id) =>
        _iconPos.TryGetValue(id, out var p) ? p : sim.GetEmployeeState(id)?.Position ?? Vector2.Zero;

    private void DrawEmployee(FacilitySimulation sim, string id)
    {
        var st = sim.GetEmployeeState(id);
        var def = sim.GetEmployeeDef(id);
        if (st == null || def == null) return;

        // 근무 배치에서 빠진 직원은 오늘 근무자가 아니다 — 지도에 띄우지 않는다.
        // (격리된 직원은 배치가 해제되지만 위치는 계속 보여야 하므로 예외.)
        if (string.IsNullOrEmpty(st.AssignedRoomId) && !st.Isolated) return;

        // LIGHTING이 꺼지면(정전 등) 비상 조명이 없는 방의 직원 아이콘은 안 보인다 — 직원을
        // 지우거나 옮기는 게 아니라, 관리자가 위치를 볼 수 없게 되는 것뿐이다(데이터/시뮬레이션은
        // 그대로 유지).
        bool lightingOk = NSP.Core.GameState.Instance?.IsConsumerPowered(NSP.Data.PowerConsumer.Lighting) ?? true;
        if (!lightingOk && !(sim.GetRoomDef(st.CurrentRoomId)?.HasEmergencyLighting ?? false))
            return;

        // 금기 페널티(위치 두절) 중인 직원도 지도에서 사라진다 — 데이터는 그대로다.
        if (NSP.Taboo.TabooRuleSystem.Instance?.IsTrackingLost(id) == true) return;

        Vector2 p = IconPos(sim, id);
        Color c = st.Alive ? def.IconColor : new Color(0.35f, 0.35f, 0.35f);
        // 쓰러졌다 — 빨간색. 처음 한두 초는 원래 색과 번갈아 깜빡이고,
        // 그 뒤에는 의무실에서 깨어날 때까지 계속 빨간 상태로 남는다(§4).
        // 위험 구간 이상이면 아이콘을 구간 색으로 물들인다 — 지도만 봐도 누가 한계인지 보인다(F-2).
        var stress = StressTint(st, sim);
        if (stress.HasValue) c = stress.Value;
        var faint = FaintTint(id, sim);
        if (faint.HasValue) c = faint.Value;

        // 스트레스 구간이 방금 올라갔다 — 아이콘이 점멸하고 위에 붉은 ! 가 같이 깜빡인다.
        bool stressBeat = false;
        if (_stressBlink.TryGetValue(id, out float sage))
        {
            stressBeat = (int)(sage / StressBlinkSeconds * StressBlinkCount * 2f) % 2 == 0;
            if (stressBeat) c = FacilitySimulation.StressBandColor(sim.StressBandName(st));
        }

        // 직원 아이콘은 고유색으로 구분한다 — 작게 그리면 색이 안 읽히므로 넉넉한 크기로.
        if (id == SelectedEmployeeId)
            DrawCircle(p, EmpDotRadius + 4f, new Color(1f, 1f, 1f, 0.9f), false, 2.4f);
        if (stressBeat)
            DrawCircle(p, EmpDotRadius + 5f, c with { A = 0.55f }, false, 3f);
        DrawCircle(p, EmpDotRadius, c);
        DrawCircle(p, EmpDotRadius, new Color(0f, 0f, 0f, 0.7f), false, 1.6f);
        if (stressBeat)
            DrawString(_font, p + new Vector2(-14f, -EmpDotRadius - 6f), "!", HorizontalAlignment.Center,
                28f, ViewFont.S(20), new Color(1f, 0.26f, 0.22f));

        // 코드네임은 아이콘 아래 — 방 이름(상자 위쪽)과 부딪히지 않는다.
        DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 12f), def.Codename, HorizontalAlignment.Center, 60f, ViewFont.S(11),
            new Color(0.95f, 0.95f, 0.8f));
        if (st.Isolated)
            DrawString(_font, p + new Vector2(-30f, EmpDotRadius + 23f), "[격리]", HorizontalAlignment.Center, 60f, ViewFont.S(9),
                new Color(0.9f, 0.5f, 0.9f));
    }

    private static string Clock(float s)
    {
        int t = Mathf.CeilToInt(Mathf.Max(0f, s));
        return $"{t / 60:0}:{t % 60:00}";
    }

    // --- input -------------------------------------------------------

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;

        var sim = FacilitySimulation.Instance;
        if (sim == null) return;

        string empHit = EmployeeAt(sim, mb.Position);
        if (empHit != null)
        {
            var st = sim.GetEmployeeState(empHit);
            // 살아 있고 격리되지 않았으면 끌어다 놓을 수 있다. 선택은 손을 뗄 때 처리한다.
            if (st is { Alive: true, Isolated: false })
            {
                _dragEmp = empHit;
                _pressPos = mb.Position;
                _lastDragPos = mb.Position;
                _dragging = false;
            }
            else OnEmployeeSelected?.Invoke(empHit);
            AcceptEvent();
            return;
        }

        string roomHit = RoomAt(mb.Position);
        if (roomHit != null)
        {
            // 오늘 잠긴 작업실은 선택해도 볼 것이 없다 — 인스펙터/CCTV를 그쪽으로 돌리지 않는다.
            if (sim.IsRoomActive(roomHit)) OnRoomSelected?.Invoke(roomHit);
            AcceptEvent();
        }

        // 통로는 **가장 마지막** 에 본다. 직원 아이콘 · 작업실 상자가 먼저이므로
        // 새로 생긴 통로 히트박스 때문에 기존 조작이 가려지는 일이 없다.
        string corridorHit = CorridorAt(sim, mb.Position);
        if (corridorHit == null) return;
        // 차폐 가능한 구간이면 레버의 제어 대상도 함께 바뀐다.
        // 봉쇄된 남측 격벽은 CCTV 로 보기만 하고 제어 대상은 건드리지 않는다.
        sim.Corridors.Select(corridorHit);
        NSP.Core.Sfx.Instance?.Play("tick", -14f);
        OnCorridorSelected?.Invoke(corridorHit);
        AcceptEvent();
    }

    // 통로를 골랐다 — 차폐 레버의 제어 대상이 바뀐다.
    public Action<string> OnCorridorSelected;

    // 문 마커 주변 작은 상자. **카메라가 붙은 구간**만 집힌다 — 그래야 누르면 볼 것이 있다.
    // (봉쇄된 남측 격벽도 카메라가 있으므로 눌러 확인할 수 있다. 다만 차폐 대상은 못 된다.)
    private const float DoorHitRadius = 15f;

    public string CorridorAt(FacilitySimulation sim, Vector2 pos)
    {
        var net = sim?.Corridors;
        if (net == null) return null;
        Vector2 hub = CenterOf(FacilitySimulation.DeployOriginRoomId);
        foreach (var seg in net.Segments)
        {
            if (string.IsNullOrEmpty(seg.CctvCameraId)) continue;
            if (seg.PermanentSeal)
            {
                if (SealRect().Grow(5f).HasPoint(pos)) return seg.Id;
                continue;
            }
            if (!Layout.ContainsKey(seg.RoomA) || !Layout.ContainsKey(seg.RoomB)) continue;
            Vector2 a = CenterOf(seg.RoomA), b = CenterOf(seg.RoomB);
            Vector2 door = DoorPoint(a, CorridorElbow.Compute(a, b, hub), b);
            if (pos.DistanceTo(door) <= DoorHitRadius) return seg.Id;
        }
        return null;
    }

    // 문이 놓이는 지점 — 그리기와 히트 판정이 **같은 식**을 써야 눌리는 자리와 보이는 자리가 맞는다.
    private static Vector2 DoorPoint(Vector2 a, Vector2? elbow, Vector2 b)
    {
        Vector2 p0 = a, p1 = elbow ?? b;
        if (elbow != null && elbow.Value.DistanceSquaredTo(b) > a.DistanceSquaredTo(elbow.Value))
        {
            p0 = elbow.Value;
            p1 = b;
        }
        // 한가운데(0.5)가 아니라 조금 앞쪽에 둔다. 통로 두 개가 직각으로 만나는 자리가
        // 하필 서로의 중점인 경우가 있어(저장고↔정비실 세로선과 서비스 통로의 꺾임이
        // 같은 점에서 만난다) 문 마커와 상태 표가 겹쳤다.
        return p0.Lerp(p1, 0.38f);
    }

    // 같은 방에 여러 명이 겹쳐 있어도 클릭 지점에 가장 가까운 직원을 집는다.
    private string EmployeeAt(FacilitySimulation sim, Vector2 pos)
    {
        string best = null;
        float bestDist = float.MaxValue;
        foreach (var id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st == null) continue;
            float d = IconPos(sim, id).DistanceTo(pos);
            if (d <= EmpDotRadius + 6f && d < bestDist) { best = id; bestDist = d; }
        }
        return best;
    }

    private string RoomAt(Vector2 pos)
    {
        foreach (var roomId in Layout.Keys)
            if (BoxOf(roomId).HasPoint(pos))
                return roomId;
        return null;
    }

    // 상자를 살짝 벗어난 곳에 놓아도 가장 가까운 작업실로 들어간다.
    private string NearestRoom(Vector2 pos)
    {
        string best = null;
        float bestDist = 70f;
        foreach (var roomId in Layout.Keys)
        {
            float d = CenterOf(roomId).DistanceTo(pos);
            if (d >= bestDist) continue;
            bestDist = d;
            best = roomId;
        }
        return best;
    }

    // --- 직원 끌어다 놓기 (수동 구현) --------------------------------------
    //
    // Godot 기본 DnD(_GetDragData/_CanDropData)는 이 미니맵처럼 SubViewport 안에서
    // PushInput 으로 입력을 받는 화면에서는 시작조차 되지 않는다. 게다가 _GuiInput 이
    // 눌림을 AcceptEvent 로 먹어버려서 드래그가 아예 걸리지 않았다.
    // 배치표(ScheduleBoardUI)와 같은 방식으로 눌림 → 이동 → 뗌을 직접 추적한다.
    private string _dragEmp = "";
    private Vector2 _pressPos, _lastDragPos;
    private bool _dragging;

    public bool IsDraggingEmployee => _dragging;

    public override void _Input(InputEvent e)
    {
        if (string.IsNullOrEmpty(_dragEmp))
        {
            // 끌지 않을 때의 이동 = 작업실 호버. CRT 로 밀려 들어오는 이동 이벤트는 _GuiInput 까지
            // 오지 않으므로 여기서 받는다(ScheduleMapView 와 같은 방식).
            if (IsVisibleInTree() && MakeInputLocal(e) is InputEventMouseMotion hover)
                SetHoverRoom(RoomAt(hover.Position));
            return;
        }

        // 이 뷰는 스케일 프레임 안에 있다 — 입력이 뷰포트(확대) 좌표로 들어오므로 로컬로 바꾼다.
        e = MakeInputLocal(e);

        if (e is InputEventMouseMotion mm)
        {
            _lastDragPos = mm.Position;
            if (!_dragging && mm.Position.DistanceTo(_pressPos) > 6f) _dragging = true;
            if (_dragging) QueueRedraw();
        }
        else if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            if (_dragging) DropEmployee(_lastDragPos);
            else OnEmployeeSelected?.Invoke(_dragEmp);   // 끌지 않았으면 그냥 선택
            _dragEmp = "";
            _dragging = false;
            QueueRedraw();
        }
    }

    private void SetHoverRoom(string roomId)
    {
        roomId ??= "";
        if (_hoverRoom == roomId) return;
        _hoverRoom = roomId;
        // 열린 작업실 위로 들어온 순간 한 번 — 잠긴 방은 눌러도 볼 것이 없으니 조용히 지난다.
        if (roomId.Length > 0 && FacilitySimulation.Instance?.IsRoomActive(roomId) == true)
            NSP.Core.Sfx.Instance?.Play("tick", -20f);
        QueueRedraw();
    }

    private void DropEmployee(Vector2 pos)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(pos) ?? NearestRoom(pos);
        if (sim == null || roomId == null) { OnEmployeeSelected?.Invoke(_dragEmp); return; }

        var emp = sim.GetEmployeeState(_dragEmp);
        if (emp == null || !emp.Alive || emp.Isolated) return;
        if (emp.AssignedRoomId == roomId) return;
        // 근무 중 재배치는 근무표 정원(RoomSlotCapacity)에 묶이지 않는다.
        // 사고가 나면 한 방에 셋을 몰아넣어야 할 때가 있고, 그게 이 게임의 조작이다.
        if (!sim.IsRoomActive(roomId)) return;

        // ClearAssignment 없이 바로 재배치 — AssignToRoom 이 이동까지 처리한다.
        sim.AssignToRoom(_dragEmp, roomId);
        OnEmployeeSelected?.Invoke(_dragEmp);
    }

    private void UnusedDropData(Vector2 atPosition, Variant data)
    {
        var sim = FacilitySimulation.Instance;
        string roomId = RoomAt(atPosition);
        if (sim == null || roomId == null) return;
        sim.AssignToRoom(data.AsString(), roomId);
        OnRoomSelected?.Invoke(roomId);
    }
}
