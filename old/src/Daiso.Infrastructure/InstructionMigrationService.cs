using System.Text;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 도구 사이 지시문 마이그레이션의 파일 쪽. (ARCHITECTURE §5.6)
///
/// - 프로젝트 루트의 지시문 파일들만 본다 (`.claude/CLAUDE.md`는 대상이 아니다. REQUIREMENTS §6.5)
/// - <c>@경로</c> import는 재귀로 해석한다. 깊이 제한과 순환 차단이 있다
/// - 쓰는 것은 고른 방향의 **대상 파일 하나**뿐이다. 원본은 읽기만 한다
///
/// <para>
/// 도구 목록은 <c>IProvider</c> 들에서 온다. 도구를 늘리면 이 클래스를 고치지 않아도 방향이 늘어난다
/// (docs/REVIEW_BACKLOG.md A5).
/// </para>
/// </summary>
public sealed class InstructionMigrationService : IInstructionMigrationService
{
    /// <summary>import 안의 import를 따라갈 최대 깊이.</summary>
    private const int MaxImportDepth = 5;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IInstructionMigrator _migrator;
    private readonly IReadOnlyList<InstructionToolInfo> _tools;

    public InstructionMigrationService(IEnumerable<IProvider> providers)
        : this(new InstructionMigrator(), providers)
    {
    }

    public InstructionMigrationService(IInstructionMigrator migrator, IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _migrator = migrator ?? throw new ArgumentNullException(nameof(migrator));
        _tools =
        [
            .. providers.Select(provider => new InstructionToolInfo(
                provider.Kind,
                provider.RulesFileName,
                provider.SupportsInstructionImports)),
        ];
    }

    /// <summary>도구 목록을 직접 주는 생성자. 테스트가 쓴다.</summary>
    public InstructionMigrationService(IInstructionMigrator migrator, IReadOnlyList<InstructionToolInfo> tools)
    {
        ArgumentNullException.ThrowIfNull(migrator);
        ArgumentNullException.ThrowIfNull(tools);

        _migrator = migrator;
        _tools = tools;
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolKind> Tools => [.. _tools.Select(info => info.Tool)];

    /// <inheritdoc />
    public string RulesFileName(ToolKind tool) =>
        _tools.FirstOrDefault(info => info.Tool == tool)?.RulesFileName ?? string.Empty;

    /// <inheritdoc />
    public InstructionMigrationPlan Plan(string projectDir) =>
        Plan(projectDir, DefaultPair(projectDir));

    /// <inheritdoc />
    public InstructionMigrationPlan Plan(string projectDir, (ToolKind Left, ToolKind Right) pair)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var sources = _tools.ToDictionary(
            info => info.Tool,
            info => info.SupportsImports
                ? ReadWithImports(Path.Combine(projectDir, info.RulesFileName), projectDir)
                : Read(Path.Combine(projectDir, info.RulesFileName)));

        return _migrator.Plan(_tools, sources, pair.Left, pair.Right);
    }

    /// <inheritdoc />
    public MigrationResult Apply(string projectDir, MigrationDirection direction, bool dryRun)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);

        var plan = Plan(projectDir, (direction.From, direction.To));
        var result = _migrator.Render(plan, direction);

        if (dryRun)
        {
            return result;
        }

        var targetPath = Path.Combine(projectDir, RulesFileName(result.Target));
        var existing = File.Exists(targetPath) ? File.ReadAllText(targetPath, Encoding.UTF8) : null;

        // 내용이 같으면 mtime을 흔들지 않는다.
        if (!string.Equals(existing, result.Content, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(targetPath, result.Content, Utf8NoBom);
        }

        return result;
    }

    /// <summary>
    /// 기본 비교 짝. <b>파일이 실제로 있는 도구 둘</b>을 먼저 고르고, 모자라면 목록 앞에서 채운다.
    /// 셋 다 있으면 앞의 둘이다 — 나머지 짝은 화면에서 골라 볼 수 있다.
    /// </summary>
    private (ToolKind Left, ToolKind Right) DefaultPair(string projectDir)
    {
        var present = _tools
            .Where(info => File.Exists(Path.Combine(projectDir, info.RulesFileName)))
            .Select(info => info.Tool)
            .ToList();

        var order = _tools.Select(info => info.Tool).ToList();

        foreach (var tool in order)
        {
            if (present.Count >= 2)
            {
                break;
            }

            if (!present.Contains(tool))
            {
                present.Add(tool);
            }
        }

        return present.Count >= 2
            ? (present[0], present[1])
            : (present.FirstOrDefault(), present.FirstOrDefault());
    }

    private static InstructionSource Read(string path) =>
        File.Exists(path)
            ? InstructionSource.Of(File.ReadAllText(path, Encoding.UTF8))
            : InstructionSource.Missing;

    /// <summary>본문을 읽고 <c>@경로</c> import를 재귀로 해석한다. 값이 null이면 읽지 못한 것이다.</summary>
    private static InstructionSource ReadWithImports(string path, string projectDir)
    {
        if (!File.Exists(path))
        {
            return InstructionSource.Missing;
        }

        var content = File.ReadAllText(path, Encoding.UTF8);
        var imports = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var import in InstructionImports.Find(content))
        {
            // 순환 판정은 import 사슬마다 따로 한다. 형제 import가 같은 파일을 봐도 막지 않는다.
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Normalize(path) };
            imports[import] = Expand(import, projectDir, visited, MaxImportDepth);
        }

        return new InstructionSource(true, content, imports);
    }

    /// <summary>
    /// import 하나를 읽어 그 안의 import까지 펼친 내용을 돌려준다.
    /// 읽지 못하거나 깊이·순환에 걸리면 null이다.
    /// </summary>
    private static string? Expand(string import, string baseDir, HashSet<string> visited, int depth)
    {
        if (depth <= 0)
        {
            return null;
        }

        var resolved = Resolve(import, baseDir);
        if (resolved is null || !File.Exists(resolved) || !visited.Add(Normalize(resolved)))
        {
            return null;
        }

        var content = File.ReadAllText(resolved, Encoding.UTF8);
        var nested = InstructionImports.Find(content);

        if (nested.Count == 0)
        {
            return content;
        }

        var dir = Path.GetDirectoryName(resolved) ?? baseDir;
        var lines = InstructionImports.SplitLines(content);
        var output = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (InstructionImports.TryReadImport(line, out var child)
                && Expand(child, dir, visited, depth - 1) is { } childContent)
            {
                output.AddRange(InstructionImports.SplitLines(childContent));
                continue;
            }

            output.Add(line);
        }

        return string.Join('\n', output);
    }

    /// <summary><c>~</c>는 홈, 그 밖은 파일 기준 상대 경로. 경로로 쓸 수 없는 문자열이면 null.</summary>
    private static string? Resolve(string import, string baseDir)
    {
        try
        {
            if (import.StartsWith("~/", StringComparison.Ordinal) || import.StartsWith(@"~\", StringComparison.Ordinal))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.GetFullPath(Path.Combine(home, import[2..]));
            }

            return Path.GetFullPath(Path.Combine(baseDir, import));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
