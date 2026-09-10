using Daiso.Core;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// 도구마다 모델 목록을 얻는 길이 다르다 (ARCHITECTURE §3.2 <c>ListModelsAsync</c>).
/// Claude 는 별칭 + 계정 캐시, Codex 는 CLI 가 받아 둔 캐시 파일, Antigravity 는 <c>agy models</c> 명령.
/// 어느 쪽이든 못 읽으면 <b>빈 목록이지 예외가 아니다</b> — 모델 칸은 거들 뿐이고 사람은 인자 칸에 직접 적을 수 있다.
/// </summary>
public sealed class ModelListTests : IDisposable
{
    private readonly string _home = Fixtures.CreateTempDirectory();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // 임시 폴더 정리 실패는 테스트 결과와 무관하다.
        }
    }

    // ── Claude ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claude_offers_the_help_aliases_even_without_any_file()
    {
        IProvider provider = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Select(model => model.Id).Should().Equal("fable", "opus", "sonnet");
    }

    [Fact]
    public async Task Claude_adds_the_models_the_account_is_offered()
    {
        await File.WriteAllTextAsync(Path.Combine(_home, ".claude.json"), """
            {
              "oauthAccount": { "emailAddress": "someone@example.com" },
              "additionalModelOptionsCache": [
                { "value": "claude-fable-5-1[1m]", "label": "Fable", "description": "Fable 5.1 · Most capable" },
                { "value": "opus", "label": "already an alias" },
                { "label": "no value" }
              ]
            }
            """);
        IProvider provider = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Select(model => model.Id).Should().Equal("fable", "opus", "sonnet", "claude-fable-5-1[1m]");
        models[^1].Name.Should().Be("Fable");
        models[^1].Description.Should().Be("Fable 5.1 · Most capable");
    }

    [Fact]
    public async Task Claude_with_a_broken_file_still_offers_the_aliases()
    {
        await File.WriteAllTextAsync(Path.Combine(_home, ".claude.json"), "{ not json");
        IProvider provider = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Should().HaveCount(3);
    }

    // ── Codex ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Codex_lists_what_its_model_menu_shows()
    {
        var config = Directory.CreateDirectory(Path.Combine(_home, ".codex")).FullName;
        await File.WriteAllTextAsync(Path.Combine(config, "models_cache.json"), """
            {
              "fetched_at": "2026-09-10T09:36:09Z",
              "models": [
                { "slug": "gpt-6-astra", "display_name": "GPT-6-Astra", "description": "Most capable", "visibility": "list" },
                { "slug": "codex-auto-review", "display_name": "Codex Auto Review", "visibility": "hide" },
                { "slug": "gpt-5.5", "visibility": "list" }
              ]
            }
            """);
        IProvider provider = new CodexProvider(new ProviderHome(_home));

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Select(model => model.Id).Should().Equal("gpt-6-astra", "gpt-5.5");
        models.Select(model => model.Name).Should().Equal("GPT-6-Astra", "gpt-5.5");
    }

    [Fact]
    public async Task Codex_that_never_ran_has_no_list()
    {
        IProvider provider = new CodexProvider(new ProviderHome(_home));

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Should().BeEmpty();
    }

    // ── Antigravity ───────────────────────────────────────────────────────

    [Fact]
    public async Task Antigravity_asks_its_models_command()
    {
        var runner = new FakeCommandRunner(
            "Fetching available models...\r\n" +
            "gemini-3.8-flash-high\tGemini 3.8 Flash (High)\r\n" +
            "claude-sonnet-4-6\tClaude Sonnet 4.6 (Thinking)\r\n");
        IProvider provider = new AntigravityProvider(new ProviderHome(_home), new NoCredentials(), runner);

        var models = await provider.ListModelsAsync(CancellationToken.None);

        runner.Arguments.Should().Be("models");
        models.Select(model => model.Id).Should().Equal("gemini-3.8-flash-high", "claude-sonnet-4-6");
        models[0].Name.Should().Be("Gemini 3.8 Flash (High)");
    }

    [Fact]
    public async Task Antigravity_that_cannot_answer_has_no_list()
    {
        IProvider provider = new AntigravityProvider(new ProviderHome(_home), new NoCredentials(), new FakeCommandRunner(null));

        var models = await provider.ListModelsAsync(CancellationToken.None);

        models.Should().BeEmpty();
    }

    // ── 열린 세션에서 바꾸기 ──────────────────────────────────────────────

    [Fact]
    public void Claude_and_Antigravity_switch_by_name_and_Codex_only_opens_its_picker()
    {
        IProvider claude = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());
        IProvider codex = new CodexProvider(new ProviderHome(_home));
        IProvider antigravity = new AntigravityProvider(new ProviderHome(_home), new NoCredentials(), new FakeCommandRunner(null));

        claude.ModelSwitchInput("opus").Should().Be("/model opus");
        antigravity.ModelSwitchInput("gemini-3.8-flash-high").Should().Be("/model gemini-3.8-flash-high");
        codex.ModelSwitchInput("gpt-5.5").Should().BeNull(
            because: "Codex 의 /model 이 이름을 받는다는 근거가 없다. 붙여 보내면 AI 에게 가는 메시지가 될 수 있다");

        foreach (var provider in new[] { claude, codex, antigravity })
        {
            provider.ModelSwitchInput(null).Should().Be("/model", because: "세 도구 모두 /model 만 치면 자기 고르기 창을 연다");
        }
    }

    private sealed class FakeCommandRunner(string? output) : ICommandRunner
    {
        public string? Arguments { get; private set; }

        public Task<string?> RunAsync(string executable, string arguments, TimeSpan timeout, CancellationToken ct)
        {
            Arguments = arguments;
            return Task.FromResult(output);
        }
    }

    private sealed class NoCredentials : ICredentialProbe
    {
        public bool Exists(string target) => false;
    }
}
