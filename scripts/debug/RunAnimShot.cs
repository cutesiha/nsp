using Godot;
using NSP.View;

namespace NSP.Debug;

// 달리기 포즈 프레임별 검수(지시서 §14). 창 모드로 돌려야 그림이 나온다.
//
//   godot --path . res://scenes/debug/RunAnimShot.tscn -- <저장 폴더> <직원id|all> [walk|run]
//
// 한 직원을 정면 · 좌측 · 우측 · 후면 · 45도 다섯 방향에서, 걸음 주기를 네 토막으로
// 끊어 찍는다. 팔이 몸을 뚫는지 · 팔꿈치가 뒤집히는지는 이 격자로만 잡힌다.
public partial class RunAnimShot : Node
{
    private static readonly (string Name, float Yaw)[] Angles =
    {
        // 캐릭터는 자기 -Z 를 본다. 카메라는 +Z 쪽에 있으므로 yaw 180 이 정면이다.
        ("1front", 180f), ("2q45", 135f), ("3sideL", 90f), ("4sideR", -90f), ("5back", 0f),
    };

    private static readonly float[] Phases = { 0f, 0.25f, 0.5f, 0.75f };

    private static readonly string[] All = { "fox", "dog", "cat", "sheep", "rabbit", "wolf" };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        string who = args.Length > 1 ? args[1] : "fox";
        var gait = args.Length > 2 && args[2] == "walk" ? CutsceneActor.Gait.Walk : CutsceneActor.Gait.Run;

        BuildWorld();
        if (who == "motion") { await Motion(dir, args.Length > 3 ? args[3] : "dog"); return; }
        string[] list = who == "all" ? All : new[] { who };

        foreach (string id in list)
        {
            var actor = new CutsceneActor();
            AddChild(actor);
            actor.Spawn(id, Vector3.Zero);
            await Frames(3);

            foreach (var (name, yaw) in Angles)
            {
                // 인물을 돌린다 — 카메라를 돌리면 조명까지 같이 돌아 실루엣 비교가 안 된다.
                actor.DebugYaw(yaw);
                foreach (float ph in Phases)
                {
                    actor.DebugPose(gait, ph);
                    await Frames(2);
                    Shot(dir, $"{id}_{name}_{ph:0.00}");
                }
            }
            // 실측 : 한 걸음 = 두 발이 가장 벌어졌을 때의 간격. 한 주기는 두 걸음이므로
            // 주기 거리(Stride)가 그 두 배여야 발이 미끄러지지 않는다(§8).
            float maxGap = 0f;
            for (int i = 0; i < 48; i++)
            {
                actor.DebugPose(gait, i / 48f);
                maxGap = Mathf.Max(maxGap, actor.DebugFootGap());
            }
            float want = maxGap * 2f;
            GD.Print($"{id}: stride={actor.DebugStride:0.000}  필요={want:0.000}  " +
                     $"오차={(actor.DebugStride / Mathf.Max(0.01f, want) - 1f) * 100f:0.0}%");
            actor.QueueFree();
            await Frames(2);
        }
        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    // 실제로 움직이는 동안을 찍는다 — 정지에서 출발해 달리다가 90도 꺾고, 마지막엔
    // 감속해서 선다. 미끄러짐 · 출발 · 정지 · 방향전환은 이 모드로만 확인된다.
    private async System.Threading.Tasks.Task Motion(string dir, string id)
    {
        _cam = GetNodeOrNull<Camera3D>("Cam");
        if (_cam != null) _cam.Fov = 46f;

        var actor = new CutsceneActor();
        AddChild(actor);
        actor.Spawn(id, new Vector3(0f, 0f, 5f));
        actor.SnapFaceTo(new Vector3(0f, 0f, -1f));
        await Frames(3);

        _follow = actor;
        Track();
        double t = 0;
        int shot = 0;
        // ① 정지 → 출발 → 직선 달리기
        actor.MoveTo(new Vector3(0f, 0f, -5f), 4.6f, CutsceneActor.Gait.Run);
        while (t < 2.6) { t += await Step(); Track(); if (Due(ref shot, t, 0.2)) Shot(dir, $"m{shot:00}_{t:0.00}"); }
        // ② 90도 방향 전환
        actor.MoveTo(new Vector3(6f, 0f, -5f), 4.6f, CutsceneActor.Gait.Run);
        while (t < 4.6) { t += await Step(); Track(); if (Due(ref shot, t, 0.2)) Shot(dir, $"m{shot:00}_{t:0.00}"); }
        // ③ 감속 · 정지 · 숨 고르기
        while (t < 6.4) { t += await Step(); Track(); if (Due(ref shot, t, 0.2)) Shot(dir, $"m{shot:00}_{t:0.00}"); }

        GD.Print("saved → " + dir);
        GetTree().Quit();
    }

    private Camera3D _cam;
    private CutsceneActor _follow;

    // 옆에서 따라붙는다 — 화면 밖으로 나가 버리면 달리기를 볼 수가 없다.
    private void Track()
    {
        if (_cam == null || _follow == null) return;
        Vector3 at = _follow.Position;
        _cam.Position = at + new Vector3(3.6f, 1.9f, 3.0f);
        _cam.LookAt(at + new Vector3(0f, 0.85f, 0f), Vector3.Up);
    }

    private double _next;

    private bool Due(ref int shot, double t, double every)
    {
        if (t < _next) return false;
        _next = t + every;
        shot++;
        return true;
    }

    private async System.Threading.Tasks.Task<double> Step()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return GetProcessDeltaTime();
    }

    private void BuildWorld()
    {
        var cam = new Camera3D { Name = "Cam", Position = new Vector3(0f, 1.15f, 3.0f), Fov = 38f };
        AddChild(cam);
        cam.LookAt(new Vector3(0f, 0.92f, 0f), Vector3.Up);
        cam.Current = true;

        // 실루엣이 읽히도록 키 라이트 하나 + 뒤쪽에서 받쳐 주는 림 라이트 하나.
        var key = new DirectionalLight3D { RotationDegrees = new Vector3(-38f, 28f, 0f), LightEnergy = 1.5f };
        AddChild(key);
        var rim = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-12f, 196f, 0f),
            LightEnergy = 0.7f,
            LightColor = new Color(0.65f, 0.75f, 1f),
        };
        AddChild(rim);

        var env = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.17f, 0.18f, 0.21f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.4f, 0.43f, 0.5f),
                AmbientLightEnergy = 0.55f,
            },
        };
        AddChild(env);

        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(40f, 40f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.26f, 0.27f, 0.3f) },
        };
        AddChild(floor);

        // 1 m 격자 — 발이 바닥을 미끄러지는지는 기준선이 있어야 눈에 보인다.
        for (int i = -10; i <= 10; i++)
        {
            AddChild(Bar(new Vector3(i, 0.002f, 0f), new Vector3(0.02f, 0.004f, 40f)));
            AddChild(Bar(new Vector3(0f, 0.002f, i), new Vector3(40f, 0.004f, 0.02f)));
        }
    }

    private static MeshInstance3D Bar(Vector3 at, Vector3 size) => new()
    {
        Mesh = new BoxMesh { Size = size },
        Position = at,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.42f, 0.44f, 0.5f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    private void Shot(string dir, string name) =>
        GetViewport().GetTexture().GetImage().SavePng($"{dir}/{name}.png");

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
