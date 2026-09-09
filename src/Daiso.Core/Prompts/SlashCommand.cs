namespace Daiso.Core.Prompts;

/// <summary>어디서 온 슬래시 명령인가. 목록에서 출처를 배지로 보여 준다.</summary>
public enum SlashCommandSource
{
    /// <summary>CLI에 붙박이로 있는 명령.</summary>
    BuiltIn,

    /// <summary>사용자 홈의 사용자 정의 명령·프롬프트.</summary>
    User,

    /// <summary>프로젝트 폴더의 명령.</summary>
    Project,

    /// <summary>사용자 스킬(Claude).</summary>
    Skill,
}

/// <summary>
/// 채팅 입력에서 `/`로 부를 수 있는 명령 하나. 내장 표와 디스크에서 읽은 것을 합친 결과. (FEATURE_PLAN 뒤로→`/` 선택기)
/// </summary>
/// <param name="Name">`/` 없이 이름만. 예: <c>compact</c>.</param>
/// <param name="Description">한 줄 설명. 없으면 null.</param>
/// <param name="Source">출처.</param>
public sealed record SlashCommand(string Name, string? Description, SlashCommandSource Source)
{
    /// <summary>입력 칸에 넣을 문자열. <c>/compact</c>.</summary>
    public string Invocation => "/" + Name;
}

/// <summary>도구마다 붙박이로 있는 슬래시 명령 표. 디스크에서 읽은 사용자·프로젝트 명령과 합쳐 목록을 만든다.</summary>
public static class BuiltInSlashCommands
{
    private static readonly (string Name, string Description)[] Claude =
    [
        ("clear", "대화를 비우고 새로 시작"),
        ("compact", "대화를 요약해 컨텍스트를 줄임"),
        ("config", "설정 열기"),
        ("cost", "이번 세션 토큰·비용 보기"),
        ("help", "명령 도움말"),
        ("init", "CLAUDE.md 만들기"),
        ("memory", "CLAUDE.md 편집"),
        ("model", "모델 고르기"),
        ("review", "코드 리뷰 요청"),
        ("resume", "지난 세션 이어서"),
    ];

    private static readonly (string Name, string Description)[] Codex =
    [
        ("clear", "대화를 비움"),
        ("compact", "대화를 요약해 컨텍스트를 줄임"),
        ("diff", "작업 트리 변경 보기"),
        ("help", "명령 도움말"),
        ("model", "모델 고르기"),
        ("new", "새 대화 시작"),
        ("quit", "종료"),
    ];

    /// <summary>Antigravity CLI(`agy`)의 내장 명령. 공식 CLI 참고 문서의 슬래시 명령 표를 따른다.</summary>
    private static readonly (string Name, string Description)[] Antigravity =
    [
        ("agents", "하위 에이전트 관리"),
        ("boost", "더 센 모델로 이번 요청을 다시"),
        ("clear", "새 대화 시작"),
        ("config", "설정 열기"),
        ("fork", "앞 지점에서 대화를 갈라내기"),
        ("keybindings", "단축키 편집"),
        ("permissions", "권한 관리"),
        ("resume", "지난 대화 골라 이어서"),
        ("rewind", "대화를 앞 지점으로 되감기"),
    ];

    /// <summary>그 도구의 내장 명령들.</summary>
    public static IReadOnlyList<SlashCommand> For(ToolKind tool)
    {
        var table = tool switch
        {
            ToolKind.Claude => Claude,
            ToolKind.Codex => Codex,
            ToolKind.Antigravity => Antigravity,
            _ => [],
        };

        return table
            .Select(entry => new SlashCommand(entry.Name, entry.Description, SlashCommandSource.BuiltIn))
            .ToList();
    }
}
