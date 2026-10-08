using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Prologue;
using NSP.View;

namespace NSP.Debug;

// 스토리 컷인 공통 프레임워크 검증(Phase 1).
//
//   godot --path . res://scenes/debug/StoryCutinTest.tscn --quit-after 60000
//
// 눈으로 볼 때는 그냥 실행하면 된다 — 토끼 1인 → 고양이↔강아지 2인 → 표정 → 폴백 순으로
// 돌아가며, 각 단계에서 자동으로 넘긴다(사람이 클릭해도 같은 결과가 나와야 한다).
//
// 대본은 검증용 더미 문장이다. DAY0 실제 대사가 아니다.
public partial class StoryCutinTest : Node
{
    private int _pass, _fail;
    private StoryCutinHud _hud;
    private StoryCutinDirector _dir;

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        GD.Print("\n\n################ 스토리 컷인 — Phase 1 ################");

        // 메인 씬 없이 컷인만 올린다(컷인은 3D 제어실에 의존하지 않아야 한다).
        _hud = new StoryCutinHud();
        AddChild(_hud);
        _dir = new StoryCutinDirector();
        AddChild(_dir);
        // 근무 중 정지가 실제로 걸리는지 보려면 Live 단계여야 한다.
        GameState.Instance?.SetPhase(GamePhase.Live);
        await Frames(3);

        Check(StoryCutinHud.Instance == _hud, "StoryCutinHud.Instance 가 잡혔다");
        Check(StoryCutinDirector.Instance == _dir, "StoryCutinDirector.Instance 가 잡혔다");
        Check(!StoryCutinDirector.PausesGameplay, "시작 전에는 근무가 멈춰 있지 않다");
        Check(!StoryCutinDirector.SuppressesAmbientDialogue, "시작 전에는 엿들은 대화가 멈춰 있지 않다");

        await LayoutRegression();
        await SinglePortrait();
        await TwoPortraits();
        await StagingMoves();
        await ExpressionSwitch();
        await ExpressionFallback();
        await SkipRules();
        await PhoneGuard();
        await PhoneStanding();

        GD.Print($"\n################ 통과 {_pass} · 실패 {_fail} ################\n");
        if (_fail > 0) GD.PushError($"StoryCutinTest: {_fail}건 실패");
        await Frames(2);
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }

    // ── ① 기존 화면의 스탠딩 크기 · 위치가 바뀌지 않았는가 ───────────────
    //
    // 추출 전 InterviewCCTVView 안에 있던 식을 여기에 그대로 적어 두고, 공용 유틸의
    // 결과와 한 픽셀도 다르지 않은지 여섯 명 전부 비교한다.
    private async Task LayoutRegression()
    {
        GD.Print("\n── ① 배치 계산 회귀(InterviewCCTVView · ScheduleStaffView) ──");
        var sim = FacilitySimulation.Instance;
        Check(sim != null, "FacilitySimulation 오토로드가 있다");
        if (sim == null) return;

        // 인터뷰 화면의 실제 값: Frame = (28,56,744,460) → 표시 영역 (744,460).
        var box = new Vector2(744, 460);
        const float topMargin = 12f, zoom = 1.45f;

        // 추출 전 PortraitUnit: 가장 큰 원화 기준 공통 배율(한 번 구해 캐시).
        float avail = box.Y - topMargin;
        float tallestOld = 1f;
        foreach (string id in sim.GetEmployeeIds())
        {
            var t = sim.GetEmployeeDef(id)?.StandingImage;
            if (t != null) tallestOld = Mathf.Max(tallestOld, OldContentBox(t).Size.Y);
        }
        float unitOld = avail / Mathf.Max(1f, tallestOld);
        Check(Mathf.IsEqualApprox(unitOld, StandingPortraitLayout.PortraitUnit(avail)),
            $"공통 배율이 같다 (old={unitOld:F6} new={StandingPortraitLayout.PortraitUnit(avail):F6})");

        int same = 0, total = 0;
        foreach (string id in sim.GetEmployeeIds().OrderBy(x => x))
        {
            var def = sim.GetEmployeeDef(id);
            var tex = def?.StandingImage;
            if (tex == null) continue;
            total++;

            // 추출 전 ContentBox 와 같은 결과인가(알파 임계 24 · 캐시).
            var oldBox = OldContentBox(tex);
            var newBox = StandingPortraitLayout.ContentBox(tex);
            bool boxSame = oldBox == newBox;

            // 추출 전 ApplyPortrait 의 식 그대로.
            float unit = unitOld * zoom;
            float drop = avail * (zoom - 1f);
            var oldSize = new Vector2(tex.GetWidth() * unit, tex.GetHeight() * unit);
            var oldPos = new Vector2(
                box.X / 2f - (oldBox.Position.X + oldBox.Size.X / 2f) * unit,
                box.Y - (oldBox.Position.Y + oldBox.Size.Y) * unit + drop - def.InterviewPortraitLift);

            var (newSize, newPos) = StandingPortraitLayout.Place(
                tex, box, topMargin, zoom, def.InterviewPortraitLift);

            bool ok = boxSame
                      && oldSize.IsEqualApprox(newSize)
                      && oldPos.IsEqualApprox(newPos);
            if (ok) same++;
            else GD.Print($"   ! {id}: box {oldBox}→{newBox}  size {oldSize}→{newSize}  pos {oldPos}→{newPos}");
        }
        Check(total == 6, $"스탠딩 원화가 여섯 장 모두 있다 ({total})");
        Check(same == total, $"여섯 명 모두 크기 · 위치 · 그림영역이 추출 전과 같다 ({same}/{total})");

        // ScheduleStaffView 는 ContentBox 만 쓴다 — 그 값이 같으면 표시도 같다.
        Check(same == total, "ScheduleStaffView 가 쓰는 ContentBox 결과도 같다(같은 측정값)");
        await Frames(1);
    }

    // 추출 전 InterviewCCTVView.Measure 와 같은 알파 기반 측정(비교 기준).
    private static Rect2I OldContentBox(Texture2D tex)
    {
        var img = tex.GetImage();
        if (img == null) return new Rect2I(0, 0, tex.GetWidth(), tex.GetHeight());
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        byte[] data = img.GetData();
        int w = img.GetWidth(), h = img.GetHeight();
        if (data == null || data.Length < w * h * 4) return new Rect2I(0, 0, w, h);

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
        if (maxX < 0) return new Rect2I(0, 0, w, h);
        return new Rect2I(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ── ② 토끼 1인 컷인 ──────────────────────────────────────────────
    private async Task SinglePortrait()
    {
        GD.Print("\n── ② 토끼 1인 컷인 ──");
        var beat = new StoryBeat { BeatId = "test_single" }
            .Add("rabbit", "관리자님, 이쪽 작업이 멈췄어요. (검증용 문장)")
            .Add("rabbit", "자재가 부족한 것 같은데요? (검증용 문장)");

        var task = _dir.Play(beat);
        await Frames(4);

        Check(_dir.IsPlaying, "컷인이 돌고 있다");
        Check(_hud.IsShown, "컷인 화면이 떠 있다");
        Check(StoryCutinDirector.PausesGameplay, "근무 시간이 멈췄다");
        Check(StoryCutinDirector.SuppressesAmbientDialogue, "엿들은 대화가 멈췄다");
        Check(_hud.LeftPortrait.EmployeeId == "rabbit", "왼쪽에 토끼가 섰다");
        Check(_hud.RightPortrait.EmployeeId == "", "오른쪽은 비어 있다");
        Check(_hud.LeftPortrait.CurrentTexture != null, "토끼 원화가 걸렸다");
        Check(_hud.CurrentSpeakerName == (FacilitySimulation.Instance?.GetEmployeeDef("rabbit")?.Codename ?? "rabbit"),
            $"이름이 코드네임으로 떴다 ({_hud.CurrentSpeakerName})");
        Check(EmployeeMouthAnimator.Speaker == "rabbit", "입 모양의 화자가 토끼다");

        // 입이 실제로 움직이는가 — 몇 프레임 동안 프레임 값이 바뀐다.
        var seen = new System.Collections.Generic.HashSet<GuideMouthFrame>();
        for (int i = 0; i < 40; i++) { seen.Add(EmployeeMouthAnimator.Frame); await Frames(1); }
        Check(seen.Count >= 2, $"말하는 동안 입 모양이 바뀐다 ({seen.Count}종)");

        await AdvanceAll(task, 2);
        Check(!_dir.IsPlaying, "마지막 줄 뒤 컷인이 끝났다");
        Check(!_hud.IsShown, "fade out 후 화면이 걷혔다");
        Check(!StoryCutinDirector.PausesGameplay, "종료 후 근무 시간이 다시 흐른다");
        Check(!StoryCutinDirector.SuppressesAmbientDialogue, "종료 후 엿들은 대화가 복원됐다");
        Check(EmployeeMouthAnimator.Speaker == "", "종료 후 전역 입 모양 상태가 비었다");
        Check(ControlRoom3DController.Instance?.IsInputLocked != true, "종료 후 입력 잠금이 풀렸다");
    }

    // ── ③ 고양이 ↔ 강아지 2인 ────────────────────────────────────────
    private async Task TwoPortraits()
    {
        GD.Print("\n── ③ 고양이 ↔ 강아지 2인 대화 ──");
        var beat = new StoryBeat { BeatId = "test_pair" }
            .Add("cat", "더 만들어도 둘 데가 없는데요. (검증용 문장)", CutinSide.Left)
            .Add("dog", "그럼 저장고부터 봐야 하지 않을까요? (검증용 문장)", CutinSide.Right)
            .Add("cat", "그러니까 그 얘기를 했잖아요. (검증용 문장)", CutinSide.Left);

        var task = _dir.Play(beat);
        await Frames(4);

        Check(_hud.LeftPortrait.EmployeeId == "cat", "왼쪽 = 고양이");
        Check(EmployeeMouthAnimator.Speaker == "cat", "첫 줄의 화자는 고양이");
        Check(_hud.LeftPortrait.IsSpeaking, "고양이만 말하고 있다");
        Check(!_hud.RightPortrait.IsSpeaking, "강아지는 아직 서 있지도 않다(말하지 않음)");

        // 강아지 차례 — 화자가 바뀌면 입도 함께 바뀌어야 한다.
        await Advance(2);
        await Frames(3);
        Check(_hud.RightPortrait.EmployeeId == "dog", "오른쪽 = 강아지");
        Check(_hud.LeftPortrait.EmployeeId == "cat", "고양이는 왼쪽에 그대로 서 있다");
        Check(EmployeeMouthAnimator.Speaker == "dog", "화자가 강아지로 넘어갔다");
        Check(_hud.RightPortrait.IsSpeaking, "강아지가 말하고 있다");
        Check(!_hud.LeftPortrait.IsSpeaking, "고양이는 말하지 않는다(동시 발화 없음)");

        // 듣는 쪽은 닫은 입이어야 한다.
        var catClosed = EmployeeArt.Get("cat", _hud.LeftPortrait.Expression, GuideMouthFrame.Closed);
        Check(_hud.LeftPortrait.CurrentTexture == catClosed, "듣는 고양이는 닫은 입 원화다");

        // 다시 고양이 차례.
        await Advance(2);
        await Frames(3);
        Check(EmployeeMouthAnimator.Speaker == "cat", "화자가 다시 고양이로 돌아왔다");
        Check(_hud.LeftPortrait.IsSpeaking && !_hud.RightPortrait.IsSpeaking,
            "교대 후에도 말하는 쪽만 입이 움직인다");
        var dogClosed = EmployeeArt.Get("dog", _hud.RightPortrait.Expression, GuideMouthFrame.Closed);
        Check(_hud.RightPortrait.CurrentTexture == dogClosed, "듣는 강아지는 닫은 입 원화다");

        await AdvanceAll(task, 2);
        Check(!_dir.IsPlaying, "2인 컷인이 정상 종료됐다");
    }

    // ── ③-B 등장 · 퇴장 동선 ─────────────────────────────────────────
    //
    // 혼자면 정중앙, 둘이 되면 먼저 있던 쪽이 왼쪽으로 비켜 주고 오른쪽이 등장,
    // 한 명이 빠지면 남은 한 명이 다시 정중앙으로. 이동은 전부 트윈 · 등장/퇴장은 페이드.
    private async Task StagingMoves()
    {
        GD.Print("\n── ③-B 등장 · 퇴장 동선 ──");
        var beat = new StoryBeat { BeatId = "test_staging" }
            .Add("rabbit", "혼자 서 있는 줄입니다.", CutinSide.Left)
            .Add("wolf", "둘이 되는 줄입니다.", CutinSide.Right)
            .Add("rabbit", "다시 혼자가 되는 줄입니다.", CutinSide.Left, exitSide: CutinSide.Right);

        var task = _dir.Play(beat);
        await Frames(4);

        float screenW = _hud.LeftPortrait.GetParent<Control>().Size.X;
        float Center(EmployeeStandingPortrait p) => p.Position.X + p.Size.X / 2f;

        // ① 혼자 — 정중앙
        float solo = Center(_hud.LeftPortrait);
        Check(Mathf.Abs(solo - screenW / 2f) < 2f,
            $"혼자일 때 정중앙에 선다 (중심 {solo:F0} / 화면중앙 {screenW / 2f:F0})");
        Check(_hud.RightPortrait.Modulate.A < 0.02f, "오른쪽은 아직 투명하다");

        // ② 둘 — 왼쪽은 왼쪽으로 비켜 주고, 오른쪽이 페이드로 등장
        await Advance(2);
        await Frames(2);
        float rightAtEnter = Center(_hud.RightPortrait);
        Check(_hud.RightPortrait.Modulate.A < 0.5f, "오른쪽은 페이드 도중이다(갑자기 뜨지 않는다)");
        Check(rightAtEnter > screenW * 0.6f, $"오른쪽은 처음부터 제자리에 있다(미끄러져 들어오지 않는다) ({rightAtEnter:F0})");

        // 이동 · 페이드가 끝날 때까지(헤드리스는 프레임당 시간이 짧아 프레임 수로 못 센다).
        await Settle(() => _hud.RightPortrait.Modulate.A > 0.98f
                           && Mathf.Abs(Center(_hud.LeftPortrait) - screenW * 0.30f) < 1f);
        float l = Center(_hud.LeftPortrait), r = Center(_hud.RightPortrait);
        Check(l < screenW / 2f - 20f, $"왼쪽이 중앙에서 왼쪽으로 비켜섰다 ({solo:F0} → {l:F0})");
        Check(r > screenW / 2f + 20f, $"오른쪽이 오른편에 섰다 ({r:F0})");
        Check(Mathf.Abs((screenW - r) - l) < 4f, "좌우가 화면 중앙 기준으로 대칭이다");
        Check(_hud.RightPortrait.Modulate.A > 0.98f, "오른쪽 페이드인이 끝났다");

        // ③ 한 명 퇴장 — 페이드로 사라지고 남은 한 명이 정중앙으로
        await Advance(2);
        await Frames(2);
        Check(_hud.RightPortrait.Modulate.A < 1f, "퇴장이 페이드로 시작됐다");
        await Settle(() => _hud.RightPortrait.Modulate.A < 0.02f
                           && Mathf.Abs(Center(_hud.LeftPortrait) - screenW / 2f) < 2f);
        float back = Center(_hud.LeftPortrait);
        Check(Mathf.Abs(back - screenW / 2f) < 2f, $"남은 한 명이 정중앙으로 돌아왔다 ({l:F0} → {back:F0})");
        Check(_hud.RightPortrait.Modulate.A < 0.02f, "퇴장한 쪽은 완전히 사라졌다");
        Check(_hud.RightPortrait.EmployeeId == "", "퇴장한 자리가 비었다");

        await AdvanceAll(task, 1);
    }

    // ── ④ smile ↔ bad 표정 ──────────────────────────────────────────
    private async Task ExpressionSwitch()
    {
        GD.Print("\n── ④ 표정 지정(smile ↔ bad) ──");
        var beat = new StoryBeat { BeatId = "test_expr" }
            .Add("rabbit", "검증용 문장입니다.", CutinSide.Left, "smile")
            .Add("rabbit", "검증용 문장입니다.", CutinSide.Left, "bad");

        var task = _dir.Play(beat);
        await Frames(4);
        Check(_hud.LeftPortrait.Expression == "smile", $"지정한 smile 이 적용됐다 ({_hud.LeftPortrait.Expression})");
        Check(EmployeeMouthAnimator.Expression == "smile", "입 모양 쪽 표정도 smile 이다");
        var smileTex = _hud.LeftPortrait.CurrentTexture;

        await Advance(2);
        await Frames(3);
        Check(_hud.LeftPortrait.Expression == "bad", $"지정한 bad 로 바뀌었다 ({_hud.LeftPortrait.Expression})");
        Check(EmployeeMouthAnimator.Expression == "bad", "입 모양 쪽 표정도 bad 다");
        Check(_hud.LeftPortrait.CurrentTexture != smileTex, "원화가 실제로 교체됐다");

        await AdvanceAll(task, 2);
    }

    // ── ⑤ 없는 표정 → 안전한 폴백 ───────────────────────────────────
    private async Task ExpressionFallback()
    {
        GD.Print("\n── ⑤ 없는 expression 폴백 ──");
        Check(EmployeeArt.ResolveExpression("rabbit", "normal") == EmployeeArt.DefaultExpression("rabbit"),
            "없는 표정 'normal' 은 평소 표정으로 떨어진다");
        Check(EmployeeArt.ResolveExpression("rabbit", "") == EmployeeArt.DefaultExpression("rabbit"),
            "빈 표정은 평소 표정이다");
        Check(EmployeeArt.ResolveExpression("rabbit", "smile") == "smile", "있는 표정은 그대로 쓴다");

        var beat = new StoryBeat { BeatId = "test_fallback" }
            .Add("sheep", "검증용 문장입니다.", CutinSide.Left, "does_not_exist");

        var task = _dir.Play(beat);
        await Frames(4);
        Check(_hud.LeftPortrait.Expression == EmployeeArt.DefaultExpression("sheep"),
            $"없는 표정을 줘도 평소 표정으로 선다 ({_hud.LeftPortrait.Expression})");
        Check(_hud.LeftPortrait.CurrentTexture != null, "원화가 비지 않았다(검은 화면 없음)");

        await AdvanceAll(task, 1);
    }

    // ── ⑥ 1차 클릭 = 문장 완성 / 2차 클릭 = 다음 줄 ─────────────────
    private async Task SkipRules()
    {
        GD.Print("\n── ⑥ 넘기기 규칙(문서 §33) ──");
        var beat = new StoryBeat { BeatId = "test_skip" }
            .Add("fox", "꽤 긴 검증용 문장입니다. 타이핑이 끝나기 전에 눌러 보기 위해 일부러 길게 적어 둡니다.")
            .Add("fox", "두 번째 줄입니다.");

        var task = _dir.Play(beat);
        await Frames(3);
        Check(_hud.IsTyping, "아직 타이핑 중이다");
        Check(_hud.IsWaitingForInput, "입력을 기다리고 있다");

        // 1차 — 문장 즉시 완성. 줄은 넘어가지 않는다.
        string before = _hud.CurrentText;
        int advBefore = _hud.AdvanceCount;
        _hud.RequestAdvance();
        await Frames(1);
        Check(!_hud.IsTyping, "1차 클릭에 문장이 즉시 완성됐다");
        Check(_hud.CurrentText == before, "아직 같은 줄이다(넘어가지 않았다)");
        Check(_hud.AdvanceCount == advBefore, "넘긴 횟수가 늘지 않았다");
        Check(!EmployeeMouthAnimator.Talking, "완성과 함께 입이 멈췄다");

        // 2차 — 다음 줄.
        _hud.RequestAdvance();
        await Frames(3);
        Check(_hud.AdvanceCount == advBefore + 1, "2차 클릭에 줄이 넘어갔다");
        Check(_hud.CurrentText != before, $"다음 줄이 떴다 ({_hud.CurrentText})");

        // PrologueAdvanceInput 경로도 같은 창을 본다.
        Check(StoryCutinHud.Instance.IsWaitingForInput,
            "PrologueAdvanceInput 이 보는 IsWaitingForInput 이 켜져 있다");

        await AdvanceAll(task, 2);
        Check(!_hud.IsShown, "마지막 줄 뒤 fade out 됐다");
    }

    // ── ⑦ 통화 중에는 컷인을 띄우지 않는다 ──────────────────────────
    private async Task PhoneGuard()
    {
        GD.Print("\n── ⑦ 전화 충돌 가드 ──");
        Check(_dir.CanPlay, "평소에는 컷인을 띄울 수 있다");

        // 실제 통화창을 열어 가드를 확인한다(열 수 없는 환경이면 건너뛴다).
        var phone = new PhoneCallHud();
        AddChild(phone);
        await Frames(2);
        bool opened = false;
        try
        {
            phone.Open("rabbit");
            await Frames(2);
            opened = phone.IsOpen;
        }
        catch (System.Exception e)
        {
            GD.Print($"   (통화창을 열 수 없어 가드 검사를 코드 경로로만 확인합니다: {e.GetType().Name})");
        }

        if (opened)
        {
            Check(!_dir.CanPlay, "통화 중에는 컷인을 띄울 수 없다");
            Check(_dir.BlockedReason().Contains("통화"), $"거부 사유가 통화다 ({_dir.BlockedReason()})");
            bool played = await _dir.Play(new StoryBeat { BeatId = "test_blocked" }
                .Add("rabbit", "이 줄은 떠서는 안 됩니다."));
            Check(!played, "Play 가 false 로 거부했다");
            Check(!_hud.IsShown, "통화 위에 컷인이 겹치지 않았다");
            phone.RequestClose();
            await Frames(3);
            Check(_dir.CanPlay, "통화를 끊으면 다시 컷인을 띄울 수 있다");
        }
        else
        {
            Check(_dir.BlockedReason() == "", "통화가 없으면 거부 사유가 없다");
        }

        // 통화창은 지우지 않고 닫힌 채로 둔다. PhoneCallHud 는 static Instance 를 비우지
        // 않으므로(기존 구조), 지우면 죽은 참조가 남아 검사 환경이 실제와 달라진다.
        await Frames(2);

        // 페일세이프 — 재생 중 Abort 해도 남는 것이 없어야 한다.
        GD.Print("\n── ⑧ 중단 페일세이프 ──");
        var task = _dir.Play(new StoryBeat { BeatId = "test_abort" }
            .Add("wolf", "중단 검사용 문장입니다.")
            .Add("wolf", "여기까지 가지 않아야 합니다."));
        await Frames(4);
        Check(_dir.IsPlaying && StoryCutinDirector.PausesGameplay, "컷인이 돌며 근무가 멈춰 있다");
        _dir.Abort();
        await Frames(3);
        await task;
        Check(!_dir.IsPlaying, "Abort 후 재생 상태가 꺼졌다");
        Check(!_hud.IsShown, "Abort 후 화면이 걷혔다");
        Check(!StoryCutinDirector.PausesGameplay, "Abort 후 근무 정지가 풀렸다");
        Check(!StoryCutinDirector.SuppressesAmbientDialogue, "Abort 후 엿들은 대화가 복원됐다");
        Check(ControlRoom3DController.Instance?.IsInputLocked != true, "Abort 후 입력 잠금이 풀렸다");
        Check(EmployeeMouthAnimator.Speaker == "", "Abort 후 전역 입 모양이 비었다");
    }

    // ── ⑨ 통화 중 입 모양 ───────────────────────────────────────────
    //
    // 통화창의 2D 스탠딩은 **없앴다.** 전화는 목소리만 오는 수단이고, 관리자는 상대를
    // 보지 못한다 — 그게 이 게임에서 전화와 CCTV 가 다른 이유다. 그래서 여기서 보는 것은
    // "스탠딩이 뜨는가"가 아니라 "스탠딩 없이도 입 모양 · 화자 · 컷인 차단이 그대로인가"다.
    private async Task PhoneStanding()
    {
        GD.Print("\n── ⑨ 통화 중 입 모양(Phase 2) ──");
        var phone = PhoneCallHud.Instance;
        if (phone == null || !IsInstanceValid(phone))
        {
            GD.Print("   (PhoneCallHud 가 없어 건너뜁니다)");
            return;
        }

        var field = typeof(PhoneCallHud).GetField("_standing",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Check(field == null, "통화창에는 스탠딩 자리가 없다");

        // 말하는 동안 입 애니메이션 → 문장이 끝나면 닫은 입.
        phone.Open("rabbit");
        await Frames(4);
        Check(EmployeeMouthAnimator.Speaker == "rabbit", "입 모양의 화자가 토끼다");

        var frames = new System.Collections.Generic.HashSet<GuideMouthFrame>();
        for (int i = 0; i < 40; i++) { frames.Add(EmployeeMouthAnimator.Frame); await Frames(1); }
        Check(frames.Count >= 2, $"말하는 동안 입 모양이 바뀐다 ({frames.Count}종)");

        for (int i = 0; i < 400 && EmployeeMouthAnimator.Talking; i++) await Frames(1);
        await Frames(3);
        Check(!EmployeeMouthAnimator.Talking, "문장이 끝나면 입이 멈춘다");
        Check(EmployeeMouthAnimator.Frame == GuideMouthFrame.Closed, "문장 종료 시 닫은 입이다");

        // 통화 중에는 컷인이 막힌다(Phase 1 규칙 유지).
        Check(!_dir.CanPlay, "통화 중에는 Story Cut-in 이 열리지 않는다");

        phone.RequestClose();
        await Settle(() => EmployeeMouthAnimator.Speaker == "");
        Check(EmployeeMouthAnimator.Speaker == "", "통화 종료 후 전역 입 모양이 비었다");

        // 전화가 끝나면 컷인을 다시 쓸 수 있다.
        Check(_dir.CanPlay, "통화 종료 후 Story Cut-in 을 다시 쓸 수 있다");
        bool played = await _dir.Play(new StoryBeat { BeatId = "test_after_call" }
            .Add("fox", "통화 뒤 컷인 확인용 문장입니다.", CutinSide.Left, "", 0.1));
        Check(played, "통화 뒤 컷인이 정상 재생됐다");
        await Settle(() => !_dir.IsPlaying);
        Check(!_dir.IsPlaying, "그 컷인도 정상 종료됐다");
    }

    // --- 헬퍼 -------------------------------------------------------------

    // 한 줄을 넘긴다(필요하면 타이핑 완성까지 두 번).
    private async Task Advance(int clicks)
    {
        for (int i = 0; i < clicks; i++)
        {
            _hud.RequestAdvance();
            await Frames(1);
        }
    }

    // 남은 줄을 끝까지 넘기고 묶음이 닫히기를 기다린다.
    private async Task AdvanceAll(Task<bool> task, int remainingLines)
    {
        for (int i = 0; i < remainingLines + 1 && !task.IsCompleted; i++)
        {
            await Advance(2);
            await Frames(2);
        }
        // fade out 이 끝나기를 기다린다.
        for (int i = 0; i < 90 && !task.IsCompleted; i++) await Frames(1);
        await task;
        await Frames(2);
    }

    private async Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    // 트윈이 끝나기를 기다린다. 헤드리스는 프레임당 실제 시간이 짧아 "몇 프레임"으로는
    // 셀 수 없다 — 조건이 설 때까지(또는 충분히 오래) 본다.
    private async Task Settle(System.Func<bool> done, int maxFrames = 1200)
    {
        for (int i = 0; i < maxFrames && !done(); i++) await Frames(1);
    }

    private void Check(bool ok, string label)
    {
        if (ok) { _pass++; GD.Print($"  OK   {label}"); }
        else { _fail++; GD.Print($"  FAIL {label}"); }
    }
}
