using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.View;

// 근무 중 딴짓을 CCTV 에서 그린다 — 같은 방 **안에서의** 표현 위치와 클립만 정한다.
//
// 여우가 바닥에 눕고 고양이가 벽에 기대도 실제로 다른 방에 간 것이 아니다. 여기서 옮기는 것은
// 3D 노드의 Position 뿐이며 CurrentRoomId · AssignedRoomId · IsMoving · 시설 로그는 읽지도 쓰지도 않는다.
//
// **발생·진행·목격 판정은 전부 DistractionSystem(시뮬레이션)이 쥐고 있다.** 여기서 하는 일은 셋뿐이다.
//   ① 그 상태(단계 · 경과 시간)를 클립과 위치로 옮긴다
//   ② 지금 **실제로 화면에 보이고 있는가**를 그쪽에 알린다(NoteSeen) — 목격 판정의 근거
//   ③ 방 구조상 그 행동을 할 자리가 없으면 되돌린다(ReportPlaceUnavailable)
// 그래서 CCTV 채널을 돌렸다 돌아와도 같은 행동이 그 사이 흐른 만큼 진행돼 있다.
public sealed partial class RoomWorkVisualController
{
    private const float DisBodyRadius = 0.30f;      // 설비에서 이만큼은 떨어져 선다
    private const float DisPersonalSpace = 0.72f;   // 다른 직원과 이만큼은 떨어진다
    private const float DisFloorClearance = 0.62f;  // 누울 자리 — 설비에서 떨어진 빈 바닥
    private const float DisFloorBodyLength = 0.85f; // 누운 몸이 머리 쪽으로 뻗는 길이
    private const float WallStandOff = 0.145f;      // 벽면에서 몸 중심까지(등이 벽에 닿는 거리)
    private const float RoomHalf = 2.925f;          // 벽 안쪽 면(6×6 방 + 두께 0.15)

    private enum DisStage { Pre, Walk, Turn, Enter, Loop }

    // 딴짓 한 번의 연출 기억. 사건(DistractionEvent.Serial)마다 새로 만든다.
    private sealed class DisVisual
    {
        public int Serial;
        public DistractionKind Kind;
        public DisStage Stage;
        public float StageT;
        public bool Settled;             // 자리를 잡고 반복 동작에 들어갔다
        public Vector3 Goal;             // 딴짓을 하는 자리
        public float GoalYaw;
        public RoomWorkSpot Home;        // 끝나면 돌아갈 업무 자리
        public RoomWorkSpot Target;      // 다른 설비를 살피는 딴짓의 대상
        public float LoopT;              // 반복 클립이 몇 초째인가(돌아와서 이어 볼 때의 시작점)
        public float SfxPrev = -1f;      // 지난 프레임의 **실제 클립 재생 위치**(접촉음 타이밍)
        public float BoostLeft;          // 복귀 직후 작업 모션 가속이 남은 시간
        public bool NeedsPlace;          // 화면 밖에 있다가 다시 보였다 — 자리에 바로 둔다
        public bool Reported;            // 적발 반응을 재생했다고 알렸다
    }

    // ── 한 프레임 ────────────────────────────────────────────────────────

    // 돌려주는 값이 true 면 이 프레임의 표현을 여기서 다 정했다는 뜻이다(호출한 쪽은 continue).
    private bool TickDistraction(FacilitySimulation sim, string id, Node3D node, EmployeeCctvAnimator anim,
                                 Actor actor, List<RoomWorkSpot> spots, string taskId, string roomId,
                                 Dictionary<RoomWorkSpot, int> used, bool female, float delta)
    {
        var ds = sim?.Distractions;
        var ev = ds?.ActiveOf(id);
        if (ev == null)
        {
            // 끝났다 — 작업 모션 가속만 남아 있으면 그것까지 흘려보낸다.
            if (actor.Dis == null) return false;
            PhoneShow(actor, false);
            if (actor.Dis.BoostLeft > 0f)
            {
                actor.Dis.BoostLeft -= delta;
                anim?.SetSpeedFactor(SpeedBoost(actor.Dis));
                if (actor.Dis.BoostLeft > 0f) return false;
            }
            actor.Dis = null;
            return false;
        }

        var prof = DistractionProfiles.Get(ev.Kind);
        if (prof == null) return false;

        // 저장고 상자를 나르는 중이면 지금 할 수 없는 행동이다 — 조용히 되돌린다(아직 아무도 못 봤다).
        if (actor.Carrier >= 0)
        {
            ds.ReportPlaceUnavailable(id);
            ClearDistraction(actor);
            return false;
        }

        var d = actor.Dis;
        if (d == null || d.Serial != ev.Serial)
        {
            d = new DisVisual { Serial = ev.Serial, Kind = ev.Kind };
            if (!PickGoal(actor, ev, prof, spots, taskId, used, female, id, d))
            {
                // 이 방에는 그 행동을 할 자리가 없다(지시서 §12-3).
                ds.ReportPlaceUnavailable(id);
                ClearDistraction(actor);
                return false;
            }
            actor.Dis = d;
            // 이미 진행 중인 사건을 뒤늦게 보게 됐다 — 그 사이 가 있었을 자리에 바로 둔다.
            if (ev.Phase != DistractionPhase.Entering) d.NeedsPlace = true;
        }

        // 복귀 단계는 기존 작업 자리 처리에 넘긴다 — 걸어가서 하던 일을 다시 한다.
        // 자리 점유는 그쪽이 세므로 여기서 미리 세지 않는다(두 번 세면 "찼다" 가 되어 다른 자리로 간다).
        if (ev.Phase == DistractionPhase.Returning)
        {
            actor.Spot = d.Home;
            PhoneShow(actor, false);
            d.BoostLeft = prof.WorkSpeedSeconds;
            anim?.SetSpeedFactor(SpeedBoost(d));
            return false;
        }

        // 돌아갈 자리는 계속 잡아 둔다 — 그 사이 다른 사람이 차지하면 돌아갈 곳이 없어진다.
        if (d.Home != null)
        {
            used[d.Home] = used.GetValueOrDefault(d.Home) + 1;
            actor.Spot = d.Home;
        }

        // 앉아서 일하던 중이면 먼저 일어난다(의자에서 곧바로 다른 자세로 바뀌지 않게).
        if (!StandUpFirst(node, anim, actor, female, delta)) return true;
        HideBox(actor);
        ShowProps(actor, "");

        // 지금 **실제로** 보이고 있는가 — 소리와 목격 판정의 기준.
        bool onScreen = FeedLive(roomId) && node.Visible;

        switch (ev.Phase)
        {
            case DistractionPhase.Entering:
            case DistractionPhase.Distracted:
                if (!d.Settled)
                {
                    TickSettle(node, anim, actor, d, prof, onScreen, delta);
                    SyncPhone(actor, d, prof, ev);
                    if (!d.Settled) return true;
                }
                SyncPhone(actor, d, prof, ev);
                TickLoop(node, anim, d, prof, onScreen, delta);
                // 딴짓을 하는 중이고, 화면에 실제로 보인다 — 목격 판정은 그쪽이 한다.
                if (onScreen && ev.Phase == DistractionPhase.Distracted) ds.NoteSeen(id, delta);
                return true;

            case DistractionPhase.Noticed:
                if (!d.Settled) SnapToGoal(node, d);
                SyncPhone(actor, d, prof, ev);
                TickNotice(node, anim, d, prof, ev, delta);
                if (!d.Reported) { d.Reported = true; ds.NoteReacted(id); }
                return true;

            case DistractionPhase.Recovering:
                if (!d.Settled) SnapToGoal(node, d);
                TickRecover(node, anim, d, prof, ev, delta);
                SyncPhone(actor, d, prof, ev);
                return true;
        }
        return true;
    }

    // ① 자리 잡기 — 준비 동작 → 걸어가기 → 몸 돌리기 → 자세 잡기.
    private void TickSettle(Node3D node, EmployeeCctvAnimator anim, Actor actor, DisVisual d,
                            DistractionProfile prof, bool onScreen, float delta)
    {
        // 화면 밖에 있던 동안 이미 도착했을 자리로 바로 옮긴다(걸어오는 장면을 다시 보여주지 않는다).
        if (d.NeedsPlace)
        {
            d.NeedsPlace = false;
            SnapToGoal(node, d);
            return;
        }

        switch (d.Stage)
        {
            case DisStage.Pre:
                if (!string.IsNullOrEmpty(prof.PreClip) && d.StageT < prof.PreSeconds)
                {
                    // 하던 자리에서 — 휴대폰을 꺼내거나 주변을 살핀다.
                    ClearVisualOffset(node);
                    anim?.PlayClip(prof.PreClip, 0.2);
                    d.StageT += delta;
                    return;
                }
                d.Stage = DisStage.Walk;
                d.StageT = 0f;
                d.SfxPrev = -1f;
                actor.HasRoute = false;
                goto case DisStage.Walk;

            case DisStage.Walk:
                if (Flat(node.Position).DistanceTo(d.Goal) > ArriveDistance
                    && !FollowRoute(node, anim, actor, d.Goal, delta))
                    return;
                node.Position = d.Goal;
                d.Stage = DisStage.Turn;
                d.StageT = 0f;
                goto case DisStage.Turn;

            case DisStage.Turn:
                if (!TurnTo(node, anim, d.GoalYaw, delta)) return;
                d.Stage = DisStage.Enter;
                d.StageT = 0f;
                d.SfxPrev = -1f;
                goto case DisStage.Enter;

            case DisStage.Enter:
                node.Position = d.Goal;
                node.Rotation = new Vector3(0f, d.GoalYaw, 0f);
                anim?.PlayClip(prof.EnterClip, 0.25);
                ClipSfx(anim, d, prof.EnterClip, prof.EnterSfx, loop: false, onScreen);
                d.StageT += delta;
                if (d.StageT < prof.EnterSeconds) return;
                d.Stage = DisStage.Loop;
                d.StageT = 0f;
                d.LoopT = 0f;
                d.SfxPrev = -1f;
                d.Settled = true;
                return;
        }
    }

    // ② 반복 — 딴짓을 하고 있다. 자리와 방향은 고정, 접촉음만 클립 박자에 맞춘다.
    private void TickLoop(Node3D node, EmployeeCctvAnimator anim, DisVisual d, DistractionProfile prof,
                          bool onScreen, float delta)
    {
        node.Position = d.Goal;
        node.Rotation = new Vector3(0f, d.GoalYaw, 0f);
        anim?.PlayClip(prof.LoopClip, 0.22, d.LoopT);
        ClipSfx(anim, d, prof.LoopClip, prof.LoopSfx, loop: true, onScreen);
        d.LoopT += delta;
        // 딴짓에 몰입해 있다 — 카메라를 보지 않는다(보는 순간이 곧 적발이다).
        anim?.SetLook(0f, 0f, 8f);
    }

    // ③ 적발 — 개인별 반응. 고개(Neck)가 몸보다 먼저 카메라를 향한다.
    private void TickNotice(Node3D node, EmployeeCctvAnimator anim, DisVisual d, DistractionProfile prof,
                            DistractionEvent ev, float delta)
    {
        float t = ev.PhaseElapsed;
        node.Position = d.Goal;
        anim?.PlayClip(prof.NoticeClip, 0.06, t);

        // 몸은 개인차만큼만 돌아간다(양은 황급히 돌아보고, 늑대는 거의 그대로다).
        float camYaw = YawTo(Flat(node.Position), Flat(DistractionProfiles.CameraPoint), d.GoalYaw);
        float want = Mathf.LerpAngle(d.GoalYaw, camYaw, Mathf.Clamp(prof.BodyTurnToCamera, 0f, 1f));
        node.Rotation = new Vector3(0f,
            Mathf.LerpAngle(node.Rotation.Y, want, 1f - Mathf.Exp(-prof.BodyTurnRate * delta)), 0f);

        float w = t >= prof.NoticeLookDelay ? prof.LookInNotice : 0f;
        anim?.SetLook(Mathf.AngleDifference(node.Rotation.Y, camYaw), w, prof.LookRate);
    }

    // ④ 정리 — 휴대폰을 넣고, 일어나고, 자세를 바로잡는다. 끝나면 복귀 단계로 넘어간다.
    private void TickRecover(Node3D node, EmployeeCctvAnimator anim, DisVisual d, DistractionProfile prof,
                             DistractionEvent ev, float delta)
    {
        float t = ev.PhaseElapsed;
        node.Position = d.Goal;
        anim?.PlayClip(prof.RecoverClip, 0.22, t);

        // 몸을 원래 방향으로 되돌린다.
        node.Rotation = new Vector3(0f,
            Mathf.LerpAngle(node.Rotation.Y, d.GoalYaw, 1f - Mathf.Exp(-3.5f * delta)), 0f);
        // 시선은 서서히 풀린다.
        float camYaw = YawTo(Flat(node.Position), Flat(DistractionProfiles.CameraPoint), d.GoalYaw);
        float span = Mathf.Max(0.4f, (ev.Def?.RecoverSeconds ?? 1.5f) * 0.55f);
        float w = Mathf.Max(0f, prof.LookInNotice * (1f - t / span));
        anim?.SetLook(Mathf.AngleDifference(node.Rotation.Y, camYaw), w, prof.LookRate);
    }

    private static void SnapToGoal(Node3D node, DisVisual d)
    {
        node.Position = d.Goal;
        node.Rotation = new Vector3(0f, d.GoalYaw, 0f);
        d.Settled = true;
        d.Stage = DisStage.Loop;
        d.NeedsPlace = false;
    }

    private static float SpeedBoost(DisVisual d)
    {
        var prof = DistractionProfiles.Get(d.Kind);
        if (prof == null || d.BoostLeft <= 0f || prof.WorkSpeedSeconds <= 0f) return 1f;
        // 끝으로 갈수록 평소 속도로 돌아온다.
        float k = Mathf.Clamp(d.BoostLeft / prof.WorkSpeedSeconds, 0f, 1f);
        return Mathf.Lerp(1f, prof.WorkSpeedAfter, k * k);
    }

    private static void ClearDistraction(Actor a)
    {
        PhoneShow(a, false);
        a.Dis = null;
    }

    // ── 휴대폰 ───────────────────────────────────────────────────────────

    // 지금 단계에서 손에 들고 있어야 하는가를 매 프레임 맞춘다 —
    // 그래야 CCTV 를 돌렸다 돌아와도(연출 중간에 합류해도) 손과 소품이 어긋나지 않는다.
    private static void SyncPhone(Actor actor, DisVisual d, DistractionProfile prof, DistractionEvent ev)
    {
        if (string.IsNullOrEmpty(prof.PhoneHand)) return;
        bool want = ev.Phase switch
        {
            DistractionPhase.Entering => actor.PhoneOut || PhoneDue(d, prof),
            DistractionPhase.Distracted or DistractionPhase.Noticed => true,
            DistractionPhase.Recovering => ev.PhaseElapsed < prof.PhoneHideAt,
            _ => false,
        };
        PhoneShow(actor, want, prof.PhoneHand);
    }

    // 꺼내는 시점을 지났는가 — 준비 동작이 있으면 그 클립 기준, 없으면 자세 잡기 클립 기준이다.
    private static bool PhoneDue(DisVisual d, DistractionProfile prof)
    {
        if (!string.IsNullOrEmpty(prof.PreClip))
            return d.Stage > DisStage.Pre || d.StageT >= prof.PhoneShowAt;
        return d.Stage >= DisStage.Enter && d.StageT >= prof.PhoneShowAt;
    }

    private static void PhoneShow(Actor actor, bool on, string hand = "R")
    {
        if (actor == null) return;
        if (on)
        {
            if (actor.OwnerNode == null) return;
            actor.Phone = EmployeePhoneProp.Ensure(actor.OwnerNode, ref actor.Phone, hand);
            actor.PhoneOut = actor.Phone != null;
            EmployeePhoneProp.Show(actor.Phone, true);
            return;
        }
        if (!actor.PhoneOut) return;
        actor.PhoneOut = false;
        EmployeePhoneProp.Show(actor.Phone, false);
    }

    // ── 소리 ─────────────────────────────────────────────────────────────

    // 클립의 "그 순간"(발이 기기에 닿는 순간 · 셔터를 누르는 순간)을 이번 프레임에 지났으면 한 번 낸다.
    //
    // 기준은 **애니메이션의 실제 재생 위치**다. 직원마다 재생 속도가 조금씩 다르고(위상 흔들림)
    // 복귀 직후에는 배속도 걸리므로, 따로 센 시간으로 소리를 내면 화면과 어긋난다.
    private static void ClipSfx(EmployeeCctvAnimator anim, DisVisual d, string clip,
                                (float At, string Key, float Db, float Pitch)[] list, bool loop, bool onScreen)
    {
        if (anim == null || list == null || list.Length == 0) return;
        if (anim.CurrentClip != clip) { d.SfxPrev = -1f; return; }
        float now = anim.CurrentClipTime;
        float prev = d.SfxPrev;
        d.SfxPrev = now;
        if (prev < 0f || !onScreen) return;
        foreach (var (at, key, db, pitch) in list)
            if (Crossed(prev, now, at, loop))
                Sfx.Instance?.Play(key, db, pitch);
    }

    // 반복 클립은 끝에서 처음으로 되돌아간다 — 그 이음매를 넘는 경우도 '지났다' 로 본다.
    private static bool Crossed(float prev, float now, float at, bool loop)
    {
        if (now >= prev) return prev < at && now >= at;
        return loop && (at > prev || at <= now);
    }

    // 관리자가 **지금 이 방을 실제로 보고 있는가** — 전력 · 채널 · 신호 · CRT 표시까지 모두.
    // (사고 사진을 찍는 조건과 같은 판정을 쓴다. 화면이 꺼져 있으면 목격도 소리도 없다.)
    private static bool FeedLive(string roomId) =>
        DistractionProfiles.ForceFeedLive || CctvSnapshotRecorder.CanSee(roomId);

    // ── 자리 고르기 ───────────────────────────────────────────────────────

    private bool PickGoal(Actor actor, DistractionEvent ev, DistractionProfile prof,
                          List<RoomWorkSpot> spots, string taskId, Dictionary<RoomWorkSpot, int> used,
                          bool female, string id, DisVisual d)
    {
        var def = ev.Def;
        if (def == null) return false;

        // 돌아갈 업무 자리 — 지금 쓰던 자리를 그대로 들고 있는다.
        d.Home = actor.Spot ?? PickSpot(spots, taskId, null, used);
        if (d.Home == null || IsLyingSpot(d.Home)) return false;

        switch (def.Place)
        {
            case DistractionPlace.AtSpot:
                d.Goal = SpotStand(d.Home, prof, female);
                d.GoalYaw = FaceCamera(d.Goal, d.Home.Rotation.Y, prof.FaceCameraBias);
                return true;

            case DistractionPlace.OtherSpot:
            {
                var other = PickOtherSpot(spots, d.Home, used);
                if (other == null) return false;
                d.Target = other;
                d.Goal = SpotStand(other, prof, female);
                d.GoalYaw = other.Rotation.Y;
                return Free(id, d.Goal, DisPersonalSpace * 0.8f);
            }

            case DistractionPlace.HideSpot:
            {
                var best = PickHidePoint(id);
                if (best == null) return false;
                d.Goal = best.Value;
                // 방 안쪽을 향해 웅크린다 — 구석을 등지고 앉는 그림이 된다.
                d.GoalYaw = YawTo(d.Goal, Vector3.Zero, 0f);
                return true;
            }

            case DistractionPlace.WallLean:
            {
                if (!PickWallLean(id, out var p, out float yaw)) return false;
                d.Goal = p;
                d.GoalYaw = yaw;
                return true;
            }

            case DistractionPlace.FreeFloor:
            {
                if (!PickFloorSpot(id, out var p, out float yaw)) return false;
                d.Goal = p;
                d.GoalYaw = yaw;
                return true;
            }
        }
        return false;
    }

    // 몸을 카메라 쪽으로 조금 튼다 — 설비만 보고 서면 등만 보여 딴짓이 읽히지 않는다.
    private static float FaceCamera(Vector3 at, float yaw, float bias)
    {
        if (bias <= 0f) return yaw;
        float camYaw = YawTo(at, Flat(DistractionProfiles.CameraPoint), yaw);
        return Mathf.LerpAngle(yaw, camYaw, Mathf.Clamp(bias, 0f, 1f));
    }

    // 설비를 만지는 딴짓은 **손이 실제로 닿는 거리**에 세운다. 아니면 평소 작업 자리 그대로.
    private static Vector3 SpotStand(RoomWorkSpot spot, DistractionProfile prof, bool female)
    {
        var fwd = Flat(-spot.Transform.Basis.Z).Normalized();
        var target = spot.TargetInParent();
        if (!string.IsNullOrEmpty(prof.TouchClip) && target.HasValue
            && DistractionClipReach.TryGet(prof.TouchClip, out float reach, out _))
            return Flat(target.Value) - fwd * reach;
        // 앉는 자리였으면 의자 앞에 선다(딴짓은 모두 서서 한다).
        if (spot.IsSeated) return Flat(spot.Position) + fwd * RoomWorkSpot.SitApproach;
        return StandPos(spot, spot.ClipFor(female), female);
    }

    // 지금 자리가 아닌 다른 설비(손이 닿는 점이 있는 자리)를 고른다.
    private static RoomWorkSpot PickOtherSpot(List<RoomWorkSpot> spots, RoomWorkSpot home,
                                              Dictionary<RoomWorkSpot, int> used)
    {
        foreach (var s in spots)
        {
            if (s == home || IsLyingSpot(s) || IsCarrierSpot(s) || s.IsSeated) continue;
            if (s.TargetInParent() == null) continue;
            if (used.GetValueOrDefault(s) > 0) continue;
            return s;
        }
        return null;
    }

    private Vector3? PickHidePoint(string id)
    {
        if (_roomHide == null) return null;
        Vector3? best = null;
        float bestScore = float.MinValue;
        foreach (var h in _roomHide)
        {
            if (_roomClear != null && _roomClear.At(h) < DisBodyRadius * 0.7f) continue;
            if (!Free(id, h, DisPersonalSpace * 0.8f)) continue;
            // 카메라에서 너무 먼 구석보다 조금 가까운 쪽이 잘 보인다.
            float score = -h.DistanceTo(Flat(DistractionProfiles.CameraPoint));
            if (score > bestScore) { bestScore = score; best = h; }
        }
        return best;
    }

    // 등을 댈 벽면 — 뒷벽(-Z)과 왼쪽 벽(-X)만 있다(카메라가 +X/+Z 코너에 있다).
    private bool PickWallLean(string id, out Vector3 point, out float yaw)
    {
        point = Vector3.Zero;
        yaw = 0f;
        if (_roomClear == null) return false;
        float bestScore = float.MinValue;
        for (int wall = 0; wall < 2; wall++)
        {
            // wall 0 = 뒷벽(몸이 +Z 를 본다) · 1 = 왼쪽 벽(+X 를 본다)
            var inward = wall == 0 ? Vector3.Back : Vector3.Right;
            for (float t = -2.0f; t <= 2.2f; t += 0.35f)
            {
                var p = wall == 0
                    ? new Vector3(t, 0f, -RoomHalf + WallStandOff)
                    : new Vector3(-RoomHalf + WallStandOff, 0f, t);
                // 설비가 벽에 붙어 있으면 그 앞에는 설 수 없다. 앞쪽 빈 공간을 본다.
                if (_roomClear.At(p + inward * 0.32f) < 0.40f) continue;
                if (_roomClear.At(p + inward * 0.8f) < 0.45f) continue;
                if (!Free(id, p, DisPersonalSpace)) continue;
                // 카메라에 잘 보이고(가까운 쪽) 벽 가운데에 가까운 자리를 좋게 본다.
                float score = -p.DistanceTo(Flat(DistractionProfiles.CameraPoint)) - Mathf.Abs(t) * 0.25f;
                if (score <= bestScore) continue;
                bestScore = score;
                point = p;
                yaw = Mathf.Atan2(-inward.X, -inward.Z);
            }
        }
        return bestScore > float.MinValue;
    }

    // 누울 빈 바닥 — 몸이 머리 쪽으로 뻗을 공간까지 있어야 한다. 카메라에 옆모습으로 보이게 눕는다.
    private bool PickFloorSpot(string id, out Vector3 point, out float yaw)
    {
        point = Vector3.Zero;
        yaw = 0f;
        if (_roomClear == null) return false;
        float bestScore = float.MinValue;
        var cam = Flat(DistractionProfiles.CameraPoint);
        for (float x = -2.1f; x <= 2.1f; x += 0.3f)
            for (float z = -2.1f; z <= 2.1f; z += 0.3f)
            {
                var p = new Vector3(x, 0f, z);
                if (_roomClear.At(p) < DisFloorClearance) continue;
                if (!Free(id, p, 1.0f)) continue;
                // 몸은 바라보는 방향의 **반대쪽**(등 뒤)으로 뻗는다 — RigRoot 를 눕히면 머리가 +Z 로 간다.
                float camYaw = YawTo(p, cam, 0f);
                foreach (float side in new[] { 1f, -1f })
                {
                    float y = camYaw + side * Mathf.Pi / 2f;      // 카메라에 옆모습으로
                    var head = p + new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y)) * DisFloorBodyLength;
                    var feet = p - new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y)) * (DisFloorBodyLength * 0.8f);
                    if (_roomClear.At(head) < 0.35f || _roomClear.At(feet) < 0.35f) continue;
                    float score = _roomClear.At(p) - p.DistanceTo(cam) * 0.18f;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    point = p;
                    yaw = y;
                }
            }
        return bestScore > float.MinValue;
    }

    // 다른 직원(또는 이미 정해 둔 자리)과 겹치지 않는가.
    // 방의 자리를 재는 중(PublishPlaces)에는 사람을 보지 않는다 — 방 구조만 묻는 것이다.
    private bool Free(string id, Vector3 p, float space)
    {
        if (_measuringPlaces) return true;
        foreach (var (other, q) in _occupied)
            if (other != id && p.DistanceTo(q) < space) return false;
        return true;
    }

    private bool _measuringPlaces;

    // ── 방마다 어떤 딴짓 자리가 있는가(시작할 때 한 번) ─────────────────────

    // 그 방을 처음 비추는 프레임에 한 번 잰다(Update 안에서 부른다 — 이미 세워 둔 방 정보를 쓴다).
    // 미리 다 재 두고 싶으면 PublishPlaces 를 방마다 부르면 된다(검수 도구가 그렇게 쓴다).
    public void PublishPlaces(Node3D room, string roomId)
    {
        if (room == null || string.IsNullOrEmpty(roomId)) return;
        _roomClear = GetClearance(room, roomId);
        _roomHide = GetHideSpots(room, roomId);
        PublishPlacesNow(room, roomId);
    }

    // 시뮬레이션은 이 결과를 읽어 "누울 자리가 없는 방" 에서 눕는 딴짓을 고르지 않는다.
    private void PublishPlacesNow(Node3D room, string roomId)
    {
        if (room == null || string.IsNullOrEmpty(roomId) || _roomClear == null) return;
        _measuringPlaces = true;

        var places = new List<DistractionPlace> { DistractionPlace.AtSpot };
        if (PickFloorSpot("", out _, out _)) places.Add(DistractionPlace.FreeFloor);
        if (PickWallLean("", out _, out _)) places.Add(DistractionPlace.WallLean);
        if (PickHidePoint("") != null) places.Add(DistractionPlace.HideSpot);

        var spots = GetSpots(room, roomId);
        int usable = 0;
        foreach (var s in spots)
            if (!IsLyingSpot(s) && !IsCarrierSpot(s) && !s.IsSeated && s.TargetInParent() != null) usable++;
        if (usable >= 2) places.Add(DistractionPlace.OtherSpot);

        DistractionPlaces.Publish(roomId, places);
        _measuringPlaces = false;
    }
}
