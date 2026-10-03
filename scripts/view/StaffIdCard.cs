using System;
using System.Collections.Generic;
using Godot;
using NSP.Facility;

namespace NSP.View;

// 직원 신원 카드 — 타이틀 화면(TitleStaffIdView)과 엔딩(EndingStaffIdView)이 같은 그림을 쓴다.
//
// 두 화면이 **똑같아 보여야** 하는 것이 요점이다. LOOSE 엔딩에서 다시 뜨는 명단이
// 타이틀에서 보던 그 명단과 한 픽셀이라도 다르면, 마지막에 카드 한 장이 깨지는 장면이
// "타이틀에서 늘 보던 그 연출"로 읽히지 않는다. 그래서 자리 · 색 · 글자 크기를
// 여기 한 곳에만 둔다.
public static class StaffIdCard
{
    public static readonly Color Ink = new(0.84f, 0.89f, 0.88f);
    public static readonly Color Mint = new(0.46f, 0.90f, 0.80f);
    public static readonly Color Dim = new(0.36f, 0.44f, 0.45f);
    public static readonly Color Err = new(0.92f, 0.28f, 0.24f);

    public const float GridLeft = 82f, GridTop = 150f;
    public const float CardW = 208f, CardH = 152f, GapX = 16f, GapY = 18f;

    // 화면에 놓는 순서(윗줄 3명 / 아랫줄 3명). 목록에 없는 직원은 뒤에 붙는다.
    public static readonly string[] Order = { "rabbit", "cat", "fox", "sheep", "wolf", "dog" };

    // 데이터에 코드네임이 없을 때만 쓰는 폴백. 화면에는 한글 이름만 찍는다.
    private static readonly Dictionary<string, string> Fallback = new()
    {
        { "rabbit", "토끼" }, { "cat", "고양이" }, { "fox", "여우" },
        { "sheep", "양" }, { "wolf", "늑대" }, { "dog", "강아지" },
    };

    public sealed class Data
    {
        public string Id = "";
        public string Codename = "";
        public Texture2D Face;
        public Color Tint = Colors.White;
    }

    // 카드에 얹는 상태 — 화면마다 쓰는 것이 다르다.
    public struct Style
    {
        public bool Hot;        // 커서가 올라가 있다
        public bool Broken;     // 신원 오류(얼굴·이름이 깨진다)
        public bool Dead;       // 회색으로 죽은 카드
        public string Stamp;    // 이름 아래 도장 한 줄("확인" · "격리 · 이송 완료")
        public Color StampColor;
    }

    public static List<string> Sorted(IEnumerable<string> ids)
    {
        var list = new List<string>(ids ?? Array.Empty<string>());
        list.Sort((a, b) =>
        {
            int ia = Array.IndexOf(Order, a), ib = Array.IndexOf(Order, b);
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.CompareOrdinal(a, b);
        });
        return list;
    }

    // 타이틀에서는 해금 여부와 상관없이 여섯 명을 전부 보여준다(allIds = true).
    public static List<Data> Build(FacilitySimulation sim, bool allIds = true)
    {
        var o = new List<Data>();
        if (sim == null) return o;
        foreach (string id in Sorted(allIds ? sim.GetEmployeeIds() : sim.GetActiveEmployeeIds()))
        {
            var def = sim.GetEmployeeDef(id);
            if (def == null) continue;
            o.Add(new Data
            {
                Id = id,
                Codename = string.IsNullOrEmpty(def.Codename) ? Fallback.GetValueOrDefault(id, id) : def.Codename,
                Face = def.IdPhoto ?? def.FacePortrait,
                Tint = def.IconColor,
            });
        }
        return o;
    }

    public static Rect2 Rect(int i)
    {
        int col = i % 3, row = i / 3;
        return new Rect2(GridLeft + col * (CardW + GapX), GridTop + row * (CardH + GapY), CardW, CardH);
    }

    // 카드 한 장. rng 는 깨진 얼굴의 지지직거림에만 쓴다(없으면 고정 패턴).
    public static void Draw(CanvasItem ci, Font font, Data c, Rect2 r, Style s, RandomNumberGenerator rng = null)
    {
        bool broken = s.Broken;
        var edge = broken ? Err : s.Dead ? Dim : s.Hot ? Mint : Dim;

        ci.DrawRect(r, new Color(0.04f, 0.09f, 0.10f, s.Dead ? 0.5f : 0.9f));
        ci.DrawRect(r, edge with { A = broken ? 0.9f : s.Hot ? 0.85f : s.Dead ? 0.3f : 0.4f },
            false, s.Hot || broken ? 2f : 1.2f);
        if (s.Hot && !broken) ci.DrawRect(r, Mint with { A = 0.07f });

        // 가면 초상.
        var box = new Rect2(r.Position.X + (r.Size.X - 72f) * 0.5f, r.Position.Y + 14f, 72f, 72f);
        ci.DrawRect(box, new Color(0.02f, 0.05f, 0.06f, 0.9f));
        if (broken)
        {
            // 얼굴이 뭉개진다.
            for (int k = 0; k < 8; k++)
                ci.DrawRect(new Rect2(box.Position.X, box.Position.Y + k * 9f, box.Size.X, 7f),
                    new Color(0.55f, 0.58f, 0.58f, rng?.RandfRange(0.25f, 0.85f) ?? (0.3f + k * 0.07f)));
        }
        else if (c.Face != null)
        {
            var src = c.Face.GetSize();
            if (src.X > 0f && src.Y > 0f)
            {
                float k = Mathf.Min(box.Size.X / src.X, box.Size.Y / src.Y);
                var dst = src * k;
                ci.DrawTextureRect(c.Face, new Rect2(box.Position + (box.Size - dst) * 0.5f, dst), false,
                    s.Dead ? new Color(0.45f, 0.45f, 0.45f) : Colors.White);
            }
        }
        else
        {
            ci.DrawCircle(box.GetCenter(), 22f, s.Dead ? Dim : c.Tint);
        }
        ci.DrawRect(box, (broken ? Err : Dim) with { A = 0.6f }, false, 1f);

        // 이름 한 줄만 둔다. 신원 오류는 이름이 깨지고 테두리가 붉어지는 것으로 읽힌다.
        ci.DrawString(font, new Vector2(r.Position.X, r.Position.Y + 118f),
            broken ? "████" : c.Codename, HorizontalAlignment.Center, r.Size.X,
            ViewFont.S(20), broken ? Err : s.Dead ? Dim : s.Hot ? Ink : Ink with { A = 0.9f });

        if (!string.IsNullOrEmpty(s.Stamp))
            ci.DrawString(font, new Vector2(r.Position.X, r.Position.Y + 142f), s.Stamp,
                HorizontalAlignment.Center, r.Size.X, ViewFont.S(13),
                s.StampColor.A > 0f ? s.StampColor : Mint);
    }
}
