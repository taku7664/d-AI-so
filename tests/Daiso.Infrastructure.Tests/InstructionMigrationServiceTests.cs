using System.Text;
using Daiso.Core;
using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.6 — 원본은 읽기만, 쓰는 것은 대상 파일 하나. import는 재귀 해석.</summary>
public sealed class InstructionMigrationServiceTests : IDisposable
{
    private readonly string _directory = Fixtures.CreateTempDirectory();
    private readonly InstructionMigrationService _service = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더 정리 실패는 테스트 결과와 무관하다.
        }
    }

    [Fact]
    public void An_empty_folder_has_nothing_to_migrate()
    {
        var plan = _service.Plan(_directory);

        plan.CanMigrate.Should().BeFalse();
        plan.Suggested.Should().BeNull();
    }

    [Fact]
    public void With_only_claude_the_plan_points_at_codex()
    {
        Write("CLAUDE.md", "# 규칙\n한국어로 답한다\n");

        _service.Plan(_directory).Suggested.Should().Be(MigrationDirection.ClaudeToCodex);
    }

    [Fact]
    public void Apply_creates_the_missing_file_and_leaves_the_source_alone()
    {
        const string Source = "# 규칙\n한국어로 답한다\n";
        Write("CLAUDE.md", Source);

        var result = _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        result.Target.Should().Be(ToolKind.Codex);
        Read("AGENTS.md").Should().Contain("한국어로 답한다").And.Contain("Read and follow");
        Read("CLAUDE.md").Should().Be(Source);
    }

    [Fact]
    public void Apply_with_dry_run_writes_nothing()
    {
        Write("CLAUDE.md", "# 규칙\n");

        var result = _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: true);

        result.Content.Should().Contain("Read and follow");
        File.Exists(Path.Combine(_directory, "AGENTS.md")).Should().BeFalse();
    }

    [Fact]
    public void Created_files_have_no_byte_order_mark()
    {
        Write("CLAUDE.md", "# 규칙\n");

        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        File.ReadAllBytes(Path.Combine(_directory, "AGENTS.md")).Take(3)
            .Should().NotEqual(Encoding.UTF8.GetPreamble());
    }

    [Fact]
    public void Applying_twice_gives_the_same_file()
    {
        Write("CLAUDE.md", "# 규칙\n한국어\n");

        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);
        var first = Read("AGENTS.md");
        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        Read("AGENTS.md").Should().Be(first);
    }

    [Fact]
    public void Applying_the_same_content_does_not_touch_the_file()
    {
        Write("CLAUDE.md", "# 규칙\n");
        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        var path = Path.Combine(_directory, "AGENTS.md");
        var before = File.GetLastWriteTimeUtc(path);

        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        File.GetLastWriteTimeUtc(path).Should().Be(before);
    }

    [Fact]
    public void Imports_are_read_from_disk_and_inlined_for_codex()
    {
        Write("CLAUDE.md", "# 규칙\n@docs/style.md\n");
        Write(Path.Combine("docs", "style.md"), "들여쓰기는 4칸\n");

        var result = _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        Read("AGENTS.md").Should().Contain("들여쓰기는 4칸").And.NotContain("@docs/style.md");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("인라인 전개");
    }

    [Fact]
    public void Nested_imports_are_expanded_too()
    {
        Write("CLAUDE.md", "@a.md\n");
        Write("a.md", "가\n@b.md\n");
        Write("b.md", "나\n");

        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        Read("AGENTS.md").Should().Contain("가").And.Contain("나").And.NotContain("@b.md");
    }

    [Fact]
    public void A_circular_import_does_not_hang_and_warns()
    {
        Write("CLAUDE.md", "@a.md\n");
        Write("a.md", "가\n@b.md\n");
        Write("b.md", "나\n@a.md\n");

        var result = _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        result.Warnings.Should().NotBeEmpty();
        Read("AGENTS.md").Should().Contain("가").And.Contain("나").And.Contain("@a.md");
    }

    [Fact]
    public void A_missing_import_keeps_the_line_and_warns()
    {
        Write("CLAUDE.md", "@없는파일.md\n");

        var result = _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("읽지 못해");
        Read("AGENTS.md").Should().Contain("@없는파일.md");
    }

    [Fact]
    public void Bytes_outside_the_marker_block_of_the_target_are_replaced_by_the_source_body()
    {
        Write("CLAUDE.md", "# 새 규칙\n");
        Write("AGENTS.md", "# 낡은 규칙\n");

        _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        Read("AGENTS.md").Should().Contain("# 새 규칙").And.NotContain("낡은");
    }

    [Fact]
    public void Migrating_to_claude_writes_the_import_form()
    {
        Write("AGENTS.md", "# 규칙\n한국어\n");

        _service.Apply(_directory, MigrationDirection.CodexToClaude, dryRun: false);

        Read("CLAUDE.md").Should().Contain("@PROJECT_RULES.daiso").And.Contain("한국어");
    }

    [Fact]
    public void Migrating_without_the_source_file_fails()
    {
        Write("AGENTS.md", "# 규칙\n");

        var act = () => _service.Apply(_directory, MigrationDirection.ClaudeToCodex, dryRun: false);

        act.Should().Throw<InvalidOperationException>();
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(_directory, relativePath), Encoding.UTF8);
}
