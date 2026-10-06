using System.Text.RegularExpressions;
using Daiso.Core;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>/ 목록 한 줄.</summary>
/// <param name="Name"><c>/</c> 로 시작하는 이름.</param>
/// <param name="Argument">받는 인자 모양. 없으면 빈 문자열.</param>
/// <param name="Description">설명 한 줄.</param>
/// <param name="Terminal">화면이 있어야 하는 명령. 고르면 터미널 보기로 넘긴다.</param>
public sealed record CommandItem(string Name, string Argument, string Description, bool Terminal);

/// <summary>/ 목록의 묶음.</summary>
/// <param name="Kind"><c>builtin</c>(도구 기본 명령) · <c>user</c>(내 명령·스킬) · <c>project</c>(이 프로젝트의 명령·스킬).</param>
/// <param name="Source">어디서 읽었나. 화면이 묶음 제목 옆에 보인다.</param>
/// <param name="Items">명령들.</param>
public sealed record CommandGroup(string Kind, string Source, IReadOnlyList<CommandItem> Items);

/// <summary>
/// 말풍선 입력칸의 / 목록. 도구 기본 명령(정해 둔 목록)과, 사람이 만든 명령·스킬(파일에서 읽음)을 모은다.
/// 기본 명령 목록은 CLI 가 알려 주지 않으므로 여기 적어 둔다. 화면이 있어야 하는 것(<c>/login</c> 등)은 표시해 둔다.
/// </summary>
public static partial class Commands
{
    private static readonly CommandItem[] ClaudeBuiltins =
    [
        new("/compact", "[지시]", "대화를 줄여 컨텍스트를 비웁니다", false),
        new("/clear", string.Empty, "새 대화로 비웁니다", false),
        new("/model", "<모델>", "모델을 바꿉니다", false),
        new("/context", string.Empty, "컨텍스트 사용량을 봅니다", false),
        new("/review", string.Empty, "변경 사항을 검토합니다", false),
        new("/init", string.Empty, "CLAUDE.md 를 만듭니다", false),
        new("/login", string.Empty, "다시 로그인합니다", true),
        new("/config", string.Empty, "설정 화면", true),
        new("/permissions", string.Empty, "권한 규칙 화면", true),
        new("/mcp", string.Empty, "MCP 서버 화면", true),
        new("/resume", string.Empty, "다른 대화로 바꿉니다", true),
    ];

    private static readonly CommandItem[] CodexBuiltins =
    [
        new("/compact", string.Empty, "대화를 줄여 컨텍스트를 비웁니다", false),
        new("/new", string.Empty, "새 대화를 시작합니다", false),
        new("/init", string.Empty, "AGENTS.md 를 만듭니다", false),
        new("/review", string.Empty, "변경 사항을 검토합니다", false),
        new("/diff", string.Empty, "git 변경을 봅니다", false),
        new("/status", string.Empty, "세션 설정과 사용량을 봅니다", false),
        new("/model", string.Empty, "모델 고르기 화면", true),
        new("/approvals", string.Empty, "승인 방식 화면", true),
    ];

    public static IReadOnlyList<CommandGroup> For(IProvider provider, string? folder)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var groups = new List<CommandGroup>();
        if (provider.Kind == ToolKind.Claude)
        {
            groups.Add(new CommandGroup("builtin", "Claude Code", ClaudeBuiltins));
            // 세션 폴더(~/.claude/projects)의 부모가 Claude 설정 폴더다. 시험은 가짜 홈을 쓴다
            if (Path.GetDirectoryName(provider.SessionsRoot) is { } config)
            {
                Add(groups, "user", "~/.claude", config);
            }

            if (folder is not null && Directory.Exists(folder))
            {
                Add(groups, "project", ".claude", Path.Combine(folder, ".claude"));
            }
        }
        else if (provider.Kind == ToolKind.Codex)
        {
            groups.Add(new CommandGroup("builtin", "Codex", CodexBuiltins));
        }

        return groups;
    }

    /// <summary><c>{root}/commands/*.md</c> 와 <c>{root}/skills/*/SKILL.md</c>. 설명은 앞머리(frontmatter)의 description, 없으면 첫 줄.</summary>
    private static void Add(List<CommandGroup> groups, string kind, string label, string root)
    {
        var items = new List<CommandItem>();

        var commands = Path.Combine(root, "commands");
        if (Directory.Exists(commands))
        {
            items.AddRange(Directory.EnumerateFiles(commands, "*.md", SearchOption.AllDirectories)
                .Select(file => new CommandItem("/" + Path.GetFileNameWithoutExtension(file), Argument(file), Describe(file), false)));
        }

        var skills = Path.Combine(root, "skills");
        if (Directory.Exists(skills))
        {
            items.AddRange(Directory.EnumerateDirectories(skills)
                .Where(folder => File.Exists(Path.Combine(folder, "SKILL.md")))
                .Select(folder => new CommandItem("/" + Path.GetFileName(folder), string.Empty, Describe(Path.Combine(folder, "SKILL.md")), false)));
        }

        if (items.Count > 0)
        {
            groups.Add(new CommandGroup(kind, label, [.. items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)]));
        }
    }

    private static string Describe(string file) => Front(file, "description") ?? FirstLine(file) ?? string.Empty;

    private static string Argument(string file) => Front(file, "argument-hint") ?? string.Empty;

    private static string? Front(string file, string key)
    {
        try
        {
            var head = string.Join('\n', File.ReadLines(file).Take(30));
            var match = FrontMatter().Match(head);
            if (!match.Success)
            {
                return null;
            }

            foreach (var line in match.Groups[1].Value.Split('\n'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2 && parts[0].Trim() == key)
                {
                    return Short(parts[1].Trim().Trim('"', '\''));
                }
            }

            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? FirstLine(string file)
    {
        try
        {
            return File.ReadLines(file).Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0 && line != "---") is { } line
                ? Short(line.TrimStart('#', ' '))
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string Short(string text) => text.Length > 120 ? text[..120] + "…" : text;

    [GeneratedRegex(@"\A---\s*\n(.*?)\n---", RegexOptions.Singleline)]
    private static partial Regex FrontMatter();
}
