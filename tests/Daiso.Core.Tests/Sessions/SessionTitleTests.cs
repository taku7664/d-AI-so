using Daiso.Core.Sessions;

namespace Daiso.Core.Tests.Sessions;

/// <summary>
/// <see cref="SessionTitle.Clean"/> — 세션 한 줄 제목 정제 (docs/TERMINAL_CARD_PLAN.md §6 Stage 0).
/// <para>
/// 처음에는 App(<c>SessionsViewModel.CleanPrompt</c>)의 동작을 그대로 고정하는 특성화 테스트로 시작했고
/// (0-1), Core로 옮긴 뒤(0-2) 결함 둘을 고치면서(0-3) 아래 표시된 기댓값을 뒤집었다.
/// 그 커밋의 diff가 곧 "화면에서 무엇이 달라졌는가"의 전문이다.
/// </para>
/// </summary>
public sealed class SessionTitleTests
{
    // ── 옮겨 오기 전에도 옳던 동작 ──────────────────────────────────────

    [Fact]
    public void 슬래시_명령_원문에서_태그를_걷어내고_속살만_남긴다()
    {
        var raw = "<command-name>/goal</command-name>\n"
            + "<command-message>goal</command-message>\n"
            + "<command-args>착수</command-args>";

        SessionTitle.Clean(raw).Should().Be("/goal goal 착수");
    }

    [Fact]
    public void 여러_줄을_한_줄로_만든다()
    {
        SessionTitle.Clean("첫 줄\n둘째 줄\r\n셋째 줄").Should().Be("첫 줄 둘째 줄 셋째 줄");
    }

    [Fact]
    public void 연속된_공백을_한_칸으로_줄이고_앞뒤를_턴다()
    {
        SessionTitle.Clean("   앞뒤   공백이   많다   ").Should().Be("앞뒤 공백이 많다");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t ")]
    public void 빈_프롬프트는_빈_문자열이다(string? raw)
    {
        // 폴백 문구("(첫 메시지 없음)")는 부르는 쪽이 정한다. 여기서 문구를 만들지 않는다
        SessionTitle.Clean(raw).Should().BeEmpty();
    }

    [Fact]
    public void 닫히는_태그는_정상적으로_건너뛴다()
    {
        SessionTitle.Clean("앞 <tag> 뒤").Should().Be("앞 뒤");
    }

    [Fact]
    public void 길이를_자르지_않는다()
    {
        // 자르기는 부르는 쪽의 몫이다(터미널 카드는 80자, 세션 목록은 안 자른다).
        // 이 함수가 자르기 시작하면 두 화면이 다시 갈라진다
        var raw = new string('가', 500);

        SessionTitle.Clean(raw).Should().HaveLength(500);
    }

    [Fact]
    public void 속성이_붙은_태그도_통째로_건너뛴다()
    {
        SessionTitle.Clean("<a href=\"x\">링크</a>").Should().Be("링크");
    }

    // ── Stage 0-3 에서 고친 것: 아래 셋은 0-1 에서 반대 기댓값으로 박혀 있었다 ──────

    [Fact]
    public void 제어문자와_ANSI_이스케이프를_지운다()
    {
        // 고치기 전: ESC 가 그대로 남아 화면에 네모(□)로 나왔다
        const char esc = (char)0x1b;

        SessionTitle.Clean($"안녕{esc}[31m세상").Should().Be("안녕세상");
    }

    [Fact]
    public void 끝까지_안_닫힌_이스케이프는_나머지를_버린다()
    {
        const char esc = (char)0x1b;

        SessionTitle.Clean($"앞{esc}[31").Should().Be("앞");
    }

    [Fact]
    public void 본문에_나온_여는_꺾쇠는_살려_둔다()
    {
        // 고치기 전: 닫는 '>' 가 없으면 문자열 끝까지 버려 "if (a" 만 남았다.
        // 코드 얘기로 시작한 세션이 이걸로 잘렸다
        SessionTitle.Clean("if (a < b) return;").Should().Be("if (a < b) return;");
    }

    [Fact]
    public void 비교_연산자_두_개를_태그로_오해하지_않는다()
    {
        // 고치기 전: '<' 로 열고 '>' 로 닫힌 것처럼 보여 그 사이가 통째로 사라져 "a d 이다" 가 됐다
        SessionTitle.Clean("a < b 이고 c > d 이다").Should().Be("a < b 이고 c > d 이다");
    }

    [Fact]
    public void 여는_꺾쇠_뒤가_글자가_아니면_태그가_아니다()
    {
        SessionTitle.Clean("하트 <3 이다").Should().Be("하트 <3 이다");
    }
}
