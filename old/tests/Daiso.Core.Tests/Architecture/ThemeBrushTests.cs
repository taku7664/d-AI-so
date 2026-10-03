using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 코드가 <c>Application.Current.Resources["…Brush"]</c> 로 테마 브러시를 직접 꺼내지 않는지.
///
/// <para>
/// 이 앱은 테마를 창의 <c>RootGrid.RequestedTheme</c> 에 입힌다(<c>ShellWindow.ApplyTheme</c>).
/// 그래서 앱 사전에서 꺼낸 브러시는 <b>앱 테마</b>로 풀리고, 다크로 두어도 라이트의 색이 나온다.
/// 게다가 그 값은 한 번 꺼내면 굳어 테마를 바꿔도 따라오지 않는다 —
/// 다크 모드에서 상태 줄 글씨가 검게 나온 일이 그것이다 (2026-09-11 사람의 지적).
/// </para>
///
/// <para>
/// 색이 아니라 <b>스타일</b>을 건넨다. 스타일 안의 <c>ThemeResource</c> 는 그 요소의 테마로 풀리고,
/// 테마가 바뀌면 다시 풀린다. 값의 정본은 <c>App.xaml</c> 한 곳이다.
/// </para>
/// </summary>
public sealed partial class ThemeBrushTests
{
    /// <summary>
    /// 꺼내도 되는 것. 테마를 타지 않는 자체 색이거나, 테마를 <b>코드가 직접 갈라</b> 고르는 자리다.
    /// 늘릴 때는 "이 이름이 라이트·다크에서 같은 값인가"를 먼저 확인한다.
    /// </summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // App.xaml 의 단색 브러시 — 테마와 무관하게 같은 색이다
        "AuthOkBrush",
        "AuthWarnBrush",
        "AuthErrorBrush",
        "PulseDotBrush",
    };

    [Fact]
    public void Code_does_not_pull_theme_brushes_out_of_the_app_dictionary()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in LookupPattern().Matches(lines[index]))
                {
                    var key = match.Groups[1].Value;

                    if (!Allowed.Contains(key))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{index + 1} — {key}");
                    }
                }
            }
        }

        offenders.Should().BeEmpty(
            because: "테마 브러시는 App.xaml 의 스타일(ThemeResource)로 입힌다. 코드가 꺼내면 앱 테마로 굳어 다크에서 라이트 색이 나온다");
    }

    /// <summary>`Application.Current.Resources["…Brush"]` 한 줄. 이름이 Brush 로 끝나는 것만 본다 — 치수·스타일은 테마를 타지 않는다.</summary>
    [GeneratedRegex(@"Application\.Current\.Resources\[\s*""([A-Za-z0-9]+Brush)""\s*\]")]
    private static partial Regex LookupPattern();

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Daiso.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("저장소 뿌리를 찾지 못했다");
    }
}
