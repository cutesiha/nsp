using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그 컷씬의 비상 경보창(alert: / alertrow: / alertfoot:).
// 짙은 적색 패널 + 경고 삼각형 + 항목이 한 줄씩 켜진다.
//
// 엔딩(BAD)이 격벽 개방을 알릴 때 **같은 양식**을 그대로 쓴다 — 프롤로그에서
// "격리 구역에서 개체들이 빠져나왔어요" 를 알리던 그 창이 마지막에 다시 뜬다.
public partial class AlertBoard : Control
{
    public string Title = "";
    public string Sub = "";
    public string Foot = "";
    public IReadOnlyList<(string Label, string Value)> Rows = Array.Empty<(string, string)>();

    private const float Lead = 0.30f, RowGap = 0.22f;
    private static readonly Color Deep = new(0.14f, 0.015f, 0.015f, 0.93f);
    private static readonly Color Line = new(1f, 0.22f, 0.18f);
    private static readonly Color Soft = new(1f, 0.46f, 0.40f);

    private int _shown;
    private bool _footShown;
    private float _t;

    public void Reset()
    {
        _shown = 0;
        _footShown = false;
        _t = 0f;
        QueueRedraw();
    }

    public void Tick(double elapsed)
    {
        _t = (float)elapsed;
        int want = elapsed < Lead ? 0 : Mathf.Min(Rows.Count, (int)((elapsed - Lead) / RowGap) + 1);
        if (want != _shown)
        {
            _shown = want;
            Sfx.Instance?.Play("tick", -14f);
        }
        if (!_footShown && Rows.Count > 0 && _shown >= Rows.Count
            && elapsed > Lead + Rows.Count * RowGap + 0.15)
        {
            _footShown = true;
            Sfx.Instance?.Play("sensor_beep", -10f);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ViewFont.Default;
        var box = new Rect2(Vector2.Zero, Size);
        float pulse = 0.65f + 0.35f * (0.5f + 0.5f * Mathf.Sin(_t * 7.5f));

        // 패널 — 짙은 적색 바탕 + 두꺼운 경보 테두리.
        DrawRect(box.Grow(6f), new Color(0.35f, 0.03f, 0.03f, 0.35f * pulse));
        DrawRect(box, Deep);
        DrawRect(box, Line with { A = 0.95f * pulse }, false, 3f);
        DrawRect(box.Grow(-6f), Line with { A = 0.35f }, false, 1f);
        for (float y = 0; y < Size.Y; y += 3f)
            DrawRect(new Rect2(0, y, Size.X, 1f), new Color(0f, 0f, 0f, 0.16f));

        // 경고 삼각형.
        var c = new Vector2(Size.X * 0.5f, 60f);
        var pts = new[]
        {
            c + new Vector2(0f, -30f), c + new Vector2(30f, 22f), c + new Vector2(-30f, 22f),
        };
        DrawColoredPolygon(pts, Line with { A = 0.92f * pulse });
        DrawPolyline(new[] { pts[0], pts[1], pts[2], pts[0] }, new Color(1f, 0.75f, 0.70f, 0.9f), 2.2f);
        DrawString(font, new Vector2(c.X - 12f, c.Y + 16f), "!", HorizontalAlignment.Center, 24f,
            ViewFont.S(24), Deep);

        // 제목 / 부제.
        DrawString(font, new Vector2(0f, 128f), Title, HorizontalAlignment.Center, Size.X,
            ViewFont.S(36), new Color(1f, 0.30f, 0.24f));
        if (!string.IsNullOrEmpty(Sub))
            DrawString(font, new Vector2(0f, 156f), Sub, HorizontalAlignment.Center, Size.X,
                ViewFont.S(17), Soft);

        DrawRect(new Rect2(34f, 178f, Size.X - 68f, 1.6f), Line with { A = 0.55f });

        // 항목 — 왼쪽 라벨 / 오른쪽 값.
        float y2 = 212f;
        for (int i = 0; i < Rows.Count; i++)
        {
            if (i >= _shown) break;
            var (label, value) = Rows[i];
            DrawString(font, new Vector2(40f, y2), label, HorizontalAlignment.Left,
                Size.X - 200f, ViewFont.S(19), Soft);
            // 마지막에 켜진 항목은 잠깐 밝게 깜빡인다.
            bool fresh = i == _shown - 1;
            DrawString(font, new Vector2(Size.X - 200f, y2), value, HorizontalAlignment.Right,
                160f, ViewFont.S(19), fresh ? new Color(1f, 0.85f, 0.80f, pulse) : new Color(1f, 0.32f, 0.26f));
            y2 += 32f;
        }

        if (_footShown && !string.IsNullOrEmpty(Foot))
            DrawString(font, new Vector2(0f, Size.Y - 30f), Foot, HorizontalAlignment.Center,
                Size.X, ViewFont.S(24), new Color(1f, 0.36f, 0.30f, 0.55f + 0.45f * pulse));
    }
}
