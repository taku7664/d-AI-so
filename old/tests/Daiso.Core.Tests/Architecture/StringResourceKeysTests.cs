using System.Text.RegularExpressions;

namespace Daiso.Core.Tests.Architecture;

/// <summary>
/// ARCHITECTURE §6.1 — 화면 문구는 키로만 참조하고, 값의 정본은 resw 하나다.
/// 코드가 부르는 키는 resw에 있어야 하고, resw의 키는 어딘가에서 불려야 한다.
/// 12차 점검에서 "죽은 키"로 잘못 지운 16개가 switch 식 안에서 쓰이던 일이 있어, 따옴표 안 리터럴 전부를 참조로 본다.
/// </summary>
public sealed partial class StringResourceKeysTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string ResourcePath = Path.Combine(Root, "src", "Daiso.App", "Strings", "ko-KR", "Resources.resw");

    /// <summary>
    /// 문구 키를 부르는 코드가 있는 곳.
    /// <para>
    /// <c>Daiso.App</c> 뿐이 아니다 — Provider 가 <c>AuthNote</c> 로 문구 키를 내놓기 때문이다
    /// (docs/REVIEW_BACKLOG.md A6). App 만 훑으면 그 키들이 전부 "죽은 키"로 잡힌다.
    /// </para>
    /// </summary>
    private static readonly string SourceRoot = Path.Combine(Root, "src");

    [Fact]
    public void Every_key_the_app_refers_to_exists_in_the_resw()
    {
        var defined = DefinedKeys();
        var (referenced, prefixes) = ReferencedKeys();

        var missing = referenced
            .Where(key => !defined.Contains(key))
            .Where(key => !prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(because: "코드가 부르는 문구 키는 resw에 있어야 한다. 없으면 화면에 키 이름이 그대로 보인다");
    }

    [Fact]
    public void Every_resw_key_is_referenced_somewhere()
    {
        var defined = DefinedKeys();
        var (referenced, prefixes) = ReferencedKeys();

        var dead = defined
            .Where(key => !referenced.Contains(key))
            .Where(key => !prefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        dead.Should().BeEmpty(because: "쓰지 않는 문구는 지우고, 동적으로 조합하는 키는 코드에 \"접두어_\" 리터럴을 남긴다");
    }

    private static HashSet<string> DefinedKeys()
    {
        var text = File.ReadAllText(ResourcePath);
        return DataNamePattern().Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>참조된 키와, "PresetCategory_"처럼 뒤에 값을 붙여 쓰는 접두어.</summary>
    private static (HashSet<string> Keys, HashSet<string> Prefixes) ReferencedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var prefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            foreach (Match match in XamlKeyPattern().Matches(text))
            {
                keys.Add(match.Groups[1].Value);
            }

            foreach (Match match in LiteralPattern().Matches(text))
            {
                var literal = match.Groups[1].Value;

                if (literal.EndsWith('_'))
                {
                    prefixes.Add(literal);
                }
                else if (LooksLikeKey(literal))
                {
                    keys.Add(literal);
                }
            }
        }

        return (keys, prefixes);
    }

    /// <summary>키는 `대문자로 시작_그다음` 꼴이고 소문자가 하나는 있다. `DAISO_PROFILES_DIR` 같은 환경 변수는 빠진다.</summary>
    private static bool LooksLikeKey(string literal) =>
        KeyShapePattern().IsMatch(literal) && literal.Any(char.IsLower);

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(SourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
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

        throw new InvalidOperationException("Daiso.sln 이 있는 저장소 루트를 찾지 못했다");
    }

    [GeneratedRegex("""<data name="([A-Za-z0-9_]+)" xml:space="preserve">""")]
    private static partial Regex DataNamePattern();

    [GeneratedRegex(@"\{loc:Str Key=([A-Za-z0-9_]+)\}")]
    private static partial Regex XamlKeyPattern();

    [GeneratedRegex(@"""([A-Za-z][A-Za-z0-9]*_[A-Za-z0-9_]*)""")]
    private static partial Regex LiteralPattern();

    [GeneratedRegex(@"^[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+$")]
    private static partial Regex KeyShapePattern();
}
