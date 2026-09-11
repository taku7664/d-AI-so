using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

namespace Daiso.Providers.Tests.Manifest;

/// <summary>README 의 "도구 직접 추가하기" 예제가 실제로 실린다. 문서의 예제가 안 도는 것이 가장 나쁘다.</summary>
public sealed class ReadmeExampleTests
{
    [Fact]
    public void The_readme_manifest_loads()
    {
        var home = Fixtures.CreateTempDirectory();
        var tools = Path.Combine(home, ".daiso", "tools");
        Directory.CreateDirectory(tools);

        File.WriteAllText(
            Path.Combine(tools, "mycli.yaml"),
            """
            schema: 1
            id: mycli
            name: My CLI
            short: My
            vendor: 우리팀
            initial: M
            color: "#7A5AF8"
            executable: mycli.cmd
            install:
              uri: https://example.com/mycli
              # command: npm install -g mycli
            sessionsRoot: "{USERPROFILE}/.mycli/sessions"
            rules:
              fileName: MYCLI.md
            context:
              - "{PROJECT}/MYCLI.md"
            resume: "--resume {id}"
            """);

        var loads = new ToolPluginLoader(tools, new ProviderHome(home)).Load();

        loads.Should().ContainSingle();
        loads[0].Errors.Should().BeEmpty(because: string.Join(" / ", loads[0].Errors));

        var tool = loads[0].Provider!;

        tool.Kind.Should().Be(ToolKind.Of("mycli"));
        tool.Display.Title.Should().Be("My CLI");
        tool.Display.Short.Should().Be("My");
        tool.SessionsRoot.Should().Be(Path.Combine(home, ".mycli", "sessions"));
        tool.RulesFileName.Should().Be("MYCLI.md");
        tool.ContextFilePatterns(@"C:\Work\Proj").Should().Contain(@"C:\Work\Proj\MYCLI.md");
        tool.HasAdapter.Should().BeFalse(because: "1단계 예제에는 어댑터가 없다");
    }
}
