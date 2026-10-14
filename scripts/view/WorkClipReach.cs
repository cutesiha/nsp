// 자동 생성 — tools/gen_cctv_clips.py 가 쓴다. 손으로 고치지 말 것.
using System.Collections.Generic;

namespace NSP.View;

// 작업 클립마다 "발 사이 중심에서 작업하는 손까지 앞 거리 · 손 높이"(m). 표현 전용이다.
// RoomWorkVisualController 가 이 값으로 직원을 작업 대상(InteractionTarget)에서 알맞은 거리에 세운다 —
// 손이 허공에 뜨거나 기계 속으로 들어가지 않게.
public static class WorkClipReach
{
    private static readonly Dictionary<string, (float MF, float MH, float FF, float FH)> _reach = new()
    {
        ["work"] = (0.656f, 1.256f, 0.585f, 1.009f),
        ["repair"] = (0.786f, 1.270f, 0.694f, 1.017f),
        ["hammer_work"] = (0.677f, 1.234f, 0.602f, 0.990f),
        ["inspect"] = (0.016f, 0.841f, 0.014f, 0.631f),
        ["panel_press"] = (0.434f, 1.226f, 0.394f, 1.066f),
        ["console_operate"] = (0.445f, 1.000f, 0.405f, 0.950f),
        ["knob_turn"] = (0.431f, 1.280f, 0.390f, 1.120f),
        ["lever_operate"] = (0.390f, 1.014f, 0.350f, 0.880f),
        ["crouch_repair"] = (0.429f, 0.476f, 0.389f, 0.432f),
        ["shelf_reach"] = (0.381f, 1.534f, 0.341f, 1.374f),
        ["shelf_low"] = (0.446f, 0.580f, 0.406f, 0.540f),
        ["clipboard_check"] = (0.290f, 1.127f, 0.290f, 0.977f),
    };

    // 박스를 손에 쥐는 순간 / 놓는 순간(초) · 클립 길이.
    private static readonly Dictionary<string, (float At, float Length)> _box = new()
    {
        ["pickup_box_male"] = (0.50f, 1.00f),
        ["place_box_male"] = (0.48f, 0.90f),
        ["pickup_box_female"] = (0.60f, 1.20f),
        ["place_box_female"] = (0.55f, 1.00f),
        ["pickup_box_sheep"] = (0.95f, 1.75f),
        ["place_box_sheep"] = (0.72f, 1.35f),
    };

    // 체형 전용 클립(name_m / name_f)도 기본 이름으로 찾는다.
    public static bool TryGet(string clip, bool female, out float forward, out float height)
    {
        forward = height = 0f;
        if (string.IsNullOrEmpty(clip)) return false;
        string key = clip.EndsWith("_m") || clip.EndsWith("_f") ? clip[..^2] : clip;
        if (!_reach.TryGetValue(key, out var r)) return false;
        (forward, height) = female ? (r.FF, r.FH) : (r.MF, r.MH);
        return true;
    }

    public static (float At, float Length) Box(string clip) =>
        _box.TryGetValue(clip, out var b) ? b : (0.5f, 1f);

    // 의자 · 침대 가장자리에 앉고 일어날 때 골반이 내려간 정도(0~1) — chair_sit / chair_stand 와 같은 박자.
    public static float SitCurve(float u) => Curve(u, SitKeys);
    public static float StandCurve(float u) => Curve(u, StandKeys);
    private static readonly float[,] SitKeys = { { 0.00f, 0.00f }, { 0.20f, 0.12f }, { 0.55f, 0.72f }, { 0.80f, 1.00f }, { 1.00f, 1.00f } };
    private static readonly float[,] StandKeys = { { 0.00f, 1.00f }, { 0.25f, 0.95f }, { 0.60f, 0.35f }, { 0.85f, 0.00f }, { 1.00f, 0.00f } };

    private static float Curve(float u, float[,] k)
    {
        if (u <= k[0, 0]) return k[0, 1];
        for (int i = 0; i < k.GetLength(0) - 1; i++)
            if (u <= k[i + 1, 0])
            {
                float f = (u - k[i, 0]) / (k[i + 1, 0] - k[i, 0]);
                f = f * f * (3f - 2f * f);
                return k[i, 1] + (k[i + 1, 1] - k[i, 1]) * f;
            }
        return k[k.GetLength(0) - 1, 1];
    }
}
