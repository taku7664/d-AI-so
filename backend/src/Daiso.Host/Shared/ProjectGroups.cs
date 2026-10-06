using System.Text.RegularExpressions;

namespace Daiso.Host.Shared;

/// <summary>
/// 세션에 적힌 폴더를 사람이 생각하는 프로젝트로 묶는다. 2026-10-06 다시 만든 인덱스에서 프로젝트 65개 중 23개가
/// 워크트리와 임시 폴더라 목록이 어지러웠다.
/// <list type="bullet">
/// <item>Claude 워크트리 <c>{저장소}\.claude\worktrees\{이름}</c> → <c>{저장소}</c></item>
/// <item>Codex 워크트리 <c>~\.codex\worktrees\{id}\{폴더}</c> → 폴더 이름이 같은 다른 프로젝트. 하나로 못 정하면 그대로 둔다</item>
/// <item>Claude 데스크톱 임시 폴더 <c>…\Claude\scratch-workspaces\…</c> → 그 <c>scratch-workspaces</c> 폴더 하나</item>
/// </list>
/// 인덱스와 세션 파일은 그대로다. 묶는 것은 화면에 보여 줄 때만이다.
/// </summary>
public static partial class ProjectGroups
{
    /// <summary>Claude 데스크톱 임시 폴더를 모은 프로젝트의 보여 줄 이름.</summary>
    public const string ScratchLabel = "Claude 임시 작업 공간";

    private const string ClaudeWorktrees = @"\.claude\worktrees\";
    private const string Scratch = @"\scratch-workspaces";

    /// <summary>폴더마다 묶을 프로젝트 경로. 묶이지 않는 폴더는 자기 자신이다.</summary>
    public static Dictionary<string, string> Map(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var all = paths.Select(Trim).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Codex 워크트리가 기댈 곳: 워크트리도 임시 폴더도 아닌 폴더를 이름으로 찾는다
        var plain = all
            .Where(path => !IsClaudeWorktree(path) && CodexWorktree().Match(path) is { Success: false } && ScratchRoot(path) is null)
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in all)
        {
            map[path] = GroupOf(path, plain);
        }

        return map;
    }

    /// <summary>끝의 구분자를 뗀다. 같은 폴더가 <c>C:\a</c> 와 <c>C:\a\</c> 로 두 번 나오지 않게.</summary>
    public static string Trim(string path) =>
        path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;

    /// <summary>보여 줄 이름을 따로 정한 묶음이면 그 이름.</summary>
    public static string? LabelFor(string group) =>
        group.EndsWith(Scratch, StringComparison.OrdinalIgnoreCase) ? ScratchLabel : null;

    private static string GroupOf(string path, Dictionary<string, List<string>> plain)
    {
        var at = path.IndexOf(ClaudeWorktrees, StringComparison.OrdinalIgnoreCase);
        if (at > 0)
        {
            return path[..at];
        }

        if (ScratchRoot(path) is { } scratch)
        {
            return scratch;
        }

        if (CodexWorktree().Match(path) is { Success: true } codex
            && plain.TryGetValue(codex.Groups["name"].Value, out var same)
            && same.Count == 1)
        {
            return same[0];
        }

        return path;
    }

    private static bool IsClaudeWorktree(string path) => path.Contains(ClaudeWorktrees, StringComparison.OrdinalIgnoreCase);

    private static string? ScratchRoot(string path)
    {
        var at = path.IndexOf(Scratch + @"\", StringComparison.OrdinalIgnoreCase);
        return at > 0 ? path[..(at + Scratch.Length)] : null;
    }

    [GeneratedRegex(@"\\\.codex\\worktrees\\[^\\]+\\(?<name>[^\\]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CodexWorktree();
}
