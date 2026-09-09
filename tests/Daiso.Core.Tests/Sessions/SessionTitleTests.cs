using Daiso.Core.Sessions;

namespace Daiso.Core.Tests.Sessions;

/// <summary>
/// <see cref="SessionTitle.Clean"/>의 <b>특성화 테스트</b>.
/// <para>
/// 이 파일은 "이렇게 되어야 한다"가 아니라 <b>"지금 이렇게 된다"</b>를 고정한다.
/// 정제 로직을 App(<c>SessionsViewModel.CleanPrompt</c>)에서 Core로 옮기는 동안 동작이
/// 한 글자도 안 바뀌었음을 증명하는 것이 목적이다 (docs/TERMINAL_CARD_PLAN.md §6 Stage 0-1·0-2).
/// </para>
/// <para>
/// <b>아래 "알려진 결함" 두 건은 일부러 통과하도록 적혀 있다.</b> Stage 0-3에서 고칠 때
/// 그 기댓값을 뒤집어야 하며, 그 diff가 곧 "화면에서 무엇이 달라지는가"의 전문이다.
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

    // ── 알려진 결함: 지금 동작을 고정한다 (Stage 0-3에서 뒤집는다) ──────────

    [Fact]
    public void 결함_제어문자와_ANSI_이스케이프가_그대로_살아남는다()
    {
        // char.IsWhiteSpace 는 ESC(U+001B)를 안 잡는다. 그래서 화면에 네모(□)로 나온다.
        // Stage 0-3 기댓값: "안녕 세상" 또는 "안녕세상" — 제어문자가 사라져야 한다
        const char esc = (char)0x1b;
        var raw = $"안녕{esc}[31m세상";

        SessionTitle.Clean(raw).Should().Be($"안녕{esc}[31m세상");
    }

    [Fact]
    public void 결함_본문에_나온_여는_꺾쇠가_뒤를_통째로_삼킨다()
    {
        // '<' 에서 depth++ 하고 닫는 '>' 가 없으면 문자열 끝까지 버린다.
        // 코드 얘기로 시작한 세션이 이걸로 잘린다.
        // Stage 0-3 기댓값: "if (a < b) return;" 이 그대로 남아야 한다
        SessionTitle.Clean("if (a < b) return;").Should().Be("if (a");
    }

    [Fact]
    public void 결함_비교_연산자_두_개가_있으면_사이를_태그로_오해한다()
    {
        // '<' 로 열고 '>' 로 닫힌 것처럼 보여 그 사이가 통째로 사라진다
        SessionTitle.Clean("a < b 이고 c > d 이다").Should().Be("a d 이다");
    }
}
