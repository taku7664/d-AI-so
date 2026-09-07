using System.Text;

namespace Daiso.Core;

/// <summary>
/// REQUIREMENTS §6.4 양식으로 프리셋을 렌더한다.
/// 조건식 문자열화·괄호 규칙은 ARCHITECTURE §2.1을 따른다.
/// </summary>
/// <remarks>
/// §6.4 양식에는 프리셋 description 자리가 없으므로 렌더에도 넣지 않는다.
/// 줄바꿈은 항상 LF다. 파일에 넣을 때 개행을 맞추는 일은 호출자 몫이다.
/// </remarks>
public sealed class MarkdownRuleRenderer : IMarkdownRuleRenderer
{
    /// <summary>리프 텍스트를 큰따옴표로 감싸야 하는 문자들.</summary>
    private static readonly char[] OperatorCharacters = ['&', '|', '!', '(', ')'];

    /// <inheritdoc />
    public string Render(RulePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var builder = new StringBuilder();
        builder.Append("## ").Append(preset.Name).Append('\n');

        if (preset.Global.Count > 0)
        {
            builder.Append('\n');
            AppendActions(builder, preset.Global);
        }

        foreach (var rule in preset.Rules)
        {
            builder.Append('\n');
            builder.Append("### ").Append(Describe(rule.When, parent: null)).Append('\n');
            builder.Append('\n');
            AppendActions(builder, rule.Then);
        }

        return builder.ToString();
    }

    /// <summary>조건식을 `A &amp; (B | C)` 형태 한 줄로 만든다.</summary>
    public string Describe(Condition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return Describe(condition, parent: null);
    }

    private static void AppendActions(StringBuilder builder, IReadOnlyList<RuleAction> actions)
    {
        foreach (var action in actions)
        {
            builder
                .Append(action.Action)
                .Append(" (")
                .Append(PriorityNames.ToYaml(action.Priority))
                .Append(")\n");
        }
    }

    private static string Describe(Condition condition, Condition? parent) => condition switch
    {
        LeafCondition leaf => Quote(leaf.Text),
        NotCondition not => "!" + Describe(not.Item, not),
        AndCondition and => Group(Join(and.Items, and, " & "), and, parent),
        OrCondition or => Group(Join(or.Items, or, " | "), or, parent),
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "알 수 없는 조건 종류"),
    };

    private static string Join(IReadOnlyList<Condition> items, Condition parent, string separator) =>
        string.Join(separator, items.Select(item => Describe(item, parent)));

    /// <summary>
    /// 부모와 연산자가 다른 복합(and/or) 자식은 괄호로 감싼다.
    /// not의 자식이 and/or면 항상 감싼다. not 자체는 전위 단항이라 감싸지 않는다 (`!!A`).
    /// </summary>
    private static string Group(string rendered, Condition self, Condition? parent) => parent switch
    {
        null => rendered,
        NotCondition => $"({rendered})",
        _ when parent.GetType() != self.GetType() => $"({rendered})",
        _ => rendered,
    };

    /// <summary>연산자 문자를 포함한 리프는 큰따옴표로 감싸 경계를 분명히 한다.</summary>
    private static string Quote(string text) =>
        text.IndexOfAny(OperatorCharacters) >= 0 ? $"\"{text}\"" : text;
}
