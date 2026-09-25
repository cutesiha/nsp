using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.View;

namespace NSP.Debug;

// 괴물 동작(GhostBehaviour) 검사 — 표현만 본다. 시뮬레이션은 돌리지 않는다.
//
//   godot --headless --path . res://scenes/debug/GhostPoseTest.tscn
//
// 보는 것:
//   ① 관통 — 머리가 벽 뒤나 바닥 밑으로 들어가지 않는가
//   ② 충돌 — 머리 박기마다 Impact 가 실제로 나는가
//   ③ 박자 — 충돌 간격이 일정하지 않은가(기계처럼 쿵 쿵 쿵 하면 안 된다)
//   ④ 접힘 — 바닥 박기가 모델을 통째로 내리는 게 아니라 무릎·상체를 접는가
//   ⑤ 잠금 — 한 동작이 끝나기 전에 다른 동작으로 넘어가지 않는가
//   ⑥ 비중 — 걷는 동작이 가장 많지 않은가
public partial class GhostPoseTest : Node
{
    private const float Step = 1f / 60f;
    private const float EntityScale = 3.2f;
    private const float StandY = 0.499f * EntityScale;
    // room_base.tscn 의 벽면. 이 뒤로 머리가 넘어가면 관통이다.
    private const float WallBackZ = -2.925f;
    private const float WallLeftX = -2.925f;

    private Node3D _entity;
    private EntityRig _rig;
    private GhostBehaviour _ghost;
    private Node3D _room;

    private readonly List<(GhostImpactKind Kind, Vector3 At, float Time)> _impacts = new();
    private float _clock;
    private int _pass, _fail;

    public override void _Ready()
    {
        var ps = GD.Load<PackedScene>("res://scenes/props/entity.tscn");
        if (ps == null) { GD.PrintErr("entity.tscn 없음"); GetTree().Quit(); return; }
        _entity = ps.Instantiate<Node3D>();
        _entity.Scale = Vector3.One * EntityScale;
        AddChild(_entity);

        // 실제 작업실 하나를 띄운다 — 설비 좌표를 진짜로 쓰는지 보려면 진짜 방이 필요하다.
        var roomPs = GD.Load<PackedScene>("res://scenes/rooms/room_maintenance.tscn");
        if (roomPs != null) { _room = roomPs.Instantiate<Node3D>(); AddChild(_room); }

        _rig = EntityRig.Build(_entity);
        _ghost = new GhostBehaviour();
        _ghost.Attach(_entity, _rig, EntityScale, StandY);
        _ghost.Impact += (kind, at) => _impacts.Add((kind, at, _clock));

        CallDeferred(nameof(RunAll));
    }

    private void RunAll()
    {
        GD.Print("################ 괴물 동작 검사 ################");

        Check(_rig.Valid, "리그가 만들어졌다(팔·다리·상체·머리로 갈렸다)");
        Check(_rig.Torso != null && _rig.Head != null, "상체와 머리가 따로 움직이는 관절이다");

        CheckReachMath();
        CheckPose(GhostPose.WallHeadbang, "벽");
        CheckPose(GhostPose.MachineHeadbang, "설비");
        CheckPose(GhostPose.FloorHeadbang, "바닥");
        CheckPose(GhostPose.RapidHeadbang, "빠른 연타");
        CheckFloorFolds();
        CheckPoseLock();
        CheckMix();

        GD.Print($"\n################ 결과: {_pass} PASS / {_fail} FAIL ################");
        GetTree().Quit();
    }

    // ── ① 도달 거리 계산이 맞는가 ────────────────────────────────────
    private void CheckReachMath()
    {
        GD.Print("\n---------------- 머리 도달 거리 ----------------");
        // 숙일수록 머리가 앞으로 나간다. 그래야 "이만큼 떨어져 서면 겨우 닿는다"가 성립한다.
        float up = EntityRig.HeadFrontLocal(0f, 0f).Z;
        float bent = EntityRig.HeadFrontLocal(0.62f, 0.45f).Z;
        GD.Print($"   똑바로 섰을 때 {up * EntityScale:0.00} · 최대로 꺾었을 때 {bent * EntityScale:0.00} (월드)");
        Check(bent > up, "상체를 꺾으면 머리가 앞으로 나간다");

        float down = EntityRig.HeadFrontLocal(1.48f, 0.55f).Y;
        GD.Print($"   바닥 자세일 때 머리 높이 {down * EntityScale:0.00} (루트 기준)");
        Check(down < 0f, "바닥 자세에서 머리가 루트보다 아래로 내려간다");
    }

    // ── ②③ 충돌과 박자 ──────────────────────────────────────────────
    private void CheckPose(GhostPose pose, string label)
    {
        GD.Print($"\n---------------- {label} 머리 박기 ----------------");
        var gaps = new List<float>();
        int runs = 0, withImpact = 0;
        float worstWall = float.MaxValue, lowestHead = float.MaxValue;
        float worstTarget = float.MaxValue;
        float longest = 0f;

        for (int trial = 0; trial < 24; trial++)
        {
            _ghost.Reset(new Vector3(0.6f, StandY, 0.4f));
            _impacts.Clear();
            _clock = 0f;
            if (!_ghost.Force(pose, new Vector3(3.5f, 3.05f, 3.5f), null, _room)) continue;
            runs++;

            // 시퀀스가 끝날 때까지 돌린다.
            float t0 = _clock;
            for (int i = 0; i < 60 * 60 && _ghost.Busy; i++)
            {
                _clock += Step;
                _ghost.Tick(Step, new Vector3(3.5f, 3.05f, 3.5f), null, _room);

                // 관통 검사 — 머리 앞면이 벽 뒤나 바닥 밑으로 가면 안 된다.
                Vector3 head = _ghost.HeadWorld();
                worstWall = Mathf.Min(worstWall, Mathf.Min(head.Z - WallBackZ, head.X - WallLeftX));
                lowestHead = Mathf.Min(lowestHead, head.Y);
                // 노리는 표면(벽·설비) 안쪽으로 들어갔는가 — 법선 방향 거리로 잰다.
                //
                // 걸어가는 동안은 재지 않는다. 표면은 무한 평면이 아니라 그 물체의 한 면이라서,
                // 방을 가로질러 가는 길에 그 평면 "뒤"를 지나가는 것은 관통이 아니다.
                // 자리에 도착한 뒤(= 설 자리 가까이 있을 때)부터가 진짜 판정 구간이다.
                bool arrived = new Vector3(_ghost.Position.X, 0f, _ghost.Position.Z)
                    .DistanceTo(new Vector3(_ghost.StandPoint.X, 0f, _ghost.StandPoint.Z)) < 0.2f;
                if (pose != GhostPose.FloorHeadbang && arrived)
                    worstTarget = Mathf.Min(worstTarget, (head - _ghost.TargetPoint).Dot(_ghost.TargetNormal));
            }
            longest = Mathf.Max(longest, _clock - t0);
            if (_ghost.Busy) Check(false, $"{label} — 시퀀스가 60초 안에 끝난다");

            if (_impacts.Count > 0) withImpact++;
            for (int i = 1; i < _impacts.Count; i++) gaps.Add(_impacts[i].Time - _impacts[i - 1].Time);
        }

        if (runs == 0) { Check(false, $"{label} 동작이 시작된다"); return; }
        GD.Print($"   {runs}회 실행 · 충돌이 난 회차 {withImpact} · 충돌 간격 표본 {gaps.Count}개");
        GD.Print($"   벽까지 최소 여유 {worstWall:0.000} · 머리 최저 높이 {lowestHead:0.000}");

        GD.Print($"   가장 긴 시퀀스 {longest:0.0}초");
        Check(withImpact == runs, $"{label} — 매번 충돌이 발생한다");
        Check(longest < 22f, $"{label} — 한 동작이 22초를 넘지 않는다 ({longest:0.0}초)");
        if (pose != GhostPose.FloorHeadbang)
            Check(worstTarget > -0.01f, $"{label} — 머리가 표면 안쪽으로 들어가지 않는다 ({worstTarget:0.000})");
        Check(worstWall > 0f, $"{label} — 머리가 벽을 뚫지 않는다 (최소 여유 {worstWall:0.000})");
        Check(lowestHead > -0.02f, $"{label} — 머리가 바닥을 뚫지 않는다 (최저 {lowestHead:0.000})");

        if (gaps.Count >= 4)
        {
            float avg = gaps.Average();
            float spread = gaps.Max() - gaps.Min();
            GD.Print($"   간격 평균 {avg:0.000}초 · 최소 {gaps.Min():0.000} · 최대 {gaps.Max():0.000}");
            Check(spread > 0.03f, $"{label} — 충돌 간격이 일정하지 않다 (폭 {spread:0.000}초)");
        }
    }

    // ── ④ 바닥 박기는 실제로 몸이 접혀야 한다 ────────────────────────
    private void CheckFloorFolds()
    {
        GD.Print("\n---------------- 바닥 박기 — 몸이 접히는가 ----------------");
        _ghost.Reset(new Vector3(0.6f, StandY, 0.4f));
        _ghost.Force(GhostPose.FloorHeadbang, new Vector3(3.5f, 3.05f, 3.5f), null, _room);

        float maxTorso = 0f, minRootY = float.MaxValue, maxHeadPitch = 0f;
        float maxRoot = 0f, maxLean = 0f, minHeadY = float.MaxValue;
        bool kneesBent = false;
        for (int i = 0; i < 60 * 30 && _ghost.Busy; i++)
        {
            _ghost.Tick(Step, new Vector3(3.5f, 3.05f, 3.5f), null, _room);
            maxTorso = Mathf.Max(maxTorso, _rig.Torso?.Rotation.X ?? 0f);
            maxHeadPitch = Mathf.Max(maxHeadPitch, _rig.Head?.Rotation.X ?? 0f);
            maxRoot = Mathf.Max(maxRoot, _ghost.RootPitch);
            // 앞으로 숙인 총량 — 관절 하나를 크게 꺾으면 메시가 벌어지므로 나눠 쓴다.
            maxLean = Mathf.Max(maxLean,
                _ghost.RootPitch + (_rig.Torso?.Rotation.X ?? 0f) + (_rig.Head?.Rotation.X ?? 0f));
            minRootY = Mathf.Min(minRootY, _ghost.Position.Y);
            minHeadY = Mathf.Min(minHeadY, _ghost.HeadWorld().Y);
            var knee = _entity.FindChild("LowerLegL", true, false) as Node3D;
            if (knee != null && Mathf.Abs(knee.Rotation.X) > 1.0f) kneesBent = true;
        }

        GD.Print($"   몸 전체 {Mathf.RadToDeg(maxRoot):0}도 + 상체 {Mathf.RadToDeg(maxTorso):0}도 + " +
                 $"목 {Mathf.RadToDeg(maxHeadPitch):0}도 = 총 {Mathf.RadToDeg(maxLean):0}도");
        GD.Print($"   골반 최저 {minRootY:0.00} (선 자세 {StandY:0.00}) · 머리 최저 {minHeadY:0.00}");
        Check(maxLean > 1.3f, $"몸이 크게 접힌다 (총 {Mathf.RadToDeg(maxLean):0}도)");
        // 관절 하나가 40도를 넘으면 스킨 웨이트 없는 메시가 눈에 띄게 벌어진다.
        Check(maxTorso < 0.70f && maxHeadPitch < 0.70f,
            $"관절 하나에 몰아 꺾지 않는다 (상체 {Mathf.RadToDeg(maxTorso):0}도 · 목 {Mathf.RadToDeg(maxHeadPitch):0}도)");
        Check(kneesBent, "무릎이 실제로 접힌다");
        Check(minRootY < StandY - 0.2f, $"골반이 내려앉는다 ({StandY - minRootY:0.00} 만큼)");
        Check(minHeadY < StandY * 0.75f, $"머리가 바닥 쪽으로 내려온다 ({minHeadY:0.00})");
    }

    // ── ⑤ 동작이 끝나기 전에 다른 동작으로 넘어가지 않는다 ───────────
    private void CheckPoseLock()
    {
        GD.Print("\n---------------- 동작 잠금 ----------------");
        _ghost.Reset(new Vector3(0.6f, StandY, 0.4f));
        _ghost.Force(GhostPose.WallHeadbang, new Vector3(3.5f, 3.05f, 3.5f), null, _room);
        var started = _ghost.Pose;

        bool changed = false;
        for (int i = 0; i < 60 * 60 && _ghost.Busy; i++)
        {
            _ghost.Tick(Step, new Vector3(3.5f, 3.05f, 3.5f), null, _room);
            if (_ghost.Pose != started) changed = true;
        }
        Check(!changed, "시퀀스가 도는 동안 동작이 바뀌지 않는다");
        Check(!_ghost.Busy, "시퀀스는 끝난다(무한 반복하지 않는다)");
    }

    // ── ⑥ 걷는 시간이 가장 많으면 안 된다 ───────────────────────────
    private void CheckMix()
    {
        GD.Print("\n---------------- 동작 비중 ----------------");
        var seconds = new Dictionary<GhostPose, float>();
        _ghost.Reset(new Vector3(0.6f, StandY, 0.4f));

        // 10분치를 굴려 어떤 동작에 시간을 얼마나 쓰는지 잰다.
        for (int i = 0; i < 60 * 600; i++)
        {
            _ghost.Tick(Step, new Vector3(3.5f, 3.05f, 3.5f), null, _room);
            seconds[_ghost.Pose] = seconds.GetValueOrDefault(_ghost.Pose) + Step;
        }

        float total = seconds.Values.Sum();
        foreach (var (pose, sec) in seconds.OrderByDescending(x => x.Value))
            GD.Print($"   {pose,-16} {sec / total * 100f,5:0.0}%  ({sec:0}초)");

        float headbang = seconds.GetValueOrDefault(GhostPose.WallHeadbang)
                       + seconds.GetValueOrDefault(GhostPose.MachineHeadbang)
                       + seconds.GetValueOrDefault(GhostPose.FloorHeadbang)
                       + seconds.GetValueOrDefault(GhostPose.RapidHeadbang);
        float roam = seconds.GetValueOrDefault(GhostPose.Roam);

        Check(headbang / total is > 0.35f and < 0.62f,
            $"머리 박기 계열이 35~62% 사이다 ({headbang / total * 100f:0.0}%)");
        Check(roam / total < headbang / total, $"걷는 시간이 머리 박기보다 적다 (배회 {roam / total * 100f:0.0}%)");
        Check(seconds.Count >= 6, $"동작이 고르게 섞인다 ({seconds.Count}종)");
    }

    private void Check(bool ok, string label)
    {
        if (ok) _pass++; else _fail++;
        GD.Print($"   [{(ok ? "PASS" : "FAIL")}] {label}");
    }
}
