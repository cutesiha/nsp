using Godot;
using NSP.Core;

namespace NSP.Ui;

// 도전과제 30개의 아이콘을 **직접 그린다.**
//
// 예전에는 분류마다 ◇ △ ○ ★ ◆ 한 글자를 찍었다. 서른 개가 다섯 종류로 보여서
// 어느 것이 무엇인지 아이콘만으로는 전혀 알 수 없었다.
//
// 여기서는 도전과제마다 그 내용을 가리키는 그림을 선으로 그린다 — CRT, 미로, 수화기,
// 방패, 철창, 월계관처럼 **그 과제가 무엇이었는지 한눈에 떠오르는 모양**이다.
// 시설 단말기의 선 그림 느낌을 유지한다: 단색 · 굵은 실루엣 · 잔디테일 없음.
//
// 쓰는 곳은 둘이다(같은 그림이 나와야 한다).
//   · AchievementArchiveView — 기록실 목록
//   · AchievementToast       — 달성 팝업
//
// **판정 · 저장 · 이름 · 조건문에는 손대지 않는다.** 이 파일은 그리기만 한다.
// 미달성은 자물쇠, 숨김 과제는 달성 전까지 물음표라 내용이 새지 않는다.
public static class AchievementIcons
{
    // 100×100 좌표계 위에 그리는 붓. 실제 칸 크기에 맞춰 알아서 줄이고 가운데 정렬한다.
    private readonly struct Pen
    {
        private readonly CanvasItem _ci;
        private readonly Vector2 _at;
        private readonly float _k;
        public readonly Color Ink;
        private readonly float _w;

        public Pen(CanvasItem ci, Rect2 box, Color ink)
        {
            _ci = ci;
            float side = Mathf.Min(box.Size.X, box.Size.Y);
            _k = side / 100f;
            _at = box.Position + (box.Size - new Vector2(side, side)) * 0.5f;
            Ink = ink;
            // 칸이 작아도 선이 사라지지 않게 하한을 둔다(팝업 아이콘은 한 변 40px 남짓).
            _w = Mathf.Max(1.1f, side * 0.050f);
        }

        public Vector2 V(float x, float y) => _at + new Vector2(x, y) * _k;
        public float S(float v) => v * _k;
        private float Wide(float w) => w <= 0f ? _w : Mathf.Max(0.9f, S(w));

        public void Line(float x1, float y1, float x2, float y2, float w = 0f, Color? c = null)
            => _ci.DrawLine(V(x1, y1), V(x2, y2), c ?? Ink, Wide(w));

        public void Box(float x, float y, float w, float h, bool fill = false, float lw = 0f, Color? c = null)
            => _ci.DrawRect(new Rect2(V(x, y), new Vector2(S(w), S(h))), c ?? Ink, fill, fill ? -1f : Wide(lw));

        public void Circle(float cx, float cy, float r, bool fill = false, float lw = 0f, Color? c = null)
        {
            if (fill) _ci.DrawCircle(V(cx, cy), S(r), c ?? Ink);
            else _ci.DrawArc(V(cx, cy), S(r), 0f, Mathf.Tau, 28, c ?? Ink, Wide(lw));
        }

        // 각도는 도(度). 0 = 오른쪽, 시계 방향으로 증가(화면 좌표라 y 가 아래로 간다).
        public void Arc(float cx, float cy, float r, float from, float to, float lw = 0f, Color? c = null)
            => _ci.DrawArc(V(cx, cy), S(r), Mathf.DegToRad(from), Mathf.DegToRad(to), 26, c ?? Ink, Wide(lw));

        public void Path(float w, Color? c, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = V(xy[i * 2], xy[i * 2 + 1]);
            _ci.DrawPolyline(pts, c ?? Ink, Wide(w));
        }

        public void Fill(Color? c, params float[] xy)
        {
            var pts = new Vector2[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = V(xy[i * 2], xy[i * 2 + 1]);
            _ci.DrawColoredPolygon(pts, c ?? Ink);
        }

        // 등급 기호(S · A · D)처럼 **글자 자체가 디자인인** 자리에만 쓴다.
        public void Glyph(Font font, string s, float cx, float cy, float size, Color? c = null)
        {
            if (font == null) return;
            int px = Mathf.Max(7, Mathf.RoundToInt(S(size)));
            var w = font.GetStringSize(s, HorizontalAlignment.Left, -1f, px);
            _ci.DrawString(font, V(cx, cy) + new Vector2(-w.X * 0.5f, px * 0.36f),
                s, HorizontalAlignment.Left, -1f, px, c ?? Ink);
        }

        // ── 자주 쓰는 조각 ──────────────────────────────────────────────

        // 체크 표시.
        public void Check(float cx, float cy, float r, Color? c = null)
            => Path(0f, c, cx - r, cy, cx - r * 0.25f, cy + r * 0.7f, cx + r, cy - r * 0.8f);

        // 가위표.
        public void Cross(float cx, float cy, float r, float w = 0f, Color? c = null)
        {
            Line(cx - r, cy - r, cx + r, cy + r, w, c);
            Line(cx + r, cy - r, cx - r, cy + r, w, c);
        }

        // 사람 머리 + 어깨(실루엣).
        public void Person(float cx, float cy, float s, bool fill = true, Color? c = null)
        {
            Circle(cx, cy - s * 0.55f, s * 0.42f, fill, 0f, c);
            if (fill) Fill(c, cx - s * 0.75f, cy + s, cx - s * 0.62f, cy + s * 0.1f,
                           cx + s * 0.62f, cy + s * 0.1f, cx + s * 0.75f, cy + s);
            else Path(0f, c, cx - s * 0.75f, cy + s, cx - s * 0.6f, cy + s * 0.15f,
                      cx + s * 0.6f, cy + s * 0.15f, cx + s * 0.75f, cy + s);
        }

        // 문서 한 장(접힌 모서리 + 글줄).
        public void Sheet(float x, float y, float w, float h, int lines, Color? c = null)
        {
            float fold = w * 0.26f;
            Path(0f, c, x, y, x + w - fold, y, x + w, y + fold, x + w, y + h, x, y + h, x, y);
            Line(x + w - fold, y, x + w - fold, y + fold, 0f, c);
            Line(x + w - fold, y + fold, x + w, y + fold, 0f, c);
            for (int i = 0; i < lines; i++)
            {
                float ly = y + h * (0.34f + 0.17f * i);
                if (ly > y + h - h * 0.12f) break;
                Line(x + w * 0.16f, ly, x + w * 0.84f, ly, 2.6f, c);
            }
        }

        // 전화 수화기(가로로 누운 모양).
        public void Handset(float cx, float cy, float s, Color? c = null)
        {
            Arc(cx, cy + s * 0.15f, s, 200f, 340f, 0f, c);
            Fill(c, cx - s * 1.28f, cy - s * 0.42f, cx - s * 0.5f, cy - s * 0.42f,
                 cx - s * 0.5f, cy + s * 0.26f, cx - s * 1.28f, cy + s * 0.26f);
            Fill(c, cx + s * 0.5f, cy - s * 0.42f, cx + s * 1.28f, cy - s * 0.42f,
                 cx + s * 1.28f, cy + s * 0.26f, cx + s * 0.5f, cy + s * 0.26f);
        }
    }

    // ── 입구 ─────────────────────────────────────────────────────────────
    //
    // unlocked = false 면 내용을 그리지 않는다. 숨김 과제는 그 위에 물음표까지 둔다 —
    // 아이콘만 보고 무엇인지 알아차리는 일이 없어야 한다.
    public static void Draw(CanvasItem ci, AchievementDefinition def, Rect2 box,
                            Color ink, bool unlocked, Font font = null)
    {
        if (ci == null || def == null) return;
        var p = new Pen(ci, box, ink);
        if (!unlocked)
        {
            if (def.Hidden) Unknown(p, font);
            else Locked(p);
            return;
        }
        Paint(p, def.Id, font);
    }

    // 미달성 — 자물쇠.
    private static void Locked(Pen p)
    {
        p.Box(28f, 46f, 44f, 34f, false, 5.5f);
        p.Arc(50f, 46f, 15f, 180f, 360f, 5.5f);
        p.Circle(50f, 62f, 4.5f, true);
    }

    // 미달성 숨김 과제 — 무엇인지도 알려주지 않는다.
    private static void Unknown(Pen p, Font font)
    {
        p.Box(24f, 24f, 52f, 52f, false, 4f);
        if (font != null) p.Glyph(font, "?", 50f, 50f, 46f);
        else { p.Arc(50f, 42f, 12f, 200f, 380f, 5f); p.Line(50f, 54f, 50f, 64f, 5f); p.Circle(50f, 73f, 3.2f, true); }
    }

    private static void Paint(Pen p, string id, Font font)
    {
        switch (id)
        {
            // ── A. 기본 플레이 ───────────────────────────────────────────
            // 전원이 켜진 CRT.
            case Achievements.FirstLaunch:
                p.Box(14f, 20f, 72f, 50f, false, 4.5f);
                p.Line(40f, 70f, 40f, 80f); p.Line(60f, 70f, 60f, 80f);
                p.Line(30f, 80f, 70f, 80f, 4.5f);
                p.Arc(50f, 45f, 13f, -60f, 240f, 4.5f);
                p.Line(50f, 29f, 50f, 45f, 4.5f);
                break;

            // 펼친 교본 + 체크.
            case Achievements.TutorialComplete:
                p.Path(0f, null, 10f, 30f, 46f, 36f, 46f, 80f, 10f, 72f, 10f, 30f);
                p.Path(0f, null, 90f, 30f, 54f, 36f, 54f, 80f, 90f, 72f, 90f, 30f);
                p.Line(50f, 36f, 50f, 80f, 2.6f);
                p.Check(70f, 54f, 11f);
                break;

            // 초승달 + 시계.
            case Achievements.FirstShift:
                p.Arc(38f, 44f, 26f, 40f, 310f, 5f);
                p.Arc(28f, 44f, 22f, 300f, 420f, 5f);
                p.Circle(66f, 68f, 20f, false, 4.5f);
                p.Line(66f, 68f, 66f, 56f, 3.4f);
                p.Line(66f, 68f, 76f, 72f, 3.4f);
                break;

            // 수화기 + 하트.
            case Achievements.OutgoingCall:
                p.Handset(42f, 56f, 20f);
                p.Arc(70f, 26f, 7f, 180f, 360f, 3.4f);
                p.Arc(84f, 26f, 7f, 180f, 360f, 3.4f);
                p.Path(3.4f, null, 63f, 28f, 77f, 44f, 91f, 28f);
                break;

            // 여러 줄이 적힌 기록 문서.
            case Achievements.FirstLog:
                p.Sheet(22f, 14f, 56f, 72f, 4);
                break;

            // 돋보기 안의 사람.
            case Achievements.FirstInterview:
                p.Circle(43f, 42f, 27f, false, 5f);
                p.Line(62f, 61f, 84f, 83f, 6.5f);
                p.Person(43f, 42f, 13f);
                break;

            // ── B. 실수 · 시설 관리 ──────────────────────────────────────
            // 미로 + 가위표.
            case Achievements.MazeFailOnce:
                p.Box(14f, 14f, 72f, 72f, false, 4f);
                p.Line(14f, 38f, 62f, 38f, 3.4f);
                p.Line(38f, 62f, 86f, 62f, 3.4f);
                p.Line(38f, 38f, 38f, 62f, 3.4f);
                p.Cross(50f, 50f, 13f, 6f);
                break;

            // 붕대 감은 손가락 + 균열.
            case Achievements.MazeFailThree:
                p.Path(0f, null, 38f, 86f, 38f, 34f, 46f, 20f, 58f, 20f, 64f, 34f, 64f, 86f);
                p.Box(32f, 44f, 38f, 16f, false, 4f);
                p.Line(32f, 48f, 70f, 56f, 2.6f);
                p.Path(3.2f, null, 52f, 30f, 46f, 38f, 54f, 40f, 48f, 48f);
                break;

            // 수리 요청서 위의 사선.
            case Achievements.RepairRequestIgnored:
                p.Sheet(24f, 16f, 52f, 68f, 3);
                p.Line(16f, 84f, 84f, 16f, 6.5f);
                break;

            // 쓰러진 직원.
            case Achievements.FirstFaint:
                p.Circle(26f, 56f, 12f, true);
                p.Fill(null, 40f, 46f, 78f, 58f, 76f, 70f, 38f, 68f);
                p.Line(14f, 80f, 86f, 80f, 3.4f);
                break;

            // 경고등 + 금지 표시.
            case Achievements.WarningNeglected:
                p.Fill(null, 50f, 16f, 84f, 74f, 16f, 74f);
                p.Line(50f, 36f, 50f, 56f, 5f, Dark);
                p.Circle(50f, 65f, 3.6f, true, 0f, Dark);
                p.Circle(50f, 50f, 34f, false, 5f);
                p.Line(26f, 26f, 74f, 74f, 5f);
                break;

            // 빛나는 전구.
            case Achievements.PowerRestored:
                p.Arc(50f, 44f, 20f, 160f, 380f, 4.5f);
                p.Path(0f, null, 34f, 54f, 40f, 68f, 60f, 68f, 66f, 54f);
                p.Line(40f, 74f, 60f, 74f, 4f);
                p.Line(43f, 82f, 57f, 82f, 4f);
                p.Line(50f, 12f, 50f, 20f, 3.4f);
                p.Line(18f, 26f, 24f, 32f, 3.4f);
                p.Line(82f, 26f, 76f, 32f, 3.4f);
                p.Line(12f, 52f, 20f, 52f, 3.4f);
                p.Line(88f, 52f, 80f, 52f, 3.4f);
                break;

            // 렌치 + 톱니바퀴.
            case Achievements.FirstFacilityRepair:
                p.Circle(62f, 38f, 18f, false, 5f);
                p.Circle(62f, 38f, 7f, false, 4f);
                for (int i = 0; i < 8; i++)
                {
                    float a = Mathf.DegToRad(i * 45f);
                    p.Line(62f + Mathf.Cos(a) * 18f, 38f + Mathf.Sin(a) * 18f,
                           62f + Mathf.Cos(a) * 25f, 38f + Mathf.Sin(a) * 25f, 4f);
                }
                p.Line(20f, 84f, 48f, 56f, 7f);
                p.Path(4f, null, 48f, 56f, 40f, 48f, 48f, 40f, 56f, 48f, 48f, 56f);
                break;

            // 방패 + 체크.
            case Achievements.SafeShift:
                p.Path(0f, null, 50f, 12f, 82f, 24f, 82f, 52f, 50f, 88f, 18f, 52f, 18f, 24f, 50f, 12f);
                p.Check(50f, 48f, 16f);
                break;

            // ── C. 직원 · 이상현상 · 추리 ────────────────────────────────
            // 울리는 전화기 + 작은 가위표.
            case Achievements.MissedCallOnce:
                p.Handset(44f, 50f, 20f);
                p.Arc(44f, 50f, 34f, 200f, 250f, 3.2f);
                p.Arc(44f, 50f, 34f, 290f, 340f, 3.2f);
                p.Cross(80f, 80f, 11f, 5f);
                break;

            // 서리 낀 수화기 + 눈송이.
            case Achievements.MissedCallThree:
                p.Handset(42f, 56f, 19f);
                for (int i = 0; i < 3; i++)
                {
                    float a = Mathf.DegToRad(i * 60f);
                    p.Line(76f - Mathf.Cos(a) * 14f, 26f - Mathf.Sin(a) * 14f,
                           76f + Mathf.Cos(a) * 14f, 26f + Mathf.Sin(a) * 14f, 3.4f);
                }
                break;

            // 눈 감은 얼굴 + 뒤에 뜬 귀신.
            case Achievements.GhostIgnored:
                Ghost(p, 66f, 38f, 20f);
                p.Circle(40f, 56f, 24f, false, 4.5f);
                p.Path(3.4f, null, 30f, 52f, 35f, 57f, 40f, 52f);
                p.Path(3.4f, null, 44f, 52f, 49f, 57f, 54f, 52f);
                p.Line(34f, 68f, 48f, 68f, 3.4f);
                break;

            // 눈 + 사라지는 개체 — 끝까지 마주 본 결과.
            case Achievements.GhostDispelled:
                Ghost(p, 68f, 34f, 17f);
                p.Line(84f, 20f, 90f, 14f, 2.8f);
                p.Line(52f, 20f, 46f, 14f, 2.8f);
                p.Path(0f, null, 12f, 64f, 32f, 46f, 52f, 64f, 32f, 82f, 12f, 64f);
                p.Circle(32f, 64f, 8f, false, 3.6f);
                p.Circle(32f, 64f, 3.4f, true);
                break;

            // CCTV 화면 속, 설비를 만지는 손.
            case Achievements.SuspiciousCctv:
                p.Box(12f, 18f, 76f, 56f, false, 4.5f);
                p.Line(40f, 74f, 60f, 74f, 4f); p.Line(30f, 84f, 70f, 84f, 4f);
                p.Box(22f, 44f, 20f, 20f, false, 3.4f);
                p.Path(0f, null, 76f, 66f, 62f, 58f, 52f, 46f, 56f, 42f, 64f, 48f,
                       58f, 34f, 62f, 31f, 70f, 44f, 72f, 32f, 76f, 32f, 78f, 46f);
                break;

            // 돋보기로 확대한 문서의 단서.
            case Achievements.FirstEvidence:
                p.Sheet(16f, 12f, 48f, 62f, 3);
                p.Circle(64f, 62f, 22f, false, 5f);
                p.Line(79f, 77f, 92f, 90f, 6f);
                p.Line(54f, 58f, 74f, 58f, 4f);
                p.Line(54f, 68f, 68f, 68f, 4f);
                break;

            // 서로 부딪히는 말풍선 둘.
            case Achievements.FirstContradiction:
                p.Box(8f, 16f, 44f, 32f, false, 4f);
                p.Fill(null, 18f, 48f, 30f, 48f, 20f, 60f);
                p.Check(30f, 32f, 9f);
                p.Box(48f, 48f, 44f, 32f, false, 4f);
                p.Fill(null, 62f, 48f, 74f, 48f, 72f, 38f);
                p.Cross(70f, 64f, 9f, 4f);
                break;

            // 철창 문 + 자물쇠.
            case Achievements.FirstIsolation:
                p.Box(16f, 12f, 68f, 76f, false, 4.5f);
                p.Line(34f, 12f, 34f, 88f, 3.4f);
                p.Line(50f, 12f, 50f, 88f, 3.4f);
                p.Line(66f, 12f, 66f, 88f, 3.4f);
                p.Box(38f, 44f, 24f, 18f, true);
                p.Arc(50f, 44f, 8f, 180f, 360f, 4f);
                break;

            // 철창 뒤의 사람 + 물음표.
            case Achievements.InnocentIsolation:
                p.Person(44f, 58f, 20f);
                p.Line(20f, 10f, 20f, 90f, 3.6f);
                p.Line(38f, 10f, 38f, 90f, 3.6f);
                p.Line(56f, 10f, 56f, 90f, 3.6f);
                p.Line(74f, 10f, 74f, 90f, 3.6f);
                if (font != null) p.Glyph(font, "?", 84f, 24f, 34f);
                break;

            // 여섯 사람 — 3×2.
            case Achievements.TalkToSix:
                for (int i = 0; i < 6; i++)
                    p.Person(24f + i % 3 * 26f, 32f + i / 3 * 38f, 10f);
                break;

            // ── D. 근무 평가 ─────────────────────────────────────────────
            // 월계관 메달 + S.
            case Achievements.GradeS:
                p.Circle(50f, 54f, 28f, false, 5f);
                p.Arc(50f, 54f, 38f, 100f, 170f, 4f);
                p.Arc(50f, 54f, 38f, 10f, 80f, 4f);
                p.Line(36f, 26f, 30f, 10f, 3.6f);
                p.Line(64f, 26f, 70f, 10f, 3.6f);
                if (font != null) p.Glyph(font, "S", 50f, 54f, 34f);
                break;

            // 별 훈장 + A.
            case Achievements.GradeA:
                Star(p, 50f, 48f, 40f, 17f);
                if (font != null) p.Glyph(font, "A", 50f, 50f, 28f);
                p.Line(38f, 78f, 34f, 94f, 3.6f);
                p.Line(62f, 78f, 66f, 94f, 3.6f);
                break;

            // 구겨진 평가서 + D 도장.
            case Achievements.GradeLowest:
                p.Path(0f, null, 18f, 14f, 82f, 14f, 82f, 86f, 18f, 86f, 18f, 14f);
                p.Path(2.8f, null, 18f, 40f, 40f, 34f, 26f, 52f, 54f, 44f, 38f, 62f, 72f, 54f);
                p.Circle(62f, 62f, 20f, false, 4.5f);
                if (font != null) p.Glyph(font, "D", 62f, 62f, 28f);
                break;

            // ── E. 특수 · 엔딩 ───────────────────────────────────────────
            // 100% 를 가리키는 원형 코어 게이지.
            case Achievements.TutorialCore100:
                p.Circle(50f, 50f, 34f, false, 4f, Dark);
                p.Arc(50f, 50f, 34f, -90f, 268f, 7f);
                p.Circle(50f, 50f, 13f, true);
                if (font != null) p.Glyph(font, "100", 50f, 86f, 20f);
                break;

            // 열린 문 너머로 쏟아지는 빛.
            case Achievements.TrueEnding:
                p.Box(16f, 10f, 68f, 80f, false, 4.5f);
                p.Fill(null, 50f, 14f, 80f, 14f, 80f, 86f, 50f, 86f);
                p.Line(50f, 10f, 50f, 90f, 4f);
                p.Circle(56f, 52f, 3.4f, true, 0f, Dark);
                p.Line(84f, 30f, 96f, 24f, 3.2f);
                p.Line(84f, 50f, 98f, 50f, 3.2f);
                p.Line(84f, 70f, 96f, 76f, 3.2f);
                break;

            // 인물 사진이 붙은 보고서 + 붉은 도장.
            case Achievements.BadEnding:
                p.Sheet(18f, 12f, 58f, 70f, 0);
                p.Box(26f, 24f, 20f, 22f, false, 3.4f);
                p.Person(36f, 36f, 8f);
                p.Line(52f, 28f, 68f, 28f, 3f);
                p.Line(52f, 38f, 68f, 38f, 3f);
                p.Line(26f, 56f, 68f, 56f, 3f);
                p.Circle(64f, 68f, 22f, false, 4.5f, Stamp);
                p.Cross(64f, 68f, 12f, 5.5f, Stamp);
                break;

            // 목록에 없는 id — 빈 칸보다는 표식 하나.
            default:
                p.Box(26f, 26f, 48f, 48f, false, 4.5f);
                p.Line(26f, 26f, 74f, 74f, 4f);
                break;
        }
    }

    // 떠 있는 이상 개체 — 아래가 물결인 둥근 실루엣.
    private static void Ghost(Pen p, float cx, float cy, float s)
    {
        p.Arc(cx, cy, s, 180f, 360f, 4f);
        p.Path(4f, null,
            cx - s, cy, cx - s, cy + s * 0.85f, cx - s * 0.5f, cy + s * 0.5f,
            cx, cy + s * 0.9f, cx + s * 0.5f, cy + s * 0.5f, cx + s, cy + s * 0.85f, cx + s, cy);
        p.Circle(cx - s * 0.38f, cy - s * 0.12f, s * 0.15f, true);
        p.Circle(cx + s * 0.38f, cy - s * 0.12f, s * 0.15f, true);
    }

    // 꼭짓점 다섯 개짜리 별.
    private static void Star(Pen p, float cx, float cy, float outer, float inner)
    {
        var xy = new float[20];
        for (int i = 0; i < 10; i++)
        {
            float r = i % 2 == 0 ? outer : inner;
            float a = Mathf.DegToRad(-90f + i * 36f);
            xy[i * 2] = cx + Mathf.Cos(a) * r;
            xy[i * 2 + 1] = cy + Mathf.Sin(a) * r;
        }
        var closed = new float[22];
        xy.CopyTo(closed, 0);
        closed[20] = xy[0]; closed[21] = xy[1];
        p.Path(3.8f, null, closed);
    }

    // 그림 안에서 바탕을 도로 파내거나(경고등 속 느낌표) 도장을 찍을 때 쓰는 색.
    private static readonly Color Dark = new(0.05f, 0.07f, 0.09f, 0.95f);
    private static readonly Color Stamp = new(0.86f, 0.30f, 0.26f, 0.95f);
}

// 도전과제 아이콘 한 칸을 그리는 작은 Control. 달성 팝업이 쓴다
// (기록실은 자기 _Draw 안에서 AchievementIcons.Draw 를 직접 부른다).
public partial class AchievementIconView : Control
{
    public Color Ink { get; set; } = new(0.55f, 0.95f, 1f);

    private AchievementDefinition _def;

    // 달성 팝업은 **달성한 순간**에만 뜬다 — 늘 본래 아이콘을 보여 준다.
    public void Show(AchievementDefinition def)
    {
        _def = def;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_def == null) return;
        AchievementIcons.Draw(this, _def, new Rect2(Vector2.Zero, Size), Ink,
            unlocked: true, font: NSP.View.ViewFont.Default);
    }
}
