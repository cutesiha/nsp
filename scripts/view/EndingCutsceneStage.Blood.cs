using Godot;

namespace NSP.View;

// 처분실의 핏자국 · 때 묻은 콘크리트(2026-10-31 사용자 요청 "주변에 피 튄 흔적도 있고 좀 퀄리티 높게").
//
// 프로젝트에 피 텍스처가 하나도 없어서 **실행할 때 그린다** — 번진 웅덩이 · 튄 자국 · 흘러내린 자국 세 가지.
// 그림은 처음 한 번만 만들고 무대가 몇 번 서든 같은 것을 쓴다. 바닥 · 벽에는 Decal 로 투사한다
// (메시를 깔면 바닥과 겹쳐 깜빡이고, 벽 모서리에서 허공에 뜬다).
public partial class EndingCutsceneStage
{
    private enum BloodKind { Pool, Splat, Drip }

    private const int BloodTexSize = 256;
    private static readonly System.Collections.Generic.Dictionary<(BloodKind, int), ImageTexture> _bloodCache = new();
    private static ImageTexture _wetOrm;

    // 젖은 피 — 거의 검붉은 바탕에 가장자리만 조금 밝다(마른 테두리).
    private static readonly Color BloodDeep = new(0.075f, 0.0f, 0.004f);
    private static readonly Color BloodEdge = new(0.30f, 0.008f, 0.01f);

    // 바닥(위를 보는 면)에 투사한다. yaw 는 바닥 위에서 돌린 각도(도).
    private static void FloorBlood(Node3D parent, Vector3 pos, Vector2 size, float yawDeg, BloodKind kind, int seed)
    {
        var basis = new Basis(Vector3.Up, Mathf.DegToRad(yawDeg));
        AddBloodDecal(parent, new Transform3D(basis, pos), new Vector3(size.X, 0.3f, size.Y), kind, seed);
    }

    // 벽에 투사한다. inward 는 벽에서 방 안쪽으로 향하는 방향 — 그림의 위쪽이 언제나 천장 쪽이라
    // 흘러내린 자국이 아래로 흐른다.
    private static void WallBlood(Node3D parent, Vector3 pos, Vector3 inward, Vector2 size, BloodKind kind, int seed)
    {
        Vector3 y = inward.Normalized();
        Vector3 z = Vector3.Down;
        Vector3 x = y.Cross(z);
        AddBloodDecal(parent, new Transform3D(new Basis(x, y, z), pos), new Vector3(size.X, 0.4f, size.Y), kind, seed);
    }

    private static void AddBloodDecal(Node3D parent, Transform3D at, Vector3 size, BloodKind kind, int seed)
    {
        var d = new Decal
        {
            Name = $"Blood{kind}{seed}",
            Transform = at,
            Size = size,
            TextureAlbedo = BloodTexture(kind, seed),
            TextureOrm = WetOrm(),
            AlbedoMix = 1f,
            UpperFade = 0.15f,
            LowerFade = 0.15f,
        };
        parent.AddChild(d);
    }

    // 젖은 표면 — 거칠기만 낮춘다(붉은 조명이 웅덩이에 번들거리게).
    private static ImageTexture WetOrm()
    {
        if (_wetOrm != null) return _wetOrm;
        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        img.Fill(new Color(1f, 0.18f, 0f, 1f));
        return _wetOrm = ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture BloodTexture(BloodKind kind, int seed)
    {
        if (_bloodCache.TryGetValue((kind, seed), out var hit)) return hit;

        int n = BloodTexSize;
        var rng = new RandomNumberGenerator { Seed = (ulong)(seed * 7919 + (int)kind * 104729 + 17) };
        var noise = new FastNoiseLite
        {
            Seed = seed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex,
            Frequency = 0.035f,
            FractalOctaves = 3,
        };
        var alpha = new float[n * n];

        // ① 몸통 — 가장자리가 울퉁불퉁한 덩어리 하나와 그 주변의 작은 덩어리들.
        float baseR = kind switch { BloodKind.Pool => 0.40f, BloodKind.Drip => 0.22f, _ => 0.26f } * n;
        Vector2 center = kind == BloodKind.Drip ? new Vector2(n * 0.5f, n * 0.26f) : new Vector2(n * 0.5f, n * 0.5f);
        Blob(alpha, n, center, baseR, noise, kind == BloodKind.Pool ? 0.22f : 0.42f);
        int lobes = kind == BloodKind.Pool ? 5 : 7;
        for (int i = 0; i < lobes; i++)
        {
            float ang = rng.RandfRange(0f, Mathf.Tau);
            float dist = baseR * rng.RandfRange(0.45f, 0.95f);
            var c = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
            Blob(alpha, n, c, baseR * rng.RandfRange(0.25f, 0.5f), noise, 0.35f);
        }

        // ② 튄 방울 — 몸통 밖으로 흩어진 점들. 멀수록 작고, 일부는 날아간 방향으로 길쭉하다.
        int drops = kind switch { BloodKind.Pool => 14, BloodKind.Drip => 22, _ => 46 };
        for (int i = 0; i < drops; i++)
        {
            float ang = rng.RandfRange(0f, Mathf.Tau);
            float dist = baseR * rng.RandfRange(1.05f, kind == BloodKind.Splat ? 2.3f : 1.6f);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            var c = center + dir * dist;
            float r = Mathf.Max(1.2f, n * rng.RandfRange(0.006f, 0.022f) * (1.6f - dist / (baseR * 2.3f)));
            float stretch = rng.Randf() < 0.45f ? rng.RandfRange(1.6f, 3.6f) : 1f;
            Drop(alpha, n, c, r, dir, stretch);
        }

        // ③ 흘러내린 자국 — 벽에만. 굵기 · 길이가 제각각이고 끝에 맺힌 방울이 있다.
        if (kind == BloodKind.Drip)
        {
            int streaks = 6 + rng.RandiRange(0, 4);
            for (int i = 0; i < streaks; i++)
            {
                float x = center.X + rng.RandfRange(-baseR * 0.9f, baseR * 0.9f);
                float top = center.Y + rng.RandfRange(-baseR * 0.2f, baseR * 0.4f);
                float len = n * rng.RandfRange(0.25f, 0.70f);
                float w = n * rng.RandfRange(0.008f, 0.020f);
                for (float y = top; y < Mathf.Min(n - 2f, top + len); y += 0.5f)
                {
                    float k = (y - top) / len;
                    float wob = noise.GetNoise2D(x * 3f, y * 0.6f) * 2.2f;
                    Disc(alpha, n, new Vector2(x + wob, y), w * (1f - 0.45f * k), 1f);
                }
                Disc(alpha, n, new Vector2(x, Mathf.Min(n - 4f, top + len)), w * 1.5f, 1f);
            }
        }

        // 색 — 고인 곳(알파가 진한 곳)일수록 검붉다. 얇게 번진 가장자리는 조금 밝고 바랜다.
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            float a = Mathf.Clamp(alpha[y * n + x], 0f, 1f);
            if (a <= 0.003f) { img.SetPixel(x, y, new Color(0f, 0f, 0f, 0f)); continue; }
            float grain = noise.GetNoise2D(x * 2.3f + 400f, y * 2.3f) * 0.5f + 0.5f;
            var col = BloodEdge.Lerp(BloodDeep, Mathf.Clamp(a * 1.4f - 0.25f + grain * 0.12f, 0f, 1f));
            img.SetPixel(x, y, new Color(col.R, col.G, col.B, Mathf.Clamp(a * 1.05f, 0f, 0.98f)));
        }
        img.GenerateMipmaps();
        return _bloodCache[(kind, seed)] = ImageTexture.CreateFromImage(img);
    }

    // 가장자리를 노이즈로 흔든 원. edgeNoise 가 클수록 테두리가 거칠다.
    private static void Blob(float[] alpha, int n, Vector2 c, float r, FastNoiseLite noise, float edgeNoise)
    {
        int x0 = Mathf.Max(0, (int)(c.X - r * 1.5f)), x1 = Mathf.Min(n - 1, (int)(c.X + r * 1.5f));
        int y0 = Mathf.Max(0, (int)(c.Y - r * 1.5f)), y1 = Mathf.Min(n - 1, (int)(c.Y + r * 1.5f));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = new Vector2(x, y).DistanceTo(c);
            float edge = r * (1f + edgeNoise * noise.GetNoise2D(x, y));
            float a = 1f - Mathf.SmoothStep(edge - 2.5f, edge + 1.5f, d);
            int i = y * n + x;
            if (a > alpha[i]) alpha[i] = a;
        }
    }

    private static void Disc(float[] alpha, int n, Vector2 c, float r, float strength)
    {
        int x0 = Mathf.Max(0, (int)(c.X - r - 2f)), x1 = Mathf.Min(n - 1, (int)(c.X + r + 2f));
        int y0 = Mathf.Max(0, (int)(c.Y - r - 2f)), y1 = Mathf.Min(n - 1, (int)(c.Y + r + 2f));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float d = new Vector2(x, y).DistanceTo(c);
            float a = (1f - Mathf.SmoothStep(r - 1f, r + 1f, d)) * strength;
            int i = y * n + x;
            if (a > alpha[i]) alpha[i] = a;
        }
    }

    // 날아간 방향(dir)으로 늘어난 방울 — 늘어난 쪽 끝이 가늘어진다.
    private static void Drop(float[] alpha, int n, Vector2 c, float r, Vector2 dir, float stretch)
    {
        if (stretch <= 1.01f) { Disc(alpha, n, c, r, 1f); return; }
        int steps = Mathf.CeilToInt(r * stretch * 2f);
        for (int s = 0; s <= steps; s++)
        {
            float k = s / (float)Mathf.Max(1, steps);
            Disc(alpha, n, c + dir * (k * r * stretch * 2f), r * (1f - 0.7f * k), 1f);
        }
    }

    // 얼룩진 콘크리트 — 밝기 얼룩(알베도)과 패인 자국(노멀)을 노이즈로 깐다. 삼면 투사라 상자
    // 어느 면에 붙어도 늘어나지 않는다.
    // stain: 얼룩이 가장 진한 곳이 원래 색보다 얼마나 어두운가(처분실 0.58 = 짙게, 다른 장면은 옅게).
    private static StandardMaterial3D GrimeMat(Color albedo, int seed, float uvScale, float bump, float rough = 0.92f,
        float stain = 0.58f, float metal = 0f)
    {
        var tint = new Gradient();
        tint.SetColor(0, new Color(1f - stain, 1f - stain * 1.03f, 1f - stain * 1.03f));
        tint.SetColor(1, new Color(1f, 0.98f, 0.97f));
        var spots = new FastNoiseLite
        {
            Seed = seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 0.012f,
            FractalType = FastNoiseLite.FractalTypeEnum.Fbm, FractalOctaves = 4,
        };
        var pits = new FastNoiseLite
        {
            Seed = seed + 31, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = 0.05f,
            FractalOctaves = 4,
        };
        return new StandardMaterial3D
        {
            AlbedoColor = albedo,
            AlbedoTexture = new NoiseTexture2D { Width = 512, Height = 512, Seamless = true, Noise = spots, ColorRamp = tint },
            NormalEnabled = true,
            NormalScale = 1f,
            NormalTexture = new NoiseTexture2D
            {
                Width = 512, Height = 512, Seamless = true, Noise = pits, AsNormalMap = true, BumpStrength = bump,
            },
            Roughness = rough,
            Metallic = metal,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(uvScale, uvScale, uvScale),
        };
    }
}
