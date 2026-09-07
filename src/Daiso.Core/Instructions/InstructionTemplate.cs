namespace Daiso.Core;

/// <summary>
/// REQUIREMENTS §6.5 지시문. Claude는 `@파일명` import 문법을 쓰고, Codex는 import가 없어 읽기 지시문을 쓴다.
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

        return tool switch
        {
            ToolKind.Claude => $"@{rulesFileName}\nRules above are YAML. {PriorityOrder}",
            ToolKind.Codex => $"Read and follow the rules in ./{rulesFileName} (YAML). {PriorityOrder}",
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "알 수 없는 도구"),
        };
    }
}
