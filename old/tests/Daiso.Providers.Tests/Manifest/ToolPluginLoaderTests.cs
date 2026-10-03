using Daiso.Core;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

namespace Daiso.Providers.Tests.Manifest;

/// <summary>
/// 매니페스트 한 장이 도구가 되는가, 그리고 <b>깨진 것이 나머지를 죽이지 않는가</b>
/// (docs/PLUGIN_PLAN.md Stage 4 완료 기준).
/// </summary>
public sealed class ToolPluginLoaderTests : IDisposable
{
    private const string Good = """
        schema: 1
        id: mycli
        name: My CLI
        short: My
        vendor: Someone
        initial: M
        color: "#4E86F7"
        executable: mycli.cmd
        install:
          command: npm install -g my-cli
        sessionsRoot: "{USERPROFILE}/.mycli/sessions"
        rules:
          fileName: MYCLI.md
        context:
          - "{PROJECT}/MYCLI.md"
        resume: "--resume {id}"
        auth:
          files:
            - path: "{USERPROFILE}/.mycli/auth.json"
              required: true
        models:
          list:
            - id: fast
              name: 빠른 모델
        """;

    private readonly string _root = Fixtures.CreateTempDirectory();

    private string Tools => Path.Combine(_root, "tools");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더 정리 실패는 테스트 결과와 무관하다.
        }
    }

    private ToolPluginLoader Loader() => new(Tools, new ProviderHome(_root));

    private void Write(string name, string text)
    {
        Directory.CreateDirectory(Tools);
        File.WriteAllText(Path.Combine(Tools, name), text);
    }

    [Fact]
    public void A_manifest_becomes_a_tool()
    {
        Write("mycli.yaml", Good);

        var load = Loader().Load().Should().ContainSingle().Subject;

        load.Ok.Should().BeTrue(because: string.Join(" · ", load.Errors));

        var tool = load.Provider!;
        tool.Kind.Should().Be(ToolKind.Of("mycli"));
        tool.Display.Title.Should().Be("My CLI");
        tool.Display.Vendor.Should().Be("Someone");
        tool.ExecutableName.Should().Be("mycli.cmd");
        tool.RulesFileName.Should().Be("MYCLI.md");
        tool.InstallCommand.Should().Be("npm install -g my-cli");
    }

    [Fact]
    public void Paths_get_their_placeholders_filled()
    {
        Write("mycli.yaml", Good);

        var tool = Loader().Load().Single().Provider!;

        tool.SessionsRoot.Should().Be(Path.Combine(_root, ".mycli", "sessions"));
        tool.AuthFiles.Should().ContainSingle()
            .Which.Path.Should().Be(Path.Combine(_root, ".mycli", "auth.json"));
        tool.ContextFilePatterns(@"C:\Work\Proj").Should().ContainSingle()
            .Which.Should().Be(Path.Combine(@"C:\Work\Proj", "MYCLI.md"));
    }

    [Fact]
    public void Resume_arguments_come_from_the_format()
    {
        Write("mycli.yaml", Good);

        var tool = Loader().Load().Single().Provider!;
        var session = new SessionInfo(
            tool.Kind, "abc-123", "f.jsonl", null, default, default, 0, 0, 0, null, TokenUsage.Zero, null, false, false);

        tool.BuildResumeArguments(session).Should().Be("--resume abc-123");
    }

    [Fact]
    public async Task Models_listed_in_the_manifest_are_offered()
    {
        Write("mycli.yaml", Good);

        var tool = Loader().Load().Single().Provider!;
        var models = await tool.ListModelsAsync(CancellationToken.None);

        models.Should().ContainSingle().Which.Name.Should().Be("빠른 모델");
    }

    [Fact]
    public void A_broken_manifest_does_not_take_the_others_down()
    {
        Write("mycli.yaml", Good);
        Write("broken.yaml", "id: [이건 글이 아니다");

        var loads = Loader().Load();

        loads.Should().HaveCount(2);
        loads.Count(load => load.Ok).Should().Be(1, because: "하나가 깨져도 나머지는 실려야 한다");
        loads.Single(load => !load.Ok).Errors.Should().NotBeEmpty(because: "왜 안 됐는지 말해야 고칠 수 있다");
    }

    [Fact]
    public void A_manifest_cannot_claim_a_built_in_id()
    {
        Write("fake-claude.yaml", Good.Replace("id: mycli", "id: claude", StringComparison.Ordinal));

        var load = Loader().Load().Should().ContainSingle().Subject;

        load.Ok.Should().BeFalse();
        load.Errors.Should().ContainMatch("*claude*",
            because: "앱에 묻어 있는 도구를 가로채면 인덱스와 계정 보관함이 섞인다");
    }

    [Fact]
    public void Two_manifests_with_one_id_are_both_rejected()
    {
        Write("a.yaml", Good);
        Write("b.yaml", Good.Replace("name: My CLI", "name: Other CLI", StringComparison.Ordinal));

        var loads = Loader().Load();

        loads.Should().HaveCount(2);
        loads.Should().OnlyContain(load => !load.Ok,
            because: "먼저 읽은 것이 이기게 하면 파일 이름 순서에 따라 앱이 달라진다");
    }

    [Fact]
    public void An_unknown_key_is_an_error()
    {
        Write("typo.yaml", Good + "\nexcutable: oops\n");

        var load = Loader().Load().Single();

        load.Ok.Should().BeFalse();
        load.Errors.Should().ContainMatch("*excutable*",
            because: "조용히 무시하면 오타를 넣고 왜 안 되는지 못 찾는다");
    }

    [Fact]
    public void A_manifest_from_a_newer_app_is_refused_by_name()
    {
        Write("future.yaml", Good.Replace("schema: 1", "schema: 99", StringComparison.Ordinal));

        var load = Loader().Load().Single();

        load.Ok.Should().BeFalse();
        load.Errors.Should().ContainMatch("*99*", because: "판을 맞춰 보지 않으면 조용히 반쯤 동작한다");
    }

    [Fact]
    public void No_folder_means_no_plugins_and_no_noise()
    {
        Loader().Load().Should().BeEmpty(because: "플러그인을 안 쓰는 사람에게 오류를 보여 줄 이유가 없다");
    }

    [Fact]
    public async Task A_tool_without_an_adapter_simply_has_no_sessions()
    {
        Write("mycli.yaml", Good);

        var tool = Loader().Load().Single().Provider!;
        var sessions = new List<SessionInfo>();

        await foreach (var session in tool.EnumerateSessionsAsync(CancellationToken.None))
        {
            sessions.Add(session);
        }

        sessions.Should().BeEmpty(
            because: "어댑터가 없으면 세션 기록만 빈다. 실행·설치·규칙은 그대로 된다 (docs/PLUGIN_PLAN.md §5 D)");
    }
}
