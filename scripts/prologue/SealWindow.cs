using System;
using System.Collections.Generic;
using Godot;
using NSP.Core;
using NSP.View;

namespace NSP.Prologue;

// 프롤로그 컷씬의 시설 시스템 창(win: / winbig: / winsub:).
// 경보(붉은 패널)와 달리 "시설이 뭔가를 실행했다"는 담담한 알림창이다.
//
//   비상 차폐 / 120:00:00 / EMERGENCY SEAL ENGAGED   ← 프롤로그
//   영구 봉쇄 / 00:00:00 / PERMANENT SEAL ENGAGED    ← LATE 엔딩
//
// 같은 틀에 숫자만 0 인 것이 요점이라 두 곳이 반드시 같은 클래스를 써야 한다.
public partial class SealWindow : Control
{
    public string Title = "";
    public string Big = "";
    public string Sub = "";

    private static readonly Color Line = new(0.62f, 0.92f, 1f);
    private static readonly Color Deep = new(0.03f, 0.10f, 0.13f, 0.93f);

    private float _t;

    public void Reset() { _t = 0f; QueueRedraw(); }

    public void Tick(double elapsed)
    {
        _t = (float)elapsed;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var box = new Rect2(Vector2.Zero, Size);
        // 창이 '열리는' 짧은 순간 — 세로로 펼쳐진다.
        float grow = Mathf.Clamp(_t / 0.18f, 0.08f, 1f);
        var shown = new Rect2(box.Position.X, box.Position.Y + Size.Y * (1f - grow) * 0.5f,
            Size.X, Size.Y * grow);

        DrawRect(shown, Deep);
        DrawRect(shown, Line with { A = 0.9f }, false, 2f);
        if (grow < 1f) return;

        // 제목 표시줄.
        var font = ViewFont.Default;
        var bar = new Rect2(shown.Position.X, shown.Position.Y, shown.Size.X, 30f);
        DrawRect(bar, new Color(0.10f, 0.30f, 0.36f, 0.95f));
        DrawString(font, bar.Position + new Vector2(12f, 21f), Title,
            HorizontalAlignment.Left, shown.Size.X - 24f, ViewFont.S(16), Line);

        // 가운데 큰 글자 — 아주 느리게 맥동한다.
        float pulse = 0.82f + 0.18f * Mathf.Sin(_t * 3.4f);
        DrawString(font, new Vector2(0f, 104f), Big, HorizontalAlignment.Center,
            Size.X, ViewFont.S(44), new Color(0.88f, 1f, 1f, pulse));

        if (!string.IsNullOrEmpty(Sub))
            DrawString(font, new Vector2(0f, 136f), Sub, HorizontalAlignment.Center,
                Size.X, ViewFont.S(14), Line with { A = 0.7f });

        DrawRect(new Rect2(shown.Position.X + 20f, shown.Position.Y + shown.Size.Y - 22f,
            shown.Size.X - 40f, 1f), Line with { A = 0.3f });
    }
}
