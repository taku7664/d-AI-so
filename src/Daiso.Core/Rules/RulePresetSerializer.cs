using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Daiso.Core;

/// <summary>
/// .daiso YAML ↔ <see cref="RulePreset"/> 변환. (ARCHITECTURE §2.1, §3.1)
/// 출력은 정규 형식이며 키 순서가 고정된다. 주석·공백은 보존하지 않는다.
/// </summary>
public sealed class RulePresetSerializer : IRulePresetSerializer
{
    private const string ExplicitDocumentEnd = "...\n";

    private readonly RuleValidator _validator;

    public RulePresetSerializer()
        : this(new RuleValidator())
    {
    }

    public RulePresetSerializer(RuleValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
    }

    /// <inheritdoc />
    public RulePreset Parse(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var root = LoadRoot(yaml);
        _validator.Validate(root);
        return BuildPreset((YamlMappingNode)root);
    }

    /// <inheritdoc />
    public string Serialize(RulePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        var root = new YamlMappingNode
        {
            { Key(RuleKeys.Daiso), Plain(preset.Daiso.ToString(CultureInfo.InvariantCulture)) },
            { Key(RuleKeys.Name), Text(preset.Name) },
        };

        if (preset.Description is not null)
        {
            root.Add(Key(RuleKeys.Description), Text(preset.Description));
        }

        root.Add(Key(RuleKeys.Global), Actions(preset.Global));
        root.Add(Key(RuleKeys.Rules), RuleNodes(preset.Rules));

        return Emit(root);
    }

    private static YamlNode LoadRoot(string yaml)
    {
        var stream = new YamlStream();

        try
        {
            using var reader = new StringReader(yaml);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            throw new RuleParseException((int)ex.Start.Line, (int)ex.Start.Column, YamlMessage(ex));
        }

        if (stream.Documents.Count == 0)
        {
            throw new RuleParseException(1, 1, "빈 문서다");
        }

        return stream.Documents[0].RootNode;
    }

    private static string YamlMessage(YamlException ex) =>
        ex.InnerException is null ? ex.Message : $"{ex.Message} ({ex.InnerException.Message})";

    // ── 모델 만들기 (검증 통과한 트리만 들어온다) ─────────────────────────

    private static RulePreset BuildPreset(YamlMappingNode root)
    {
        var global = TryGet(root, RuleKeys.Global) is YamlSequenceNode globalNode
            ? globalNode.Children.Select(BuildAction).ToList()
            : [];

        var rules = TryGet(root, RuleKeys.Rules) is YamlSequenceNode rulesNode
            ? rulesNode.Children.Select(BuildRule).ToList()
            : [];

        return new RulePreset(
            RuleValidator.SupportedSchemaVersion,
            ScalarValue(TryGet(root, RuleKeys.Name)!),
            TryGet(root, RuleKeys.Description) is { } description ? ScalarValue(description) : null,
            global,
            rules);
    }

    private static Rule BuildRule(YamlNode node)
    {
        var map = (YamlMappingNode)node;
        var then = (YamlSequenceNode)TryGet(map, RuleKeys.Then)!;
        return new Rule(
            BuildCondition(TryGet(map, RuleKeys.When)!),
            then.Children.Select(BuildAction).ToList());
    }

    private static RuleAction BuildAction(YamlNode node)
    {
        var map = (YamlMappingNode)node;
        var priority = Priority.Should;

        if (TryGet(map, RuleKeys.PriorityKey) is { } priorityNode)
        {
            PriorityNames.TryParse(ScalarValue(priorityNode), out priority);
        }

        return new RuleAction(ScalarValue(TryGet(map, RuleKeys.Action)!), priority);
    }

    private static Condition BuildCondition(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            return new LeafCondition(scalar.Value ?? string.Empty);
        }

        var map = (YamlMappingNode)node;
        var first = map.Children.First();
        var op = ((YamlScalarNode)first.Key).Value;

        var items = ((YamlSequenceNode)first.Value).Children.Select(BuildCondition).ToList();
        return string.Equals(op, RuleKeys.And, StringComparison.Ordinal)
            ? new AndCondition(items)
            : new OrCondition(items);
    }

    private static YamlNode? TryGet(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string ScalarValue(YamlNode node) => ((YamlScalarNode)node).Value ?? string.Empty;

    // ── YAML 노드 만들기 ─────────────────────────────────────────────────

    private static YamlSequenceNode Actions(IReadOnlyList<RuleAction> actions)
    {
        var sequence = new YamlSequenceNode();

        foreach (var action in actions)
        {
            sequence.Add(new YamlMappingNode
            {
                { Key(RuleKeys.Action), Text(action.Action) },
                { Key(RuleKeys.PriorityKey), Plain(PriorityNames.ToYaml(action.Priority)) },
            });
        }

        return sequence;
    }

    private static YamlSequenceNode RuleNodes(IReadOnlyList<Rule> rules)
    {
        var sequence = new YamlSequenceNode();

        foreach (var rule in rules)
        {
            sequence.Add(new YamlMappingNode
            {
                { Key(RuleKeys.When), ConditionNode(rule.When) },
                { Key(RuleKeys.Then), Actions(rule.Then) },
            });
        }

        return sequence;
    }

    private static YamlNode ConditionNode(Condition condition) => condition switch
    {
        LeafCondition leaf => Text(leaf.Text),
        AndCondition and => Operator(RuleKeys.And, and.Items),
        OrCondition or => Operator(RuleKeys.Or, or.Items),
        _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, "알 수 없는 조건 종류"),
    };

    private static YamlMappingNode Operator(string op, IReadOnlyList<Condition> items)
    {
        var sequence = new YamlSequenceNode();

        foreach (var item in items)
        {
            sequence.Add(ConditionNode(item));
        }

        return new YamlMappingNode { { Key(op), sequence } };
    }

    private static YamlScalarNode Key(string name) => new(name) { Style = ScalarStyle.Plain };

    private static YamlScalarNode Plain(string value) => new(value) { Style = ScalarStyle.Plain };

    /// <summary>사용자 텍스트. 인용 필요 여부는 에미터가 판단한다.</summary>
    private static YamlScalarNode Text(string value) => new(value) { Style = ScalarStyle.Any };

    private static string Emit(YamlMappingNode root)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        var emitter = new Emitter(writer, EmitterSettings.Default.WithIndentedSequences());
        new YamlStream(new YamlDocument(root)).Save(emitter, assignAnchors: false);

        var yaml = writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        // YamlStream.Save는 문서 끝에 명시적 종료 표시 "..."를 붙인다. .daiso는 단일 문서라 필요 없다.
        return yaml.EndsWith(ExplicitDocumentEnd, StringComparison.Ordinal)
            ? yaml[..^ExplicitDocumentEnd.Length]
            : yaml;
    }
}
