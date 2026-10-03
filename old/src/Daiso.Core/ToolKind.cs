namespace Daiso.Core;

/// <summary>
/// AI CLI 도구 하나를 가리키는 id.
/// <para>
/// <b>enum 이 아니다.</b> 빌드된 앱에 도구를 더할 수 있어야 하는데 바깥에서는 enum 값을 만들 수 없다
/// (docs/PLUGIN_PLAN.md §1 · Stage 1). 값을 감싼 구조체라 플러그인이 자기 id 로 하나를 만들 수 있다.
/// </para>
/// <para>
/// <b>이 값은 디스크에 적힌다.</b> SQLite 인덱스의 <c>sessions.tool</c> 열, 계정 보관함의 <c>meta.json</c>,
/// 그리고 계정 보관함 <b>폴더 이름</b>이다. 그래서 id 는 파일 이름으로 안전해야 하고, 한 번 정하면 바꾸지 않는다.
/// 사람이 id 를 바꾸면 그 도구의 지난 세션·계정은 다른 도구의 것이 된다.
/// </para>
/// <para>
/// 정본은 소문자다. 다만 <see cref="TryParse"/> 는 <b>대소문자를 가리지 않는다</b> —
/// 옛 기록에는 enum 이름 그대로 <c>"Claude"</c> 가 적혀 있고, 그것도 읽을 수 있어야 한다.
/// </para>
/// </summary>
public readonly record struct ToolKind : IComparable<ToolKind>
{
    /// <summary>id 에 쓸 수 있는 길이. 폴더 이름이 되므로 짧게 묶는다.</summary>
    private const int MinLength = 2;

    private const int MaxLength = 32;

    private readonly string? _id;

    private ToolKind(string id) => _id = id;

    /// <summary>Anthropic Claude Code.</summary>
    public static ToolKind Claude { get; } = new("claude");

    /// <summary>OpenAI Codex CLI.</summary>
    public static ToolKind Codex { get; } = new("codex");

    /// <summary>
    /// Google Antigravity CLI(`agy`). 2026-06-18에 개인 계정용 Gemini CLI가 요청을 멈추고 이것으로 대체됐다.
    /// </summary>
    public static ToolKind Antigravity { get; } = new("antigravity");

    /// <summary>앱에 묻어 있는 도구. 플러그인이 이 id 를 다시 쓰지 못한다.</summary>
    public static IReadOnlyList<ToolKind> BuiltIn { get; } = [Claude, Codex, Antigravity];

    /// <summary>디스크에 적히는 값. 소문자.</summary>
    public string Id => _id ?? string.Empty;

    /// <summary>아무 도구도 가리키지 않는 값(<c>default</c>)인가.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_id);

    /// <summary>
    /// id 로 도구를 가리킨다. 꼴이 틀리면 던진다 — 플러그인 매니페스트는 <see cref="TryParse"/> 로 읽어
    /// 틀린 것을 사람에게 보여 준다(docs/PLUGIN_PLAN.md Stage 6).
    /// </summary>
    public static ToolKind Of(string id) =>
        TryParse(id, out var kind)
            ? kind
            : throw new ArgumentException($"도구 id 로 쓸 수 없다: '{id}'", nameof(id));

    /// <summary>
    /// id 를 읽는다. 소문자·숫자·<c>-</c> 로만 이루어진 2~32 글자여야 한다.
    /// 대소문자는 가리지 않고 소문자로 눕힌다.
    /// </summary>
    public static bool TryParse(string? id, out ToolKind kind)
    {
        kind = default;

        if (id is null)
        {
            return false;
        }

        var trimmed = id.Trim();

        if (trimmed.Length is < MinLength or > MaxLength
            || !trimmed.All(IsAllowed)
            || !char.IsAsciiLetter(trimmed[0]))
        {
            return false;
        }

        kind = new ToolKind(trimmed.ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// 쓸 수 있는 글자. 첫 글자가 글자여야 한다는 규칙은 <see cref="TryParse"/> 가 본다 —
    /// 주석은 그렇게 적혀 있는데 검사에 없어서 <c>-x</c>·<c>99</c> 같은 id 가 통과했다 (2026-09-11 점검).
    /// 이 값은 계정 보관함의 폴더 이름이 된다.
    /// </summary>
    private static bool IsAllowed(char ch) =>
        ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';

    /// <summary>이름 순. 목록을 안정적으로 놓을 때만 쓴다 — 화면 순서는 표시 규칙이 따로 정한다.</summary>
    public int CompareTo(ToolKind other) => string.CompareOrdinal(Id, other.Id);

    public override string ToString() => Id;
}
