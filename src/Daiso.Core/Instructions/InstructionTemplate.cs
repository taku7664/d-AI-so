namespace Daiso.Core;

/// <summary>
/// REQUIREMENTS §6.5 지시문. Claude는 `@파일명` import 문법을 쓰고, Codex와 Antigravity는 읽기 지시문을 쓴다.
/// </summary>
public sealed class InstructionTemplate : IInstructionTemplate
{
    /// <summary>규칙 파일의 기본 이름.</summary>
    public const string DefaultRulesFileName = "PROJECT_RULES.daiso";

    private const string PriorityOrder = "Priority MUST > SHOULD > MAY.";

    /// <inheritdoc />
    public string For(ToolKind tool, string rulesFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesFileName);

        // ToolKind 는 더 이상 enum 이 아니라 id 를 감싼 값이다(플러그인). 그래서 id 로 가른다
        return tool.Id switch
        {
            "claude" => $"@{rulesFileName}\nRules above are YAML. {PriorityOrder}",
            // 은퇴한 Gemini CLI 의 @import 는 .md 만 받았고, Antigravity 가 .daiso 를 import 할 수 있는지는 확인하지 못했다.
            // 읽기 지시문은 어느 쪽이든 통하므로 Codex 와 같은 형태로 둔다.
            // 모르는 도구(플러그인)도 읽기 지시문이 가장 안전하다 — import 를 못 읽는 도구가 그 줄을 글자 그대로 읽는 것보다 낫다
            _ => $"Read and follow the rules in ./{rulesFileName} (YAML). {PriorityOrder}",
        };
    }
}
