namespace Daiso.Core;

/// <summary>마이그레이션 방향. (REQUIREMENTS §7)</summary>
public enum MigrationDirection
{
    /// <summary>CLAUDE.md 내용을 AGENTS.md로.</summary>
    ClaudeToCodex,

    /// <summary>AGENTS.md 내용을 CLAUDE.md로.</summary>
    CodexToClaude,
}

/// <summary>diff 한 줄의 성격. 왼쪽이 CLAUDE.md, 오른쪽이 AGENTS.md다.</summary>
public enum DiffKind
{
    /// <summary>양쪽에 같은 줄.</summary>
    Same,

    /// <summary>오른쪽(AGENTS.md)에만 있는 줄.</summary>
    Added,

    /// <summary>왼쪽(CLAUDE.md)에만 있는 줄.</summary>
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

/// <summary>좌우 비교 결과와 권장 방향.</summary>
public sealed record InstructionMigrationPlan(
    InstructionSource Claude,
    InstructionSource Codex,
    string ClaudeBody,
    string CodexBody,
    IReadOnlyList<DiffLine> Diff,
    MigrationDirection? Suggested,
    IReadOnlyList<string> Notes)
{
    /// <summary>양쪽 다 없으면 할 일이 없다.</summary>
    public bool CanMigrate => Claude.Exists || Codex.Exists;

    /// <summary>마커 블록을 뺀 본문이 완전히 같은가.</summary>
    public bool BodiesEqual => Diff.All(line => line.Kind == DiffKind.Same);
}

/// <summary>마이그레이션이 만들어 낸 대상 파일 내용.</summary>
public sealed record MigrationResult(ToolKind Target, string Content, IReadOnlyList<string> Warnings);
