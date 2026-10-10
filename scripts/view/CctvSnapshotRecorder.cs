using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Dialogue;
using NSP.Facility;

namespace NSP.View;

// 사고 · 방해공작 · 이상 개체 사고가 시설 로그에 기록된 순간, **관리자가 그때 보고 있던**
// CCTV 화면을 한 장 떠서 그 사고 자료에 붙인다(관리자 패드 단서 카드의 썸네일).
//
// 보고 있지 않던 방은 찍지 않는다 — 플레이어가 보지 못한 장면이 자료가 되면
// 그 방에 누가 있었는지가 사진으로 새어 나간다(조사 자료의 규칙과 같다).
// CCTV 전력이 꺼졌거나 신호가 끊겼으면 찍지 않는다 — 카드는 NO SIGNAL 로 남는다.
//
// 매 프레임 찍지 않는다. 사건이 난 순간에만, 이미 그려진 CRT 화면을 한 번 읽는다(추가 렌더 없음).
// 디스크에 저장하지 않는다.
public partial class CctvSnapshotRecorder : Node
{
    public static readonly Vector2I SnapshotSize = new(320, 180);

    // 오른쪽 CRT 의 CCTV 화면(노이즈 · REC · 방 이름까지 그려진 그대로).
    public SubViewport Source;

    // 검증용 — 지금까지 붙인 장 수.
    public int Captured { get; private set; }

    private bool _wired;
    private readonly List<(int Day, string Id)> _pending = new();
    private bool _capturing;

    public override void _Process(double delta)
    {
        if (_wired || EventLog.Instance == null) return;
        EventLog.Instance.EntryLogged += OnEntryLogged;
        _wired = true;
    }

    public override void _ExitTree()
    {
        if (_wired && EventLog.Instance != null) EventLog.Instance.EntryLogged -= OnEntryLogged;
    }

    private void OnEntryLogged()
    {
        var list = EventLog.Instance?.GetAllEntries();
        if (list == null || list.Count == 0) return;
        var e = list[^1];
        if (!InterviewEvidenceBoard.IsIncidentType(e.EventType)) return;
        if (!CanSee(e.RoomId)) return;

        string id = InterviewEvidenceBoard.IncidentIdOf(
            InterviewEvidenceBoard.IncidentKeyOf(e.EventType, e.RoomId, e.GameTimeSeconds));
        _pending.Add((e.Day, id));
        if (!_capturing) Capture();
    }

    // 그 방을 지금 CCTV 로 보고 있는가 — 근무 중 · 오른쪽 CRT 가 CCTV · 전원이 들어와 있고 신호가 정상.
    public static bool CanSee(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return false;
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return false;
        if (FacilitySimulation.Instance?.SurveillanceTargetRoomId != roomId) return false;
        if (CCTVMonitorView.Instance?.FeedVisible != true) return false;
        return ControlRoom3DController.Instance?.CctvOnScreen == true;
    }

    // 사건이 난 뒤 화면이 한 번 더 그려지기를 기다렸다가(사건의 결과가 화면에 뜬 뒤) 읽는다.
    private async void Capture()
    {
        _capturing = true;
        for (int i = 0; i < 2; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServerInstance.SignalName.FramePostDraw);
        _capturing = false;

        var shots = new List<(int Day, string Id)>(_pending);
        _pending.Clear();
        var tex = Grab();
        if (tex == null) return;   // 그릴 수 없는 환경(헤드리스 등) — 카드는 NO SIGNAL 로 남는다
        foreach (var (day, id) in shots)
        {
            ClueBoard.OfferSnapshot(day, id, tex);
            Captured++;
        }
    }

    private ImageTexture Grab()
    {
        if (Source == null || !IsInstanceValid(Source)) return null;
        var img = Source.GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) return null;

        // 4:3 CRT 화면의 가운데를 16:9 로 잘라 작은 썸네일로 줄인다.
        int w = img.GetWidth(), h = img.GetHeight();
        int ch = Mathf.Min(h, Mathf.RoundToInt(w * 9f / 16f));
        var crop = img.GetRegion(new Rect2I(0, (h - ch) / 2, w, ch));
        crop.Resize(SnapshotSize.X, SnapshotSize.Y, Image.Interpolation.Bilinear);
        return ImageTexture.CreateFromImage(crop);
    }
}
