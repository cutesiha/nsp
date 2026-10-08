using System.Collections.Generic;
using Godot;

namespace NSP.View;

// ── 지상(地上) ───────────────────────────────────────────────────────────
//
// 대재난 사이렌 직후 한 번 보여 주는 바깥 풍경. 목적은 하나다 —
// **"지하 시설만의 사고가 아니다. 밖은 이미 끝났다."**
//
// 정지 그림 한 장이 아니라 살아 있는 장면이어야 하므로, 여기서 짓는 것은 배경이 아니라
// 계속 움직이는 것들이다 : 날리는 재 · 피어오르는 연기 · 흔들리는 불빛 · 아직도
// 무너지는 구조물. 카메라는 Prologue3DDirector.Surface 쪽에서 움직인다.
//
// 이 파일도 partial 이다 — 엔딩 쪽 파일은 건드리지 않는다.
public partial class EndingCutsceneStage
{
    private readonly List<(OmniLight3D Light, float Base, float Seed)> _fireLights = new();
    private readonly List<Node3D> _emberBits = new();
    private Node3D _collapseTower;          // 와이드 샷 도중 무너지는 건물
    private WorldEnvironment _envNode;
    private float _surfT;

    // 지하 세트와 하늘 · 공기가 완전히 다르다. 들어갈 때 바꾸고 나올 때 되돌린다.
    private static readonly Color SkyRed = new(0.115f, 0.031f, 0.026f);
    private static readonly Color DustRed = new(0.30f, 0.105f, 0.072f);

    private WorldEnvironment EnvNode
    {
        get
        {
            if (_envNode != null) return _envNode;
            if (_world == null) return null;
            foreach (var c in _world.GetChildren())
                if (c is WorldEnvironment w) { _envNode = w; break; }
            return _envNode;
        }
    }

    public void SurfaceAtmosphere(bool on)
    {
        var e = EnvNode?.Environment;
        if (e == null) return;
        if (on)
        {
            // 단색 배경은 '하늘' 로 안 보인다 — 수평선 쪽이 타오르고 위로 갈수록 검어지는
            // 그라데이션이라야 한다. 하늘만 붉고 땅이 멀쩡해도 안 되므로 안개로 공기
            // 전체를 재에 담근다.
            e.BackgroundMode = Godot.Environment.BGMode.Sky;
            e.Sky ??= new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.035f, 0.012f, 0.014f),
                    SkyHorizonColor = new Color(0.46f, 0.105f, 0.045f),
                    SkyCurve = 0.11f,
                    GroundHorizonColor = new Color(0.20f, 0.065f, 0.04f),
                    GroundBottomColor = new Color(0.03f, 0.018f, 0.016f),
                    GroundCurve = 0.04f,
                    SunAngleMax = 1f,
                    EnergyMultiplier = 1.0f,
                },
            };
            e.BackgroundColor = SkyRed;
            e.AmbientLightColor = new Color(0.34f, 0.13f, 0.09f);
            e.AmbientLightEnergy = 0.75f;
            e.FogEnabled = true;
            e.FogLightColor = DustRed;
            e.FogLightEnergy = 0.9f;
            e.FogDensity = 0.016f;
            e.FogSkyAffect = 1f;
            e.FogAerialPerspective = 0.5f;
        }
        else
        {
            e.BackgroundMode = Godot.Environment.BGMode.Color;
            e.BackgroundColor = Colors.Black;
            e.AmbientLightColor = new Color(0.10f, 0.10f, 0.12f);
            e.AmbientLightEnergy = 0.35f;
            e.FogEnabled = false;
        }
    }

    // 불빛이 흔들리고 재가 날린다. 연출기가 매 프레임 돌린다.
    public void TickSurface(float delta)
    {
        if (_pSurface == null || !_pSurface.Visible) return;
        _surfT += delta;
        foreach (var (light, base_, seed) in _fireLights)
        {
            if (light == null) continue;
            float f = Mathf.Sin(_surfT * (5.3f + seed * 3.1f) + seed * 11f)
                    * Mathf.Sin(_surfT * (11.7f + seed) + seed * 5f);
            light.LightEnergy = base_ * (0.62f + 0.38f * (0.5f + 0.5f * f));
        }
        // 멀리서 아직도 떨어지는 잔해 — 바닥에 닿으면 다시 위로 올린다.
        foreach (var bit in _emberBits)
        {
            if (bit == null) continue;
            var p = bit.Position;
            p.Y -= delta * (3.5f + Mathf.Abs(p.X) % 2.5f);
            if (p.Y < 0.2f) p.Y = 14f + Mathf.Abs(p.Z) % 9f;
            bit.Position = p;
            bit.RotateY(delta * 1.7f);
        }
    }

    // 무너지는 건물을 0~1 로 쓰러뜨린다(와이드 샷에서 한 동만).
    public void SurfaceCollapse(float k)
    {
        if (_collapseTower == null) return;
        k = Mathf.Clamp(k, 0f, 1f);
        float e = k * k * (3f - 2f * k);                    // 처음엔 천천히, 중간에 빠르게
        _collapseTower.RotationDegrees = new Vector3(0f, 14f, -7f - 62f * e);
        _collapseTower.Position = _collapseTower.Position with { Y = -6f * e * e };
    }

    // ── 낡은 기록영상 질감 ──────────────────────────────────────────────
    // Screen(컷씬 출력) 바로 위에 한 겹 덮는다. 자막 · 경보창 같은 UI 는 이 위에 오므로
    // 글자는 또렷하게 남고 **영상만** 거칠어진다.
    private ArchiveFilm _film;

    public void EnableFilm(ArchiveFilm.Look look)
    {
        if (Screen == null) return;
        if (_film == null)
        {
            _film = new ArchiveFilm();
            Screen.GetParent().AddChild(_film);
            _film.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            Screen.GetParent().MoveChild(_film, Screen.GetIndex() + 1);
        }
        _film.Visible = true;
        _film.SetLook(look);
    }

    public ArchiveFilm Film => _film;

    public Node3D SurfaceRoot => _pSurface;

    // 마지막 컷에서 카메라 바로 앞을 덮는 먼지. 한 번만 만든다.
    private GpuParticles3D _surfDust;

    public void SurfaceDust(Vector3 at)
    {
        if (_pSurface == null) return;
        _surfDust ??= Ash(_pSurface, at, new Vector3(3.0f, 1.8f, 2.2f),
            new Vector3(0.5f, 0.35f, 1f), 1.1f, 170, 0.19f,
            new Color(0.42f, 0.29f, 0.24f), 5.0);
        _surfDust.Position = at;
        _surfDust.Restart();
        _surfDust.Emitting = true;
    }

    // 재 · 연기용 둥근 입자 그림. 공용 Smoke() 는 1 m 사각형을 통째로 키우는 방식이라
    // 크게 쓰면 **회색 네모**가 화면을 덮어 버린다. 지상은 입자를 크게 써야 하므로
    // 가장자리가 사라지는 둥근 그림을 따로 만든다.
    private static Texture2D _softDot;

    private static Texture2D SoftDot()
    {
        if (_softDot != null) return _softDot;
        var g = new Gradient();
        g.SetOffset(0, 0f);
        g.SetColor(0, new Color(1f, 1f, 1f, 1f));
        g.SetOffset(1, 1f);
        g.SetColor(1, new Color(1f, 1f, 1f, 0f));
        _softDot = new GradientTexture2D
        {
            Gradient = g,
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
        };
        return _softDot;
    }

    // 떠다니는 재 · 피어오르는 연기. size 는 입자 하나의 지름(m).
    private GpuParticles3D Ash(Node3D parent, Vector3 pos, Vector3 extents, Vector3 drift,
        float size, int amount, float alpha, Color tint, double life = 8.0)
    {
        var mat = new ParticleProcessMaterial
        {
            Direction = drift.Normalized(),
            Spread = 38f,
            InitialVelocityMin = drift.Length() * 0.6f,
            InitialVelocityMax = drift.Length() * 1.5f,
            Gravity = new Vector3(0f, -0.08f, 0f),
            ScaleMin = size * 0.45f,
            ScaleMax = size * 1.35f,
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = extents,
        };
        var p = new GpuParticles3D
        {
            Position = pos,
            Amount = amount,
            Lifetime = life,
            Preprocess = (float)life * 0.8f,   // 컷이 열리는 순간 이미 날리고 있어야 한다
            Emitting = false,
            ProcessMaterial = mat,
            DrawPass1 = new QuadMesh { Size = new Vector2(1f, 1f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = tint with { A = alpha },
                AlbedoTexture = SoftDot(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                DisableReceiveShadows = true,
                NoDepthTest = false,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(p);
        return p;
    }

    private void BuildSurface()
    {
        _pSurface = new Node3D { Name = "Surface", Visible = false };
        _world.AddChild(_pSurface);
        var rng = new RandomNumberGenerator();
        rng.Seed = 90210;

        // ── 땅 : 재가 덮인 콘크리트 ──────────────────────────────────────
        var ground = Mat(new Color(0.085f, 0.062f, 0.055f), 0.98f);
        Box(_pSurface, new Vector3(320f, 0.4f, 320f), new Vector3(0f, -0.2f, 0f), ground, "Ground");

        // 갈라진 금 — 바닥이 통짜 면으로 보이지 않게 한다.
        var crack = Mat(new Color(0.035f, 0.024f, 0.022f), 1f);
        for (int i = 0; i < 26; i++)
        {
            float a = rng.RandfRange(0f, 180f);
            var c = Box(_pSurface, new Vector3(rng.RandfRange(4f, 22f), 0.03f, rng.RandfRange(0.08f, 0.3f)),
                new Vector3(rng.RandfRange(-45f, 45f), 0.02f, rng.RandfRange(-30f, 26f)), crack, "Crack");
            c.RotationDegrees = new Vector3(0f, a, 0f);
        }

        // ── 붉은 태양 ────────────────────────────────────────────────────
        // 밝은 원반이 아니라 연기에 먹힌 검붉은 덩어리여야 한다.
        var sunMat = Glow(new Color(0.62f, 0.14f, 0.06f), 2.1f);
        var sun = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 9f, Height = 18f, RadialSegments = 24, Rings = 12 },
            Position = new Vector3(-38f, 13f, -125f),
            MaterialOverride = sunMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _pSurface.AddChild(sun);

        // 태양 쪽에서 들어오는 낮고 붉은 빛.
        _pSurface.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-9f, -163f, 0f),
            LightColor = new Color(1f, 0.42f, 0.24f),
            LightEnergy = 1.55f,
            ShadowEnabled = false,
        });

        // ── 스카이라인 : 무너진 고층 구조물 ──────────────────────────────
        var dark = Mat(new Color(0.055f, 0.042f, 0.042f), 0.95f);
        var dark2 = Mat(new Color(0.072f, 0.054f, 0.05f), 0.95f);
        for (int i = 0; i < 24; i++)
        {
            float ang = Mathf.DegToRad(rng.RandfRange(-96f, 96f));
            float dist = rng.RandfRange(48f, 118f);
            var at = new Vector3(Mathf.Sin(ang) * dist, 0f, -Mathf.Cos(ang) * dist);
            float w = rng.RandfRange(5f, 15f);
            float h = rng.RandfRange(11f, 46f);
            var t = new Node3D { Position = at };
            t.RotationDegrees = new Vector3(0f, rng.RandfRange(0f, 90f), rng.RandfRange(-9f, 9f));
            _pSurface.AddChild(t);
            Box(t, new Vector3(w, h, w * rng.RandfRange(0.7f, 1.3f)), new Vector3(0f, h * 0.5f, 0f),
                i % 2 == 0 ? dark : dark2, "Tower");
            // 윗부분이 부러져 비스듬히 얹혀 있다.
            if (rng.Randf() < 0.55f)
            {
                var top = Box(t, new Vector3(w * 0.8f, rng.RandfRange(2f, 6f), w * 0.7f),
                    new Vector3(rng.RandfRange(-1.5f, 1.5f), h + 1.2f, 0f), dark, "Broken");
                top.RotationDegrees = new Vector3(rng.RandfRange(-26f, 26f), 0f, rng.RandfRange(-30f, 30f));
            }
        }

        // 와이드 샷 도중 실제로 무너지는 한 동 — 밑동을 축으로 쓰러진다.
        _collapseTower = new Node3D { Position = new Vector3(34f, 0f, -62f) };
        _collapseTower.RotationDegrees = new Vector3(0f, 14f, -7f);
        _pSurface.AddChild(_collapseTower);
        Box(_collapseTower, new Vector3(9f, 38f, 8f), new Vector3(0f, 19f, 0f), dark2, "Collapsing");

        // ── 가까운 잔해 ──────────────────────────────────────────────────
        var slab = Mat(new Color(0.13f, 0.105f, 0.095f), 0.95f);
        var slab2 = Mat(new Color(0.10f, 0.08f, 0.072f), 0.98f);
        for (int i = 0; i < 54; i++)
        {
            float ang = Mathf.DegToRad(rng.RandfRange(-130f, 130f));
            float dist = rng.RandfRange(4f, 38f);
            var at = new Vector3(Mathf.Sin(ang) * dist, 0f, -Mathf.Cos(ang) * dist);
            var sz = new Vector3(rng.RandfRange(0.6f, 3.4f), rng.RandfRange(0.25f, 1.5f),
                                 rng.RandfRange(0.6f, 3.0f));
            var b = Box(_pSurface, sz, at with { Y = sz.Y * 0.4f }, i % 3 == 0 ? slab2 : slab, "Rubble");
            b.RotationDegrees = new Vector3(rng.RandfRange(-22f, 22f), rng.RandfRange(0f, 180f),
                                            rng.RandfRange(-22f, 22f));
        }

        // 휘어진 철골 — 폐허의 실루엣을 만드는 건 결국 이 선들이다.
        var steel = Mat(new Color(0.09f, 0.072f, 0.07f), 0.6f, 0.75f);
        for (int i = 0; i < 20; i++)
        {
            float ang = Mathf.DegToRad(rng.RandfRange(-130f, 130f));
            float dist = rng.RandfRange(5f, 34f);
            var at = new Vector3(Mathf.Sin(ang) * dist, 0f, -Mathf.Cos(ang) * dist);
            var g = new Node3D { Position = at };
            g.RotationDegrees = new Vector3(rng.RandfRange(-70f, -20f), rng.RandfRange(0f, 180f),
                                            rng.RandfRange(-25f, 25f));
            _pSurface.AddChild(g);
            float len = rng.RandfRange(3.5f, 11f);
            Box(g, new Vector3(0.16f, 0.16f, len), new Vector3(0f, 0f, len * 0.5f), steel, "Girder");
        }

        // 반쯤 쓰러진 간판 — 사람이 살던 곳이라는 표시.
        var signPost = new Node3D { Position = new Vector3(-7.5f, 0f, -11f) };
        signPost.RotationDegrees = new Vector3(-34f, 28f, 6f);
        _pSurface.AddChild(signPost);
        Box(signPost, new Vector3(0.22f, 7f, 0.22f), new Vector3(0f, 3.5f, 0f), steel, "Post");
        Box(signPost, new Vector3(4.2f, 1.4f, 0.12f), new Vector3(1.6f, 6.4f, 0f),
            Mat(new Color(0.17f, 0.13f, 0.12f), 0.9f), "Sign");

        // ── 불 ───────────────────────────────────────────────────────────
        var fireMat = Glow(new Color(1f, 0.44f, 0.12f), 3.2f);
        var firePts = new[]
        {
            new Vector3(12.5f, 0f, -17f), new Vector3(-15f, 0f, -23f), new Vector3(5f, 0f, -34f),
            new Vector3(-26f, 0f, -14f), new Vector3(23f, 0f, -29f), new Vector3(-4f, 0f, -45f),
        };
        for (int i = 0; i < firePts.Length; i++)
        {
            var at = firePts[i];
            Box(_pSurface, new Vector3(1.1f, 0.7f, 1.1f), at with { Y = 0.35f }, fireMat, "Fire");
            var l = new OmniLight3D
            {
                Position = at with { Y = 1.3f },
                LightColor = new Color(1f, 0.46f, 0.17f),
                LightEnergy = 3.4f, OmniRange = 17f, ShadowEnabled = false,
            };
            _pSurface.AddChild(l);
            _fireLights.Add((l, 3.4f, i * 0.37f));
            // 불마다 연기 기둥이 올라간다 — 가늘고 길게, 위로.
            var sm = Ash(_pSurface, at with { Y = 2.4f }, new Vector3(0.7f, 1.6f, 0.7f),
                new Vector3(0.18f, 1f, 0.05f), 2.9f, 22, 0.17f,
                new Color(0.24f, 0.18f, 0.16f), 7.0);
            sm.Emitting = true;
        }

        // ── 공기 : 날리는 재 ─────────────────────────────────────────────
        // 화면 전체를 덮어야 "공기가 탁하다" 가 된다. 카메라 앞을 가로질러 흐른다.
        // 잘고 많아야 '재' 가 된다. 크고 적으면 그냥 회색 덩어리다.
        Ash(_pSurface, new Vector3(0f, 7f, -16f), new Vector3(38f, 10f, 26f),
            new Vector3(1f, 0.1f, 0.25f), 0.21f, 520, 0.13f,
            new Color(0.38f, 0.26f, 0.22f), 9.0).Emitting = true;

        // 더 멀리, 더 크고 느린 연무 한 겹 — 깊이를 만든다.
        Ash(_pSurface, new Vector3(0f, 12f, -56f), new Vector3(70f, 14f, 34f),
            new Vector3(1f, 0.16f, 0f), 7.5f, 60, 0.065f,
            new Color(0.36f, 0.16f, 0.12f), 14.0).Emitting = true;

        // 아직도 떨어지는 잔해 조각.
        for (int i = 0; i < 16; i++)
        {
            var bit = new Node3D
            {
                Position = new Vector3(rng.RandfRange(-26f, 26f), rng.RandfRange(2f, 15f),
                                       rng.RandfRange(-42f, -6f)),
            };
            _pSurface.AddChild(bit);
            Box(bit, new Vector3(rng.RandfRange(0.1f, 0.35f), rng.RandfRange(0.1f, 0.3f),
                                 rng.RandfRange(0.1f, 0.35f)), Vector3.Zero, slab, "Falling");
            _emberBits.Add(bit);
        }

        // ── 움직이지 않는 형체 ───────────────────────────────────────────
        // 시신을 보여 주지 않는다. 멀리 선 채 움직이지 않는 실루엣 몇으로 충분하다.
        var figure = Mat(new Color(0.038f, 0.03f, 0.03f), 1f);
        var standing = new[]
        {
            (new Vector3(9f, 0f, -26f), -6f), (new Vector3(-11.5f, 0f, -31f), 4f),
            (new Vector3(17f, 0f, -39f), 0f),
        };
        foreach (var (at, tilt) in standing)
        {
            var f = new Node3D { Position = at };
            f.RotationDegrees = new Vector3(0f, 25f, tilt);
            _pSurface.AddChild(f);
            Box(f, new Vector3(0.46f, 1.25f, 0.3f), new Vector3(0f, 0.82f, 0f), figure, "Figure");
            Cyl(f, 0.17f, 0.34f, new Vector3(0f, 1.6f, 0f), figure, "FigureHead");
        }
        // 바닥에 쓰러진 흔적 하나 — 형태만 남긴다.
        var fallen = Box(_pSurface, new Vector3(1.5f, 0.26f, 0.48f), new Vector3(-3.4f, 0.13f, -8.5f),
            figure, "Fallen");
        fallen.RotationDegrees = new Vector3(0f, 61f, 0f);
    }
}
