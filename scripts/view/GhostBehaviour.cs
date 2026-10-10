using System;
using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 괴물이 작업실 안에서 "무엇을 하고 있는가". 표현 전용이다.
//
// 게임 판정(등장 · 소멸 · 사고)은 전부 GhostHauntSystem 이 가지고 있고, 여기서는 그 결과를
// 어떻게 보여 줄지만 정한다. 이 파일의 어떤 코드도 시뮬레이션을 바꾸지 않는다.
//
// 만들고 싶은 인상은 "작업실을 돌아다니는 몬스터"가 아니라
// **이유 없이 자기 머리를 벽과 기계와 바닥에 박고 있는 것**이다.
// 그래서 걷는 시간을 줄이고, 정지와 폭발적인 동작을 섞는다.
//
//   가만히 있음 → 갑자기 벽에 머리를 박음 → 정지 → 고개만 카메라로 → 다시 박음 → …
//
// 한 동작이 시작되면 그 동작이 끝날 때까지 다른 동작으로 넘어가지 않는다(Busy).
// 트윈을 쓰지 않고 매 프레임 각도를 직접 먹인다 — 서로 다른 트윈이 같은 관절을 두고
// 싸우는 일을 원천적으로 없애기 위해서다.
public enum GhostPose
{
    IdleStare,        // 가만히 서서 본다
    Roam,             // 몇 걸음 걷다 멈추기를 반복한다
    WallHeadbang,     // 벽에 머리를 박는다
    MachineHeadbang,  // 설비에 머리를 박는다
    FloorHeadbang,    // 주저앉아 바닥에 머리를 박는다
    RapidHeadbang,    // 아주 빠르게 연달아 박는다(드물게)
    TwistedStare,     // 몸은 그대로 두고 목만 꺾어 본다
    Crouch,           // 웅크린다
}

// 무엇에 부딪혔는가. 카메라 흔들림 세기와 (나중에 붙일) 효과음이 여기서 갈린다.
public enum GhostImpactKind { Wall, Machine, Floor }

public sealed class GhostBehaviour
{
    // 머리가 무언가에 닿는 순간. 화면(카메라 흔들림)과 소리는 받는 쪽이 붙인다.
    public event Action<GhostImpactKind, Vector3> Impact;

    // ── 방 치수 (scenes/rooms/room_base.tscn) ─────────────────────────
    // 바닥 6×6, 뒷벽 z=-3(두께 0.15), 왼쪽벽 x=-3. CCTV 가 +x/+z 코너에서 내려다보므로
    // 화면에 보이는 벽은 이 둘뿐이다 — 머리를 박아도 보이는 곳이어야 의미가 있다.
    private const float WallBackZ = -2.925f;
    private const float WallLeftX = -2.925f;
    private const float FloorY = 0f;
    // 벽·설비에서 이만큼 남기고 멈춘다. 머리가 표면을 뚫고 들어가지 않게 하는 여유.
    private const float SurfaceClearance = 0.05f;

    // 한 번 박을 때의 상체·목 각도. 이 둘이 함께 꺾여야 몸을 내던지는 것처럼 보인다.
    private const float BangTorso = 0.62f;   // rad — 상체가 앞으로 꺾이는 최대치
    private const float BangNeck = 0.45f;    // rad — 목이 더 숙여지는 최대치
    private const float BackTorso = -0.20f;  // 젖힐 때
    private const float BackNeck = -0.26f;

    // 바닥에 박을 때. 무릎을 접고 상체를 거의 눕힌다.
    // 각도를 더 키우면 머리가 바닥에 더 가까워지지만, 스킨 웨이트가 없는 모델이라
    // 관절이 눈에 띄게 벌어진다. 각도는 여기까지만 쓰고 모자란 높이는 골반을 더 내려 채운다.
    // 다리는 이 원화에서 제일 약한 곳이다 — 가늘어서 크게 접으면 허벅지와 정강이가
    // 눈에 띄게 벌어진다(SetWalk 의 주석도 같은 얼을 말한다).
    // 그래서 다리는 기존 웅크림과 같은 각도까지만 쓰고, 모자란 높이는 상체로 채운다.
    // 어느 관절도 35도를 넘지 않는다 — 넘으면 잘라 붙인 메시가 벌어져 몸이 조각나 보인다.
    // 모자란 각도는 몸 전체를 앞으로 기울여(FloorRoot) 채운다. 합치면 90도 가까이 숙인다.
    // 다리도 마찬가지다 — 가는 다리를 크게 접으면 무릎에서 메시가 벌어진다.
    private const float FloorHip = 0.72f;
    private const float FloorKnee = -1.15f;
    private const float FloorTorso = 0.55f;
    private const float FloorNeck = 0.55f;
    private const float FloorRoot = 0.80f;   // 몸 전체를 앞으로 기울이는 각(rad)

    // ── 붙어 있는 대상 ────────────────────────────────────────────────
    private Node3D _entity;
    private EntityRig _rig;
    private float _scale = 1f;
    private float _standY;      // 서 있을 때 루트의 Y(발이 바닥에 닿는 높이)

    // ── 상태 ──────────────────────────────────────────────────────────
    private readonly List<Step> _steps = new();
    private int _stepIndex;
    private float _stepTime;
    private readonly RandomNumberGenerator _rng = new();

    private Vector3 _pos;
    private float _faceY;        // 지금 향한 방향(도)
    private float _wantFaceY;    // 돌아가려는 방향(도)
    private float _turnSpeed = 3.2f;
    private float _phase;        // 숨·흔들림용 누적 시간
    // 몸 전체를 앞으로 기울인 각(rad). 관절만으로는 못 만드는 깊은 숙임에만 쓴다.
    private float _rootPitch;

    // 지금 동작이 쓰는 목표(벽/설비 지점과 그 법선).
    private Vector3 _targetPoint;
    private Vector3 _standPoint;
    private Vector3 _targetNormal = Vector3.Forward;
    private GhostImpactKind _targetKind = GhostImpactKind.Wall;

    // 지금 동작이 노리는 표면과 그 앞에 서는 자리(검사가 관통 여유를 재는 데 쓴다).
    public Vector3 TargetPoint => _targetPoint;
    public Vector3 TargetNormal => _targetNormal;
    public Vector3 StandPoint => _standPoint;
    // 몸 전체를 앞으로 기울인 각(rad). 깊은 숙임은 이 값과 관절 각의 합이다.
    public float RootPitch => _rootPitch;

    public GhostPose Pose { get; private set; } = GhostPose.IdleStare;
    // 시퀀스가 끝나기 전에는 다른 동작으로 넘어가지 않는다.
    public bool Busy => _stepIndex < _steps.Count;
    public Vector3 Position => _pos;
    public float FaceDegrees => _faceY;

    public void Attach(Node3D entity, EntityRig rig, float scale, float standY)
    {
        _entity = entity;
        _rig = rig;
        _scale = scale;
        _standY = standY;
        _rng.Randomize();
    }

    public void Reset(Vector3 startPos)
    {
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;
        _pos = startPos;
        _pos.Y = _standY;
        _faceY = _wantFaceY = _rng.RandfRange(-180f, 180f);
        _turnSpeed = 3.2f;
        _rootPitch = 0f;
        _phase = 0f;
        Pose = GhostPose.IdleStare;
        _rig?.Reset();
    }

    // ── 한 걸음 ───────────────────────────────────────────────────────

    public void Tick(float delta, Vector3 cameraPos, Vector3? employeePos, Node3D roomNode)
    {
        if (_entity == null) return;
        _phase += delta;

        if (!Busy) Choose(cameraPos, employeePos, roomNode);
        if (!Busy) return;

        var step = _steps[_stepIndex];
        if (_stepTime <= 0f) step.Enter?.Invoke();

        _stepTime += delta;
        float t = Mathf.Clamp(_stepTime / step.Seconds, 0f, 1f);
        step.Update?.Invoke(t);

        if (_stepTime >= step.Seconds)
        {
            step.Exit?.Invoke();
            _stepIndex++;
            _stepTime = 0f;
        }

        // 방향은 항상 조금씩 따라 돈다(동작이 목표 방향을 바꾸면 여기서 돌아간다).
        _faceY = Mathf.RadToDeg(Mathf.LerpAngle(
            Mathf.DegToRad(_faceY), Mathf.DegToRad(_wantFaceY), Mathf.Clamp(delta * _turnSpeed, 0f, 1f)));

        _entity.Position = _pos;
        // Godot 의 기본 오일러 순서(YXZ)라 X 회전은 이미 돌아간 방향 기준으로 걸린다 —
        // 즉 바라보는 쪽으로 앞으로 숙는다.
        _entity.RotationDegrees = new Vector3(Mathf.RadToDeg(_rootPitch), _faceY, 0f);
    }

    // ── 동작 고르기 ───────────────────────────────────────────────────
    //
    // 걷는 시간이 가장 길면 평범한 NPC 가 된다. 머리 박기 계열이 절반 가까이 되도록 잡는다.
    //   머리 박기 ~50% · 가만히/비틀린 응시 ~28% · 웅크림 ~10% · 배회 ~12%
    //
    // 고르는 확률과 실제로 보내는 시간은 다르다 — 머리 박기 한 번이 5~9초로 제일 길어서,
    // 확률을 이만큼 잡아야 시간 비중이 절반쯤 된다. 늘 난폭하면 금방 익숙해지므로
    // 정지 동작(응시 · 웅크림)이 그 사이를 메워야 한다.
    private void Choose(Vector3 cameraPos, Vector3? employeePos, Node3D roomNode)
    {
        float r = _rng.Randf();

        // 아주 드물게 — 짧고 빠르게 연달아 박는다. 자주 나오면 우스워진다.
        if (r < 0.05f && BeginHeadbang(GhostPose.RapidHeadbang, cameraPos, employeePos, roomNode)) return;
        if (r < 0.22f && BeginHeadbang(GhostPose.WallHeadbang, cameraPos, employeePos, roomNode)) return;
        if (r < 0.32f && BeginHeadbang(GhostPose.MachineHeadbang, cameraPos, employeePos, roomNode)) return;
        if (r < 0.42f) { BeginFloorHeadbang(); return; }
        if (r < 0.60f) { BeginTwistedStare(cameraPos, employeePos); return; }
        if (r < 0.80f) { BeginIdleStare(cameraPos, employeePos); return; }
        if (r < 0.90f) { BeginCrouch(cameraPos); return; }
        BeginRoam(employeePos);
    }

    // 디버그·강제 재생. 조건이 안 맞으면 false.
    public bool Force(GhostPose pose, Vector3 cameraPos, Vector3? employeePos, Node3D roomNode)
    {
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;
        switch (pose)
        {
            case GhostPose.WallHeadbang:
            case GhostPose.MachineHeadbang:
            case GhostPose.RapidHeadbang:
                return BeginHeadbang(pose, cameraPos, employeePos, roomNode);
            case GhostPose.FloorHeadbang: BeginFloorHeadbang(); return true;
            case GhostPose.TwistedStare: BeginTwistedStare(cameraPos, employeePos); return true;
            case GhostPose.Crouch: BeginCrouch(cameraPos); return true;
            case GhostPose.Roam: BeginRoam(employeePos); return true;
            default: BeginIdleStare(cameraPos, employeePos); return true;
        }
    }

    // ── 머리 박기 ─────────────────────────────────────────────────────

    private bool BeginHeadbang(GhostPose pose, Vector3 cameraPos, Vector3? employeePos, Node3D roomNode)
    {
        if (pose == GhostPose.MachineHeadbang)
        {
            if (!PickMachine(roomNode, employeePos, out _targetPoint, out _targetNormal)) return false;
            _targetKind = GhostImpactKind.Machine;
        }
        else
        {
            PickWall(employeePos, out _targetPoint, out _targetNormal);
            _targetKind = GhostImpactKind.Wall;
        }

        // 표면에서 얼마나 떨어져 서야 "최대로 꺾었을 때 머리가 겨우 닿는가".
        // 이 거리를 지키면 머리가 벽·기계 속으로 들어갈 수 없다.
        //
        // 여기서 방 안으로 clamp 하면 안 된다 — 거리가 줄어든 만큼 그대로 관통이 된다.
        // 설 자리가 방 밖이면 그 목표를 포기한다(설비는 PickMachine 이 미리 걸러 낸다).
        Vector3 stand = _targetPoint + _targetNormal * Standoff();
        stand.Y = _standY;
        if (!InsideRoom(stand)) return false;
        // 직원 자리와 겹치면 하지 않는다 — 이번 작업에서 괴물은 직원을 건드리지 않는다.
        if (employeePos.HasValue && Flat(stand).DistanceTo(Flat(employeePos.Value)) < 0.9f) return false;
        _standPoint = stand;

        Pose = pose;
        bool rapid = pose == GhostPose.RapidHeadbang;
        int hits = rapid ? _rng.RandiRange(3, 5) : _rng.RandiRange(1, 5);
        // 카메라를 쳐다보는 것은 한 시퀀스에 한 번까지. 여러 번 끼면 한 동작이 20초를 넘어
        // 괴물이 근무 내내 같은 벽만 보고 있게 된다.
        bool peekLeft = true;

        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;

        // ① 그 앞까지 간다. 이미 가까우면 아주 짧게.
        float walk = Mathf.Clamp(Flat(_pos).DistanceTo(Flat(stand)) * 0.75f, 0.25f, 2.6f);
        AddWalkTo(stand, FaceToward(-_targetNormal), walk);

        // ② 잠깐 멈춘다 — 벽 앞에 가만히 선 시간이 있어야 다음 동작이 갑작스러워진다.
        AddStandStill(rapid ? 0.18f : _rng.RandfRange(0.35f, 1.1f));

        for (int i = 0; i < hits; i++)
        {
            AddBang(rapid);
            if (i == hits - 1) continue;
            // ③ 간격을 매번 다르게. 같은 박자로 세 번 박으면 기계처럼 보인다.
            float gap = rapid
                ? _rng.RandfRange(0.015f, 0.030f)
                : (_rng.Randf() < 0.3f ? _rng.RandfRange(0.55f, 1.0f) : _rng.RandfRange(0.05f, 0.30f));
            AddLeanHold(gap);

            // ④ 드물게 — 박다 말고 고개만 카메라로 돌려 1초쯤 본다.
            //    "쟤 지금 카메라 보고 있는 거 맞지?" 가 이 게임에서 제일 중요한 순간이다.
            if (!rapid && peekLeft && _rng.Randf() < 0.28f) { AddPeekAtCamera(cameraPos); peekLeft = false; }
        }

        // ⑤ 끝나고 바로 멀쩡해지지 않는다. 머리를 댄 채 멈추거나 고개가 기운 채 떤다.
        AddLinger(_rng.RandfRange(0.5f, 2.0f));
        return true;
    }

    // 표면에서 떨어져 설 거리(월드). 최대로 꺾었을 때 머리 앞면이 표면에 겨우 닿는 거리다.
    private float Standoff() =>
        EntityRig.HeadFrontLocal(BangTorso, BangNeck).Z * _scale + SurfaceClearance;

    // 그 자리에 실제로 설 수 있는가(방 안이고, 벽에 끼지 않는가).
    private static bool InsideRoom(Vector3 p) =>
        p.X > WallLeftX + 0.25f && p.X < 2.6f && p.Z > WallBackZ + 0.25f && p.Z < 2.6f;

    // CCTV 화면 안에 몸이 온전히 들어오는 자리인가.
    // 카메라가 +x/+z 코너에 있어 그쪽 끝에 붙으면 몸이 화면 밖으로 잘린다 —
    // 머리를 박아도 보이지 않으면 아무 소용이 없다.
    private static bool InFrame(Vector3 p) =>
        p.X > WallLeftX + 0.3f && p.X < 1.7f && p.Z > WallBackZ + 0.3f && p.Z < 1.7f;

    // 한 번 박기 — 젖힘 → 내리꽂음(충돌) → 뺌.
    private void AddBang(bool rapid)
    {
        // 연타는 "충돌에서 다음 충돌까지" 가 0.11~0.14초여야 한다 — 젖힘·내리꽂음·뺌까지
        // 전부 그 안에 들어가야 하므로 한 토막씩이 아주 짧다.
        float windup = rapid ? _rng.RandfRange(0.030f, 0.045f) : _rng.RandfRange(0.10f, 0.20f);
        float strike = rapid ? _rng.RandfRange(0.030f, 0.040f) : _rng.RandfRange(0.07f, 0.09f);
        float back = rapid ? _rng.RandfRange(0.030f, 0.045f) : _rng.RandfRange(0.16f, 0.22f);

        // 젖힘 — 상체가 뒤로 조금, 목이 조금 더.
        Add(windup, t => Bang(Mathf.Lerp(BangTorso * 0.35f, BackTorso, Ease(t)),
                              Mathf.Lerp(BangNeck * 0.35f, BackNeck, Ease(t)), 0.25f));
        // 내리꽂음 — 아주 빠르게. 끝나는 순간이 충돌이다.
        Add(strike, t => Bang(Mathf.Lerp(BackTorso, BangTorso, t * t),
                              Mathf.Lerp(BackNeck, BangNeck, t * t), 0.10f),
            exit: () => Impact?.Invoke(_targetKind, HeadWorld()));
        // 다시 뺌 — 완전히 세우지 않는다. 절반쯤 숙인 채로 남는다.
        Add(back, t => Bang(Mathf.Lerp(BangTorso, BangTorso * 0.35f, Ease(t)),
                            Mathf.Lerp(BangNeck, BangNeck * 0.35f, Ease(t)), 0.20f));
    }

    // 박는 사이의 정지 — 숙인 자세를 유지한 채 아주 조금 떤다.
    private void AddLeanHold(float seconds) =>
        Add(seconds, _ =>
        {
            float j = Mathf.Sin(_phase * 23f) * 0.012f;
            Bang(BangTorso * 0.35f + j, BangNeck * 0.35f - j, 0.20f);
        });

    // 박다 말고 고개만 카메라로. 몸은 벽을 향한 그대로다.
    private void AddPeekAtCamera(Vector3 cameraPos)
    {
        float yaw = 0f;
        Add(0.28f, t => Bang(BangTorso * 0.30f, BangNeck * 0.20f, 0.20f, yaw * Ease(t)),
            enter: () =>
            {
                // 몸이 향한 방향과 카메라 방향의 차이를 목만으로 메운다(사람 범위보다 조금 더).
                float want = FaceToward(Flat(cameraPos) - Flat(_pos));
                yaw = Mathf.DegToRad(Mathf.Clamp(Mathf.Wrap(want - _faceY, -180f, 180f), -115f, 115f));
            });
        Add(_rng.RandfRange(0.8f, 1.3f),
            _ => Bang(BangTorso * 0.30f, BangNeck * 0.20f, 0.20f, yaw, Mathf.Sin(_phase * 1.7f) * 0.10f));
        Add(0.22f, t => Bang(BangTorso * 0.35f, BangNeck * 0.30f, 0.20f, yaw * (1f - Ease(t))));
    }

    // 동작이 끝난 뒤 — 머리를 표면에 댄 채 멈춰 있거나, 고개가 기운 채 떤다.
    private void AddLinger(float seconds)
    {
        bool rest = _rng.Randf() < 0.6f;
        float roll = _rng.RandfRange(0.22f, 0.45f) * (_rng.Randf() < 0.5f ? -1f : 1f);
        Add(seconds, _ =>
        {
            float tremble = Mathf.Sin(_phase * 19f) * 0.008f;
            if (rest) Bang(BangTorso * 0.92f + tremble, BangNeck * 0.9f, 0.75f, 0f, roll * 0.4f);
            else Stand(BangTorso * 0.25f, BangNeck * 0.15f + tremble, neckRoll: roll, arms: Arms.Limp);
        });
    }

    // ── 바닥에 머리 박기 ──────────────────────────────────────────────
    //
    // 모델 전체를 아래로 내리지 않는다. 무릎을 접어 골반을 낮추고, 상체를 앞으로 크게 접고,
    // 목을 더 숙여야 실제로 몸이 접히는 것처럼 보인다.
    private void BeginFloorHeadbang()
    {
        Pose = GhostPose.FloorHeadbang;
        _targetKind = GhostImpactKind.Floor;
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;

        // 머리가 바닥에 겨우 닿도록 골반을 얼마나 낮출지 계산한다(관통 금지).
        float drop = KneelDropForFloor();
        int hits = _rng.RandiRange(1, 4);

        // ① 갑자기 주저앉는다.
        Add(_rng.RandfRange(0.28f, 0.42f), t =>
        {
            float k = Ease(t);
            Kneel(k * drop, FloorHip * k, FloorKnee * k,
                FloorTorso * 0.45f * k, FloorNeck * 0.4f * k, FloorRoot * 0.5f * k);
        });
        Add(_rng.RandfRange(0.1f, 0.5f),
            _ => Kneel(drop, FloorHip, FloorKnee, FloorTorso * 0.45f, FloorNeck * 0.4f, FloorRoot * 0.5f));

        for (int i = 0; i < hits; i++)
        {
            // 들어올림
            Add(_rng.RandfRange(0.12f, 0.22f), t =>
                Kneel(drop, FloorHip, FloorKnee,
                    Mathf.Lerp(FloorTorso * 0.95f, FloorTorso * 0.35f, Ease(t)),
                    Mathf.Lerp(FloorNeck, -0.1f, Ease(t)),
                    Mathf.Lerp(FloorRoot, FloorRoot * 0.45f, Ease(t))));
            // 내리찍음 — 끝이 충돌
            Add(_rng.RandfRange(0.07f, 0.10f), t =>
                Kneel(drop, FloorHip, FloorKnee,
                    Mathf.Lerp(FloorTorso * 0.35f, FloorTorso, t * t),
                    Mathf.Lerp(-0.1f, FloorNeck, t * t),
                    Mathf.Lerp(FloorRoot * 0.45f, FloorRoot, t * t)),
                exit: () => Impact?.Invoke(GhostImpactKind.Floor, HeadWorld()));
            if (i == hits - 1) continue;
            float gap = _rng.Randf() < 0.35f ? _rng.RandfRange(0.4f, 0.9f) : _rng.RandfRange(0.06f, 0.25f);
            Add(gap, _ => Kneel(drop, FloorHip, FloorKnee, FloorTorso * 0.95f, FloorNeck * 0.9f, FloorRoot));
        }

        // ② 접힌 채로 멈춘다.
        Add(_rng.RandfRange(0.6f, 1.6f), _ =>
        {
            float tremble = Mathf.Sin(_phase * 17f) * 0.01f;
            Kneel(drop, FloorHip, FloorKnee, FloorTorso * 0.97f + tremble, FloorNeck, FloorRoot);
        });
        // ③ 천천히 일어난다.
        Add(_rng.RandfRange(0.7f, 1.1f), t =>
        {
            float k = 1f - Ease(t);
            Kneel(k * drop, FloorHip * k, FloorKnee * k, FloorTorso * k, FloorNeck * k, FloorRoot * k);
        });
    }

    // 무릎을 접었을 때 골반이 내려앉는 높이(월드 단위).
    //
    // "머리가 바닥에 닿게" 역산해서 내리지 않는다. 키 3.2m 짜리가 머리를 바닥까지 붙이려면
    // 관절을 사람이 못 접는 각도로 꺾어야 하고, 스킨 웨이트가 없는 모델이라 그런 각도에서는
    // 잘라 붙인 메시가 눈에 띄게 벌어진다.
    // 다리를 접은 만큼만 내리고 나머지는 상체를 깊게 숙여 "머리를 바닥으로 내리찍는" 그림을
    // 만든다 — 머리는 바닥 위 0.7m 쯤까지 내려온다. CCTV 거리에서는 바닥을 찧는 것으로 읽힌다.
    private float KneelDropForFloor() => EntityRig.CrouchDrop * _scale * 0.66f;

    // ── 가만히 / 비틀린 응시 / 웅크림 / 배회 ──────────────────────────

    private void BeginIdleStare(Vector3 cameraPos, Vector3? employeePos)
    {
        Pose = GhostPose.IdleStare;
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;
        Vector3 look = employeePos ?? cameraPos;

        // 고개를 단계적으로 돌린다 — 부드럽게 도는 것보다 "30도 · 정지 · 20도 더" 가 불쾌하다.
        int steps = _rng.RandiRange(2, 3);
        float total = Mathf.Wrap(FaceToward(Flat(look) - Flat(_pos)) - _faceY, -180f, 180f);
        float done = 0f;
        for (int i = 0; i < steps; i++)
        {
            float from = done, to = total * (i + 1) / steps;
            Add(_rng.RandfRange(0.12f, 0.22f), t =>
            {
                _wantFaceY = _faceY;   // 몸은 멈춰 있다
                Stand(0.04f, 0f, neckYaw: Mathf.DegToRad(Mathf.Lerp(from, to, Ease(t))) * 0.55f);
            });
            Add(_rng.RandfRange(0.35f, 0.9f), _ =>
            {
                _wantFaceY = _faceY;
                Stand(0.04f + Mathf.Sin(_phase * 1.3f) * 0.015f, 0f, neckYaw: Mathf.DegToRad(to) * 0.55f);
            });
            done = to;
        }
        // 마지막에 몸이 늦게 따라 돈다.
        Add(_rng.RandfRange(0.8f, 2.0f), _ =>
        {
            _wantFaceY = FaceToward(Flat(look) - Flat(_pos));
            _turnSpeed = 1.1f;
            Stand(0.04f, 0f);
        }, exit: () => _turnSpeed = 3.2f);
    }

    private void BeginTwistedStare(Vector3 cameraPos, Vector3? employeePos)
    {
        Pose = GhostPose.TwistedStare;
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;
        Vector3 look = employeePos ?? cameraPos;

        float want = FaceToward(Flat(look) - Flat(_pos));
        // 몸은 그대로. 목만 사람에게 가능한 범위보다 조금 더 돌아간다(180도는 만화가 된다).
        float yaw = Mathf.DegToRad(Mathf.Clamp(Mathf.Wrap(want - _faceY, -180f, 180f), -115f, 115f));
        float roll = _rng.RandfRange(0.18f, 0.42f) * (yaw < 0f ? -1f : 1f);

        Add(_rng.RandfRange(0.18f, 0.3f), t =>
        {
            _wantFaceY = _faceY;
            Stand(0.03f, 0f, neckYaw: yaw * 0.6f * Ease(t), neckRoll: roll * 0.4f * Ease(t));
        });
        Add(_rng.RandfRange(0.25f, 0.6f), _ =>
        {
            _wantFaceY = _faceY;
            Stand(0.03f, 0f, neckYaw: yaw * 0.6f, neckRoll: roll * 0.4f);
        });
        // 한 번 더 꺾인다.
        Add(0.14f, t =>
        {
            _wantFaceY = _faceY;
            Stand(0.03f, 0f, neckYaw: Mathf.Lerp(yaw * 0.6f, yaw, Ease(t)),
                  neckRoll: Mathf.Lerp(roll * 0.4f, roll, Ease(t)));
        });
        // 가슴이 늦게 따라온다.
        Add(_rng.RandfRange(1.2f, 2.6f), t =>
        {
            _wantFaceY = _faceY;
            Stand(0.03f, 0f, neckYaw: yaw, neckRoll: roll + Mathf.Sin(_phase * 2.1f) * 0.03f,
                  torsoYaw: Mathf.DegToRad(Mathf.Wrap(want - _faceY, -180f, 180f)) * 0.25f * Ease(t));
        });
    }

    private void BeginCrouch(Vector3 cameraPos)
    {
        Pose = GhostPose.Crouch;
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;
        float drop = EntityRig.CrouchDrop * _scale;
        Add(_rng.RandfRange(0.35f, 0.6f), t =>
        {
            float k = Ease(t);
            _wantFaceY = FaceToward(Flat(cameraPos) - Flat(_pos));
            Kneel(k * drop, 0.9f * k, -1.7f * k, 0.12f * k, 0.1f * k);
        });
        Add(_rng.RandfRange(1.5f, 3.2f),
            _ => Kneel(drop, 0.9f, -1.7f, 0.12f + Mathf.Sin(_phase * 1.5f) * 0.02f, 0.1f));
        Add(_rng.RandfRange(0.5f, 0.8f), t =>
        {
            float k = 1f - Ease(t);
            Kneel(k * drop, 0.9f * k, -1.7f * k, 0.12f * k, 0.1f * k);
        });
    }

    // 배회 — 목적지까지 한 번에 걷지 않는다. 몇 걸음 걷고 멈추고 다시 걷는다.
    private void BeginRoam(Vector3? employeePos)
    {
        Pose = GhostPose.Roam;
        _steps.Clear();
        _stepIndex = 0;
        _stepTime = 0f;

        Vector3 to = RoamSpot(employeePos);
        int legs = _rng.RandiRange(2, 3);
        Vector3 from = _pos;
        for (int i = 0; i < legs; i++)
        {
            Vector3 mid = from.Lerp(to, (i + 1f) / legs);
            mid.Y = _standY;
            AddWalkTo(mid, FaceToward(Flat(mid) - Flat(from)), _rng.RandfRange(0.7f, 1.4f));
            // 몇 걸음 걷다 멈춘다 — 목적지까지 곧장 가지 않는다.
            if (i < legs - 1) AddStandStill(_rng.RandfRange(0.3f, 1.1f));
            from = mid;
        }
        AddStandStill(_rng.RandfRange(0.4f, 1.2f));
    }

    private Vector3 RoamSpot(Vector3? employeePos)
    {
        for (int i = 0; i < 8; i++)
        {
            var p = new Vector3(_rng.RandfRange(-2.2f, 1.6f), _standY, _rng.RandfRange(-2.2f, 1.6f));
            if (Flat(p).DistanceTo(Flat(_pos)) < 0.8f) continue;
            if (employeePos.HasValue && Flat(p).DistanceTo(Flat(employeePos.Value)) < 0.9f) continue;
            return p;
        }
        return _pos;
    }

    // ── 스텝 ──────────────────────────────────────────────────────────

    private sealed class Step
    {
        public float Seconds;
        public Action<float> Update;   // 0~1 진행
        public Action Enter, Exit;
    }

    private void Add(float seconds, Action<float> update, Action enter = null, Action exit = null) =>
        _steps.Add(new Step { Seconds = Mathf.Max(0.01f, seconds), Update = update, Enter = enter, Exit = exit });

    private void AddStandStill(float seconds) =>
        Add(seconds, _ =>
        {
            _rig?.SetIdle(_phase * 1.4f);
            Stand(0.03f + Mathf.Sin(_phase * 1.2f) * 0.02f, 0f, arms: Arms.Keep);
        });

    private void AddWalkTo(Vector3 to, float faceY, float seconds)
    {
        Vector3 from = Vector3.Zero;
        Add(seconds,
            t =>
            {
                float e = Mathf.SmoothStep(0f, 1f, t);
                _pos = from.Lerp(to, e);
                _pos.Y = _standY + Mathf.Abs(Mathf.Sin(_phase * 5.2f)) * 0.02f;
                _wantFaceY = faceY;
                _rig?.SetWalk(_phase * 5.2f, 1f);
                _rig?.SetTorso(0.05f, 0f, 0f);
                _rootPitch = 0f;
            },
            enter: () => from = _pos);
    }

    // ── 자세 적용 ─────────────────────────────────────────────────────

    private enum Arms { Keep, Limp, Brace }

    // 서 있는 자세. 상체·목을 직접 먹인다.
    private void Stand(float torsoPitch, float neckPitch, float neckYaw = 0f, float neckRoll = 0f,
        float torsoYaw = 0f, Arms arms = Arms.Limp)
    {
        if (_rig == null) return;
        _rig.SetTorso(torsoPitch, torsoYaw, 0f);
        _rig.SetNeck(neckPitch, neckYaw, neckRoll);
        _rig.SetLegs(torsoPitch * 0.12f, -torsoPitch * 0.2f);
        if (arms == Arms.Limp) _rig.SetArmsLimp(1f, _phase * 1.1f);
        else if (arms == Arms.Brace) _rig.SetArmsBrace(1f);
        _rootPitch = 0f;
        _pos.Y = _standY;
    }

    // 머리 박기 전용 — 상체와 목이 함께 꺾이고, 팔이 표면을 짚는다.
    private void Bang(float torsoPitch, float neckPitch, float armBrace, float neckYaw = 0f, float neckRoll = 0f)
    {
        if (_rig == null) return;
        _rig.SetTorso(torsoPitch, 0f, 0f);
        _rig.SetNeck(neckPitch, neckYaw, neckRoll);
        _rig.SetArmsBrace(armBrace);
        _rig.SetLegs(Mathf.Max(0f, torsoPitch) * 0.18f, -Mathf.Max(0f, torsoPitch) * 0.30f);
        _rootPitch = 0f;
        _pos.Y = _standY;
    }

    // 무릎을 접어 골반을 낮추고, 필요하면 몸 전체를 앞으로 기울인 자세.
    //
    // rootPitch 가 들어오면 발이 바닥에서 떨어지지 않도록 루트 높이를 cos 로 낮춘다
    // (허리 높이를 축으로 도는 것이므로, 기울인 만큼 실제로 몸이 내려앉는다).
    private void Kneel(float drop, float hip, float knee, float torsoPitch, float neckPitch,
        float rootPitch = 0f)
    {
        if (_rig == null) return;
        _rig.SetLegs(hip, knee);
        _rig.SetTorso(torsoPitch, 0f, 0f);
        _rig.SetNeck(neckPitch, 0f, 0f);
        _rig.SetArmsLimp(0.8f, _phase * 1.3f);
        _rootPitch = rootPitch;
        _pos.Y = _standY * Mathf.Cos(rootPitch) - drop;
    }

    // ── 목표 고르기 ───────────────────────────────────────────────────

    // 화면에 보이는 두 벽 중 가까운 쪽. 직원이 붙어 있는 쪽은 피한다.
    private void PickWall(Vector3? employeePos, out Vector3 point, out Vector3 normal)
    {
        bool back = _pos.Z - WallBackZ <= _pos.X - WallLeftX;
        if (employeePos.HasValue)
        {
            var e = employeePos.Value;
            if (back && Mathf.Abs(e.Z - WallBackZ) < 1.4f) back = false;
            else if (!back && Mathf.Abs(e.X - WallLeftX) < 1.4f) back = true;
        }

        if (back)
        {
            point = new Vector3(Mathf.Clamp(_pos.X, -2.2f, 1.8f), _standY, WallBackZ);
            normal = new Vector3(0f, 0f, 1f);
        }
        else
        {
            point = new Vector3(WallLeftX, _standY, Mathf.Clamp(_pos.Z, -2.2f, 1.8f));
            normal = new Vector3(1f, 0f, 0f);
        }
    }

    // 방 안의 설비 하나를 고른다. 임의의 좌표를 쓰지 않고 실제 메시의 겉면을 쓴다 —
    // 그래야 기계 속으로 머리가 들어가지 않는다.
    private bool PickMachine(Node3D room, Vector3? employeePos, out Vector3 point, out Vector3 normal)
    {
        point = Vector3.Zero;
        normal = Vector3.Forward;
        if (room == null) return false;

        var found = new List<(Vector3 Point, Vector3 Normal)>();
        foreach (var n in room.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null || IsShell(mi)) continue;

            var aabb = mi.Mesh.GetAabb();
            var xf = mi.GlobalTransform;
            Vector3 c = xf * aabb.GetCenter();
            Vector3 half = (xf.Basis * aabb.Size).Abs() * 0.5f;

            // 머리를 박을 만한 것만 — 너무 작거나(공구) 너무 높은 것(천장등)은 뺀다.
            float top = c.Y + half.Y;
            if (top < 0.35f || top > 2.4f) continue;
            if (half.X + half.Z < 0.18f) continue;
            if (employeePos.HasValue && Flat(c).DistanceTo(Flat(employeePos.Value)) < 0.8f) continue;

            // 카메라는 +x/+z 코너에 있다 — 그쪽을 향한 면이 화면에 보이는 면이다.
            // 두 면 다 보고, 그 앞에 실제로 설 수 있는 쪽만 남긴다.
            foreach (var dir in new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f) })
            {
                Vector3 surface = c + dir * (dir.X != 0f ? half.X : half.Z);
                // 머리가 닿는 높이는 그 설비의 윗면 근처로(허공에 박지 않게).
                surface.Y = Mathf.Clamp(top, 0.5f, 2.2f);
                // 설 자리가 방 밖이거나 화면 밖이면 그 면은 쓸 수 없다.
                // (억지로 당겨 세우면 표면과의 거리가 줄어 그대로 관통이 된다.)
                if (!InFrame(surface + dir * Standoff())) continue;
                found.Add((surface, dir));
            }
        }

        if (found.Count == 0) return false;
        var pick = found[_rng.RandiRange(0, found.Count - 1)];
        point = pick.Point;
        normal = pick.Normal;
        return true;
    }

    // 방 껍데기(바닥·벽·천장·조명)는 설비가 아니다.
    private static bool IsShell(Node n)
    {
        for (Node p = n; p != null; p = p.GetParent())
        {
            string name = p.Name.ToString();
            if (name is "Shell" or "RoomBase") return true;
            if (name.StartsWith("Floor") || name.StartsWith("Wall")
                || name.StartsWith("Ceiling") || name.Contains("Lamp")) return true;
        }
        return false;
    }

    // ── 잡동사니 ──────────────────────────────────────────────────────

    // 이 모델은 앞이 +Z 다(EntityGhost 의 손끝 판정과 같은 전제).
    private static float FaceToward(Vector3 dir)
    {
        dir.Y = 0f;
        if (dir.LengthSquared() < 1e-5f) return 0f;
        return Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
    }

    private static Vector3 Flat(Vector3 v) => new(v.X, 0f, v.Z);
    private static float Ease(float t) => t * t * (3f - 2f * t);

    // 지금 머리 앞면이 있는 월드 좌표(충돌 지점 — 소리·흔들림을 여기에 건다).
    public Vector3 HeadWorld()
    {
        Vector3 local = _rig?.HeadFrontLocalNow()
                        ?? new Vector3(0f, EntityRig.NeckPivotY, EntityRig.HeadFrontZ);
        // 몸 전체 기울기 → 그 다음 방향(요) 순서로 돌린다(Godot 의 YXZ 와 같은 순서).
        float c = Mathf.Cos(_rootPitch), s = Mathf.Sin(_rootPitch);
        local = new Vector3(local.X, local.Y * c - local.Z * s, local.Y * s + local.Z * c);
        float yaw = Mathf.DegToRad(_faceY);
        Vector3 rotated = new(
            local.Z * Mathf.Sin(yaw) + local.X * Mathf.Cos(yaw),
            local.Y,
            local.Z * Mathf.Cos(yaw) - local.X * Mathf.Sin(yaw));
        return _pos + rotated * _scale;
    }
}
