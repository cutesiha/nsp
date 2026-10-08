using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그 후반 — SIGNAL LOST 뒤의 **화면 전체** 3D 재난 컷씬.
//
// 기록 영상의 틀(모니터1 · CRT 테두리)을 깨고 나온 구간이다. 여기서부터 플레이어는
// "작은 화면 속 사고"가 아니라 "지금 눈앞에서 무너지는 시설"을 본다(§C · §D).
//
// 각 컷의 구도는 기존 콘티 이미지에서 가져왔다 — 그림을 그대로 띄우는 대신
// 그 그림이 보여 주려던 **움직임**을 3D 에서 실제로 일으킨다(§0).
public partial class Prologue3DDirector
{
    // ── 유리 파편 ────────────────────────────────────────────────────────
    //
    // 큰 조각은 실제 메시로 날리고(§15 · §27), 작은 조각은 GPU 입자가 맡는다.
    // 물리 엔진을 붙이지 않는다 — 몇 개만, 연출기가 정확히 통제해야 하는 궤적이다.
    private sealed class Shard
    {
        public MeshInstance3D Node;
        public Vector3 Vel;
        public Vector3 Spin;
        public float Life;
        public float FloorY;
    }

    private readonly List<Shard> _shards = new();

    private static readonly StandardMaterial3D GlassMat = new()
    {
        AlbedoColor = new Color(0.72f, 0.88f, 0.95f, 0.55f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.05f,
        Metallic = 0.3f,
        EmissionEnabled = true,
        Emission = new Color(0.65f, 0.85f, 1f),
        EmissionEnergyMultiplier = 0.35f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // 파편 한 줌. toward 가 카메라 쪽이면 **눈앞을 스쳐 지나간다**(§28).
    private void SpawnShards(Node3D parent, Vector3 origin, Vector3 toward, int count, float speed,
        float spread = 0.55f, float floorY = 0.05f, float size = 1f)
    {
        var dir = toward.Normalized();
        for (int i = 0; i < count; i++)
        {
            float w = _rng.RandfRange(0.10f, 0.42f) * size;
            float h = _rng.RandfRange(0.14f, 0.55f) * size;
            var m = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w, h, 0.02f) },
                Position = origin + new Vector3(_rng.RandfRange(-0.6f, 0.6f), _rng.RandfRange(-0.5f, 0.9f),
                    _rng.RandfRange(-0.6f, 0.6f)),
                MaterialOverride = GlassMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            parent.AddChild(m);
            var v = (dir + new Vector3(_rng.RandfRange(-spread, spread), _rng.RandfRange(-spread * 0.5f, spread),
                _rng.RandfRange(-spread, spread))).Normalized() * speed * _rng.RandfRange(0.6f, 1.5f);
            _shards.Add(new Shard
            {
                Node = m, Vel = v, Life = _rng.RandfRange(2.2f, 3.6f), FloorY = floorY,
                Spin = new Vector3(_rng.RandfRange(-900f, 900f), _rng.RandfRange(-900f, 900f),
                    _rng.RandfRange(-900f, 900f)),
            });
        }
    }

    private void TickShards(float d)
    {
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            if (!IsInstanceValid(s.Node)) { _shards.RemoveAt(i); continue; }
            s.Life -= d;
            s.Vel += new Vector3(0f, -13f, 0f) * d;
            var p = s.Node.Position + s.Vel * d;
            if (p.Y <= s.FloorY)
            {
                // 바닥에 닿으면 한 번 튀고 미끄러지다 멈춘다.
                p.Y = s.FloorY;
                s.Vel = new Vector3(s.Vel.X * 0.35f, -s.Vel.Y * 0.28f, s.Vel.Z * 0.35f);
                s.Spin *= 0.3f;
            }
            s.Node.Position = p;
            s.Node.RotationDegrees += s.Spin * d;
            if (s.Life <= 0f) { s.Node.QueueFree(); _shards.RemoveAt(i); }
        }
    }

    // ── ⑥ 연구실 파손 (disaster_04_lab_wreck) ───────────────────────────
    //
    // 대재난의 물리적 충격을 처음 보여 주는 컷. 두 번 흔들리고, 두 번째에 유리가 깨진다(§14).
    private async Task DisasterLab(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Lab);
        _stage.PrologueLights(1f);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Lab);
        _stage.SetCam(new Vector3(-1.9f, 1.8f, 5.6f), new Vector3(0.9f, 1.35f, -1.6f), 60f);

        var worker = Spawn(root, "cat", new Vector3(-2.2f, 0f, 0.9f), 155f);
        worker.PlayClip("console_operate_f");

        // ① 조명이 한 번 흔들린다.
        await Wait(0.4);
        if (!Alive(gen)) return;
        _stage.PrologueLights(0.25f);
        Sfx.Instance?.Play("flicker", -10f);
        await Wait(0.1);
        _stage.PrologueLights(1f);

        // ② 낮은 충격음 — 아직 멀다.
        Sfx.Instance?.Play("boom", -14f, 0.7f);
        Shake(0.35f, 1.8f);
        JiggleProps(0.5f);
        await Wait(1.1);
        if (!Alive(gen)) return;

        // ③ 두 번째, 훨씬 큰 충격 — 여기서 전부 무너진다.
        Sfx.Instance?.Play("boom", -4f, 0.82f);
        Sfx.Instance?.Play("rubble_collapse", -9f);
        Shake(2.6f, 1.5f);
        _stage.PrologueLights(0.12f);
        worker.PlayClip("ghost_startle");
        JiggleProps(2.6f, knock: true);
        await Wait(0.16);
        if (!Alive(gen)) return;

        // ④ 유리 칸막이 파손 — 큰 조각은 메시, 작은 조각은 입자.
        Sfx.Instance?.Play("glass_shatter", -3f);
        var glass = _stage.LabGlass;
        if (glass != null)
        {
            glass.Visible = false;
            SpawnShards(root, glass.Position, new Vector3(-0.85f, 0.25f, 0.5f), 16, 5.4f, floorY: 0.06f);
            var dust = _stage.Debris(root, glass.Position, 0.55f, 90);
            dust.Emitting = true;
        }
        _stage.PrologueLights(0.45f);
        await Wait(0.5);
        if (!Alive(gen)) return;
        _stage.PrologueLights(0.8f);
        worker.PlayClip("ghost_uneasy");
    }

    // 실험대 위 소품이 흔들리고, 큰 충격에서는 떨어진다.
    private void JiggleProps(float power, bool knock = false)
    {
        foreach (var p in _stage.LabProps)
        {
            if (!IsInstanceValid(p)) continue;
            var t = CreateTween();
            var rest = p.Position;
            t.TweenProperty(p, "position", rest + new Vector3(
                _rng.RandfRange(-0.04f, 0.04f) * power, 0.02f * power, _rng.RandfRange(-0.04f, 0.04f) * power), 0.06);
            if (knock && _rng.Randf() < 0.55f)
            {
                // 떨어진다 — 바닥까지 포물선으로.
                t.TweenProperty(p, "position", rest + new Vector3(_rng.RandfRange(-0.9f, 0.9f), -0.95f,
                    _rng.RandfRange(0.3f, 1.1f)), 0.55).SetTrans(Tween.TransitionType.Quad)
                    .SetEase(Tween.EaseType.In);
                t.Parallel().TweenProperty(p, "rotation_degrees",
                    new Vector3(_rng.RandfRange(-90f, 90f), _rng.RandfRange(-90f, 90f), _rng.RandfRange(-90f, 90f)), 0.55);
            }
            else t.TweenProperty(p, "position", rest, 0.12);
        }
    }

    // ── ⑦ 직원 도주 (disaster_05_staff_running) ──────────────────────────
    //
    // 셋이 **실제로 달린다.** 다리 위상이 이동 거리에서 나오므로 발이 미끄러지지 않는다.
    // 타이밍은 전부 다르다(§17).
    private async Task DisasterRun(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Corridor);
        _stage.PrologueLights(0.35f);
        _stage.PrologueEmergency(1f);
        _stage.PrologueBulkheadReset();
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Corridor);

        // 오른쪽 벽에 붙어 선 트래킹 샷 — 직원들이 **옆을 스쳐** 안쪽으로 달려간다(§47).
        // 카메라를 복도 한가운데 두면 달려오는 직원을 그대로 뚫고 지나간다.
        _stage.SetCam(new Vector3(2.05f, 1.85f, 13f), new Vector3(-0.6f, 1.35f, -6f), 64f);

        // 카메라는 오른쪽 벽(x=2.05)에 붙어 있어 바로 옆을 지나는 동안은 화면 밖이다.
        // 그래서 한 명은 **이미 앞서 달리고 있는 상태**로 두고, 나머지 둘이 뒤에서 스쳐 지나간다.
        // 셋 다 뒤에서 출발시키면 컷의 첫 박자가 통째로 빈 복도가 된다.
        var a = Spawn(root, "dog", new Vector3(-1.3f, 0f, 9.0f));
        var b = Spawn(root, "fox", new Vector3(0.2f, 0f, 15.0f));
        var c = Spawn(root, "rabbit", new Vector3(-0.6f, 0f, 18.0f));
        a.MoveTo(new Vector3(-1.2f, 0f, -16f), 5.4f, CutsceneActor.Gait.Run);
        b.MoveTo(new Vector3(0.2f, 0f, -16f), 4.8f, CutsceneActor.Gait.Run);
        c.MoveTo(new Vector3(-0.7f, 0f, -16f), 5.1f, CutsceneActor.Gait.Run);

        Sfx.Instance?.Play("footsteps_run", -7f);
        Sfx.Instance?.Loop("siren", -4f);
        _ = _stage.MoveCamCut(new Vector3(2.05f, 1.8f, 6f), new Vector3(-0.4f, 1.35f, -12f), 4.2);

        // 직원 B — 뒤를 한 번 돌아보고 다시 달린다.
        await Wait(0.9);
        if (!Alive(gen)) return;
        b.LookOverShoulder(150f);
        await Wait(0.45);
        if (!Alive(gen)) return;
        b.LookOverShoulder(0f);

        // 직원 C — 충격에 균형을 잃었다가 다시 달린다.
        await Wait(0.35);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("boom", -10f, 0.75f);
        Shake(1.4f, 2.0f);
        c.Stumble = 1f;
        await Wait(1.6);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("footsteps_run", -11f);
    }

    // ── ⑧ 차폐문 폐쇄 (disaster_06_door_closing) ────────────────────────
    private async Task DisasterBulkhead(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Corridor);
        _stage.PrologueLights(0.3f);
        _stage.PrologueEmergency(1f);
        _stage.PrologueBulkheadReset();
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Corridor);

        // 문 앞쪽에서 문을 바라본다 — 닫히는 틈이 보여야 한다.
        _stage.SetCam(new Vector3(-1.5f, 1.8f, -9.5f), new Vector3(0f, 2.0f, -20f), 58f);

        var runner = Spawn(root, "sheep", new Vector3(0.3f, 0f, -7f));
        runner.MoveTo(new Vector3(0.3f, 0f, -22f), 5.6f, CutsceneActor.Gait.Run);
        Sfx.Instance?.Play("footsteps_run", -8f);

        // 경고등 점멸 → 문이 내려온다.
        for (int i = 0; i < 3 && Alive(gen); i++)
        {
            _stage.BulkheadWarn(1f);
            Sfx.Instance?.Play("alert_beep3", -9f);
            await Wait(0.18);
            _stage.BulkheadWarn(0.15f);
            await Wait(0.2);
        }
        if (!Alive(gen)) return;
        _stage.BulkheadWarn(1f);
        _stage.PrologueBulkheadClose(1.9);
        Sfx.Instance?.Loop("machinery_loop", -10f);
        _ = _stage.MoveCamCut(new Vector3(-0.9f, 1.7f, -13.5f), new Vector3(0f, 1.9f, -20f), 2.0);

        await Wait(1.95);
        if (!Alive(gen)) return;
        // 가까스로 닫힌다 — 쇳소리 한 번.
        Sfx.Instance?.StopLoop("machinery_loop");
        Sfx.Instance?.Play("metal_clang", -2f);
        Shake(1.1f, 2.2f);
        _stage.BulkheadWarn(0f);
        await Wait(0.6);
    }

    // ── ⑨ CCTV 속 그림자 (disaster_07_cctv_shadow) ──────────────────────
    //
    // 고정 카메라. 아무 일도 없다가 **화면 바로 앞을** 검은 형체가 빠르게 가로지른다.
    // 자세히 보여 주지 않는다 — "뭐 지나갔지?" 면 된다(§19).
    private async Task DisasterCctv(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Corridor);
        _stage.PrologueLights(0.22f);
        _stage.PrologueEmergency(0.35f);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Corridor);

        // 천장 구석에 매단 감시 카메라 시점 — 고정이다.
        _stage.SetCam(new Vector3(1.9f, 3.1f, 7.5f), new Vector3(-0.3f, 1.45f, -8f), 72f);

        var fig = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.62f, 1.85f, 0.34f) },
            Position = new Vector3(3.6f, 0.95f, 5.0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.012f, 0.012f, 0.014f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        fig.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.21f, Height = 0.44f, RadialSegments = 10, Rings = 6 },
            Position = new Vector3(0f, 1.1f, 0f),
            MaterialOverride = fig.MaterialOverride,
        });
        root.AddChild(fig);

        await Wait(0.55);   // 빈 복도
        if (!Alive(gen)) return;

        // 렌즈 바로 앞을 가로지른다. 너무 빠르면 한 프레임도 안 걸리고 지나가 버린다 —
        // 0.42초면 '무언가 지나갔다' 는 것은 남고 정체는 보이지 않는다(§27).
        var t = CreateTween();
        t.TweenProperty(fig, "position", new Vector3(-3.8f, 0.95f, 5.6f), 0.42);
        await Wait(0.46);
        if (!Alive(gen)) return;
        fig.Visible = false;

        Sfx.Instance?.Play("noise", -12f, 1.4f);
        await Wait(0.35);
        if (!Alive(gen)) return;
        Sfx.Instance?.Play("cctv_cut", -5f);
    }

    // ── ⑩ 봉쇄 코어 출력 급락 (disaster_08 / 10) ────────────────────────
    //
    // **엔딩이 복구하는 그 코어실이다.** 게이지 숫자만 내려가는 것이 아니라
    // 빛 · 링 · 바닥등 · 흔들림이 함께 무너진다(§20 · §21).
    private async Task CoreDrop(int gen)
    {
        _stage.ShowCoreHallForPrologue();
        _stage.PrologueCoreRestoreIntact();
        _stage.CoreHallFill(1.6f, new Color(0.62f, 0.70f, 0.85f));
        _stage.CoreFloorFill(1.4f);
        _stage.PrologueCoreOutput(100f);
        // 코어만 꽉 채우면 '거대한 홀' 이 읽히지 않는다 — 바닥 · 난간까지 들어오게 물러난다.
        _stage.SetCam(new Vector3(0.6f, 3.1f, 15.8f), new Vector3(0f, 6.6f, -3f), 62f);
        Sfx.Instance?.Loop("machinery_loop", -8f);
        Sfx.Instance?.Loop("siren", -7f);

        // 천천히 밀고 들어가면서 출력이 떨어진다.
        _ = _stage.MoveCamCut(new Vector3(0.1f, 3.0f, 8.0f), new Vector3(0f, 8.0f, -3f), 9.0);

        foreach (var (pct, wait, shake) in new[]
                 {
                     (74f, 1.5, 0.3f), (41f, 1.6, 0.8f), (16f, 1.5, 1.5f), (3f, 1.4, 2.4f),
                 })
        {
            if (!Alive(gen)) return;
            _stage.PrologueCoreOutput(pct);
            // 방 전체도 같이 어두워진다 — 코어가 이 공간의 유일한 전원이다.
            _stage.CoreHallFill(0.25f + 1.35f * (pct / 100f), new Color(0.62f, 0.70f, 0.85f));
            _stage.CoreFloorFill(1.4f * (pct / 100f));
            Sfx.Instance?.Play("power_point_lost", -8f);
            if (pct <= 41f) Sfx.Instance?.Play("electric_arc", -11f);
            if (pct <= 16f) Sfx.Instance?.Play("alert_beep3", -5f);
            Shake(shake, 1.1f);
            await Wait(wait);
        }
        if (!Alive(gen)) return;
        _stage.PrologueCoreOutput(0.6f);
        Shake(3.0f, 0.9f);
        await Wait(0.7);
    }

    // ── ⑪ 0% → 정적 → 대폭발 (프롤로그 최대 임팩트) ─────────────────────
    //
    // 0% 직후 **바로 터뜨리지 않는다.** 빛이 꺼지고 소리가 뚝 끊기는 0.5초를 둔 뒤
    // 터진다 — 그 박자가 이 장면의 전부다(§22 · §23).
    private async Task CoreExplode(int gen)
    {
        _stage.ShowCoreHallForPrologue();
        var root = _stage.CoreHallRoot;
        // 난간 뒤 — 사람 눈높이에서 올려다본다. 파편이 이쪽으로 날아와야 한다.
        _stage.SetCam(new Vector3(0.2f, 2.0f, 11.0f), new Vector3(0f, 8.0f, -3f), 60f);

        // ① 정적. 코어가 꺼지고 모든 소리가 사라진다.
        _stage.PrologueCoreSilence();
        // 완전한 암전으로 두면 '무서운 정적' 이 아니라 그냥 빈 화면이 된다.
        // 홀의 윤곽만 남기고 비상등 한 점을 켜 둔다 — 그래야 다음 섬광이 대비로 터진다.
        _stage.CoreHallFill(0.26f, new Color(0.42f, 0.5f, 0.72f));
        _stage.CoreFloorFill(0.12f, new Color(0.9f, 0.3f, 0.26f));
        foreach (var k in new[] { "machinery_loop", "siren", "alarm" }) Sfx.Instance?.StopLoop(k);
        _shake = 0f;
        _stage.ShakeOffset(Vector3.Zero);
        await Wait(0.55);
        if (!Alive(gen)) return;

        // ② 폭발. 소리를 겹쳐 쌓는다 — boom 하나로 끝내지 않는다(§29).
        Sfx.Instance?.Play("boom", 0f, 0.62f);          // 깊은 저음
        Sfx.Instance?.Play("boom", -5f, 1.35f);         // 날카로운 윗소리
        Sfx.Instance?.Play("glass_shatter", -2f);
        Sfx.Instance?.Play("metal_clang", -4f);
        Sfx.Instance?.Play("electric_arc", -3f);
        Sfx.Instance?.Play("rubble_collapse", -5f);

        Flash?.Invoke(new Color(0.85f, 0.94f, 1f), 0.09f, 0.22f);
        Shake(9f, 3.2f);
        _stage.PrologueCoreBreach();
        // 터지는 순간 공간 전체가 한 번 밝아졌다가 잦아든다 — 그래야 홀의 크기가 보인다.
        _stage.CoreHallFill(5.5f, new Color(1f, 0.62f, 0.38f));
        var fade = CreateTween();
        fade.TweenMethod(Callable.From<float>(v => _stage.CoreHallFill(v)), 5.5f, 0.75f, 1.6);

        var c = EndingCutsceneStage.CoreCenter;
        // 카메라 쪽으로 날아오는 큰 유리 조각 — "내 앞까지 튀었다" 가 이 장면의 체감이다(§28).
        SpawnShards(root, c, new Vector3(0.05f, -0.35f, 1f), 14, 17f, spread: 0.3f, floorY: 0.1f, size: 1.5f);
        SpawnShards(root, c, new Vector3(-0.8f, 0.1f, 0.5f), 10, 11f, floorY: 0.1f);
        SpawnShards(root, c, new Vector3(0.8f, 0.1f, 0.5f), 10, 11f, floorY: 0.1f);
        var deb = _stage.Debris(root, c, 1.6f, 170);
        deb.Emitting = true;

        await Wait(0.22);
        if (!Alive(gen)) return;
        // ③ 2차 — 구조물이 무너지는 둔탁한 소리.
        Sfx.Instance?.Play("impact_blunt", -4f);
        Shake(4.5f, 2.4f);
        await Wait(0.45);
        if (!Alive(gen)) return;

        // ④ 이명 — 주변 소리가 한순간 멀어진다(§30).
        Sfx.Instance?.Play("tinnitus", -5f);
        Muffle?.Invoke(1.6f);
        await Wait(1.3);
        if (!Alive(gen)) return;

        // ⑤ 무너진 코어를 잠깐 보여 준다(§31).
        await _stage.MoveCamCut(new Vector3(-1.4f, 2.4f, 9.5f), new Vector3(0.4f, 7.4f, -3f), 2.4);
    }

    // 폭발 섬광 · 먹먹함은 화면 오버레이가 맡는다 — 연출기가 바깥에서 건다.
    public System.Action<Color, float, float> Flash;
    public System.Action<float> Muffle;

    // 화면을 어둡게 덮는다(검은 장막). 지상 컷씬이 소리부터 시작하고 잡음과 함께
    // 끊기는 데 쓴다 — 섬광(Flash)과 달리 알파를 그대로 쥐고 있는다.
    public System.Action<float, float> Fade;

    // ── ⑫ 폭발 직후의 시설 (무전 배경) ──────────────────────────────────
    private async Task DisasterAfter(int gen)
    {
        _stage.ShowCoreHallForPrologue();
        // 폭발 장면이 만들어 둔 상태에 기대지 않는다 — 플레이어가 폭발 컷을 빨리 넘기면
        // 멀쩡한 코어를 배경으로 "무전이 끊겼다" 가 흐르게 된다.
        _stage.PrologueCoreOutput(0f);
        _stage.PrologueCoreBreach();
        _stage.SetCam(new Vector3(-2.6f, 2.2f, 8.0f), new Vector3(0.6f, 6.6f, -3f), 56f);
        _stage.CoreHallFill(0.7f, new Color(1f, 0.55f, 0.35f));
        Sfx.Instance?.Loop("siren", -12f);
        Sfx.Instance?.Loop("electric_crackle_loop", -20f);
        Shake(0.35f, 0.25f);
        // 아주 느리게 — 무전 내용이 읽혀야 하므로 화면이 바쁘면 안 된다(§33 · §50).
        await _stage.MoveCamCut(new Vector3(-1.2f, 2.6f, 10.5f), new Vector3(0.2f, 7.0f, -3f), 10.0);
    }

    // ── ⑬ 총괄 관리자의 마지막 지시 + 비상 차폐 스위치 ──────────────────
    //
    // 아카이브 때와 같은 브리핑실이지만 붉은 비상등 · 흔들림 · 경보가 깔린다(§34).
    // 손이 **실제로 스위치까지 가서** 내린다 — 허공을 누르면 안 된다(§35).
    private async Task DirectorLast(int gen)
    {
        _stage.ShowPrologueSet(EndingCutsceneStage.PSet.Briefing);
        var root = _stage.PrologueSetRoot(EndingCutsceneStage.PSet.Briefing);
        _stage.PrologueLights(0.18f);
        EnsureEmergencyRig(root);
        SetBriefingEmergency(1f);
        _stage.SetCam(new Vector3(1.5f, 1.68f, 1.9f), new Vector3(-0.5f, 1.45f, -1.2f), 50f);

        var dir = Spawn(root, "wolf", new Vector3(0f, 0f, -1.05f), 180f);
        dir.PlayClip("talk");
        Sfx.Instance?.Loop("alarm", -16f);
        Shake(0.5f, 0.2f);

        await Wait(2.6);
        if (!Alive(gen)) return;

        // 관리자가 패널 쪽으로 몸을 돌려 다가간다.
        dir.SetGait(CutsceneActor.Gait.Walk);
        // 레버 손잡이는 월드 (-2.1, 1.05, -2.4) 에 있다. lever_operate 의 팔 길이가 0.39m 라
        // 그만큼 떨어진 자리에 서야 손이 허공을 짚지 않는다(§35).
        dir.MoveTo(new Vector3(-1.72f, 0f, -2.42f), 1.3f, CutsceneActor.Gait.Walk);
        // 관리자의 옆 · 앞쪽으로 돌아 들어간다 — 뻗는 팔과 레버가 같이 보여야 한다.
        _ = _stage.MoveCamCut(new Vector3(0.75f, 1.55f, -1.9f), new Vector3(-2.1f, 1.2f, -2.3f), 2.2, 48f);
        await Wait(1.5);
        if (!Alive(gen)) return;
        dir.Stop();
        dir.FaceTo(new Vector3(-2.4f, 1.05f, -2.42f));

        // 손이 레버까지 간다 — lever_operate 클립이 실제로 레버 높이를 짚는다.
        dir.PlayClip("lever_operate_m");
        await Wait(0.75);
        if (!Alive(gen)) return;

        // 레버가 내려가고, 프롤로그의 그 소리가 난다.
        if (_sealLever != null)
        {
            var t = CreateTween();
            t.TweenProperty(_sealLever, "rotation_degrees:x", 58f, 0.22).SetTrans(Tween.TransitionType.Quad);
        }
        Sfx.Instance?.Play("switch", -2f);
        await Wait(0.25);
        if (!Alive(gen)) return;

        // 멀리서 거대한 금속 구조가 잠긴다(§36).
        Sfx.Instance?.Play("metal_clang", -6f, 0.65f);
        Sfx.Instance?.Play("rubble_collapse", -14f);
        SetBriefingEmergency(0.6f);
        if (_sealLampMat != null) _sealLampMat.EmissionEnergyMultiplier = 3.4f;
        Shake(1.6f, 1.2f);
        await Wait(1.4);
    }

    // 브리핑실의 비상 상태 — 붉은 등과 비상 차폐 레버. 아카이브 컷에서는 꺼져 있다.
    private Node3D _sealLever;
    private StandardMaterial3D _sealLampMat;
    private readonly List<OmniLight3D> _briefRed = new();
    private readonly List<StandardMaterial3D> _briefRedMat = new();

    private void EnsureEmergencyRig(Node3D root)
    {
        if (_sealLever != null && IsInstanceValid(_sealLever)) return;
        _briefRed.Clear();
        _briefRedMat.Clear();

        var steel = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.42f, 0.46f), Metallic = 0.85f, Roughness = 0.35f };

        // 왼쪽 벽의 비상 차폐 패널.
        var panel = new Node3D { Position = new Vector3(-2.4f, 0f, -2.3f), RotationDegrees = new Vector3(0f, 90f, 0f) };
        root.AddChild(panel);
        panel.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.3f, 1.0f, 0.25f) },
            Position = new Vector3(0f, 1.2f, 0f),
            MaterialOverride = steel,
        });
        _sealLampMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.05f, 0.03f),
            EmissionEnabled = true, Emission = new Color(1f, 0.35f, 0.12f), EmissionEnergyMultiplier = 0.2f,
        };
        panel.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.14f, 0.05f) },
            Position = new Vector3(0f, 1.56f, 0.14f),
            MaterialOverride = _sealLampMat,
        });
        // 레버 — 손이 닿는 높이(lever_operate 기준 약 1.0m).
        _sealLever = new Node3D { Position = new Vector3(0.1f, 1.05f, 0.16f) };
        panel.AddChild(_sealLever);
        var lever = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.07f, 0.34f) },
            Position = new Vector3(0f, 0f, 0.17f),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.12f, 0.08f) },
        };
        _sealLever.AddChild(lever);

        // 붉은 비상등 둘.
        foreach (float x in new[] { -3.2f, 3.2f })
        {
            var m = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.25f, 0.03f, 0.02f),
                EmissionEnabled = true, Emission = new Color(1f, 0.15f, 0.1f), EmissionEnergyMultiplier = 0f,
            };
            _briefRedMat.Add(m);
            root.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.18f, 0.18f) },
                Position = new Vector3(x, 3.4f, -2.9f),
                MaterialOverride = m,
            });
            var l = new OmniLight3D
            {
                Position = new Vector3(x * 0.8f, 3.2f, -2.4f), LightColor = new Color(1f, 0.16f, 0.1f),
                LightEnergy = 0f, OmniRange = 10f, ShadowEnabled = false,
            };
            root.AddChild(l);
            _briefRed.Add(l);
        }
    }

    private void SetBriefingEmergency(float k)
    {
        foreach (var m in _briefRedMat) if (m != null) m.EmissionEnergyMultiplier = 3.2f * k;
        foreach (var l in _briefRed) if (l != null) l.LightEnergy = 2.2f * k;
    }
}
