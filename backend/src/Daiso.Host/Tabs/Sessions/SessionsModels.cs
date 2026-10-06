namespace Daiso.Host.Tabs.Sessions;

/// <summary>세션 한 줄.</summary>
/// <param name="Path">세션 파일 경로. 세션을 가리키는 열쇠로 쓴다.</param>
/// <param name="Tool">도구 id.</param>
/// <param name="Id">도구가 붙인 세션 id.</param>
/// <param name="Title">붙인 이름. 없으면 첫 질문을 다듬은 것. 둘 다 없으면 빈 문자열.</param>
/// <param name="Named">사람이 이름을 붙였는가.</param>
/// <param name="ProjectPath">프로젝트 폴더. 모르면 null.</param>
/// <param name="ProjectLabel">보여 줄 프로젝트 이름. 이름이 겹치면 상위 폴더까지 붙인다.</param>
/// <param name="StartedAt">시작한 때.</param>
/// <param name="ModifiedAt">마지막으로 바뀐 때.</param>
/// <param name="SizeBytes">파일 크기.</param>
/// <param name="UserMessages">사람이 보낸 메시지 수.</param>
/// <param name="AssistantMessages">모델이 보낸 메시지 수.</param>
/// <param name="Active">지금 실행 중인가. 실행 중이면 지울 수 없다. Claude 만 안다.</param>
/// <param name="Archived">Codex 가 보관함으로 옮긴 세션인가.</param>
/// <param name="Orphan">프로젝트 폴더가 없거나 사라졌는가.</param>
public sealed record SessionRow(
    string Path,
    string Tool,
    string Id,
    string Title,
    bool Named,
    string? ProjectPath,
    string ProjectLabel,
    DateTimeOffset StartedAt,
    DateTimeOffset ModifiedAt,
    long SizeBytes,
    int UserMessages,
    int AssistantMessages,
    bool Active,
    bool Archived,
    bool Orphan);

/// <summary>정리 기준. "골라 체크"와 요약의 "손볼 것"이 같은 값을 쓴다.</summary>
/// <param name="OlderThanDays">이 일수보다 오래 안 쓴 세션.</param>
/// <param name="LargerThanMegabytes">이 크기를 넘는 세션.</param>
public sealed record CleanupRule(int OlderThanDays, int LargerThanMegabytes);

/// <summary>세션 목록.</summary>
/// <param name="Sessions">마지막으로 바뀐 것부터.</param>
/// <param name="Cleanup">정리 기준.</param>
public sealed record SessionsResponse(IReadOnlyList<SessionRow> Sessions, CleanupRule Cleanup);

/// <summary>검색에 걸린 메시지 하나.</summary>
/// <param name="Role"><c>user</c> · <c>assistant</c> · <c>tool</c> · <c>system</c>.</param>
/// <param name="At">메시지 시각.</param>
/// <param name="Snippet">찾은 말 앞뒤를 자른 것.</param>
public sealed record SearchMatch(string Role, DateTimeOffset At, string Snippet);

/// <summary>검색에 걸린 세션 하나와 그 안의 메시지들.</summary>
/// <param name="Session">세션.</param>
/// <param name="Matches">최대 20개.</param>
public sealed record SearchGroup(SessionRow Session, IReadOnlyList<SearchMatch> Matches);

/// <summary>검색 결과.</summary>
/// <param name="Groups">세션마다 묶었다. 세션이 최근에 바뀐 것부터.</param>
/// <param name="Total">걸린 메시지 수. 서버가 200개에서 끊는다.</param>
public sealed record SearchResponse(IReadOnlyList<SearchGroup> Groups, int Total);

/// <summary>대화 한 줄.</summary>
/// <param name="Role"><c>user</c> · <c>assistant</c> · <c>tool</c> · <c>system</c>.</param>
/// <param name="At">시각.</param>
/// <param name="Text">본문. 화면이 1500자에서 접는다.</param>
public sealed record MessageRow(string Role, DateTimeOffset At, string Text);

/// <summary>대화 보기.</summary>
/// <param name="Messages">처음부터. 2000개까지.</param>
/// <param name="Truncated">2000개를 넘어 뒤를 잘랐는가.</param>
public sealed record MessagesResponse(IReadOnlyList<MessageRow> Messages, bool Truncated);

/// <summary>세션 이름을 붙이거나 떼는 요청.</summary>
/// <param name="Path">세션 파일 경로.</param>
/// <param name="Name">이름. 비우면 뗀다.</param>
public sealed record RenameRequest(string Path, string? Name);

/// <summary>세션을 지우는 요청.</summary>
/// <param name="Paths">세션 파일 경로들.</param>
/// <param name="Permanent">휴지통을 거치지 않고 바로 지울지.</param>
public sealed record DeleteRequest(IReadOnlyList<string> Paths, bool Permanent);

/// <summary>못 지운 세션.</summary>
public sealed record SkippedSession(string Path, string Reason);

/// <summary>지운 결과.</summary>
/// <param name="Deleted">지운 경로.</param>
/// <param name="Skipped">못 지운 것과 까닭. 실행 중 세션은 늘 여기 온다.</param>
public sealed record DeleteResponse(IReadOnlyList<string> Deleted, IReadOnlyList<SkippedSession> Skipped);

/// <summary>세션 하나를 가리키는 요청.</summary>
public sealed record SessionRequest(string Path);
