namespace Daiso.Core.Tests;

/// <summary>글자를 반으로 쪼개지 않는다. (2026-09-11 점검 — 첫 프롬프트·요약이 이모지에서 깨졌다)</summary>
public sealed class TextCutTests
{
    [Fact]
    public void A_short_text_comes_back_whole()
    {
        TextCut.Head("짧다", 10).Should().Be("짧다");
    }

    [Fact]
    public void A_long_text_is_cut_to_the_limit()
    {
        TextCut.Head(new string('가', 300), 200).Should().HaveLength(200);
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        // 🙂 는 UTF-16 두 단위다. 5 단위에서 자르면 딱 그 사이에 걸린다
        var text = "abcd🙂efg";

        var cut = TextCut.Head(text, 5);

        cut.Should().Be("abcd", because: "짝을 끊으면 반쪽 글자가 남아 ? 로 보인다");
        cut.Should().NotContain("\ud83d");
    }

    [Fact]
    public void A_pair_that_fits_stays()
    {
        TextCut.Head("abcd🙂efg", 6).Should().Be("abcd🙂");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Nothing_in_nothing_out(string? text)
    {
        TextCut.Head(text, 10).Should().BeEmpty();
    }

    [Fact]
    public void A_zero_limit_gives_nothing()
    {
        TextCut.Head("가나다", 0).Should().BeEmpty();
    }
}
