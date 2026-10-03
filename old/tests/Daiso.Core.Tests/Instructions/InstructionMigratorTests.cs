namespace Daiso.Core.Tests.Instructions;

/// <summary>REQUIREMENTS §7, ARCHITECTURE §5.6 — 한쪽만 있으면 생성, 둘 다 있으면 diff 후 방향 선택.</summary>
public sealed class InstructionMigratorTests
{
    private const string ClaudeBlock =
        "<!-- daiso:start -->\n@PROJECT_RULES.daiso\nRules above are YAML. Priority MUST > SHOULD > MAY.\n<!-- daiso:end -->\n";

    private const string CodexBody =
        "Read and follow the rules in ./PROJECT_RULES.daiso (YAML). Priority MUST > SHOULD > MAY.";

    private readonly InstructionMigrator _migrator = new();

    /// <summary>
    /// 이 테스트가 보는 세상: Claude 는 import 를 읽고 Codex 는 못 읽는다.
    /// 도구 목록을 밖에서 주는 것이 새 계약이다 (docs/REVIEW_BACKLOG.md A5).
    /// </summary>
    private static readonly IReadOnlyList<InstructionToolInfo> Tools =
    [
        new(ToolKind.Claude, "CLAUDE.md", SupportsImports: true),
        new(ToolKind.Codex, "AGENTS.md", SupportsImports: false),
    ];

    private static readonly MigrationDirection ClaudeToCodex = new(ToolKind.Claude, ToolKind.Codex);

    private static readonly MigrationDirection CodexToClaude = new(ToolKind.Codex, ToolKind.Claude);

    /// <summary>두 도구를 비교하는 계획. 왼쪽이 Claude, 오른쪽이 Codex 다.</summary>
    private InstructionMigrationPlan Plan(InstructionSource claude, InstructionSource codex) =>
        _migrator.Plan(
            Tools,
            new Dictionary<ToolKind, InstructionSource>
            {
                [ToolKind.Claude] = claude,
                [ToolKind.Codex] = codex,
            },
            ToolKind.Claude,
            ToolKind.Codex);

    [Fact]
    public void With_neither_file_there_is_nothing_to_do()
    {
        var plan = Plan(InstructionSource.Missing, InstructionSource.Missing);

        plan.CanMigrate.Should().BeFalse();
        plan.Suggested.Should().BeNull();
        plan.Diff.Should().BeEmpty();
        plan.Notes.Should().ContainSingle().Which.Key.Should().Be("MigrationNote_NoneExist");
    }

    [Fact]
    public void With_only_claude_the_suggested_direction_is_to_codex()
    {
        var plan = Plan(InstructionSource.Of("# 규칙\n한국어로 답한다\n"), InstructionSource.Missing);

        plan.Suggested.Should().Be(ClaudeToCodex);
        plan.Diff.Should().OnlyContain(line => line.Kind == DiffKind.Removed);
    }

    [Fact]
    public void With_only_codex_the_suggested_direction_is_to_claude()
    {
        var plan = Plan(InstructionSource.Missing, InstructionSource.Of("# 규칙\n"));

        plan.Suggested.Should().Be(CodexToClaude);
        plan.Diff.Should().OnlyContain(line => line.Kind == DiffKind.Added);
    }

    [Fact]
    public void With_both_files_the_direction_is_left_to_the_user()
    {
        var plan = Plan(InstructionSource.Of("가\n"), InstructionSource.Of("나\n"));

        plan.Suggested.Should().BeNull();
        plan.Notes.Should().Contain(note => note.Key == "MigrationNote_BodiesDiffer");
    }

    [Fact]
    public void Marker_blocks_are_excluded_from_the_comparison()
    {
        // 같은 본문 + 서로 다른(도구별) 마커 블록 → 옮길 것이 없다고 봐야 한다.
        var claude = InstructionSource.Of("# 규칙\n한국어로 답한다\n\n" + ClaudeBlock);
        var codex = InstructionSource.Of(
            "# 규칙\n한국어로 답한다\n\n<!-- daiso:start -->\n" + CodexBody + "\n<!-- daiso:end -->\n");

        var plan = Plan(claude, codex);

        plan.BodiesEqual.Should().BeTrue();
        plan.Notes.Should().Contain(note => note.Key == "MigrationNote_BodiesSame");
    }

    [Fact]
    public void The_diff_reports_line_numbers_of_the_side_that_has_the_line()
    {
        var plan = Plan(InstructionSource.Of("가\n같음\n"), InstructionSource.Of("같음\n나\n"));

        plan.Diff.Should().SatisfyRespectively(
            first =>
            {
                first.Kind.Should().Be(DiffKind.Removed);
                first.Text.Should().Be("가");
                first.LeftLine.Should().Be(1);
                first.RightLine.Should().BeNull();
            },
            second =>
            {
                second.Kind.Should().Be(DiffKind.Same);
                second.Text.Should().Be("같음");
                second.LeftLine.Should().Be(2);
                second.RightLine.Should().Be(1);
            },
            third =>
            {
                third.Kind.Should().Be(DiffKind.Added);
                third.Text.Should().Be("나");
                third.LeftLine.Should().BeNull();
                third.RightLine.Should().Be(2);
            });
    }

    [Fact]
    public void Rendering_to_codex_puts_the_codex_block_back()
    {
        var plan = Plan(
            InstructionSource.Of("# 규칙\n한국어로 답한다\n\n" + ClaudeBlock),
            InstructionSource.Missing);

        var result = _migrator.Render(plan, ClaudeToCodex);

        result.Target.Should().Be(ToolKind.Codex);
        result.Content.Should().Contain("# 규칙").And.Contain(CodexBody);
        result.Content.Should().NotContain("@PROJECT_RULES.daiso");
    }

    [Fact]
    public void Rendering_to_claude_puts_the_import_block_back()
    {
        var plan = Plan(
            InstructionSource.Missing,
            InstructionSource.Of("# 규칙\n\n<!-- daiso:start -->\n" + CodexBody + "\n<!-- daiso:end -->\n"));

        var result = _migrator.Render(plan, CodexToClaude);

        result.Target.Should().Be(ToolKind.Claude);
        result.Content.Should().Contain("@PROJECT_RULES.daiso");
        result.Content.Should().NotContain("Read and follow");
    }

    [Fact]
    public void Rendering_without_a_source_file_fails()
    {
        var plan = Plan(InstructionSource.Missing, InstructionSource.Of("# 규칙\n"));

        var act = () => _migrator.Render(plan, ClaudeToCodex);

        act.Should().Throw<InvalidOperationException>().WithMessage("*CLAUDE.md*");
    }

    [Fact]
    public void Claude_imports_are_inlined_when_moving_to_codex()
    {
        var claude = new InstructionSource(
            true,
            "# 규칙\n@docs/style.md\n끝\n",
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["docs/style.md"] = "들여쓰기는 4칸\n" });

        var result = _migrator.Render(Plan(claude, InstructionSource.Missing), ClaudeToCodex);

        result.Content.Should().Contain("들여쓰기는 4칸");
        result.Content.Should().NotContain("@docs/style.md");
        result.Warnings.Should().ContainSingle().Which.Key.Should().Be("MigrationNote_ImportInlined");
    }

    [Fact]
    public void Unreadable_imports_keep_the_line_and_warn()
    {
        var claude = new InstructionSource(
            true,
            "# 규칙\n@없는파일.md\n",
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["없는파일.md"] = null });

        var result = _migrator.Render(Plan(claude, InstructionSource.Missing), ClaudeToCodex);

        result.Content.Should().Contain("@없는파일.md");
        result.Warnings.Should().ContainSingle().Which.Key.Should().Be("MigrationNote_ImportUnreadable");
    }

    [Fact]
    public void Imports_are_not_expanded_when_moving_to_claude()
    {
        // Claude는 import 문법을 쓰므로 AGENTS.md → CLAUDE.md 방향에서는 펼칠 것이 없다.
        var codex = InstructionSource.Of("# 규칙\n@docs/style.md\n");

        var result = _migrator.Render(Plan(InstructionSource.Missing, codex), CodexToClaude);

        result.Content.Should().Contain("@docs/style.md");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Imports_inside_code_fences_are_left_alone()
    {
        var claude = new InstructionSource(
            true,
            "# 규칙\n```\n@docs/style.md\n```\n",
            new Dictionary<string, string?>(StringComparer.Ordinal) { ["docs/style.md"] = "펼치면 안 된다\n" });

        var result = _migrator.Render(Plan(claude, InstructionSource.Missing), ClaudeToCodex);

        result.Content.Should().Contain("@docs/style.md");
        result.Content.Should().NotContain("펼치면 안 된다");
    }

    [Fact]
    public void Rendering_twice_gives_the_same_content()
    {
        var plan = Plan(InstructionSource.Of("# 규칙\n한국어\n\n" + ClaudeBlock), InstructionSource.Missing);

        var first = _migrator.Render(plan, ClaudeToCodex);
        var second = _migrator.Render(Plan(InstructionSource.Of(first.Content), InstructionSource.Missing),
            ClaudeToCodex);

        second.Content.Should().Be(first.Content);
    }
}
