using System.Collections.Generic;
using Godot;

namespace NSP.View;

// 책상 기기의 "백라이트" — 모델 텍스처에서 밝게 칠해진 부분(새겨진 라벨 글자 · 표식)만 골라
// 아주 약하게 자기발광시킨다. 어두운 금속 몸체는 그대로 어둡다.
// 광원을 더 쏘지 않고 앞면 글자를 읽히게 하려는 것. 모델 파일은 건드리지 않고 머티리얼만 복제해 바꾼다.
public static class DeviceBacklight
{
    // 같은 텍스처 · 문턱값이면 마스크를 한 번만 만든다.
    private static readonly Dictionary<(ulong, float), Texture2D> _masks = new();

    // src 를 복제해 발광 마스크를 붙인 머티리얼을 돌려준다. 만들 수 없으면 src 그대로.
    //   threshold : 이 밝기(0~1)를 넘는 텍스처 부분만 빛난다 — 라벨 페인트만 고르는 기준
    //   energy    : 발광 세기(Inspector 에서 조절)
    public static Material Make(Material src, float threshold, Color tint, float energy)
    {
        if (src is not StandardMaterial3D std || energy <= 0f) return src;
        var mask = MaskFor(std.AlbedoTexture, threshold);
        if (mask == null) return src;

        var m = (StandardMaterial3D)std.Duplicate();
        m.EmissionEnabled = true;
        // Multiply = 색 × 마스크(마스크가 검정인 몸체는 발광 0). Add 는 색이 표면 전체에 더해져 통째로 빛났다.
        m.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
        m.EmissionTexture = mask;
        m.Emission = tint;
        m.EmissionEnergyMultiplier = energy;
        return m;
    }

    private static Texture2D MaskFor(Texture2D albedo, float threshold)
    {
        if (albedo == null) return null;
        var key = (albedo.GetRid().Id, threshold);
        if (_masks.TryGetValue(key, out var cached)) return cached;

        var img = albedo.GetImage();
        if (img == null || img.IsEmpty()) return null;
        img = (Image)img.Duplicate();
        if (img.IsCompressed() && img.Decompress() != Error.Ok) return null;
        if (img.HasMipmaps()) img.ClearMipmaps();
        // 마스크는 흐릿해도 된다 — 작게 줄여 만드는 시간을 아낀다.
        int w = img.GetWidth(), h = img.GetHeight();
        float shrink = Mathf.Min(1f, 512f / Mathf.Max(w, h));
        if (shrink < 1f) img.Resize(Mathf.Max(1, (int)(w * shrink)), Mathf.Max(1, (int)(h * shrink)), Image.Interpolation.Bilinear);
        img.Convert(Image.Format.Rgba8);

        byte[] px = img.GetData();
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            float r = px[i] / 255f, g = px[i + 1] / 255f, b = px[i + 2] / 255f;
            float lum = 0.299f * r + 0.587f * g + 0.114f * b;
            float t = Mathf.SmoothStep(threshold, threshold + 0.15f, lum);
            px[i] = (byte)(r * t * 255f);
            px[i + 1] = (byte)(g * t * 255f);
            px[i + 2] = (byte)(b * t * 255f);
            px[i + 3] = 255;
        }
        var mask = ImageTexture.CreateFromImage(Image.CreateFromData(img.GetWidth(), img.GetHeight(), false, Image.Format.Rgba8, px));
        _masks[key] = mask;
        return mask;
    }

    // 노드 아래 모든 MeshInstance3D 의 표면 머티리얼에 백라이트를 건다(표면 오버라이드로).
    public static void ApplyToTree(Node root, float threshold, Color tint, float energy)
    {
        if (root == null || energy <= 0f) return;
        foreach (var n in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mi = (MeshInstance3D)n;
            if (mi.Mesh == null) continue;
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            {
                var cur = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
                var lit = Make(cur, threshold, tint, energy);
                if (lit != cur) mi.SetSurfaceOverrideMaterial(s, lit);
            }
        }
    }
}
