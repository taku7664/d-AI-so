using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Daiso.Core;

/// <summary>
/// .daiso YAML 노드 트리를 ARCHITECTURE §2.1 검증 규칙 표대로 검사한다.
/// 위반 시 <see cref="RuleParseException"/>을 원인 노드의 줄·칸과 함께 던진다.
/// </summary>
public sealed class RuleValidator
{
    /// <summary>지원하는 스키마 버전.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>문서 루트 노드를 검증한다.</summary>
    /// <exception cref="RuleParseException">스키마 위반.</exception>
    public void Validate(YamlNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var map = RequireMapping(root, "문서 최상위는 맵이어야 한다");
        RequireKnownKeys(map, RuleKeys.TopLevel);

        ValidateSchemaVersion(map);
        ValidateName(map);

        if (TryGet(map, RuleKeys.Description, out var description))
        {
            RequireScalar(description, "'description' 값은 문자열이어야 한다");
        }

        if (TryGet(map, RuleKeys.Global, out var global))
        {
            foreach (var action in RequireSequence(global, "'global' 값은 목록이어야 한다"))
            {
                ValidateAction(action);
            }
        }

        if (TryGet(map, RuleKeys.Rules, out var rules))
        {
            foreach (var rule in RequireSequence(rules, "'rules' 값은 목록이어야 한다"))
            {
                ValidateRule(rule);
            }
        }
    }

    private static void ValidateSchemaVersion(YamlMappingNode map)
    {
        if (!TryGet(map, RuleKeys.Daiso, out var node))
        {
            throw Error(map, "'daiso' 키가 없다. 스키마 버전 1을 명시해야 한다");
        }

        var scalar = RequireScalar(node, "'daiso' 값은 정수 1이어야 한다");
        if (!int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            || version != SupportedSchemaVersion)
        {
            throw Error(node, "지원하지 않는 스키마 버전이다. 'daiso'는 1이어야 한다");
        }
    }

    private static void ValidateName(YamlMappingNode map)
    {
        if (!TryGet(map, RuleKeys.Name, out var node))
        {
            throw Error(map, "'name' 키가 없다");
        }

        var scalar = RequireScalar(node, "'name' 값은 문자열이어야 한다");
        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw Error(node, "'name'은 빈 문자열일 수 없다");
        }
    }

    private static void ValidateRule(YamlNode node)
    {
        var map = RequireMapping(node, "'rules' 항목은 맵이어야 한다");
        RequireKnownKeys(map, RuleKeys.RuleLevel);

        if (!TryGet(map, RuleKeys.When, out var when))
        {
            throw Error(map, "규칙에 'when' 키가 없다");
        }

        ValidateCondition(when);

        if (!TryGet(map, RuleKeys.Then, out var then))
        {
            throw Error(map, "규칙에 'then' 키가 없다");
        }

        var actions = RequireSequence(then, "'then' 값은 목록이어야 한다");
        if (actions.Count == 0)
        {
            throw Error(then, "'then'에 행동이 하나도 없다");
        }

        foreach (var action in actions)
        {
            ValidateAction(action);
        }
    }

    private static void ValidateCondition(YamlNode node)
    {
        switch (node)
        {
            case YamlScalarNode scalar:
                if (string.IsNullOrWhiteSpace(scalar.Value))
                {
                    throw Error(node, "조건 문장이 비어 있다");
                }

                return;

            case YamlMappingNode map:
                ValidateOperatorCondition(map);
                return;

            default:
                throw Error(node, "조건은 문장 또는 and/or/not 맵이어야 한다");
        }
    }

    private static void ValidateOperatorCondition(YamlMappingNode map)
    {
        if (map.Children.Count != 1)
        {
            throw Error(
                map,
                $"조건 맵의 키는 정확히 하나여야 한다 (and | or | not). 현재 {map.Children.Count}개");
        }

        var first = map.Children.First();
        var keyNode = first.Key;
        var valueNode = first.Value;
        var key = RequireScalar(keyNode, "조건 맵의 키는 문자열이어야 한다").Value ?? string.Empty;

        if (!RuleKeys.ConditionOperators.Contains(key, StringComparer.Ordinal))
        {
            throw Error(keyNode, $"알 수 없는 조건 연산자 '{key}'. 허용: and, or");
        }

        var items = RequireSequence(valueNode, $"'{key}' 값은 목록이어야 한다");
        if (items.Count == 0)
        {
            throw Error(valueNode, $"'{key}' 항목이 하나도 없다");
        }

        foreach (var item in items)
        {
            ValidateCondition(item);
        }
    }

    private static void ValidateAction(YamlNode node)
    {
        var map = RequireMapping(node, "행동 항목은 'action' 키를 가진 맵이어야 한다");
        RequireKnownKeys(map, RuleKeys.ActionLevel);

        if (!TryGet(map, RuleKeys.Action, out var action))
        {
            throw Error(map, "행동에 'action' 키가 없다");
        }

        var scalar = RequireScalar(action, "'action' 값은 문자열이어야 한다");
        if (string.IsNullOrWhiteSpace(scalar.Value))
        {
            throw Error(action, "'action' 문장이 비어 있다");
        }

        if (!TryGet(map, RuleKeys.PriorityKey, out var priority))
        {
            return;
        }

        var priorityScalar = RequireScalar(priority, "'priority' 값은 문자열이어야 한다");
        if (!PriorityNames.TryParse(priorityScalar.Value, out _))
        {
            throw Error(
                priority,
                $"'priority' 값은 MUST, SHOULD, MAY 중 하나여야 한다. 현재 '{priorityScalar.Value}'");
        }
    }

    private static void RequireKnownKeys(YamlMappingNode map, IReadOnlyList<string> allowed)
    {
        foreach (var key in map.Children.Keys)
        {
            var scalar = RequireScalar(key, "키는 문자열이어야 한다");
            var name = scalar.Value ?? string.Empty;
            if (!allowed.Contains(name, StringComparer.Ordinal))
            {
                throw Error(key, $"알 수 없는 키 '{name}'. 허용: {string.Join(", ", allowed)}");
            }
        }
    }

    private static bool TryGet(YamlMappingNode map, string key, out YamlNode value)
    {
        if (map.Children.TryGetValue(new YamlScalarNode(key), out var found))
        {
            value = found;
            return true;
        }

        value = null!;
        return false;
    }

    private static YamlMappingNode RequireMapping(YamlNode node, string message) =>
        node as YamlMappingNode ?? throw Error(node, message);

    private static YamlScalarNode RequireScalar(YamlNode node, string message) =>
        node as YamlScalarNode ?? throw Error(node, message);

    private static IList<YamlNode> RequireSequence(YamlNode node, string message) =>
        node is YamlSequenceNode sequence ? sequence.Children : throw Error(node, message);

    private static RuleParseException Error(YamlNode node, string message) =>
        new((int)node.Start.Line, (int)node.Start.Column, message);
}
