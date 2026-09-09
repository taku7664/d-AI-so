namespace Daiso.Core;

/// <summary>
/// 마이그레이션 방향. 어느 도구의 지시문을 어느 도구로 옮기는가. (REQUIREMENTS §7)
///
/// <para>
/// 예전에는 <c>ClaudeToCodex</c> / <c>CodexToClaude</c> 두 값짜리 열거형이었다.
/// 그래서 도구를 세 번째로 늘려도 마이그레이션만은 늘어나지 않았다 —
/// 열거형 자체가 "도구는 둘"이라고 못 박고 있었기 때문이다 (docs/REVIEW_BACKLOG.md A5).
/// </para>
/// </summary>
public readonly record struct MigrationDirection(ToolKind From, ToolKind To)
{
    /// <summary>자기 자신으로 옮기는 것은 방향이 아니다.</summary>
    public bool IsValid => From != To;
}

/// <summary>
/// 화면에 한 줄로 붙는 안내·경고. <see cref="AuthNote"/> 와 같은 이유로 완성된 문장을 담지 않는다.
/// Core 는 어느 나라 말도 모른다 — 문구 키와 값만 담고, 그리는 쪽이 문구를 찾는다.
/// </summary>
/// <param name="Key">문구 키. 앱의 <c>Resources.resw</c> 항목 이름이다.</param>
/// <param name="Argument">문구의 <c>{0}</c> 자리에 들어갈 값. 없으면 null.</param>
/// <param name="Argument2">문구의 <c>{1}</c> 자리. 없으면 null.</param>
public sealed record MigrationNote(string Key, string? Argument = null, string? Argument2 = null);

/// <summary>diff 한 줄의 성격. 왼쪽·오른쪽이 어느 도구인지는 <see cref="InstructionMigrationPlan"/>이 말한다.</summary>
public enum DiffKind
{
    /// <summary>양쪽에 같은 줄.</summary>
    Same,

    /// <summary>오른쪽에만 있는 줄.</summary>
    Added,

    /// <summary>왼쪽에만 있는 줄.</summary>
    Removed,
}

/// <summary>좌우 비교 한 줄. 줄 번호는 해당 쪽에 없으면 null이다.</summary>
public sealed record DiffLine(DiffKind Kind, int? LeftLine, int? RightLine, string Text);

/// <summary>
/// 지시문 파일 한쪽의 상태.
/// <paramref name="Imports"/>는 본문에 있는 <c>@경로</c>를 읽어온 결과다. 값이 null이면 읽지 못한 것으로 경고 대상이다.
/// </summary>
public sealed record InstructionSource(
    bool Exists,
    string? Content,
    IReadOnlyDictionary<string, string?> Imports)
{
    /// <summary>파일이 없는 쪽.</summary>
    public static InstructionSource Missing { get; } =
        new(false, null, new Dictionary<string, string?>(StringComparer.Ordinal));

    /// <summary>import가 없는 내용.</summary>
    public static InstructionSource Of(string content) =>
        new(true, content, new Dictionary<string, string?>(StringComparer.Ordinal));
}

/// <summary>
/// 도구 하나에 대해 마이그레이션이 알아야 하는 사실. 앱·인프라가 <c>IProvider</c> 에서 모아 넘긴다.
/// Core 는 파일 이름도 문법도 스스로 알지 않는다.
/// </summary>
/// <param name="Tool">도구.</param>
/// <param name="RulesFileName">그 도구가 읽는 지시문 파일 이름 (<c>CLAUDE.md</c> 등).</param>
/// <param name="SupportsImports">
/// <c>@경로</c> import 문법을 읽을 수 있는가.
/// <b>확인된 것만 true 다.</b> 모르면 false — 그러면 옮길 때 내용을 그 자리에 펼쳐 두므로 안전한 쪽으로 틀린다.
/// </param>
public sealed record InstructionToolInfo(ToolKind Tool, string RulesFileName, bool SupportsImports);

/// <summary>여러 도구의 지시문을 비교한 결과와 권장 방향.</summary>
/// <param name="Tools">비교에 참여한 도구들. 화면 순서를 그대로 따른다.</param>
/// <param name="Sources">도구별 파일 상태.</param>
/// <param name="Bodies">도구별 본문(마커 블록 제외).</param>
/// <param name="Left">diff 의 왼쪽 도구.</param>
/// <param name="Right">diff 의 오른쪽 도구.</param>
public sealed record InstructionMigrationPlan(
    IReadOnlyList<InstructionToolInfo> Tools,
    IReadOnlyDictionary<ToolKind, InstructionSource> Sources,
    IReadOnlyDictionary<ToolKind, string> Bodies,
    ToolKind Left,
    ToolKind Right,
    IReadOnlyList<DiffLine> Diff,
    MigrationDirection? Suggested,
    IReadOnlyList<MigrationNote> Notes)
{
    /// <summary>하나라도 있어야 옮길 것이 있다.</summary>
    public bool CanMigrate => Sources.Values.Any(source => source.Exists);

    /// <summary>비교한 두 본문이 완전히 같은가.</summary>
    public bool BodiesEqual => Diff.All(line => line.Kind == DiffKind.Same);

    /// <summary>그 도구의 파일 상태. 모르는 도구는 "없음".</summary>
    public InstructionSource SourceOf(ToolKind tool) =>
        Sources.TryGetValue(tool, out var source) ? source : InstructionSource.Missing;

    /// <summary>그 도구의 본문. 모르는 도구는 빈 문자열.</summary>
    public string BodyOf(ToolKind tool) =>
        Bodies.TryGetValue(tool, out var body) ? body : string.Empty;

    /// <summary>그 도구의 파일 이름.</summary>
    public string FileNameOf(ToolKind tool) =>
        Tools.FirstOrDefault(info => info.Tool == tool)?.RulesFileName ?? string.Empty;

    /// <summary>
    /// 지금 고를 수 있는 방향 전부. 파일이 있는 도구에서 나머지 도구로 가는 모든 짝이다.
    /// 도구가 셋이면 최대 여섯 방향이 된다.
    /// </summary>
    public IReadOnlyList<MigrationDirection> Directions =>
    [
        .. Tools
            .Where(origin => SourceOf(origin.Tool).Exists)
            .SelectMany(
                _ => Tools,
                (origin, destination) => new MigrationDirection(origin.Tool, destination.Tool))
            .Where(direction => direction.IsValid),
    ];
}

/// <summary>마이그레이션이 만들어 낸 대상 파일 내용.</summary>
public sealed record MigrationResult(ToolKind Target, string Content, IReadOnlyList<MigrationNote> Warnings);
