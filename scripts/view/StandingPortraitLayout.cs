using Godot;
using NSP.Facility;

namespace NSP.View;

// 직원 스탠딩 원화(standing_v2)를 화면에 앉히는 공용 계산.
//
// 원래 인터뷰 화면(InterviewCCTVView) 안에만 있던 계산을 그대로 꺼내 온 것이다.
// 숫자와 순서를 바꾸지 않았다 — 인터뷰 화면 · 배치 콘솔(ScheduleStaffView)에서
// 보이는 크기와 위치는 추출 전과 완전히 같다.
//
// 원화는 캐릭터마다 따로 잘려 있어 캔버스 크기가 제각각이다. 화면에 "맞춰" 그리면
// 키가 작은 캐릭터가 여우만큼 커 보인다. 그래서 모든 원화에 같은 배율을 적용하고
// 발끝을 화면 아래에 붙인다 — 원화 안의 실제 그림 높이가 곧 키가 된다.
//
// 배율 기준은 가장 큰 원화가 표시 영역에 딱 들어가는 값이며,
// 나머지는 그 비율대로 자동으로 작아진다. (원화를 교체하면 이 기준도 다시 확인할 것)
public static class StandingPortraitLayout
{
    private static readonly System.Collections.Generic.Dictionary<ulong, Rect2I> _contentBoxes = new();
    // 여섯 명 중 가장 큰 원화의 그림 높이. 한 번 구하면 바뀌지 않는다.
    private static float _tallestContent = -1f;

    // 원화에서 실제로 그림이 그려진 영역(투명 여백 제외). 원화를 교체해도 자동으로 다시 잡힌다.
    public static Rect2I ContentBox(Texture2D tex)
    {
        ulong key = tex.GetInstanceId();
        if (_contentBoxes.TryGetValue(key, out var cached)) return cached;

        Rect2I box = Measure(tex.GetImage()) ?? new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight());
        _contentBoxes[key] = box;
        return box;
    }

    // 여섯 명 중 가장 큰 원화가 표시 높이에 맞도록 하는 공통 배율.
    //
    // 예전에는 배율 자체를 캐시했다(표시 높이가 한 곳뿐이었다). 지금은 표시 영역이 둘 이상
    // 이므로 '가장 큰 원화의 높이'만 캐시하고 나누기는 매번 한다 — 화면별 배율이 서로를
    // 덮어쓰지 않게 하려는 것이고, 같은 표시 높이에 대한 결과값은 예전과 같다.
    public static float PortraitUnit(float availableHeight) =>
        availableHeight / Mathf.Max(1f, TallestContentHeight());

    // 원화가 아직 준비되지 않았으면(시뮬레이션 전) 캐시하지 않고 1 을 돌려준다 —
    // 그 값이 굳어 버리면 이후 모든 화면의 배율이 어긋난다.
    public static float TallestContentHeight()
    {
        if (_tallestContent > 0f) return _tallestContent;

        float tallest = 1f;
        bool found = false;
        var sim = FacilitySimulation.Instance;
        if (sim != null)
        {
            foreach (string id in sim.GetEmployeeIds())
            {
                var t = sim.GetEmployeeDef(id)?.StandingImage;
                if (t == null) continue;
                found = true;
                tallest = Mathf.Max(tallest, ContentBox(t).Size.Y);
            }
        }
        if (found) _tallestContent = tallest;
        return tallest;
    }

    // 표시 영역 안에서 이 원화가 차지할 크기와 위치.
    //
    //   boxSize   : 표시 영역(ClipContents 를 켠 Control)의 크기
    //   topMargin : 제일 큰 캐릭터의 머리가 영역 위선에 닿지 않게 하는 여백
    //   zoom      : 얼굴이 잘 보이도록 전원에게 같은 배율로 키우는 값(1 = 키우지 않음)
    //   lift      : 그 직원만 위로 올리는 보정(EmployeeDef.InterviewPortraitLift)
    public static (Vector2 Size, Vector2 Position) Place(
        Texture2D tex, Vector2 boxSize, float topMargin, float zoom, float lift)
    {
        Rect2I box = ContentBox(tex);
        float avail = boxSize.Y - topMargin;
        float unit = PortraitUnit(avail) * zoom;
        // 확대로 늘어난 만큼 전원 똑같이 내린다 → 가장 큰 직원의 머리가 원래 자리(위 여백)에 머문다.
        float drop = avail * (zoom - 1f);

        var size = new Vector2(tex.GetWidth() * unit, tex.GetHeight() * unit);
        var pos = new Vector2(
            // 가로는 그림의 중심을 표시 영역 중앙에.
            boxSize.X / 2f - (box.Position.X + box.Size.X / 2f) * unit,
            // 세로는 발끝을 바닥에 맞춘 자리에서 공통 drop 만큼 아래로.
            boxSize.Y - (box.Position.Y + box.Size.Y) * unit + drop - lift);
        return (size, pos);
    }

    // 알파가 충분히 진한 픽셀만 그림으로 본다. Image.GetUsedRect() 는 알파가 1이라도
    // 포함해서, 원화 위쪽에 남은 아주 옅은 선까지 키로 계산되어 비율이 어긋난다.
    private static Rect2I? Measure(Image img)
    {
        if (img == null) return null;
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);

        byte[] data = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        if (data == null || data.Length < w * h * 4) return null;

        const int AlphaThreshold = 24;
        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (data[row + x * 4 + 3] <= AlphaThreshold) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                maxY = y;
            }
        }
        if (maxX < 0) return null;
        return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
