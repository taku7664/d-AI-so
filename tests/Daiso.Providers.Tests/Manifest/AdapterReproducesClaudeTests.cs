using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

namespace Daiso.Providers.Tests.Manifest;

/// <summary>
/// <b>Stage 5 의 완료 기준</b> (docs/PLUGIN_PLAN.md): 매니페스트 + 어댑터만으로 내장 도구를 재현할 수 있는가.
/// <para>
/// 앱에 묻어 있는 <see cref="ClaudeProvider"/> 와, 그것을 감싼 참조 어댑터를 <b>같은 표본에 대고</b> 견준다.
/// 값이 하나라도 다르면 프로토콜이 무언가를 흘린 것이다 — 그 자리가 어디인지 이 테스트가 짚어 준다.
/// </para>
/// <para>
/// 진짜 프로세스를 띄운다. 느리지만 여기서 재는 것이 <b>바깥 프로세스로 갈라도 값이 같은가</b>라서,
/// 프로세스를 흉내 내면 재는 의미가 없다.
/// </para>
/// </summary>
public sealed class AdapterReproducesClaudeTests : IDisposable
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

    /// <summary>참조 어댑터 실행 파일. 테스트 프로젝트가 참조해 두어 출력 폴더에 함께 놓인다.</summary>
    private static string AdapterPath =>
        Path.Combine(AppContext.BaseDirectory, "Daiso.Adapter.Claude.exe");

    /// <summary>
    /// Claude 를 <b>매니페스트로만</b> 기술한 것. 세션 읽기는 전부 어댑터가 한다.
    /// <para>
    /// 어댑터 명령은 YAML <b>홑따옴표</b>로 감싼다 — 겹따옴표 안에서는 <c>\</c> 가 이스케이프라
    /// 윈도 경로를 그대로 적을 수 없다. 홑따옴표는 <c>''</c> 말고는 아무것도 해석하지 않는다.
    /// </para>
    /// </summary>
    private string Manifest() =>
        """
        schema: 1
        id: claude-via-adapter
        name: Claude (어댑터)
        vendor: Anthropic
        initial: C
        color: "#D97757"
        executable: claude.cmd
        install:
          command: npm install -g @anthropic-ai/claude-code
        sessionsRoot: "{USERPROFILE}/.claude/projects"
        rules:
          fileName: CLAUDE.md
        resume: "--resume {id}"
        adapter:
          command: 'ADAPTER'
        """
        .Replace("ADAPTER", $"\"{AdapterPath}\" --home \"{_home}\"", StringComparison.Ordinal);

    private ManifestProvider Plugin()
    {
        var tools = Path.Combine(_home, "tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "claude-via-adapter.yaml"), Manifest());

        var load = new ToolPluginLoader(tools, new ProviderHome(_home)).Load().Single();

        load.Ok.Should().BeTrue(because: string.Join(" · ", load.Errors));

        return load.Provider!;
    }

    [Fact]
    public async Task The_adapter_answers_hello()
    {
        Fixtures.CreateClaudeHome(_home);
        using var plugin = Plugin();

        File.Exists(AdapterPath).Should().BeTrue(because: $"참조 어댑터가 출력 폴더에 있어야 한다: {AdapterPath}");

        var ok = await plugin.HandshakeAsync(CancellationToken.None);

        ok.Should().BeTrue(because: plugin.AdapterError ?? "판이 맞아야 나머지가 다 성립한다");
    }

    [Fact]
    public async Task Sessions_read_through_the_adapter_match_the_built_in_tool()
    {
        Fixtures.CreateClaudeHome(_home);

        var builtIn = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());
        using var plugin = Plugin();

        var expected = await Enumerate(builtIn);
        var actual = await Enumerate(plugin);

        actual.Should().HaveCount(expected.Count, because: plugin.AdapterError ?? "세션 수가 같아야 한다");

        foreach (var (want, got) in expected.Zip(actual))
        {
            // 도구 id 는 다르다(플러그인이니까). 나머지는 다 같아야 한다
            Describe(got).Should().Be(Describe(want));
        }
    }

    [Fact]
    public async Task A_scanned_session_matches_the_built_in_tool()
    {
        Fixtures.CreateClaudeHome(_home);

        var builtIn = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());
        using var plugin = Plugin();

        var path = (await Enumerate(builtIn)).First(session => session.FilePath.EndsWith("session-basic.jsonl", StringComparison.Ordinal)).FilePath;

        var want = await builtIn.ReadSessionInfoAsync(path, CancellationToken.None);
        var got = await plugin.ReadSessionInfoAsync(path, CancellationToken.None);

        Describe(got).Should().Be(
            Describe(want),
            because: "본문을 훑어 얻는 값(카운트·사용량·첫 프롬프트)까지 프로토콜이 옮겨야 한다");
    }

    [Fact]
    public async Task Messages_read_through_the_adapter_match_the_built_in_tool()
    {
        Fixtures.CreateClaudeHome(_home);

        var builtIn = new ClaudeProvider(new ProviderHome(_home), new FakeProcessProbe());
        using var plugin = Plugin();

        var path = (await Enumerate(builtIn)).First(session => session.FilePath.EndsWith("session-basic.jsonl", StringComparison.Ordinal)).FilePath;

        var want = await Messages(builtIn, path);
        var got = await Messages(plugin, path);

        got.Should().Equal(
            want,
            because: "역할·사이드체인·시각까지 같아야 한다. 하나라도 흘리면 세션 화면이 달라진다");
    }

    /// <summary>견줄 수 있게 한 줄로 편다. 도구 id 와 파일 크기는 뺀다 — 플러그인은 id 가 다르다.</summary>
    private static string Describe(SessionInfo session) =>
        $"{session.Id} | {session.ProjectPath} | {session.StartedAt:O} | {session.ModifiedAt:O} | "
        + $"{session.UserMessageCount}/{session.AssistantMessageCount} | "
        + $"{session.Usage.Input}/{session.Usage.Output}/{session.Usage.CacheCreate}/{session.Usage.CacheRead}/{session.Usage.Model} | "
        + $"{session.ToolVersion} | {session.IsArchived} | {session.FirstPrompt}";

    private static async Task<List<SessionInfo>> Enumerate(IProvider provider)
    {
        var list = new List<SessionInfo>();

        await foreach (var session in provider.EnumerateSessionsAsync(CancellationToken.None))
        {
            list.Add(session);
        }

        return [.. list.OrderBy(session => session.FilePath, StringComparer.Ordinal)];
    }

    private static async Task<List<string>> Messages(IProvider provider, string path)
    {
        var list = new List<string>();

        await foreach (var message in provider.ReadMessagesAsync(path, 0, CancellationToken.None))
        {
            list.Add($"{message.At:O} {message.Role} {message.IsSidechain} {message.Text}");
        }

        return list;
    }
}
