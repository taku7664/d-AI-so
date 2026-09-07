using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Daiso.Core;

/// <summary>
/// 컨텍스트 파일들의 중복 줄과 상반 지시 후보를 찾는다. (ARCHITECTURE §5.4)
/// 순수 텍스트 처리만 하며 파일이나 경로를 다루지 않는다.
/// </summary>
public sealed class ContextAnalyzer : IContextAnalyzer
{
    /// <summary>이보다 짧은 줄은 중복·충돌 판정에서 뺀다. 짧은 제목이 잡히는 것을 막는다.</summary>
    private const int MinimumLineLength = 8;

    /// <summary>충돌 후보가 너무 많아지면 UI가 못 쓰게 되므로 상한을 둔다.</summary>
    private const int MaxConflicts = 50;

    private static readonly char[] BulletCharacters = ['-', '*', '+', '#', '>'];

    private readonly IReadOnlyList<ConflictKeywordPair> _pairs;

    public ContextAnalyzer()
        : this(ConflictKeywords.Load())
    {
    }

    public ContextAnalyzer(IReadOnlyList<ConflictKeywordPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        _pairs = pairs;
    }

    /// <inheritdoc />
    public ContextReport Analyze(ToolKind tool, IReadOnlyList<ContextFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var present = files.Where(file => file.Exists).ToList();
        var lines = present
            .SelectMany(file => Lines(file).Select(line => (File: file.Path, Line: line)))
            .ToList();

        return new ContextReport(
            tool,
            files,
            present.Sum(file => file.Content.Length),
            Duplicates(lines),
            Conflicts(lines));
    }

    /// <summary>줄 정규화: 좌우 공백 제거, 소문자, 연속 공백 1개, 마크다운 불릿 제거.</summary>
    public static string NormalizeLine(string line)
    {
        var trimmed = line.Trim();

        // 중첩 목록("- - 항목")도 벗겨내려면 불릿과 공백을 번갈아 계속 걷어내야 한다.
        while (trimmed.Length > 0 && BulletCharacters.Contains(trimmed[0]))
        {
            trimmed = trimmed.TrimStart(BulletCharacters).Trim();
        }

        var builder = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<NormalizedLine> Lines(ContextFile file)
    {
        foreach (var raw in file.Content.Split('\n'))
        {
            var normalized = NormalizeLine(raw);
            if (normalized.Length >= MinimumLineLength)
            {
                yield return new NormalizedLine(raw.Trim(), normalized);
            }
        }
    }

    private static IReadOnlyList<DuplicateLine> Duplicates(
        IReadOnlyList<(string File, NormalizedLine Line)> lines) =>
        lines
            .GroupBy(entry => entry.Line.Normalized, StringComparer.Ordinal)
            .Select(group => new
            {
                Text = group.Key,
                Files = group
                    .Select(entry => entry.File)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            })
            .Where(group => group.Files.Count >= 2)
            .Select(group => new DuplicateLine(group.Text, group.Files))
            .ToList();

    private IReadOnlyList<ConflictHint> Conflicts(
        IReadOnlyList<(string File, NormalizedLine Line)> lines)
    {
        var hints = new List<ConflictHint>();

        foreach (var pair in _pairs)
        {
            // 한쪽 키워드가 다른 쪽을 포함하는 짝("must" / "must not")이 있으므로
            // 왼쪽 후보는 오른쪽 키워드를 포함하지 않아야 한다.
            var left = lines
                .Where(entry => Contains(entry.Line.Normalized, pair.Left)
                    && !Contains(entry.Line.Normalized, pair.Right))
                .ToList();

            if (left.Count == 0)
            {
                continue;
            }

            var right = lines
                .Where(entry => Contains(entry.Line.Normalized, pair.Right))
                .ToList();

            foreach (var a in left)
            {
                foreach (var b in right)
                {
                    if (string.Equals(a.Line.Normalized, b.Line.Normalized, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    hints.Add(new ConflictHint(a.File, a.Line.Raw, b.File, b.Line.Raw, pair.Reason));

                    if (hints.Count >= MaxConflicts)
                    {
                        return hints;
                    }
                }
            }
        }

        return hints;
    }

    private static bool Contains(string line, string keyword) =>
        line.Contains(keyword, StringComparison.Ordinal);

    private sealed record NormalizedLine(string Raw, string Normalized);
}

/// <summary>상반 키워드 한 짝.</summary>
public sealed record ConflictKeywordPair(string Left, string Right, string Reason);

/// <summary>Core에 묻어둔 상반 키워드 표를 읽는다.</summary>
public static class ConflictKeywords
{
    private const string ResourceName = "Daiso.Core.Resources.conflict-keywords.json";

    /// <summary>임베디드 리소스에서 키워드 짝을 읽는다. 키워드는 소문자로 맞춘다.</summary>
    public static IReadOnlyList<ConflictKeywordPair> Load()
    {
        using var stream = typeof(ConflictKeywords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"리소스를 찾을 수 없다: {ResourceName}");

        using var document = JsonDocument.Parse(stream);
        var pairs = new List<ConflictKeywordPair>();

        if (!document.RootElement.TryGetProperty("pairs", out var items))
        {
            return pairs;
        }

        foreach (var item in items.EnumerateArray())
        {
            var left = Text(item, "left");
            var right = Text(item, "right");

            if (left is null || right is null)
            {
                continue;
            }

            pairs.Add(new ConflictKeywordPair(
                left.ToLowerInvariant(),
                right.ToLowerInvariant(),
                Text(item, "reason") ?? "상반되는 지시로 보인다"));
        }

        return pairs;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
