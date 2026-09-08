using Daiso.Core;
using Daiso.Core.Prompts;
using Daiso.Providers.Common;

namespace Daiso.Infrastructure;

/// <summary>
/// 채팅 입력의 `/` 선택기에 넣을 명령 목록을 만든다. 내장 표(<see cref="BuiltInSlashCommands"/>)에
/// 디스크의 사용자·프로젝트 명령과 스킬을 합친다. 파일은 읽기만 한다. (FEATURE_PLAN 뒤로→`/` 선택기)
///
/// | 도구 | 사용자 | 프로젝트 | 그 밖 |
/// |---|---|---|---|
/// | Claude | `~/.claude/commands/*.md` | `&lt;proj&gt;/.claude/commands/*.md` | `~/.claude/skills/*/SKILL.md` |
/// | Codex  | `~/.codex/prompts/*.md`   | (없음) | |
/// | Gemini | `~/.gemini/commands/*.toml`| `&lt;proj&gt;/.gemini/commands/*.toml` | |
/// </summary>
public sealed class SlashCommandReader
{
    private readonly ProviderHome _home;

    public SlashCommandReader(ProviderHome home) => _home = home ?? throw new ArgumentNullException(nameof(home));

    /// <summary>그 도구·프로젝트에서 부를 수 있는 명령. 내장 먼저, 그다음 사용자·프로젝트·스킬. 이름 중복은 앞의 것을 남긴다.</summary>
    public IReadOnlyList<SlashCommand> Read(ToolKind tool, string? projectDirectory)
    {
        var result = new List<SlashCommand>(BuiltInSlashCommands.For(tool));

        switch (tool)
        {
            case ToolKind.Claude:
                AddMarkdown(result, _home.Combine(".claude", "commands"), SlashCommandSource.User);
                AddMarkdown(result, ProjectDir(projectDirectory, ".claude", "commands"), SlashCommandSource.Project);
                AddSkills(result, _home.Combine(".claude", "skills"));
                break;

            case ToolKind.Codex:
                AddMarkdown(result, _home.Combine(".codex", "prompts"), SlashCommandSource.User);
                break;

            case ToolKind.Gemini:
                AddToml(result, _home.Combine(".gemini", "commands"), SlashCommandSource.User);
                AddToml(result, ProjectDir(projectDirectory, ".gemini", "commands"), SlashCommandSource.Project);
                break;

            default:
                break;
        }

        // 이름이 겹치면 먼저 넣은 것(내장·사용자)을 남긴다
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return result.Where(command => seen.Add(command.Name)).ToList();
    }

    private static string? ProjectDir(string? projectDirectory, params string[] parts) =>
        string.IsNullOrWhiteSpace(projectDirectory) ? null : Path.Combine([projectDirectory, .. parts]);

    /// <summary>`*.md`. 이름은 파일명(하위 폴더는 `부모:이름`), 설명은 frontmatter description 또는 첫 제목/문장.</summary>
    private static void AddMarkdown(List<SlashCommand> into, string? directory, SlashCommandSource source)
    {
        foreach (var file in EnumerateFiles(directory, "*.md"))
        {
            var name = CommandName(directory!, file);
            var description = DescriptionFromMarkdown(file);
            into.Add(new SlashCommand(name, description, source));
        }
    }

    /// <summary>스킬: `skills/&lt;이름&gt;/SKILL.md`의 frontmatter name·description.</summary>
    private static void AddSkills(List<SlashCommand> into, string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var skillDir in SafeEnumerateDirectories(directory))
        {
            var manifest = Path.Combine(skillDir, "SKILL.md");
            if (!File.Exists(manifest))
            {
                continue;
            }

            var front = Frontmatter(manifest);
            var name = front.GetValueOrDefault("name") ?? Path.GetFileName(skillDir);
            into.Add(new SlashCommand(name, front.GetValueOrDefault("description"), SlashCommandSource.Skill));
        }
    }

    /// <summary>Gemini `*.toml`: 이름은 파일명, 설명은 `description = "..."` 줄.</summary>
    private static void AddToml(List<SlashCommand> into, string? directory, SlashCommandSource source)
    {
        foreach (var file in EnumerateFiles(directory, "*.toml"))
        {
            var name = CommandName(directory!, file);
            into.Add(new SlashCommand(name, TomlDescription(file), source));
        }
    }

    private static IEnumerable<string> EnumerateFiles(string? directory, string pattern)
    {
        if (directory is null || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>파일 경로에서 명령 이름. 하위 폴더 안이면 `폴더:이름`(CLI의 네임스페이스 규칙).</summary>
    private static string CommandName(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        var withoutExtension = Path.Combine(
            Path.GetDirectoryName(relative) ?? string.Empty,
            Path.GetFileNameWithoutExtension(relative));

        return withoutExtension
            .Replace(Path.DirectorySeparatorChar, ':')
            .Replace(Path.AltDirectorySeparatorChar, ':');
    }

    private static string? DescriptionFromMarkdown(string file)
    {
        var front = Frontmatter(file);
        if (front.GetValueOrDefault("description") is { Length: > 0 } fromFront)
        {
            return fromFront;
        }

        // frontmatter가 없으면 첫 뜻 있는 줄(제목 # 는 벗겨서)
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line == "---")
                {
                    continue;
                }

                return line.TrimStart('#').Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>`---` … `---` 사이의 <c>key: value</c>를 읽는다. 없으면 빈 사전.</summary>
    private static Dictionary<string, string> Frontmatter(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var reader = new StreamReader(file);
            if (reader.ReadLine()?.Trim() != "---")
            {
                return map;
            }

            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (line.Trim() == "---")
                {
                    break;
                }

                var colon = line.IndexOf(':', StringComparison.Ordinal);
                if (colon > 0)
                {
                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim().Trim('"');
                    map[key] = value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 못 읽으면 빈 사전
        }

        return map;
    }

    private static string? TomlDescription(string file)
    {
        try
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("description", StringComparison.OrdinalIgnoreCase) && line.Contains('=', StringComparison.Ordinal))
                {
                    return line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim().Trim('"', '\'');
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
