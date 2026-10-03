namespace Daiso.Core;

/// <summary>PROJECT_RULES.daiso 한 파일에 담기는 규칙 프리셋. (ARCHITECTURE §2.1)</summary>
/// <remarks>
/// record의 기본 Equals는 IReadOnlyList 멤버를 참조 비교한다.
/// 모델 비교는 <see cref="IRulePresetSerializer.Serialize"/> 결과 문자열로 한다.
/// </remarks>
public sealed record RulePreset(
    int Daiso,
    string Name,
    string? Description,
    IReadOnlyList<RuleAction> Global,
    IReadOnlyList<Rule> Rules);

/// <summary>조건과 그 조건이 만족될 때 취할 행동 묶음.</summary>
public sealed record Rule(Condition When, IReadOnlyList<RuleAction> Then);

/// <summary>행동 한 줄과 그 중요도.</summary>
public sealed record RuleAction(string Action, Priority Priority = Priority.Should);

/// <summary>행동의 중요도. MUST &gt; SHOULD &gt; MAY.</summary>
public enum Priority
{
    Must,
    Should,
    May,
}

/// <summary>조건식 트리 노드.</summary>
public abstract record Condition;

/// <summary>자연어 문장 하나로 표현된 말단 조건.</summary>
public sealed record LeafCondition(string Text) : Condition;

/// <summary>모든 하위 조건이 만족되어야 하는 조건.</summary>
public sealed record AndCondition(IReadOnlyList<Condition> Items) : Condition;

/// <summary>하위 조건 중 하나 이상이 만족되면 되는 조건.</summary>
public sealed record OrCondition(IReadOnlyList<Condition> Items) : Condition;

/// <summary>하위 조건의 부정.</summary>
