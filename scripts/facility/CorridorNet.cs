using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// 차폐문의 상태. 닫히는 중 · 열리는 중이 따로 있는 이유는 문이 즉시 움직이지 않기 때문이다 —
// 그 0.8초 동안 복도를 건너던 직원을 어떻게 할지가 갈린다.
public enum BarrierState
{
    Open,
    Closing,
    Sealed,
    Opening,
    // 전력이 없어 조작 자체가 불가능한 상태(문은 열려 있다 — 안전측).
    Disabled,
}

// 복도 한 구간. 간선(방↔방)은 RoomDef 가 정하고, 여기에는 그 위에 얹힌 상태만 있다.
public sealed class CorridorSegment
{
    public string Id = "";
    public string DisplayName = "";
    public string RoomA = "", RoomB = "";
    public CorridorKind Kind = CorridorKind.Minor;
    public bool IsBlockable;
    public string CctvCameraId = "";
    public bool ThreatLane;

    public float DriveSeconds = 0.8f;
    public float MaxSealSeconds = 9f;
    public float ReengageCooldownSeconds = 3f;

    public BarrierState State = BarrierState.Open;
    // 문이 내려온 정도(0 = 완전히 열림, 1 = 완전히 닫힘). 3D 셔터와 미니맵이 그대로 읽는다.
    public float Shut;
    // 닫고 나서 흐른 시간 / 다시 닫을 수 있게 될 때까지 남은 시간.
    public float SealedSeconds;
    public float CooldownLeft;

    public bool Sealed => State == BarrierState.Sealed;
    public bool Moving => State is BarrierState.Closing or BarrierState.Opening;

    // 지금 이 구간으로 지나갈 수 있는가. **완전히 닫힌 순간부터** 막힌다 —
    // 닫히는 중에는 아직 지나갈 수 있고, 그래서 "문이 내려오는데 뛰어 들어간" 상황이 성립한다.
    public bool Passable => State != BarrierState.Sealed;

    public bool Touches(string roomId) => RoomA == roomId || RoomB == roomId;
    public string Other(string roomId) => RoomA == roomId ? RoomB : RoomA;

    public string StatusText => State switch
    {
        BarrierState.Sealed => "SEALED",
        BarrierState.Closing => "CLOSING",
        BarrierState.Opening => "OPENING",
        BarrierState.Disabled => "NO POWER",
        _ => "OPEN",
    };
}

// 시설의 복도망.
//
// **통행 그래프를 새로 만들지 않는다.** 간선은 지금까지와 똑같이 RoomDef.ConnectedRoomIds 가
// 정하고, 이 클래스는 그 간선마다 구간(CorridorSegment)을 하나씩 만들어 이름 · 차폐문 ·
// 카메라를 얹는다. 길찾기(FacilitySimulation.Neighbors/FindPath)는 여기에 "이 간선 지나갈 수
// 있나" 만 물어본다 — 그 한 줄이 차폐의 전부다.
//
// 상태 기계는 Open → Closing → Sealed → Opening → Open 한 방향으로만 돈다. 닫는 도중에
// 다시 열라고 해도 상태가 꼬이지 않도록 Closing/Opening 중의 지시는 Shut 값을 되돌리는
// 것으로 처리한다(문이 중간에서 방향을 바꾼다).
public sealed class CorridorNet
{
    private const string Folder = "res://data/corridors";

    // 동시에 닫아 둘 수 있는 문의 수. 데이터로 뺄 만큼 흔들 값이 아니라 여기 상수로 둔다.
    public const int MaxSealed = 1;

    private readonly List<CorridorSegment> _segments = new();
    private readonly Dictionary<string, CorridorSegment> _byId = new();
    private readonly Dictionary<string, CorridorSegment> _byEdge = new();
    private static List<CorridorDef> _defs;

    public IReadOnlyList<CorridorSegment> Segments => _segments;

    // 지금 관리자가 조작 대상으로 고른 구간(차폐 가능한 것만). 비어 있으면 고르지 않은 것.
    public string SelectedId { get; private set; } = "";

    // 문 상태가 바뀐 순간 — 미니맵 · 3D 셔터 · 소리가 듣는다. (구간, 새 상태)
    public event Action<CorridorSegment, BarrierState> StateChanged;

    // 길이 바뀌어 다시 찾아야 한다 — FacilitySimulation 이 받아 직원 경로를 손본다.
    public event Action<CorridorSegment> PathsDirty;

    // ── 구성 ────────────────────────────────────────────────────────

    public void Build(IReadOnlyDictionary<string, RoomDef> roomDefs)
    {
        _segments.Clear();
        _byId.Clear();
        _byEdge.Clear();
        LoadDefs();

        // ① 방 그래프의 간선을 전부 구간으로 만든다(무향 — 한 번만).
        foreach (var def in roomDefs.Values)
        {
            if (def?.ConnectedRoomIds == null) continue;
            foreach (string other in def.ConnectedRoomIds)
            {
                if (!roomDefs.ContainsKey(other)) continue;
                string key = EdgeKey(def.RoomId, other);
                if (_byEdge.ContainsKey(key)) continue;
                var seg = new CorridorSegment
                {
                    Id = "corridor_" + key.Replace("|", "_"),
                    RoomA = string.CompareOrdinal(def.RoomId, other) < 0 ? def.RoomId : other,
                    RoomB = string.CompareOrdinal(def.RoomId, other) < 0 ? other : def.RoomId,
                };
                seg.DisplayName = $"{Name(roomDefs, seg.RoomA)} ↔ {Name(roomDefs, seg.RoomB)} 통로";
                _byEdge[key] = seg;
                _segments.Add(seg);
            }
        }

        // ② 데이터가 있는 간선에만 이름 · 차폐문 · 카메라를 덧씌운다.
        foreach (var d in _defs)
        {
            string key = EdgeKey(d.RoomA, d.RoomB);
            if (!_byEdge.TryGetValue(key, out var seg))
            {
                // 실제 그래프에 없는 조합 — 여기서 새 길을 만들지 않는다(그러면 진실원이 둘이 된다).
                GD.PushWarning($"CorridorNet: {d.SegmentId} 의 {d.RoomA}↔{d.RoomB} 는 방 그래프에 없는 연결입니다.");
                continue;
            }
            if (!string.IsNullOrEmpty(d.SegmentId)) seg.Id = d.SegmentId;
            if (!string.IsNullOrEmpty(d.DisplayName)) seg.DisplayName = d.DisplayName;
            seg.Kind = d.Kind;
            seg.IsBlockable = d.IsBlockable;
            seg.CctvCameraId = d.CctvCameraId ?? "";
            seg.ThreatLane = d.ThreatLane;
            seg.DriveSeconds = Mathf.Max(0.05f, d.DriveSeconds);
            seg.MaxSealSeconds = d.MaxSealSeconds;
            seg.ReengageCooldownSeconds = Mathf.Max(0f, d.ReengageCooldownSeconds);
        }

        foreach (var seg in _segments) _byId[seg.Id] = seg;

        // 고를 수 있는 것이 하나뿐이면 처음부터 그것을 골라 둔다 — 매번 고르게 할 이유가 없다.
        var blockable = Blockable.ToList();
        SelectedId = blockable.Count > 0 ? blockable[0].Id : "";
    }

    private static void LoadDefs()
    {
        if (_defs != null) return;
        _defs = new List<CorridorDef>();
        foreach (string path in ResourceDir.ListFiles(Folder, ".tres"))
        {
            var res = GD.Load<CorridorDef>(path);
            if (res != null) _defs.Add(res);
        }
        _defs.Sort((a, b) => string.CompareOrdinal(a.SegmentId, b.SegmentId));
    }

    // 검사 · 에디터용 — 데이터를 다시 읽는다.
    public static void ReloadDefs() => _defs = null;

    private static string Name(IReadOnlyDictionary<string, RoomDef> defs, string id) =>
        defs.TryGetValue(id, out var d) ? d.DisplayName : id;

    private static string EdgeKey(string a, string b) =>
        string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

    // ── 조회 ────────────────────────────────────────────────────────

    public IEnumerable<CorridorSegment> Blockable => _segments.Where(s => s.IsBlockable);

    public CorridorSegment ById(string id) =>
        string.IsNullOrEmpty(id) ? null : _byId.GetValueOrDefault(id);

    public CorridorSegment Between(string a, string b) =>
        _byEdge.GetValueOrDefault(EdgeKey(a, b));

    public CorridorSegment Selected => ById(SelectedId);

    // 길찾기가 묻는 단 하나의 질문. 구간 정보가 없는 간선은 언제나 열려 있다.
    public bool IsPassable(string a, string b)
    {
        var seg = Between(a, b);
        return seg == null || seg.Passable;
    }

    public int SealedCount => _segments.Count(s => s.State is BarrierState.Sealed or BarrierState.Closing);

    // 지금 닫으라고 하면 받아 줄 수 있는가. 안 되면 이유를 돌려준다(화면에 그대로 띄운다).
    public bool CanSeal(CorridorSegment seg, out string reason)
    {
        reason = "";
        if (seg == null) { reason = "선택된 통로가 없습니다."; return false; }
        if (!seg.IsBlockable) { reason = "해당 통로는 차폐 불가"; return false; }
        if (!HasPower) { reason = "차폐 전력 없음"; return false; }
        if (seg.CooldownLeft > 0f) { reason = $"구동부 냉각 중 {seg.CooldownLeft:0.0}초"; return false; }
        if (seg.State is BarrierState.Sealed or BarrierState.Closing) { reason = "이미 차폐 중"; return false; }
        if (SealedCount >= MaxSealed)
        {
            var other = _segments.FirstOrDefault(s => s != seg && s.State is BarrierState.Sealed or BarrierState.Closing);
            reason = $"차폐 한도({MaxSealed}) — {other?.DisplayName ?? "다른 통로"} 를 먼저 여십시오";
            return false;
        }
        return true;
    }

    // 차폐 계통에 전력이 들어와 있는가.
    private static bool HasPower => GameState.Instance?.IsConsumerPowered(PowerConsumer.Barrier) ?? false;

    // ── 조작 ────────────────────────────────────────────────────────

    // 제어 대상만 바꾼다. **문은 건드리지 않는다** — 고른 통로가 바뀌었다고 다른 문이
    // 저절로 열리거나 닫히면 레버가 무엇을 가리키는지 알 수 없게 된다.
    public bool Select(string segmentId)
    {
        var seg = ById(segmentId);
        if (seg == null || SelectedId == segmentId) return false;
        SelectedId = segmentId;
        return true;
    }

    // 고른 통로의 문을 닫는다. 성공하면 true.
    public bool Seal(string segmentId, out string reason)
    {
        var seg = ById(segmentId);
        if (!CanSeal(seg, out reason)) return false;
        seg.State = BarrierState.Closing;
        seg.SealedSeconds = 0f;
        Raise(seg);
        return true;
    }

    // 문을 연다. 닫히는 중이어도 그 자리에서 되돌아 열린다.
    public bool Unseal(string segmentId)
    {
        var seg = ById(segmentId);
        if (seg == null || seg.State is BarrierState.Open or BarrierState.Opening) return false;
        bool wasSealed = seg.State == BarrierState.Sealed;
        seg.State = BarrierState.Opening;
        seg.SealedSeconds = 0f;
        // 냉각은 **닫혔던 문을 열 때만** 건다. 닫히다 만 문까지 묶으면 조작이 답답해진다.
        if (wasSealed) seg.CooldownLeft = seg.ReengageCooldownSeconds;
        Raise(seg);
        if (wasSealed) PathsDirty?.Invoke(seg);
        return true;
    }

    public bool Toggle(string segmentId, out string reason)
    {
        var seg = ById(segmentId);
        reason = "";
        if (seg == null) { reason = "선택된 통로가 없습니다."; return false; }
        if (seg.State is BarrierState.Sealed or BarrierState.Closing) return Unseal(segmentId);
        return Seal(segmentId, out reason);
    }

    // 전부 연다 — 근무 종료 · 새 근무 · 타이틀 복귀. 전날 문이 닫힌 채로 남지 않게 한다.
    public void ResetAll()
    {
        foreach (var seg in _segments)
        {
            seg.State = BarrierState.Open;
            seg.Shut = 0f;
            seg.SealedSeconds = 0f;
            seg.CooldownLeft = 0f;
        }
    }

    // ── 진행 ────────────────────────────────────────────────────────

    public void Tick(float delta)
    {
        bool power = HasPower;
        foreach (var seg in _segments)
        {
            if (seg.CooldownLeft > 0f) seg.CooldownLeft = Mathf.Max(0f, seg.CooldownLeft - delta);
            if (!seg.IsBlockable) continue;

            // 전력이 끊기면 문은 **열린다**. 닫힌 채로 멈추면 직원이 영영 고립된다 —
            // 관리자에게는 불리하지만 그게 안전측 기본값이다(지시서 §4-3).
            if (!power && seg.State is BarrierState.Sealed or BarrierState.Closing)
            {
                bool wasSealed = seg.State == BarrierState.Sealed;
                seg.State = BarrierState.Opening;
                seg.SealedSeconds = 0f;
                Raise(seg);
                if (wasSealed) PathsDirty?.Invoke(seg);
            }

            switch (seg.State)
            {
                case BarrierState.Closing:
                    seg.Shut = Mathf.Min(1f, seg.Shut + delta / seg.DriveSeconds);
                    if (seg.Shut < 1f) break;
                    seg.State = BarrierState.Sealed;
                    seg.SealedSeconds = 0f;
                    Raise(seg);
                    // **여기서부터** 길이 끊긴다. 건너던 사람 처리도 이 신호를 받아서 한다.
                    PathsDirty?.Invoke(seg);
                    break;

                case BarrierState.Sealed:
                    seg.SealedSeconds += delta;
                    // 유지 시간 상한 — 내려 두기만 해도 무적이 되는 것을 막는다.
                    if (seg.MaxSealSeconds > 0f && seg.SealedSeconds >= seg.MaxSealSeconds)
                        Unseal(seg.Id);
                    break;

                case BarrierState.Opening:
                    seg.Shut = Mathf.Max(0f, seg.Shut - delta / seg.DriveSeconds);
                    if (seg.Shut > 0f) break;
                    seg.State = BarrierState.Open;
                    Raise(seg);
                    break;
            }
        }
    }

    private void Raise(CorridorSegment seg) => StateChanged?.Invoke(seg, seg.State);
}
