using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// 도구를 찾다 못 찾는 것은 <b>정상</b>이다. 그러니 던지지 않는다.
///
/// <para>
/// 인덱스에는 지금 앱이 모르는 도구의 세션·계정이 남아 있을 수 있다 — 플러그인을 지웠거나,
/// 옛 기록의 id 를 읽지 못했거나. 읽는 쪽은 그것을 견디게 해 두었는데
/// (<c>SqliteSessionIndex.ReadSession</c> 은 모르는 id 를 기본값으로 넘긴다),
/// 쓰는 쪽 아홉 군데가 <c>First(p =&gt; p.Kind == …)</c> 로 예외를 던지고 있었다 —
/// 지운 도구의 세션에서 "이어서 열기"를 누르면 앱이 죽었다 (2026-09-11 점검).
/// </para>
/// <para>찾기는 <c>ProviderLookup.For</c> 하나로 한다. 없으면 null 이고, 부르는 쪽이 무엇을 보일지 정한다.</para>
/// </summary>
public sealed partial class ProviderLookupTests
{
    private static readonly string Root = FindRepositoryRoot();

    public static TheoryData<string> Layers() => new()
    {
        Path.Combine("src", "Daiso.App"),
        Path.Combine("src", "Daiso.Infrastructure"),
    };

    [Theory]
    [MemberData(nameof(Layers))]
    public void Nobody_looks_a_tool_up_with_First(string layer)
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles(Path.Combine(Root, layer)))
        {
            if (ThrowingLookup().IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            because: "모르는 도구는 예외가 아니라 빈 값이다. ProviderLookup.For 로 찾고 없을 때를 적는다");
    }

    private static IEnumerable<string> SourceFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary><c>First(… .Kind == …)</c> — 한 줄 안에 있는 것만 본다. 여러 줄로 쪼갠 것은 사람이 본다.</summary>
    [GeneratedRegex(@"\.First\([^)\n]*\.Kind\s*==", RegexOptions.CultureInvariant)]
    private static partial Regex ThrowingLookup();

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
