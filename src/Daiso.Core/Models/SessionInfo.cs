namespace Daiso.Core;

/// <summary>세션 파일 하나의 메타 정보. (ARCHITECTURE §2.2)</summary>
public sealed record SessionInfo(
    ToolKind Tool,
    string Id,
    string FilePath,
    string? ProjectPath,
    DateTimeOffset StartedAt,
    DateTimeOffset ModifiedAt,
    long SizeBytes,
    int UserMessageCount,
    int AssistantMessageCount,
    string? FirstPrompt,
    TokenUsage Usage,
    string? ToolVersion,
    bool IsArchived,
    bool IsActive);

/// <summary>토큰 사용량. 세션·일자·모델 단위로 합산한다.</summary>
public sealed record TokenUsage(long Input, long Output, long CacheCreate, long CacheRead, string? Model)
{
    public static readonly TokenUsage Zero = new(0, 0, 0, 0, null);

    /// <summary>두 사용량을 더한다. <see cref="Model"/>은 처음 등장한 non-null 값을 유지한다.</summary>
    public TokenUsage Add(TokenUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new TokenUsage(
            Input + other.Input,
            Output + other.Output,
            CacheCreate + other.CacheCreate,
            CacheRead + other.CacheRead,
            Model ?? other.Model);
    }

    /// <summary>입력·출력·캐시를 모두 더한 값.</summary>
    public long Total => Input + Output + CacheCreate + CacheRead;
}

/// <summary>세션 안의 메시지 한 건.</summary>
public sealed record SessionMessage(DateTimeOffset At, MessageRole Role, string Text, bool IsSidechain);

/// <summary>메시지 분류. (ARCHITECTURE §2.2 분류 원칙)</summary>
public enum MessageRole
{
    /// <summary>사람이 직접 입력한 텍스트.</summary>
    User,

    /// <summary>모델의 텍스트 응답.</summary>
    Assistant,

    /// <summary>도구 호출·결과. 검색 인덱스에 넣지 않는다.</summary>
    Tool,

    /// <summary>시스템·컴팩션 요약. 검색 인덱스에 넣지 않는다.</summary>
    System,
}
