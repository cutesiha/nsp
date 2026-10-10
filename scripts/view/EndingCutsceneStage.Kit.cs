using System.Collections.Generic;
using Godot;

namespace NSP.View;

// ── 표면 · 소품 키트 ────────────────────────────────────────────────────
//
// 프롤로그 세트는 전부 코드로 짓는다. 그냥 두면 같은 회색을 바른 상자 몇 개 —
// "엔진 기본 머티리얼 테스트맵" 이 된다. 그래서 여기에 두 가지를 모아 둔다.
//
//   · 재질 언어 : 구역마다 표면이 달라야 한다. 도장 콘크리트 / 금속 패널 / 에폭시 바닥 /
//                 생활 바닥 / 고무 / 플라스틱 케이스 — 밝기 · 거칠기 · 금속성이 전부 다르다.
//   · 조립 부품 : 모서리를 깎은 상자, 볼트 줄, 배관, 늘어진 케이블, 덕트, 책상, CRT 단말,
//                 서류철… 직육면체 하나로 끝내지 않고 프레임 · 이음매 · 두께를 같이 붙인다.
//
// 기준은 '폐허' 가 아니라 **운영 중인 시설의 사용감**이다. 더럽히는 게 아니라
// 표면마다 조금씩 다르게 만드는 것이 목적이다.
public partial class EndingCutsceneStage
{
    // ── 재질 ────────────────────────────────────────────────────────────
    //
    // 전부 한 번만 만들어 돌려 쓴다. 색만 다른 머티리얼을 매번 새로 만들면
    // 드로우 콜이 아니라 상태 전환이 늘어난다.

    private static StandardMaterial3D _kConcrete, _kConcreteDark, _kConcretePale, _kPanel, _kPanelDark,
        _kSteel, _kSteelDark, _kAlu, _kEpoxy, _kEpoxyWorn, _kLiving, _kRubber, _kPlastic, _kPlasticDark,
        _kPaper, _kWood, _kWarn, _kWarnDark, _kLabel, _kCable, _kBrass, _kGlassPane, _kDirt, _kFoam;

    // 도장 콘크리트 — 벽의 기본. 거칠고 빛을 거의 안 받는다.
    private static StandardMaterial3D KConcrete => _kConcrete ??= Mat(new Color(0.300f, 0.300f, 0.308f), 0.94f);
    private static StandardMaterial3D KConcreteDark => _kConcreteDark ??= Mat(new Color(0.205f, 0.207f, 0.218f), 0.96f);
    private static StandardMaterial3D KConcretePale => _kConcretePale ??= Mat(new Color(0.262f, 0.260f, 0.252f), 0.90f);

    // 금속 패널 — 벽 상단 · 설비 외장. 약하게 반사한다.
    private static StandardMaterial3D KPanel => _kPanel ??= Mat(new Color(0.268f, 0.282f, 0.305f), 0.46f, 0.72f);
    private static StandardMaterial3D KPanelDark => _kPanelDark ??= Mat(new Color(0.158f, 0.166f, 0.182f), 0.54f, 0.65f);

    // 구조 강재 · 프레임.
    private static StandardMaterial3D KSteel => _kSteel ??= Mat(new Color(0.330f, 0.342f, 0.368f), 0.34f, 0.90f);
    private static StandardMaterial3D KSteelDark => _kSteelDark ??= Mat(new Color(0.142f, 0.148f, 0.162f), 0.42f, 0.80f);
    private static StandardMaterial3D KAlu => _kAlu ??= Mat(new Color(0.368f, 0.378f, 0.398f), 0.28f, 0.95f);

    // 바닥 — 구역마다 다르다. 연구/설비는 에폭시(약한 반사), 생활 구역은 무광.
    private static StandardMaterial3D KEpoxy => _kEpoxy ??= Mat(new Color(0.258f, 0.272f, 0.292f), 0.38f, 0.10f);
    private static StandardMaterial3D KEpoxyWorn => _kEpoxyWorn ??= Mat(new Color(0.300f, 0.306f, 0.312f), 0.68f, 0.04f);
    private static StandardMaterial3D KLiving => _kLiving ??= Mat(new Color(0.272f, 0.250f, 0.222f), 0.88f);
    private static StandardMaterial3D KRubber => _kRubber ??= Mat(new Color(0.082f, 0.085f, 0.090f), 0.98f);

    // 장비 외장 · 플라스틱.
    private static StandardMaterial3D KPlastic => _kPlastic ??= Mat(new Color(0.212f, 0.218f, 0.228f), 0.62f, 0.05f);
    private static StandardMaterial3D KPlasticDark => _kPlasticDark ??= Mat(new Color(0.088f, 0.092f, 0.102f), 0.55f, 0.08f);
    private static StandardMaterial3D KFoam => _kFoam ??= Mat(new Color(0.330f, 0.318f, 0.292f), 0.99f);

    private static StandardMaterial3D KPaper => _kPaper ??= Mat(new Color(0.520f, 0.512f, 0.478f), 0.96f);
    private static StandardMaterial3D KWood => _kWood ??= Mat(new Color(0.238f, 0.178f, 0.122f), 0.70f);
    private static StandardMaterial3D KCable => _kCable ??= Mat(new Color(0.055f, 0.055f, 0.062f), 0.72f);
    private static StandardMaterial3D KBrass => _kBrass ??= Mat(new Color(0.300f, 0.238f, 0.122f), 0.44f, 0.85f);
    private static StandardMaterial3D KDirt => _kDirt ??= Mat(new Color(0.140f, 0.128f, 0.112f), 0.99f);

    // 경고 띠 — 노랑/검정.
    private static StandardMaterial3D KWarn => _kWarn ??= Mat(new Color(0.620f, 0.480f, 0.070f), 0.76f);
    private static StandardMaterial3D KWarnDark => _kWarnDark ??= Mat(new Color(0.070f, 0.068f, 0.062f), 0.86f);
    private static StandardMaterial3D KLabel => _kLabel ??= Mat(new Color(0.470f, 0.476f, 0.466f), 0.82f);

    private static StandardMaterial3D KGlassPane => _kGlassPane ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.60f, 0.74f, 0.82f, 0.17f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 0.08f, Metallic = 0.25f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    // ── 조립 부품 ────────────────────────────────────────────────────────

    // 모서리를 깎은 상자. 직육면체 하나는 아무리 조명을 줘도 '기본 도형' 으로 읽히는데,
    // 열십자로 겹친 상자 셋이면 12개 모서리가 전부 깎여 실루엣이 달라진다.
    // 히어로 소품에만 쓴다 — 배경 벽까지 이걸로 깔면 노드가 세 배가 된다.
    private static Node3D Chamfer(Node3D parent, Vector3 size, Vector3 pos, Material mat,
        float t = 0.035f, string name = "Chamfer")
    {
        var n = new Node3D { Name = name, Position = pos };
        parent.AddChild(n);
        float t2 = t * 2f;
        Box(n, new Vector3(Mathf.Max(0.01f, size.X - t2), size.Y, Mathf.Max(0.01f, size.Z - t2)), Vector3.Zero, mat, "cx");
        Box(n, new Vector3(size.X, Mathf.Max(0.01f, size.Y - t2), Mathf.Max(0.01f, size.Z - t2)), Vector3.Zero, mat, "cy");
        Box(n, new Vector3(Mathf.Max(0.01f, size.X - t2), Mathf.Max(0.01f, size.Y - t2), size.Z), Vector3.Zero, mat, "cz");
        return n;
    }

    // 두 점을 잇는 원통(배관 · 케이블 · 지지대). 축 정렬을 손으로 안 하게 해 준다.
    private static MeshInstance3D Pipe(Node3D parent, Vector3 a, Vector3 b, float radius, Material mat,
        string name = "Pipe", int seg = 8)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.0005f) len = 0.0005f;
        var m = new MeshInstance3D
        {
            Name = name,
            Mesh = SharedCyl(radius, len, seg),
            Position = (a + b) * 0.5f,
            MaterialOverride = mat,
        };
        parent.AddChild(m);
        Vector3 up = d / len;
        float dot = up.Dot(Vector3.Up);
        if (dot < -0.9999f) m.RotationDegrees = new Vector3(180f, 0f, 0f);
        else if (dot < 0.9999f)
        {
            Vector3 axis = Vector3.Up.Cross(up).Normalized();
            m.Basis = new Basis(axis, Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)));
        }
        return m;
    }

    // 볼트 줄 — 금속 패널의 이음매를 '조립된 것' 으로 보이게 하는 가장 싼 장치.
    private static void BoltRow(Node3D parent, Vector3 from, Vector3 to, int count, float r, Material mat)
    {
        if (count < 1) return;
        for (int i = 0; i < count; i++)
        {
            float k = count == 1 ? 0.5f : i / (float)(count - 1);
            var m = new MeshInstance3D
            {
                Name = "Bolt",
                Mesh = SharedCyl(r, r * 1.1f, 6, r * 1.15f),
                Position = from.Lerp(to, k),
                MaterialOverride = mat,
            };
            parent.AddChild(m);
            // 머리를 벽면 쪽으로 눕힌다 — 벽에 박힌 볼트는 축이 벽 법선 방향이다.
            Vector3 d = (to - from).Normalized();
            if (Mathf.Abs(d.Y) < 0.5f) m.RotationDegrees = new Vector3(90f, 0f, 0f);
            if (Mathf.Abs(d.Z) < 0.1f && Mathf.Abs(d.Y) < 0.5f) m.RotationDegrees = new Vector3(0f, 0f, 90f);
        }
    }

    // 늘어진 케이블 — 직선으로 그으면 봉이 된다. 중간이 처져야 케이블로 보인다.
    private static void CableSag(Node3D parent, Vector3 a, Vector3 b, float sag, float r, Material mat, int seg = 5)
    {
        Vector3 prev = a;
        for (int i = 1; i <= seg; i++)
        {
            float k = i / (float)seg;
            Vector3 p = a.Lerp(b, k);
            p.Y -= Mathf.Sin(k * Mathf.Pi) * sag;
            Pipe(parent, prev, p, r, mat, "Cable", 6);
            prev = p;
        }
    }

    // 사각 덕트 — 몸통 + 일정 간격 플랜지. 천장이 비면 공간이 가벼워진다.
    private static Node3D Duct(Node3D parent, Vector3 a, Vector3 b, float w, float h, Material mat,
        Material flange = null, float every = 2.4f)
    {
        var n = new Node3D { Name = "Duct" };
        parent.AddChild(n);
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.01f) return n;
        var body = Box(n, new Vector3(w, h, len), (a + b) * 0.5f, mat, "DuctBody");
        // 길이 방향을 z 로 잡고 만들었으므로 방향만 돌려 준다.
        Vector3 fwd = d / len;
        if (Mathf.Abs(fwd.Z) < 0.9999f)
            body.RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(fwd.X, fwd.Z)), 0f);
        int n2 = Mathf.Max(1, Mathf.RoundToInt(len / every));
        for (int i = 0; i <= n2; i++)
        {
            var f = Box(n, new Vector3(w + 0.07f, h + 0.07f, 0.055f), a.Lerp(b, i / (float)n2),
                flange ?? KSteelDark, "Flange");
            f.RotationDegrees = body.RotationDegrees;
        }
        return n;
    }

    // 케이블 트레이 — 사다리꼴 받침에 케이블 다발이 올라간다.
    private static void CableTray(Node3D parent, Vector3 a, Vector3 b, float w, Material frame, Material cable)
    {
        Vector3 d = b - a;
        float len = d.Length();
        if (len < 0.01f) return;
        Vector3 side = d.Cross(Vector3.Up).Normalized() * (w * 0.5f);
        Pipe(parent, a + side, b + side, 0.028f, frame, "TrayRail", 6);
        Pipe(parent, a - side, b - side, 0.028f, frame, "TrayRail", 6);
        int rungs = Mathf.Max(2, Mathf.RoundToInt(len / 0.55f));
        for (int i = 0; i <= rungs; i++)
        {
            Vector3 p = a.Lerp(b, i / (float)rungs);
            Pipe(parent, p + side, p - side, 0.016f, frame, "Rung", 4);
        }
        // 다발 — 굵기가 다른 선 셋.
        Pipe(parent, a + Vector3.Up * 0.03f, b + Vector3.Up * 0.03f, 0.035f, cable, "Bundle", 6);
        Pipe(parent, a + side * 0.4f + Vector3.Up * 0.05f, b + side * 0.4f + Vector3.Up * 0.05f, 0.022f, cable, "Bundle", 6);
        Pipe(parent, a - side * 0.45f + Vector3.Up * 0.02f, b - side * 0.45f + Vector3.Up * 0.02f, 0.017f, cable, "Bundle", 6);
    }

    // 노랑/검정 경고 띠. 띠 하나가 있는 것만으로 "관리되는 시설" 로 읽힌다.
    private static void WarnStripe(Node3D parent, Vector3 at, float length, float height, float depth,
        Vector3 rotDeg, int bands = 8)
    {
        var n = new Node3D { Name = "WarnStripe", Position = at, RotationDegrees = rotDeg };
        parent.AddChild(n);
        for (int i = 0; i < bands; i++)
        {
            float x = -length * 0.5f + length * (i + 0.5f) / bands;
            var b = Box(n, new Vector3(length / bands * 1.02f, height, depth), new Vector3(x, 0f, 0f),
                i % 2 == 0 ? KWarn : KWarnDark, "Band");
            b.RotationDegrees = new Vector3(0f, 0f, 14f);   // 빗금
        }
    }

    // 벽에 붙은 얇은 라벨 · 표지판.
    private static MeshInstance3D Label(Node3D parent, Vector3 at, Vector2 size, Vector3 rotDeg, Material mat = null)
    {
        var m = Box(parent, new Vector3(size.X, size.Y, 0.012f), at, mat ?? KLabel, "Label");
        m.RotationDegrees = rotDeg;
        return m;
    }

    // 통풍구 — 얇은 날 여러 장. 장비 케이스에 이것만 붙여도 '기계' 가 된다.
    private static void Louver(Node3D parent, Vector3 at, float w, float h, Vector3 rotDeg, int slats = 5)
    {
        var n = new Node3D { Name = "Louver", Position = at, RotationDegrees = rotDeg };
        parent.AddChild(n);
        Box(n, new Vector3(w, h, 0.012f), Vector3.Zero, KPlasticDark, "VentBack");
        for (int i = 0; i < slats; i++)
        {
            float y = -h * 0.5f + h * (i + 0.5f) / slats;
            var s = Box(n, new Vector3(w * 0.92f, h / slats * 0.52f, 0.022f), new Vector3(0f, y, 0.012f),
                KSteelDark, "Slat");
            s.RotationDegrees = new Vector3(-22f, 0f, 0f);
        }
    }

    // ── 가구 · 장비 ──────────────────────────────────────────────────────

    // 책상 — 상판 하나짜리 큐브가 아니라 두께 · 앞치마 · 금속 다리 · 가림판까지.
    private static Node3D DeskUnit(Node3D parent, Vector3 at, float yaw, float w, float d, float h,
        Material top, Material frame, bool drawers = true)
    {
        var n = new Node3D { Name = "Desk", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        // 상판 : 모서리를 깎고, 그 아래 얇은 테두리를 한 겹 더 둔다.
        Chamfer(n, new Vector3(w, 0.055f, d), new Vector3(0f, h, 0f), top, 0.014f, "Top");
        Box(n, new Vector3(w - 0.06f, 0.035f, d - 0.06f), new Vector3(0f, h - 0.045f, 0f), frame, "TopUnder");
        // 앞치마(상판 아래 가로대).
        Box(n, new Vector3(w - 0.12f, 0.10f, 0.045f), new Vector3(0f, h - 0.11f, -d * 0.5f + 0.06f), frame, "Apron");
        // 다리 — 네 모서리의 각관.
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                Box(n, new Vector3(0.055f, h - 0.06f, 0.055f),
                    new Vector3(sx * (w * 0.5f - 0.07f), (h - 0.06f) * 0.5f, sz * (d * 0.5f - 0.07f)), frame, "Leg");
                // 받침 — 바닥에 닿는 자리가 조금 더 굵다.
                Box(n, new Vector3(0.075f, 0.022f, 0.075f),
                    new Vector3(sx * (w * 0.5f - 0.07f), 0.011f, sz * (d * 0.5f - 0.07f)), KRubber, "Foot");
            }
        // 가로 보강대.
        Box(n, new Vector3(w - 0.2f, 0.035f, 0.035f), new Vector3(0f, 0.16f, -d * 0.5f + 0.07f), frame, "Brace");
        // 앞 가림판.
        Box(n, new Vector3(w - 0.16f, h - 0.20f, 0.028f), new Vector3(0f, (h - 0.20f) * 0.5f + 0.10f,
            d * 0.5f - 0.05f), frame, "Modesty");
        if (drawers)
        {
            var box = Box(n, new Vector3(w * 0.30f, h - 0.14f, d - 0.16f),
                new Vector3(w * 0.5f - w * 0.17f, (h - 0.14f) * 0.5f, 0f), frame, "Drawers");
            for (int i = 0; i < 3; i++)
            {
                Box(box, new Vector3(w * 0.29f, 0.012f, 0.012f),
                    new Vector3(0f, -0.22f + i * 0.22f, (d - 0.16f) * 0.5f), KSteelDark, "DrawerSeam");
                Box(box, new Vector3(w * 0.09f, 0.022f, 0.030f),
                    new Vector3(0f, -0.14f + i * 0.22f, (d - 0.16f) * 0.5f + 0.01f), KAlu, "Handle");
            }
        }
        return n;
    }

    // 산업용 터미널 / CRT. 케이스 · 베젤 · 통풍구 · 버튼 · 받침까지 붙는다.
    // energy 가 0 이면 꺼진 화면(검은 유리)으로 보인다 — 꺼진 단말도 소품이다.
    private static Node3D Terminal(Node3D parent, Vector3 at, float yaw, float scale, Color tint, float energy,
        List<StandardMaterial3D> screens = null)
    {
        var n = new Node3D { Name = "Terminal", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        float w = 0.52f * scale, h = 0.42f * scale, dp = 0.44f * scale;
        // 받침 — CRT 는 받침 위에 조금 기울어 앉는다.
        Box(n, new Vector3(w * 0.78f, 0.045f * scale, dp * 0.72f), new Vector3(0f, 0.022f * scale, 0f), KPlasticDark, "Base");
        var body = new Node3D { Position = new Vector3(0f, 0.045f * scale + h * 0.5f, 0f) };
        body.RotationDegrees = new Vector3(-5f, 0f, 0f);
        n.AddChild(body);
        Chamfer(body, new Vector3(w, h, dp), Vector3.Zero, KPlastic, 0.018f * scale, "Case");
        // 뒤쪽이 좁아지는 브라운관 몸통.
        Box(body, new Vector3(w * 0.62f, h * 0.60f, dp * 0.42f), new Vector3(0f, -0.02f * scale, -dp * 0.62f),
            KPlasticDark, "Tube");
        // 베젤 + 화면.
        Box(body, new Vector3(w * 0.90f, h * 0.76f, 0.02f * scale), new Vector3(0f, 0.02f * scale, dp * 0.5f),
            KPlasticDark, "Bezel");
        var sm = Glow(tint, energy);
        sm.AlbedoColor = energy > 0.01f ? tint : new Color(0.035f, 0.040f, 0.048f);
        Box(body, new Vector3(w * 0.78f, h * 0.60f, 0.012f), new Vector3(0f, 0.02f * scale, dp * 0.5f + 0.012f),
            sm, "Screen");
        screens?.Add(sm);
        // 통풍구 · 버튼 · 라벨.
        Louver(body, new Vector3(0f, h * 0.46f, -dp * 0.1f), w * 0.5f, h * 0.16f, new Vector3(90f, 0f, 0f), 4);
        for (int i = 0; i < 3; i++)
            Box(body, new Vector3(0.022f * scale, 0.022f * scale, 0.016f),
                new Vector3(-w * 0.3f + i * 0.055f * scale, -h * 0.40f, dp * 0.5f + 0.008f),
                i == 0 ? KBrass : KAlu, "Button");
        Box(body, new Vector3(w * 0.22f, 0.018f * scale, 0.014f),
            new Vector3(w * 0.28f, -h * 0.40f, dp * 0.5f + 0.008f), KLabel, "Plate");
        return n;
    }

    // 서류철이 꽂힌 한 줄 — 두께 · 색 · 기울기가 다 달라야 '쓰이는 선반' 이 된다.
    private static void Binders(Node3D parent, Vector3 at, float yaw, int count, ulong seed)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var n = new Node3D { Name = "Binders", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        float x = 0f;
        for (int i = 0; i < count; i++)
        {
            float w = rng.RandfRange(0.035f, 0.075f);
            float h = rng.RandfRange(0.25f, 0.33f);
            var mat = Mat(new Color(rng.RandfRange(0.07f, 0.20f), rng.RandfRange(0.065f, 0.15f),
                rng.RandfRange(0.06f, 0.14f)), 0.90f);
            var b = Box(n, new Vector3(w, h, 0.24f), new Vector3(x + w * 0.5f, h * 0.5f, 0f), mat, "Binder");
            // 마지막 몇 권은 기울어 있다.
            if (i >= count - 2) b.RotationDegrees = new Vector3(0f, 0f, rng.RandfRange(5f, 13f));
            // 등쪽 라벨.
            Box(b, new Vector3(w * 0.8f, 0.055f, 0.004f), new Vector3(0f, h * 0.26f, 0.122f), KPaper, "Spine");
            x += w + 0.004f;
        }
    }

    // 쌓인 서류 — 종이 몇 장이 조금씩 어긋나 있다.
    private static Node3D PaperStack(Node3D parent, Vector3 at, float yaw, int sheets, ulong seed, float w = 0.21f)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var n = new Node3D { Name = "Papers", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        for (int i = 0; i < sheets; i++)
        {
            var s = Box(n, new Vector3(w, 0.004f, w * 1.38f),
                new Vector3(rng.RandfRange(-0.012f, 0.012f), i * 0.0045f, rng.RandfRange(-0.012f, 0.012f)),
                KPaper, "Sheet");
            s.RotationDegrees = new Vector3(0f, rng.RandfRange(-4f, 4f), 0f);
        }
        return n;
    }

    // 파일 보관 상자 — 뚜껑 · 손잡이 구멍 · 라벨.
    private static Node3D FileBox(Node3D parent, Vector3 at, float yaw, Material mat = null)
    {
        var n = new Node3D { Name = "FileBox", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        var m = mat ?? KFoam;
        Chamfer(n, new Vector3(0.40f, 0.27f, 0.31f), new Vector3(0f, 0.135f, 0f), m, 0.012f, "Body");
        Box(n, new Vector3(0.42f, 0.035f, 0.33f), new Vector3(0f, 0.288f, 0f), m, "Lid");
        Box(n, new Vector3(0.13f, 0.035f, 0.014f), new Vector3(0f, 0.19f, 0.157f), KPlasticDark, "Grip");
        Box(n, new Vector3(0.17f, 0.075f, 0.006f), new Vector3(0f, 0.09f, 0.158f), KPaper, "Tag");
        return n;
    }

    // 클립보드 — 판 + 집게 + 끼운 종이. 책상 · 벽 어디에나 하나씩.
    private static Node3D Clipboard(Node3D parent, Vector3 at, Vector3 rotDeg)
    {
        var n = new Node3D { Name = "Clipboard", Position = at, RotationDegrees = rotDeg };
        parent.AddChild(n);
        Box(n, new Vector3(0.21f, 0.010f, 0.30f), Vector3.Zero, KWood, "Board");
        Box(n, new Vector3(0.195f, 0.005f, 0.275f), new Vector3(0f, 0.0075f, -0.008f), KPaper, "Sheet");
        Box(n, new Vector3(0.085f, 0.016f, 0.045f), new Vector3(0f, 0.013f, 0.125f), KAlu, "Clip");
        return n;
    }

    // 펜꽂이 — 통 + 길이가 다른 펜 몇 자루.
    private static void PenCup(Node3D parent, Vector3 at, ulong seed)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var n = new Node3D { Name = "PenCup", Position = at };
        parent.AddChild(n);
        Cyl(n, 0.042f, 0.095f, new Vector3(0f, 0.047f, 0f), KPlasticDark, "Cup");
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4f;
            var p = Pipe(n, new Vector3(Mathf.Cos(a) * 0.016f, 0.05f, Mathf.Sin(a) * 0.016f),
                new Vector3(Mathf.Cos(a) * 0.045f, rng.RandfRange(0.17f, 0.23f), Mathf.Sin(a) * 0.045f),
                0.005f, i % 2 == 0 ? KSteelDark : KBrass, "Pen", 5);
            p.Name = "Pen";
        }
    }

    // 내선 전화기 / 인터폰 — 본체 · 수화기 · 버튼판 · 선.
    private static Node3D Intercom(Node3D parent, Vector3 at, float yaw)
    {
        var n = new Node3D { Name = "Intercom", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        Chamfer(n, new Vector3(0.21f, 0.065f, 0.26f), new Vector3(0f, 0.032f, 0f), KPlasticDark, 0.01f, "Body");
        // 수화기.
        var h = new Node3D { Position = new Vector3(0f, 0.085f, 0.01f) };
        n.AddChild(h);
        Box(h, new Vector3(0.055f, 0.042f, 0.215f), Vector3.Zero, KPlastic, "Handset");
        Box(h, new Vector3(0.070f, 0.030f, 0.055f), new Vector3(0f, -0.022f, -0.085f), KPlastic, "Ear");
        Box(h, new Vector3(0.070f, 0.030f, 0.055f), new Vector3(0f, -0.022f, 0.085f), KPlastic, "Mic");
        // 버튼판.
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                Box(n, new Vector3(0.021f, 0.008f, 0.021f),
                    new Vector3(-0.055f + c * 0.027f, 0.067f, -0.085f + r * 0.028f), KAlu, "Key");
        CableSag(n, new Vector3(0.09f, 0.03f, -0.12f), new Vector3(0.30f, 0.0f, -0.28f), 0.05f, 0.007f, KCable, 4);
        return n;
    }

    // 벽 제어 패널 — 금속 상자 + 계기 + 스위치 줄 + 작은 표시등.
    // 표시등 재질을 돌려주므로 장면 쪽에서 상태(정상/경보)를 바꿀 수 있다.
    private static Node3D WallPanel(Node3D parent, Vector3 at, Vector3 rotDeg, float w, float h,
        Color lamp, float energy, List<StandardMaterial3D> lamps = null, ulong seed = 7)
    {
        var rng = new RandomNumberGenerator { Seed = seed };
        var n = new Node3D { Name = "WallPanel", Position = at, RotationDegrees = rotDeg };
        parent.AddChild(n);
        Chamfer(n, new Vector3(w, h, 0.11f), Vector3.Zero, KPanel, 0.016f, "Case");
        Box(n, new Vector3(w - 0.07f, h - 0.07f, 0.02f), new Vector3(0f, 0f, 0.058f), KPanelDark, "Face");
        BoltRow(n, new Vector3(-w * 0.5f + 0.05f, h * 0.5f - 0.05f, 0.062f),
            new Vector3(w * 0.5f - 0.05f, h * 0.5f - 0.05f, 0.062f), 4, 0.011f, KAlu);
        BoltRow(n, new Vector3(-w * 0.5f + 0.05f, -h * 0.5f + 0.05f, 0.062f),
            new Vector3(w * 0.5f - 0.05f, -h * 0.5f + 0.05f, 0.062f), 4, 0.011f, KAlu);
        // 계기 둘.
        foreach (float sx in new[] { -0.26f, 0.02f })
        {
            Cyl(n, w * 0.085f, 0.022f, new Vector3(w * sx, h * 0.20f, 0.070f), KSteelDark, "Gauge")
                .RotationDegrees = new Vector3(90f, 0f, 0f);
            Box(n, new Vector3(0.008f, w * 0.10f, 0.010f), new Vector3(w * sx, h * 0.20f + w * 0.03f, 0.082f),
                KAlu, "Needle").RotationDegrees = new Vector3(0f, 0f, rng.RandfRange(-35f, 35f));
        }
        // 표시등 줄.
        int n2 = Mathf.Max(3, Mathf.RoundToInt(w / 0.09f));
        for (int i = 0; i < n2; i++)
        {
            var c = i % 3 == 0 ? lamp : (i % 3 == 1 ? new Color(0.35f, 1f, 0.55f) : new Color(1f, 0.72f, 0.25f));
            var m = Glow(c, energy * rng.RandfRange(0.6f, 1.2f));
            lamps?.Add(m);
            Box(n, new Vector3(0.024f, 0.024f, 0.014f),
                new Vector3(-w * 0.5f + 0.06f + i * (w - 0.12f) / Mathf.Max(1, n2 - 1), -h * 0.12f, 0.068f),
                m, "Lamp");
        }
        // 스위치 줄.
        for (int i = 0; i < 5; i++)
        {
            var s = Box(n, new Vector3(0.016f, 0.045f, 0.022f),
                new Vector3(-w * 0.34f + i * 0.055f, -h * 0.33f, 0.072f), KAlu, "Switch");
            s.RotationDegrees = new Vector3(rng.Randf() < 0.5f ? 22f : -22f, 0f, 0f);
        }
        Label(n, new Vector3(w * 0.30f, -h * 0.33f, 0.064f), new Vector2(w * 0.28f, 0.055f), Vector3.Zero);
        return n;
    }

    // 기둥 꾸미기 — 단색 박스를 '관리되는 기둥' 으로 바꾼다.
    //   하단 보호대 · 패널 이음매 · 관리 라벨 · 경고 띠.
    private static void DressPillar(Node3D parent, Vector3 at, float w, float h, float yaw = 0f)
    {
        var n = new Node3D { Name = "PillarDress", Position = at, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(n);
        // 하단 보호대 — 지게차 · 대차에 부딪히는 자리.
        Box(n, new Vector3(w + 0.10f, 0.42f, w + 0.10f), new Vector3(0f, 0.21f, 0f), KSteelDark, "Guard");
        Box(n, new Vector3(w + 0.13f, 0.035f, w + 0.13f), new Vector3(0f, 0.425f, 0f), KAlu, "GuardCap");
        // 경고 띠 — 보호대 위로 한 바퀴.
        foreach (var (p, r) in new[]
                 {
                     (new Vector3(0f, 0.55f, w * 0.5f + 0.056f), 0f),
                     (new Vector3(0f, 0.55f, -w * 0.5f - 0.056f), 180f),
                     (new Vector3(w * 0.5f + 0.056f, 0.55f, 0f), 90f),
                     (new Vector3(-w * 0.5f - 0.056f, 0.55f, 0f), -90f),
                 })
            WarnStripe(n, p, w + 0.10f, 0.16f, 0.014f, new Vector3(0f, r, 0f), 6);
        // 패널 이음매 — 높이를 끊는다.
        for (int i = 1; i <= 3; i++)
            Box(n, new Vector3(w + 0.025f, 0.022f, w + 0.025f), new Vector3(0f, 0.42f + i * (h - 0.6f) / 4f, 0f),
                KSteelDark, "Band");
        // 관리 라벨 · 작은 표시등.
        Label(n, new Vector3(0f, 1.62f, w * 0.5f + 0.012f), new Vector2(w * 0.55f, 0.17f), Vector3.Zero);
        Box(n, new Vector3(0.05f, 0.05f, 0.014f), new Vector3(w * 0.28f, 1.92f, w * 0.5f + 0.012f),
            Glow(new Color(0.35f, 1f, 0.55f), 1.6f), "Pilot");
    }

    // 벽을 패널로 끊는다 — 하단 콘크리트 · 중간 몰딩 · 상단 금속 패널 + 볼트.
    // 벽 '앞' 에 얇게 덧대는 방식이라 기존 벽 상자를 건드리지 않는다.
    //   facing : 패널이 바라보는 방향(+Z 면 1, -Z 면 -1 … 축은 axis 로 고른다)
    private static void DressWallPanels(Node3D parent, Vector3 center, float length, float height,
        bool alongX, float facing, float sillY = 1.15f, float seamEvery = 2.0f)
    {
        var n = new Node3D { Name = "WallPanels", Position = center };
        parent.AddChild(n);
        Vector3 Along(float t) => alongX ? new Vector3(t, 0f, 0f) : new Vector3(0f, 0f, t);
        Vector3 Out(float t) => alongX ? new Vector3(0f, 0f, t * facing) : new Vector3(t * facing, 0f, 0f);
        Vector3 Size(float along, float y, float depth) =>
            alongX ? new Vector3(along, y, depth) : new Vector3(depth, y, along);

        // 하단 — 도장 콘크리트(조금 밝다).
        Box(n, Size(length, sillY, 0.035f), Out(0.018f) + new Vector3(0f, sillY * 0.5f, 0f),
            KConcretePale, "Wainscot");
        // 몰딩 — 하단과 상단을 가르는 금속 띠. 이 선 하나가 벽을 '마감된 벽' 으로 만든다.
        Box(n, Size(length, 0.075f, 0.055f), Out(0.028f) + new Vector3(0f, sillY + 0.037f, 0f), KAlu, "Molding");
        // 상단 — 금속 패널.
        float top = height - sillY - 0.075f;
        if (top > 0.1f)
            Box(n, Size(length, top, 0.028f), Out(0.014f) + new Vector3(0f, sillY + 0.075f + top * 0.5f, 0f),
                KPanel, "UpperPanel");

        // 패널 이음매 + 볼트.
        int seams = Mathf.Max(1, Mathf.RoundToInt(length / seamEvery));
        for (int i = 0; i <= seams; i++)
        {
            float t = -length * 0.5f + length * i / seams;
            Box(n, Size(0.03f, top, 0.04f), Along(t) + Out(0.03f) + new Vector3(0f, sillY + 0.075f + top * 0.5f, 0f),
                KSteelDark, "PanelSeam");
            if (top > 0.6f)
                BoltRow(n, Along(t) + Out(0.044f) + new Vector3(0f, sillY + 0.2f, 0f),
                    Along(t) + Out(0.044f) + new Vector3(0f, sillY + top - 0.1f, 0f),
                    Mathf.Clamp(Mathf.RoundToInt(top / 0.7f), 2, 6), 0.013f, KAlu);
        }
    }
}
