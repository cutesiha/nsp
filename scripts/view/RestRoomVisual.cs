using System;
using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 휴게실 안 직원 여섯 명. **표현 전용이다.**
//
// 시뮬레이션은 휴게시간에 아무 일도 하지 않는다(업무도 이동도 없다). 그래서 여기서는
// 판정을 읽기만 하고 — 누가 살아 있는가 · 격리됐는가 · 지금 누가 인터뷰에 불려 갔는가 —
// 그 결과를 자리와 애니메이션으로 옮긴다.
//
//   · 자리      : RestSeating 이 관계도로 정한다(휴게시간마다 다시 뽑는다)
//   · 앉은 자세 : 근무 중 의자 자리와 같은 방식 — 골반(VisualRoot)을 좌판 높이로 내리고
//                 상체 클립을 돌린다(RoomWorkVisualController.ApplySeatOffset 과 같은 계산)
//   · 대화      : 가까이 앉은 둘셋이 한 조가 되어, 한 명이 말하고 나머지는 그쪽을 본다
//   · 인터뷰    : 왼쪽 모니터에서 고른 직원은 자리에서 사라진다(불려 나간 것처럼)
public sealed class RestRoomVisual
{
    // 의자 좌판 높이(room_rest.tscn 의 의자와 같은 값).
    private const float SeatHeight = 0.475f;

    // 한 조가 유지되는 시간. 너무 짧으면 산만하고, 길면 정지 화면처럼 보인다.
    private const double GroupMinSeconds = 6.0;
    private const double GroupMaxSeconds = 11.0;
    // 한 사람이 계속 말하지 않게 — 조 안에서 말하는 사람이 바뀌는 주기.
    private const double TurnMinSeconds = 2.2;
    private const double TurnMaxSeconds = 4.5;

    private readonly Random _rng = new();

    private Node3D _roomNode;
    private readonly List<Vector3> _seatPos = new();
    private readonly List<float> _seatYaw = new();

    // 자리 순서대로의 직원 id(빈 문자열 = 빈 자리).
    private readonly List<string> _order = new();
    // 지금 자리를 뽑을 때 쓴 명단 — 사람이 빠지거나 돌아오면 다시 뽑는다.
    private string _rosterKey = "";

    private sealed class Chat
    {
        public readonly List<string> Members = new();
        public string Speaker = "";
        public double NextTurn;
    }

    // 인터뷰에 불려 나가고 돌아오는 과정. 그냥 사라졌다 나타나면 "UI 가 바뀐 것"으로 보여서,
    // 일어서는 동작(chair_stand)을 한 번 보여 준 뒤에 자리를 비운다.
    private enum Posture { Seated, Standing, Away, Sitting }

    private readonly Dictionary<string, Posture> _posture = new();
    private readonly Dictionary<string, float> _postureT = new();
    private const float StandSeconds = 0.9f;
    private const float SitSeconds = 1.0f;

    private readonly List<Chat> _chats = new();
    private double _nextRegroup;
    private double _now;

    // 자리에 앉은 직원 → 그 자리 번호. 인터뷰로 빠진 사람도 자리는 그대로 비워 둔다.
    public int SeatOf(string employeeId) => _order.IndexOf(employeeId ?? "");

    // 휴게실 씬에서 자리(Marker3D)를 읽어 둔다. 씬이 바뀌면 다시 부른다.
    public void Bind(Node3D roomNode)
    {
        if (roomNode == _roomNode) return;
        _roomNode = roomNode;
        _seatPos.Clear();
        _seatYaw.Clear();
        _order.Clear();
        _rosterKey = "";
        if (roomNode == null) return;

        var seats = roomNode.GetNodeOrNull<Node3D>("Seats");
        if (seats == null)
        {
            GD.PushWarning("RestRoomVisual: 휴게실 씬에 Seats 노드가 없습니다 — 직원을 앉힐 자리가 없습니다.");
            return;
        }
        foreach (var child in seats.GetChildren())
        {
            if (child is not Node3D m) continue;
            _seatPos.Add(m.Position);
            _seatYaw.Add(m.Rotation.Y);
        }
    }

    // 휴게시간 동안 매 프레임. interviewing = 지금 인터뷰에 불려 간 직원(없으면 빈 문자열).
    public void Tick(FacilitySimulation sim, IReadOnlyDictionary<string, Node3D> actors,
                     IReadOnlyDictionary<string, EmployeeCctvAnimator> animators,
                     string interviewing, float delta)
    {
        if (sim == null || _seatPos.Count == 0) return;
        _now += delta;

        EnsureSeating(sim);
        if (_now >= _nextRegroup) Regroup();

        foreach (var (id, node) in actors)
        {
            int seat = _order.IndexOf(id);
            // 휴게실에 아예 없는 사람(사망 · 격리)은 자리도 없다.
            if (seat < 0)
            {
                if (node.Visible) node.Visible = false;
                _posture.Remove(id);
                continue;
            }

            var anim = animators.GetValueOrDefault(id);
            var p = _posture.GetValueOrDefault(id, Posture.Seated);
            bool called = id == interviewing;

            // 부르면 일어나고, 통화가 끝나면 돌아와 다시 앉는다.
            if (called && p is Posture.Seated or Posture.Sitting) { p = Posture.Standing; _postureT[id] = 0f; }
            else if (!called && p is Posture.Away) { p = Posture.Sitting; _postureT[id] = 0f; }

            if (node.Visible != (p != Posture.Away)) node.Visible = p != Posture.Away;
            _posture[id] = p;
            if (p == Posture.Away) continue;

            float t = _postureT.GetValueOrDefault(id) + delta;
            _postureT[id] = t;

            switch (p)
            {
                case Posture.Standing:
                    // 골반이 좌판에서 서 있는 높이로 올라온다 — 근무 중 의자와 같은 곡선.
                    Seat(node, anim, seat, WorkClipReach.StandCurve(Mathf.Clamp(t / StandSeconds, 0f, 1f)));
                    anim?.PlayClip("chair_stand", 0.1);
                    anim?.SetSpeedFactor(1f);
                    if (t >= StandSeconds) { _posture[id] = Posture.Away; node.Visible = false; }
                    break;
                case Posture.Sitting:
                    Seat(node, anim, seat, WorkClipReach.SitCurve(Mathf.Clamp(t / SitSeconds, 0f, 1f)));
                    anim?.PlayClip("chair_sit", 0.12);
                    anim?.SetSpeedFactor(1f);
                    if (t >= SitSeconds) { _posture[id] = Posture.Seated; _postureT[id] = 0f; }
                    break;
                default:
                    Seat(node, anim, seat, 1f);
                    Act(node, anim, id, seat);
                    break;
            }
            anim?.Tick();
            anim?.Update(delta);
        }
    }

    // 자리를 정해 둔다. 아직 안 정했거나 명단이 바뀌었으면(사망 · 격리) 다시 뽑는다.
    //
    // 왼쪽 모니터의 휴게실 지도도 이 자리를 그대로 쓰므로(RestRosterView), 그쪽이 먼저
    // 그려지더라도 같은 답이 나오도록 Tick 밖에서도 부를 수 있게 열어 둔다.
    public void EnsureSeating(FacilitySimulation sim)
    {
        if (sim == null || _seatPos.Count == 0) return;
        var present = PresentEmployees(sim);
        string key = string.Join(",", present);
        if (key == _rosterKey) return;

        _rosterKey = key;
        _order.Clear();
        _order.AddRange(RestSeating.Arrange(present, _seatPos, _rng));
        Regroup();
    }

    // 휴게시간이 끝났다 — 앉히느라 내려 둔 골반을 제자리로 돌린다.
    // 이것을 빼먹으면 다음 근무 첫 몇 프레임 동안 직원들이 바닥에 반쯤 묻힌 채로 서 있다.
    public void Release(IReadOnlyDictionary<string, Node3D> actors)
    {
        foreach (var (_, node) in actors)
        {
            var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
            if (vr != null && !Mathf.IsZeroApprox(vr.Position.Y)) vr.Position = Vector3.Zero;
        }
        _chats.Clear();
        _posture.Clear();
        _postureT.Clear();
        _rosterKey = "";   // 다음 휴게시간에는 자리를 다시 뽑는다
    }

    // 휴게실에 앉아 있을 직원. 죽었거나 격리된 사람은 이 방에 없다.
    private static List<string> PresentEmployees(FacilitySimulation sim)
    {
        var list = new List<string>();
        foreach (string id in sim.GetEmployeeIds())
        {
            var st = sim.GetEmployeeState(id);
            if (st is { Alive: true, Isolated: false }) list.Add(id);
        }
        return list;
    }

    // 앉은 자세 — 근무 중 의자 자리와 같은 계산(골반을 좌판 높이로 내린다).
    // seated = 1 이면 완전히 앉은 상태, 0 이면 선 상태(일어서고 앉는 동안의 중간값).
    private void Seat(Node3D node, EmployeeCctvAnimator anim, int seat, float seated)
    {
        node.Position = _seatPos[seat];
        // 여섯이 자로 잰 듯 같은 방향을 보고 있으면 사람이 아니라 인형으로 보인다 —
        // 자리마다 몸을 조금씩(최대 7°) 틀어 둔다. 자리가 같으면 각도도 같다(매 프레임 흔들리지 않게).
        node.Rotation = new Vector3(0f, _seatYaw[seat] + BodyTilt(seat), 0f);

        var vr = node.GetNodeOrNull<Node3D>("VisualRoot");
        if (vr == null) return;
        float hip = node.GetNodeOrNull<Node3D>("VisualRoot/RigRoot/Hips")?.Position.Y ?? 0.9f;
        vr.Position = new Vector3(0f, (SeatHeight - hip) * Mathf.Clamp(seated, 0f, 1f), 0f);
        anim?.SetProceduralRoot(true);
    }

    private static float BodyTilt(int seat) => Mathf.DegToRad(((seat * 37) % 15) - 7);

    // 지금 무엇을 하고 있는가 — 말하거나, 듣거나, 그냥 쉬거나.
    private void Act(Node3D node, EmployeeCctvAnimator anim, string id, int seat)
    {
        if (anim == null) return;

        // 스토리 컷인이 도는 동안에는 그쪽이 대화의 주인이다. 테이블에 앉은 사람들이
        // **지금 말하는 사람 쪽을 같이 본다** — 그래야 2D 스탠딩 둘만 따로 떨어진
        // 비주얼 노벨이 아니라, 한 테이블에서 오가는 말로 읽힌다.
        string cutinSpeaker = StoryCutinDirector.Instance is { IsPlaying: true }
            ? EmployeeMouthAnimator.Speaker : "";
        if (cutinSpeaker.Length > 0)
        {
            bool isSpeaker = cutinSpeaker == id;
            anim.PlayClip(isSpeaker ? SpeakClip : ListenClip, 0.35);
            anim.SetSpeedFactor(isSpeaker ? 0.85f : 0.4f);
            // 말하는 사람은 테이블 가운데를 보고, 나머지는 말하는 사람 쪽을 본다.
            anim.SetLook(isSpeaker ? 0f : YawTo(seat, cutinSpeaker), isSpeaker ? 0.3f : 0.95f, 3.2f);
            return;
        }

        var chat = ChatOf(id);

        // 쓸 수 있는 클립은 **앉은 자세가 들어 있는 것**뿐이다. idle/talk 은 서 있는 클립이라
        // 다리가 펴져 의자를 뚫는다(근무 중 의자 자리도 같은 이유로 sit_* 를 쓴다).
        // 말하는 사람과 듣는 사람은 팔 움직임이 다른 클립으로 갈라 놓고,
        // "누가 누구를 보는가" 는 고개(SetLook)가 말한다 — 대화로 읽히는 건 결국 시선이다.
        if (chat == null)
        {
            // 대화에 끼지 않은 사람. 가만히 앉아 쉬면서 가끔 시선만 옮긴다.
            anim.PlayClip(ListenClip, 0.35);
            anim.SetSpeedFactor(0.45f + (seat % 3) * 0.07f);
            anim.SetLook(IdleGaze(seat), 0.45f, 2.2f);
            return;
        }

        bool speaking = chat.Speaker == id;
        anim.PlayClip(speaking ? SpeakClip : ListenClip, 0.35);
        // 말하는 사람은 손이 조금 더 움직이고, 듣는 사람은 거의 가만히 있는다.
        anim.SetSpeedFactor(speaking ? 0.85f : 0.45f + (seat % 3) * 0.07f);
        // 듣는 사람은 말하는 사람 쪽을, 말하는 사람은 듣는 사람 쪽을 본다.
        string target = speaking ? FirstOther(chat, id) : chat.Speaker;
        anim.SetLook(YawTo(seat, target), speaking ? 0.7f : 0.9f, 3.5f);
    }

    // 앉은 자세가 들어 있는 공용 클립. 둘 다 상체·팔만 움직이고 다리는 앉은 채로 있다.
    private const string ListenClip = "sit_typing";
    private const string SpeakClip = "sit_assemble";

    // 자리 i 에서 그 직원 쪽으로 고개를 돌리는 각도(몸 기준).
    private float YawTo(int seat, string targetId)
    {
        int t = _order.IndexOf(targetId ?? "");
        if (t < 0 || t == seat) return 0f;
        var d = _seatPos[t] - _seatPos[seat];
        // 캐릭터는 자기 -Z 를 본다 — 몸이 이미 돌아간 만큼(_seatYaw)을 빼야 목만 돌아간다.
        float world = Mathf.Atan2(-d.X, -d.Z);
        return Mathf.Wrap(world - _seatYaw[seat], -Mathf.Pi, Mathf.Pi);
    }

    // 대화에 끼지 않은 사람의 시선 — 아주 느리게 좌우로 흔들린다(완전히 멈춰 보이지 않게).
    private float IdleGaze(int seat) =>
        Mathf.Sin((float)_now * 0.22f + seat * 1.7f) * 0.5f;

    private Chat ChatOf(string id)
    {
        foreach (var c in _chats)
            if (c.Members.Contains(id)) return c;
        return null;
    }

    private static string FirstOther(Chat chat, string id)
    {
        foreach (string m in chat.Members)
            if (m != id) return m;
        return "";
    }

    // 대화 조를 다시 짠다. 가까이 앉은 사람끼리 묶고, 몇 명은 일부러 빼 둔다 —
    // 여섯이 전부 동시에 떠들면 "살아 있는 공간"이 아니라 소란이 된다.
    private void Regroup()
    {
        _nextRegroup = _now + GroupMinSeconds + _rng.NextDouble() * (GroupMaxSeconds - GroupMinSeconds);
        _chats.Clear();

        var left = new List<int>();
        for (int i = 0; i < _order.Count; i++)
            if (!string.IsNullOrEmpty(_order[i])) left.Add(i);
        Shuffle(left);

        while (left.Count >= 2)
        {
            int a = left[0];
            left.RemoveAt(0);
            // 가장 가까이 앉은 사람을 상대로 고른다(옆자리 > 마주봄 > 대각).
            int bestIdx = -1;
            float bestClose = 0f;
            for (int k = 0; k < left.Count; k++)
            {
                float c = RestSeating.Closeness(_seatPos[a], _seatPos[left[k]]);
                if (c > bestClose) { bestClose = c; bestIdx = k; }
            }
            if (bestIdx < 0) break;
            int b = left[bestIdx];
            left.RemoveAt(bestIdx);

            var chat = new Chat();
            chat.Members.Add(_order[a]);
            chat.Members.Add(_order[b]);

            // 가끔 셋이 모인다 — 둘씩만 갈리면 화면이 규칙적으로 보인다.
            if (left.Count > 0 && _rng.NextDouble() < 0.35)
            {
                int cIdx = -1;
                float cClose = 0f;
                for (int k = 0; k < left.Count; k++)
                {
                    float c = Mathf.Max(RestSeating.Closeness(_seatPos[a], _seatPos[left[k]]),
                                        RestSeating.Closeness(_seatPos[b], _seatPos[left[k]]));
                    if (c > cClose) { cClose = c; cIdx = k; }
                }
                if (cIdx >= 0) { chat.Members.Add(_order[left[cIdx]]); left.RemoveAt(cIdx); }
            }

            chat.Speaker = chat.Members[_rng.Next(chat.Members.Count)];
            chat.NextTurn = _now + TurnMinSeconds;
            _chats.Add(chat);

            // 한 조를 만들 때마다 한 번은 아무도 안 묶고 남겨 둔다(가만히 있는 사람).
            if (left.Count > 1 && _rng.NextDouble() < 0.4) left.RemoveAt(0);
        }
    }

    // 조 안에서 말하는 사람을 바꾼다. Tick 과 따로 두어 대사 박자가 자리 계산에 끌려가지 않게.
    public void TickTurns()
    {
        foreach (var c in _chats)
        {
            if (_now < c.NextTurn) continue;
            c.NextTurn = _now + TurnMinSeconds + _rng.NextDouble() * (TurnMaxSeconds - TurnMinSeconds);
            if (c.Members.Count < 2) continue;
            string next = c.Speaker;
            for (int i = 0; i < 4 && next == c.Speaker; i++) next = c.Members[_rng.Next(c.Members.Count)];
            c.Speaker = next;
        }
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int k = _rng.Next(i + 1);
            (list[i], list[k]) = (list[k], list[i]);
        }
    }
}
