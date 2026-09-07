namespace Daiso.Core;

/// <summary>
/// CLAUDE.md ↔ AGENTS.md 마이그레이션. (REQUIREMENTS §7, ARCHITECTURE §5.6)
///
/// 규칙 셋:
/// - 비교·복사 대상은 daiso 마커 블록을 **뺀** 본문이다. 블록은 대상 도구 형식으로 다시 만들어 붙인다
/// - 대상이 Codex면 Claude 전용 <c>@경로</c> import를 인라인 전개한다 (Codex에는 import 문법이 없다)
/// - 원본은 읽기만 한다
/// </summary>
public sealed class InstructionMigrator : IInstructionMigrator
{
    /// <summary>줄 수가 이보다 많으면 LCS를 포기하고 전체 교체로 본다.</summary>
    private const int DiffLineLimit = 3000;

    private readonly IInstructionMarkerWriter _markerWriter;
    private readonly IInstructionTemplate _template;
    private readonly string _rulesFileName;

    public InstructionMigrator()
        : this(new InstructionMarkerWriter(), new InstructionTemplate(), InstructionTemplate.DefaultRulesFileName)
    {
    }

    public InstructionMigrator(
        IInstructionMarkerWriter markerWriter,
        IInstructionTemplate template,
        string rulesFileName)
    {
        ArgumentNullException.ThrowIfNull(markerWriter);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesFileName);

        _markerWriter = markerWriter;
        _template = template;
        _rulesFileName = rulesFileName;
    }

    /// <inheritdoc />
    public InstructionMigrationPlan Plan(InstructionSource claude, InstructionSource codex)
    {
        ArgumentNullException.ThrowIfNull(claude);
        ArgumentNullException.ThrowIfNull(codex);

        var claudeBody = BodyOf(claude);
        var codexBody = BodyOf(codex);
        var diff = Compare(claudeBody, codexBody);

        MigrationDirection? suggested = (claude.Exists, codex.Exists) switch
        {
            (true, false) => MigrationDirection.ClaudeToCodex,
            (false, true) => MigrationDirection.CodexToClaude,
            _ => null,
        };

        return new InstructionMigrationPlan(
            claude,
            codex,
            claudeBody,
            codexBody,
            diff,
            suggested,
            Notes(claude, codex, diff));
    }

    /// <inheritdoc />
    public MigrationResult Render(InstructionMigrationPlan plan, MigrationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var (source, body, target) = direction switch
        {
            MigrationDirection.ClaudeToCodex => (plan.Claude, plan.ClaudeBody, ToolKind.Codex),
            MigrationDirection.CodexToClaude => (plan.Codex, plan.CodexBody, ToolKind.Claude),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "알 수 없는 방향"),
        };

        if (!source.Exists)
        {
            var missing = direction == MigrationDirection.ClaudeToCodex ? "CLAUDE.md" : "AGENTS.md";
            throw new InvalidOperationException($"원본 {missing}이 없어 옮길 내용이 없다");
        }

        var warnings = new List<string>();

        // Codex에는 import 문법이 없다. 옮기면서 내용을 그 자리에 펼친다.
        var moved = target == ToolKind.Codex ? Inline(body, source.Imports, warnings) : body;
        var content = _markerWriter.Apply(moved, _template.For(target, _rulesFileName));

        return new MigrationResult(target, content, warnings);
    }

    private string BodyOf(InstructionSource source) =>
        source is { Exists: true, Content: { } content } ? _markerWriter.Strip(content) : string.Empty;

    /// <summary><c>@경로</c> 줄을 읽어온 내용으로 바꾼다. 못 읽은 것은 줄을 그대로 두고 경고만 남긴다.</summary>
    private static string Inline(
        string body,
        IReadOnlyDictionary<string, string?> imports,
        List<string> warnings)
    {
        if (imports.Count == 0)
        {
            return body;
        }

        var lines = InstructionImports.SplitLines(body);
        var output = new List<string>(lines.Length);
        var inFence = false;

        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                output.Add(line);
                continue;
            }

            if (inFence || !InstructionImports.TryReadImport(line, out var path))
            {
                output.Add(line);
                continue;
            }

            if (!imports.TryGetValue(path, out var imported) || imported is null)
            {
                output.Add(line);
                warnings.Add($"import를 읽지 못해 줄을 그대로 두었다: @{path}");
                continue;
            }

            output.AddRange(InstructionImports.SplitLines(imported));
            warnings.Add($"import를 인라인 전개했다: @{path} ({imported.Length}자)");
        }

        return string.Join('\n', output);
    }

    private static IReadOnlyList<string> Notes(
        InstructionSource claude,
        InstructionSource codex,
        IReadOnlyList<DiffLine> diff)
    {
        var notes = new List<string>();

        switch (claude.Exists, codex.Exists)
        {
            case (false, false):
                notes.Add("CLAUDE.md와 AGENTS.md가 모두 없다. 연동으로 새로 만들 수 있다");
                break;
            case (true, false):
                notes.Add("AGENTS.md가 없다. CLAUDE.md → AGENTS.md 생성을 권한다");
                break;
            case (false, true):
                notes.Add("CLAUDE.md가 없다. AGENTS.md → CLAUDE.md 생성을 권한다");
                break;
            default:
                var changed = diff.Count(line => line.Kind != DiffKind.Same);
                notes.Add(changed == 0
                    ? "마커 블록 밖 본문이 같다. 옮길 것이 없다"
                    : $"본문이 {changed}줄 다르다. 방향을 고르면 대상 파일을 원본 본문으로 덮어쓴다");
                break;
        }

        var imports = InstructionImports.Find(claude.Content);
        if (imports.Count > 0)
        {
            notes.Add($"CLAUDE.md에 import {imports.Count}개. AGENTS.md로 옮길 때 인라인 전개된다");
        }

        return notes;
    }

    /// <summary>줄 단위 LCS. 왼쪽이 CLAUDE.md, 오른쪽이 AGENTS.md다.</summary>
    private static IReadOnlyList<DiffLine> Compare(string left, string right)
    {
        // 빈 문자열도 SplitLines는 빈 줄 하나를 준다. 없는 파일이 빈 줄로 보이지 않게 걸러 낸다.
        string[] a = left.Length == 0 ? [] : InstructionImports.SplitLines(left);
        string[] b = right.Length == 0 ? [] : InstructionImports.SplitLines(right);

        if (a.Length == 0 && b.Length == 0)
        {
            return [];
        }

        if (a.Length > DiffLineLimit || b.Length > DiffLineLimit)
        {
            return Coarse(a, b);
        }

        var lcs = new int[a.Length + 1, b.Length + 1];

        for (var i = a.Length - 1; i >= 0; i--)
        {
            for (var j = b.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var result = new List<DiffLine>(a.Length + b.Length);
        var x = 0;
        var y = 0;

        while (x < a.Length && y < b.Length)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                result.Add(new DiffLine(DiffKind.Same, x + 1, y + 1, a[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, x + 1, null, a[x]));
                x++;
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, null, y + 1, b[y]));
                y++;
            }
        }

        for (; x < a.Length; x++)
        {
            result.Add(new DiffLine(DiffKind.Removed, x + 1, null, a[x]));
        }

        for (; y < b.Length; y++)
        {
            result.Add(new DiffLine(DiffKind.Added, null, y + 1, b[y]));
        }

        return result;
    }

    /// <summary>너무 큰 파일은 줄 대응을 포기하고 전체 교체로 보여준다.</summary>
    private static IReadOnlyList<DiffLine> Coarse(string[] a, string[] b)
    {
        var result = new List<DiffLine>(a.Length + b.Length);

        result.AddRange(a.Select((line, i) => new DiffLine(DiffKind.Removed, i + 1, null, line)));
        result.AddRange(b.Select((line, i) => new DiffLine(DiffKind.Added, null, i + 1, line)));

        return result;
    }
}
