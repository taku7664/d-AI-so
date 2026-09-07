namespace Daiso.Core;

/// <summary>Context Doctor가 읽은 컨텍스트 파일 하나. (ARCHITECTURE §2.4)</summary>
/// <param name="Order">로드 순서. 0부터.</param>
public sealed record ContextFile(string Path, string Kind, string Content, bool Exists, int Order);

/// <summary>두 개 이상 파일에 같은 내용으로 등장한 줄.</summary>
public sealed record DuplicateLine(string NormalizedText, IReadOnlyList<string> Files);

/// <summary>상반되는 지시로 보이는 줄 짝.</summary>
public sealed record ConflictHint(string FileA, string LineA, string FileB, string LineB, string Reason);

/// <summary>도구 하나에 대한 컨텍스트 리포트.</summary>
public sealed record ContextReport(
    ToolKind Tool,
    IReadOnlyList<ContextFile> Files,
    int TotalChars,
    IReadOnlyList<DuplicateLine> Duplicates,
    IReadOnlyList<ConflictHint> Conflicts);
