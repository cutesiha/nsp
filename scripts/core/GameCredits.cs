namespace NSP.Core;

// 크레딧 · 외부 리소스 출처 — 한 곳에만 적는다(2026-10-31 사용자 제공).
//
//   · 엔딩 뒤 크레딧(View.CreditsRoll)은 이름만 올린다 — 링크는 넣지 않는다.
//   · 타이틀 모니터2 의 「출처」 창(View.TitleStaffIdView)은 링크까지 적는다.
//
// 리소스를 더하거나 빼면 여기 한 줄만 고치면 두 화면이 같이 바뀐다.
public static class GameCredits
{
    public const string Title = "야간근무지침";
    public const string TitleEn = "NIGHT SHIFT PROTOCOL";
    public const string Maker = "백시하";

    // 맡은 일 — 크레딧에서 "제작" 아래로 한 줄씩 올라간다.
    public static readonly (string Role, string Name)[] Roles =
    {
        ("기획", Maker),
        ("시나리오", Maker),
        ("프로그래밍", Maker),
        ("그래픽", Maker),
        ("사운드", Maker),
        ("UI / 연출", Maker),
    };

    public const string ThanksHead = "QA / Special Thanks";
    public static readonly string[] Thanks = { "백준하", "허도경", "류도훈" };

    // 외부 리소스. Url 은 출처 창에만 쓴다(https:// 는 떼고 적는다).
    public sealed record Source(string Category, string Use, string Work, string Author, string Url);

    public static readonly Source[] Sources =
    {
        new("3D 모델", "아기 괴물", "PS1 PSX Creepy Doll (Animated)", "cursedlake",
            "cursedlake.itch.io/ps1-psx-creepy-doll-animated"),
        new("3D 모델", "거미 괴물", "PS1 PSX Huntsman Spider", "cursedlake",
            "cursedlake.itch.io/ps1-psx-huntsman-spider"),
        new("3D 모델", "인간 괴물", "The Wrapped", "codyanka",
            "codyanka.itch.io/the-wrapped"),
        new("3D 모델", "직원 머리카락", "헤어 3D 모델 2종", "CGTrader",
            "cgtrader.com/items/7205705 / cgtrader.com/items/5976442"),
        new("사운드", "비명, 숨소리 등 효과음", "효과음 자료", "PGTD",
            "pgtd.tistory.com"),
        new("사운드", "메인화면 기계실 환경음", "Machine Room", "Pixabay",
            "pixabay.com/sound-effects/film-special-effects-machine-room-55632"),
        new("사운드", "환풍구 소리", "Industrial Hum Loop", "Pixabay",
            "pixabay.com/sound-effects/technology-industrial-hum-loop-248767"),
    };

    // 쓴 글꼴 — 이름만 적는다.
    public static readonly string[] Fonts = { "부크크 명조", "더 우아한 세리프", "KMU80 성곡 세리프", "날선체" };

    public const string Engine = "Made with Godot Engine";
    public const string ThankYou = "플레이해 주셔서 감사합니다.";
}
