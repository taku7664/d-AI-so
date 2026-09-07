using System.Text;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// Provider가 알려준 순서대로 컨텍스트 파일을 읽어 <see cref="IContextAnalyzer"/>에 넘긴다. (ARCHITECTURE §5.4)
/// </summary>
public sealed class ContextInspector : IContextInspector
{
    private readonly IReadOnlyList<IProvider> _providers;
    private readonly IContextAnalyzer _analyzer;

    public ContextInspector(IEnumerable<IProvider> providers)
        : this(providers, new ContextAnalyzer())
    {
    }

    public ContextInspector(IEnumerable<IProvider> providers, IContextAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(analyzer);

        _providers = providers.ToList();
        _analyzer = analyzer;
    }

    /// <inheritdoc />
    public async Task<ContextReport> InspectAsync(ToolKind tool, string projectDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var provider = _providers.FirstOrDefault(p => p.Kind == tool)
            ?? throw new InvalidOperationException($"{tool} Provider가 등록되지 않았다");

        var files = new List<ContextFile>();
        var order = 0;

        foreach (var pattern in provider.ContextFilePatterns(projectDir))
        {
            ct.ThrowIfCancellationRequested();

            foreach (var path in Expand(pattern))
            {
                files.Add(await ReadAsync(path, tool, order++, ct).ConfigureAwait(false));
            }
        }

        return _analyzer.Analyze(tool, files);
    }

    /// <summary>와일드카드 패턴은 실제 파일로 펼친다. 없으면 패턴 자체를 "없는 파일"로 남긴다.</summary>
    private static IEnumerable<string> Expand(string pattern)
    {
        if (!pattern.Contains('*', StringComparison.Ordinal))
        {
            return [pattern];
        }

        var directory = Path.GetDirectoryName(pattern);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, Path.GetFileName(pattern), SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<ContextFile> ReadAsync(string path, ToolKind tool, int order, CancellationToken ct)
    {
        var kind = Kind(path, tool);

        try
        {
            if (!File.Exists(path))
            {
                return new ContextFile(path, kind, string.Empty, Exists: false, order);
            }

            var content = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);

            // 파일 안 개행을 LF로 맞춰야 줄 단위 비교가 일관된다.
            return new ContextFile(
                path,
                kind,
                content.Replace("\r\n", "\n", StringComparison.Ordinal),
                Exists: true,
                order);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ContextFile(path, kind, string.Empty, Exists: false, order);
        }
    }

    private static string Kind(string path, ToolKind tool)
    {
        var name = Path.GetFileName(path);

        return name switch
        {
            "PROJECT_RULES.daiso" => "daiso",
            "CLAUDE.local.md" => "local",
            _ when name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && path.Contains($"{Path.DirectorySeparatorChar}rules{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) => "rule",
            _ => tool.ToString().ToLowerInvariant(),
        };
    }
}
