// 자동 생성 — tools/gen_distraction_clips.py 가 쓴다. 손으로 고치지 말 것.
using System.Collections.Generic;

namespace NSP.View;

// 딴짓 클립마다 "발 사이 중심에서 설비를 만지는 손까지 앞 거리 · 손 높이"(m). 표현 전용이다.
// RoomWorkVisualController.Distraction 이 이 값으로 직원을 설비(InteractionTarget)에서
// 알맞은 거리에 세운다 — 두드리는 손이 허공에 뜨거나 기계 속에 들어가지 않게.
public static class DistractionClipReach
{
    private static readonly Dictionary<string, (float Forward, float Height)> _reach = new()
    {
        ["dis_rabbit_peek_loop"] = (0.393f, 0.821f),
        ["dis_dog_probe_loop"] = (0.414f, 0.848f),
        ["dis_wolf_kick_loop"] = (0.375f, 1.037f),
    };

    public static bool TryGet(string clip, out float forward, out float height)
    {
        forward = height = 0f;
        if (string.IsNullOrEmpty(clip) || !_reach.TryGetValue(clip, out var r)) return false;
        (forward, height) = r;
        return true;
    }
}
