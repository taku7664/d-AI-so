namespace Daiso.Core;

/// <summary>도구별 지시문 본문을 만든다. (ARCHITECTURE §3.1, REQUIREMENTS §6.5)</summary>
public interface IInstructionTemplate
{
    /// <summary>마커 블록 안에 넣을 본문. <paramref name="rulesFileName"/>은 보통 "PROJECT_RULES.daiso".</summary>
    string For(ToolKind tool, string rulesFileName);
}
