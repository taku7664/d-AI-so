namespace Daiso.Core;

/// <summary>
/// Claude 전용 <c>@경로</c> import 줄을 찾아낸다. Codex에는 import 문법이 없어 마이그레이션 때 전개해야 한다.
/// (REQUIREMENTS §7)
/// </summary>
public static class InstructionImports
{
    private const string FenceMarker = "```";

    /// <summary>
    /// 줄 전체가 <c>@경로</c> 하나인 줄만 import로 본다. 코드블록(``` ~ ```) 안은 건너뛴다.
    /// 순서는 파일에 나온 순서이고 중복은 한 번만 담는다.
    /// </summary>
    public static IReadOnlyList<string> Find(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in OutsideCodeFences(SplitLines(content)))
        {
            if (TryReadImport(line, out var path) && seen.Add(path))
            {
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>줄 하나가 import인지. 코드블록 판정을 하지 않으므로 <see cref="Find"/>와 함께 쓴다.</summary>
    public static bool TryReadImport(string line, out string path)
    {
        var trimmed = line.Trim();
        path = string.Empty;

        if (trimmed.Length < 2 || trimmed[0] != '@')
        {
            return false;
        }

        var candidate = trimmed[1..];

        // 공백이 있으면 문장 속 언급이거나 여러 토큰이다. import 한 줄로 보지 않는다.
        if (candidate.Any(char.IsWhiteSpace))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    /// <summary>개행 종류에 상관없이 줄로 나눈다. 끝의 개행은 빈 줄을 만들지 않는다.</summary>
    public static string[] SplitLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd('\n')
            .Split('\n');

    /// <summary>코드블록 여닫이를 추적하며 줄을 훑는다.</summary>
    internal static IEnumerable<string> OutsideCodeFences(IEnumerable<string> lines)
    {
        var inFence = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith(FenceMarker, StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence)
            {
                yield return line;
            }
        }
    }
}
