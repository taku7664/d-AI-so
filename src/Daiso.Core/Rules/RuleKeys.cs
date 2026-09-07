namespace Daiso.Core;

/// <summary>.daiso YAML 키 이름. 파싱·검증·직렬화가 공유한다.</summary>
internal static class RuleKeys
{
    internal const string Daiso = "daiso";
    internal const string Name = "name";
    internal const string Description = "description";
    internal const string Global = "global";
    internal const string Rules = "rules";

    internal const string When = "when";
    internal const string Then = "then";

    internal const string Action = "action";
    internal const string PriorityKey = "priority";

    internal const string And = "and";
    internal const string Or = "or";
    internal const string Not = "not";

    internal static readonly string[] TopLevel = [Daiso, Name, Description, Global, Rules];
    internal static readonly string[] RuleLevel = [When, Then];
    internal static readonly string[] ActionLevel = [Action, PriorityKey];
    internal static readonly string[] ConditionOperators = [And, Or, Not];
}
