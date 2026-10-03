using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 화면이 <b>남의 것</b>에 처리기를 걸었으면 뗄 줄도 알아야 한다.
///
/// <para>
/// 뷰모델·셸은 싱글턴이고 화면은 <c>NavigationCacheMode</c> 를 켜지 않는 한 올 때마다 새로 만들어진다.
/// 걸어 놓고 떼지 않으면 다녀온 횟수만큼 처리기가 쌓여 같은 일을 여러 번 하고(인덱싱이 끝날 때마다
/// 목록을 N 번 다시 읽었다), 떠난 화면의 비주얼 트리도 살아남는다 (2026-09-11 점검에서 다섯 화면).
/// </para>
/// <para>
/// 검사는 <c>어떤것.사건 +=</c> 처럼 <b>점이 찍힌</b> 구독만 본다. <c>Loaded +=</c> 처럼 자기 사건은
/// 화면과 함께 사라지므로 뗄 필요가 없고, 화면이 그 자리에서 만든 것(<c>var item = new …</c>)도 마찬가지다.
/// </para>
/// </summary>
public sealed partial class PageSubscriptionTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ViewsDirectory = Path.Combine(Root, "src", "Daiso.App", "Views");

    public static TheoryData<string> Pages()
    {
        var data = new TheoryData<string>();

        foreach (var path in Directory.EnumerateFiles(ViewsDirectory, "*Page.xaml.cs").Order(StringComparer.Ordinal))
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_page_unhooks_what_it_hooked(string fileName)
    {
        var codeBehind = Path.Combine(ViewsDirectory, fileName);
        var markup = codeBehind[..^3];

        // 캐시되는 화면은 한 번 만들어져 앱이 끝날 때까지 산다. 뗄 자리가 없다
        if (File.Exists(markup)
            && File.ReadAllText(markup).Contains("NavigationCacheMode=\"Required\"", StringComparison.Ordinal))
        {
            return;
        }

        var text = File.ReadAllText(codeBehind);

        var dangling = SubscribePattern().Matches(text)
            .Select(match => match.Groups[1].Value)
            .Where(subscription => !Owned(text, subscription))
            .Where(subscription => !Unhooked(text, subscription))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        dangling.Should().BeEmpty(
            because: $"{fileName} 이 건 처리기를 떼지 않는다. 화면은 올 때마다 새로 만들어지고 뷰모델은 하나뿐이라 그만큼 쌓인다");
    }

    /// <summary>같은 이름에 <c>-=</c> 도 있는가.</summary>
    private static bool Unhooked(string text, string subscription) =>
        Regex.IsMatch(text, Regex.Escape(subscription) + @"\s*-=", RegexOptions.CultureInvariant);

    /// <summary>
    /// 이 화면이 <b>제가 만든</b> 것에 건 것인가(<c>var item = new MenuFlyoutItem…</c>).
    /// 그것은 화면과 함께 사라지므로 뗄 필요가 없다 — 남는 것은 남의 것에 건 처리기뿐이다.
    /// </summary>
    private static bool Owned(string text, string subscription)
    {
        var target = Regex.Escape(subscription.Split('.')[0]);

        return Regex.IsMatch(text, @"\bvar\s+" + target + @"\s*=\s*new\b", RegexOptions.CultureInvariant);
    }

    /// <summary><c>무엇.사건 +=</c>. 자기 사건(<c>Loaded +=</c>)은 점이 없어 걸리지 않는다.</summary>
    [GeneratedRegex(@"([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+)\s*\+=", RegexOptions.CultureInvariant)]
    private static partial Regex SubscribePattern();

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Daiso.sln 이 있는 저장소 루트를 찾지 못했다");
    }
}
