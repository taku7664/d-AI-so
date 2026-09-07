namespace Daiso.Core.Tests.Instructions;

/// <summary>REQUIREMENTS §6.5 도구별 지시문.</summary>
public sealed class InstructionTemplateTests
{
    private readonly InstructionTemplate _template = new();

    [Fact]
    public void The_Claude_body_imports_the_rules_file()
    {
        var body = _template.For(ToolKind.Claude, InstructionTemplate.DefaultRulesFileName);

        body.Should().Be("@PROJECT_RULES.daiso\nRules above are YAML. Priority MUST > SHOULD > MAY.");
    }

    [Fact]
    public void The_Codex_body_tells_the_tool_to_read_the_rules_file()
    {
        var body = _template.For(ToolKind.Codex, InstructionTemplate.DefaultRulesFileName);

        body.Should().Contain("PROJECT_RULES.daiso");
        body.Should().Contain("MUST > SHOULD > MAY");
        body.Should().NotContain("@PROJECT_RULES.daiso");
    }

    [Theory]
    [InlineData(ToolKind.Claude)]
    [InlineData(ToolKind.Codex)]
    public void The_rules_file_name_is_honoured(ToolKind tool)
    {
        _template.For(tool, "OTHER_RULES.daiso").Should().Contain("OTHER_RULES.daiso");
    }

    [Fact]
    public void A_blank_rules_file_name_is_rejected()
    {
        var act = () => _template.For(ToolKind.Claude, "  ");

        act.Should().Throw<ArgumentException>();
    }
}
