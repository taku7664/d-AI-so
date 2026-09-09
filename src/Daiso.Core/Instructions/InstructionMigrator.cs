using System.Globalization;

namespace Daiso.Core;

/// <summary>
/// 도구 사이 지시문 마이그레이션. (REQUIREMENTS §7, ARCHITECTURE §5.6)
///
/// 규칙 셋:
/// - 비교·복사 대상은 daiso 마커 블록을 **뺀** 본문이다. 블록은 대상 도구 형식으로 다시 만들어 붙인다
/// - 대상이 <c>@경로</c> import 를 못 읽는 도구면 그 내용을 자리에 펼친다
/// - 원본은 읽기만 한다
///
/// <para>
/// 도구 이름·파일 이름·문법 지원 여부를 스스로 알지 않는다. <see cref="InstructionToolInfo"/> 로 받는다 —
/// 그래서 도구가 셋이 되어도 이 클래스는 그대로다 (docs/REVIEW_BACKLOG.md A5).
/// </para>
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
    public InstructionMigrationPlan Plan(
        IReadOnlyList<InstructionToolInfo> tools,
        IReadOnlyDictionary<ToolKind, InstructionSource> sources,
        ToolKind left,
        ToolKind right)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(sources);

        var bodies = tools.ToDictionary(
            info => info.Tool,
            info => BodyOf(sources.TryGetValue(info.Tool, out var source) ? source : InstructionSource.Missing));

        var diff = Compare(
            bodies.TryGetValue(left, out var leftBody) ? leftBody : string.Empty,
            bodies.TryGetValue(right, out var rightBody) ? rightBody : string.Empty);

        var present = tools
            .Where(info => sources.TryGetValue(info.Tool, out var source) && source.Exists)
            .ToList();

        // 원본이 하나뿐이면 방향이 뻔하다: 그것에서 나머지로. 둘 이상이면 사람이 고른다
        MigrationDirection? suggested = present.Count == 1
            ? tools.Where(info => info.Tool != present[0].Tool)
                .Select(info => (MigrationDirection?)new MigrationDirection(present[0].Tool, info.Tool))
                .FirstOrDefault()
            : null;

        return new InstructionMigrationPlan(
            tools,
            sources,
            bodies,
            left,
            right,
            diff,
            suggested,
            Notes(tools, sources, left, right, diff));
    }

    /// <inheritdoc />
    public MigrationResult Render(InstructionMigrationPlan plan, MigrationDirection direction)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!direction.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "같은 도구로는 옮길 수 없다");
        }

        var source = plan.SourceOf(direction.From);
        var body = plan.BodyOf(direction.From);

        if (!source.Exists)
        {
            throw new InvalidOperationException($"원본 {plan.FileNameOf(direction.From)} 이 없어 옮길 내용이 없다");
        }

        var warnings = new List<MigrationNote>();

        // 대상이 import 문법을 못 읽으면 옮기면서 내용을 그 자리에 펼친다
        var targetInfo = plan.Tools.FirstOrDefault(info => info.Tool == direction.To);
        var moved = targetInfo is { SupportsImports: true } ? body : Inline(body, source.Imports, warnings);
        var content = _markerWriter.Apply(moved, _template.For(direction.To, _rulesFileName));

        return new MigrationResult(direction.To, content, warnings);
    }

    private string BodyOf(InstructionSource source) =>
        source is { Exists: true, Content: { } content } ? _markerWriter.Strip(content) : string.Empty;

    /// <summary><c>@경로</c> 줄을 읽어온 내용으로 바꾼다. 못 읽은 것은 줄을 그대로 두고 경고만 남긴다.</summary>
    private static string Inline(
        string body,
        IReadOnlyDictionary<string, string?> imports,
        List<MigrationNote> warnings)
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
                warnings.Add(new MigrationNote("MigrationNote_ImportUnreadable", path));
                continue;
            }

            output.AddRange(InstructionImports.SplitLines(imported));
            warnings.Add(new MigrationNote("MigrationNote_ImportInlined", path, Count(imported.Length)));
        }

        return string.Join('\n', output);
    }

    /// <summary>문구에 끼울 수. 문화권에 흔들리지 않게 고정한다.</summary>
    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<MigrationNote> Notes(
        IReadOnlyList<InstructionToolInfo> tools,
        IReadOnlyDictionary<ToolKind, InstructionSource> sources,
        ToolKind left,
        ToolKind right,
        IReadOnlyList<DiffLine> diff)
    {
        var notes = new List<MigrationNote>();

        bool Exists(ToolKind tool) => sources.TryGetValue(tool, out var source) && source.Exists;
        string Name(ToolKind tool) => tools.FirstOrDefault(info => info.Tool == tool)?.RulesFileName ?? string.Empty;

        var present = tools.Where(info => Exists(info.Tool)).ToList();

        if (present.Count == 0)
        {
            notes.Add(new MigrationNote("MigrationNote_NoneExist"));

            return notes;
        }

        foreach (var absent in tools.Where(info => !Exists(info.Tool)))
        {
            notes.Add(new MigrationNote("MigrationNote_TargetMissing", absent.RulesFileName));
        }

        if (Exists(left) && Exists(right))
        {
            var changed = diff.Count(line => line.Kind != DiffKind.Same);

            notes.Add(changed == 0
                ? new MigrationNote("MigrationNote_BodiesSame", Name(left), Name(right))
                : new MigrationNote("MigrationNote_BodiesDiffer", Count(changed)));
        }

        // import 를 쓰는 원본이 있고 그것을 못 읽는 도구가 있으면, 옮길 때 펼쳐진다고 미리 알린다
        foreach (var info in present)
        {
            var imports = InstructionImports.Find(sources[info.Tool].Content);

            if (imports.Count > 0 && tools.Any(other => other.Tool != info.Tool && !other.SupportsImports))
            {
                notes.Add(new MigrationNote("MigrationNote_HasImports", info.RulesFileName, Count(imports.Count)));
            }
        }

        return notes;
    }

    /// <summary>줄 단위 LCS. 좌우가 어느 도구인지는 부르는 쪽이 정한다.</summary>
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
