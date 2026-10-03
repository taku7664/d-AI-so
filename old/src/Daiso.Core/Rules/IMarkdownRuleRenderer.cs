namespace Daiso.Core;

/// <summary><see cref="RulePreset"/>을 사람이 읽는 마크다운으로 변환한다. (REQUIREMENTS §6.4)</summary>
public interface IMarkdownRuleRenderer
{
    string Render(RulePreset preset);
}
