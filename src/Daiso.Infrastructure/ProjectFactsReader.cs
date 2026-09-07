using System.Text;
using System.Text.Json;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 프로젝트 폴더의 설정·확장 항목·git 정보를 읽는다. (REQUIREMENTS §5.3)
/// git 명령을 실행하지 않고 `.git` 안의 파일만 읽는다.
/// </summary>
public sealed class ProjectFactsReader : IProjectFactsReader
{
    /// <inheritdoc />
    public async Task<ProjectFacts> ReadAsync(string projectDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        if (!Directory.Exists(projectDir))
        {
            return ProjectFacts.Empty(projectDir);
        }

        var claudeDir = Path.Combine(projectDir, ".claude");
        var settings = new List<string>();

        foreach (var name in new[] { "settings.json", "settings.local.json" })
        {
            var path = Path.Combine(claudeDir, name);
            if (File.Exists(path))
            {
                settings.Add(await SummarizeSettingsAsync(name, path, ct).ConfigureAwait(false));
            }
        }

        var (branch, commit) = ReadGit(projectDir);

        return new ProjectFacts(
            projectDir,
            settings,
            Count(Path.Combine(claudeDir, "skills")),
            Count(Path.Combine(claudeDir, "agents")),
            Count(Path.Combine(claudeDir, "commands")),
            File.Exists(Path.Combine(projectDir, ".mcp.json")),
            branch,
            commit);
    }

    /// <summary>권한 목록 길이와 훅 개수만 센다. 값은 읽지 않는다.</summary>
    private static async Task<string> SummarizeSettingsAsync(string name, string path, CancellationToken ct)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var allow = ArrayLength(root, "permissions", "allow");
            var deny = ArrayLength(root, "permissions", "deny");
            var ask = ArrayLength(root, "permissions", "ask");
            var hooks = root.TryGetProperty("hooks", out var hooksNode)
                && hooksNode.ValueKind == JsonValueKind.Object
                    ? hooksNode.EnumerateObject().Count()
                    : 0;

            return $"{name}: allow {allow} · deny {deny} · ask {ask} · hooks {hooks}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"{name}: 읽을 수 없음";
        }
    }

    private static int ArrayLength(JsonElement root, string parent, string child)
    {
        if (!root.TryGetProperty(parent, out var parentNode)
            || parentNode.ValueKind != JsonValueKind.Object
            || !parentNode.TryGetProperty(child, out var childNode)
            || childNode.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return childNode.GetArrayLength();
    }

    private static int Count(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFileSystemEntries(directory).Count()
                : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>`.git/HEAD`와 ref 파일만 읽는다.</summary>
    private static (string? Branch, string? Commit) ReadGit(string projectDir)
    {
        try
        {
            var gitDir = Path.Combine(projectDir, ".git");

            // 워크트리에서는 .git이 파일이고 gitdir 경로를 담고 있다.
            if (File.Exists(gitDir))
            {
                var pointer = File.ReadAllText(gitDir, Encoding.UTF8).Trim();
                const string Prefix = "gitdir:";

                if (!pointer.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return (null, null);
                }

                gitDir = pointer[Prefix.Length..].Trim();
            }

            var headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return (null, null);
            }

            var head = File.ReadAllText(headPath, Encoding.UTF8).Trim();
            const string RefPrefix = "ref:";

            if (!head.StartsWith(RefPrefix, StringComparison.OrdinalIgnoreCase))
            {
                // 분리된 HEAD는 커밋 SHA가 그대로 들어 있다.
                return ("(detached)", Short(head));
            }

            var reference = head[RefPrefix.Length..].Trim();
            var branch = reference.Split('/')[^1];
            var refPath = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(refPath))
            {
                return (branch, Short(File.ReadAllText(refPath, Encoding.UTF8).Trim()));
            }

            // 압축된 ref는 packed-refs에 들어 있다.
            var packed = Path.Combine(gitDir, "packed-refs");
            if (File.Exists(packed))
            {
                foreach (var line in File.ReadLines(packed, Encoding.UTF8))
                {
                    var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
                    if (parts.Length == 2 && string.Equals(parts[1], reference, StringComparison.Ordinal))
                    {
                        return (branch, Short(parts[0]));
                    }
                }
            }

            return (branch, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    private static string? Short(string sha) =>
        sha.Length >= 7 ? sha[..7] : (sha.Length == 0 ? null : sha);
}
