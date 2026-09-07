using System.Text;
using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;
using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.2 — .daiso 저장은 UTF-8 BOM 없음, 지시문은 마커 블록만.</summary>
public sealed class RuleFileServiceTests : IDisposable
{
    private readonly string _directory = Fixtures.CreateTempDirectory();
    private readonly RuleFileService _service = new();

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
    public void Save_then_load_keeps_the_preset()
    {
        var path = Path.Combine(_directory, "PROJECT_RULES.daiso");
        var preset = new RulePreset(1, "테스트", "설명", [new RuleAction("행동", Priority.Must)], [
            new Rule(new LeafCondition("조건"), [new RuleAction("행동 2", Priority.May)]),
        ]);

        _service.Save(preset, path);
        var loaded = _service.Load(path);

        new RulePresetSerializer().Serialize(loaded)
            .Should().Be(new RulePresetSerializer().Serialize(preset));
    }

    [Fact]
    public void Saved_files_have_no_byte_order_mark()
    {
        var path = Path.Combine(_directory, "no-bom.daiso");

        _service.Save(new RulePreset(1, "테스트", null, [], []), path);

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF]);
    }

    [Fact]
    public void Load_reports_the_position_of_a_schema_error()
    {
        var path = Path.Combine(_directory, "bad.daiso");
        File.WriteAllText(path, "daiso: 2\nname: X\n", new UTF8Encoding(false));

        var act = () => _service.Load(path);

        act.Should().Throw<RuleParseException>().Which.Line.Should().Be(1);
    }

    [Fact]
    public void EnsureInstruction_creates_both_instruction_files()
    {
        var project = Path.Combine(_directory, "project");

        _service.EnsureInstruction(project, Providers());

        var claude = File.ReadAllText(Path.Combine(project, "CLAUDE.md"), Encoding.UTF8);
        var codex = File.ReadAllText(Path.Combine(project, "AGENTS.md"), Encoding.UTF8);

        claude.Should().Contain("<!-- daiso:start -->");
        claude.Should().Contain("@PROJECT_RULES.daiso");
        codex.Should().Contain("Read and follow the rules in ./PROJECT_RULES.daiso");
    }

    [Fact]
    public void EnsureInstruction_is_idempotent()
    {
        var project = Path.Combine(_directory, "twice");

        _service.EnsureInstruction(project, Providers());
        var first = File.ReadAllBytes(Path.Combine(project, "CLAUDE.md"));

        _service.EnsureInstruction(project, Providers());
        var second = File.ReadAllBytes(Path.Combine(project, "CLAUDE.md"));

        second.Should().Equal(first);
    }

    [Fact]
    public void EnsureInstruction_keeps_existing_content_outside_the_block()
    {
        var project = Path.Combine(_directory, "existing");
        Directory.CreateDirectory(project);

        const string Original = "# 기존 문서\r\n\r\n지켜야 하는 문단\r\n";
        var path = Path.Combine(project, "CLAUDE.md");
        File.WriteAllText(path, Original, new UTF8Encoding(false));

        _service.EnsureInstruction(project, Providers());

        var updated = File.ReadAllText(path, Encoding.UTF8);
        updated.Should().StartWith(Original);
        updated.Should().Contain("<!-- daiso:end -->");
    }

    private static IEnumerable<IProvider> Providers()
    {
        var home = new ProviderHome(Path.GetTempPath());
        return [new ClaudeProvider(home, new FakeProcessProbe()), new CodexProvider(home)];
    }
}
