using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;
using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.1 인덱싱·검색·사용량 집계.</summary>
public sealed class SqliteSessionIndexTests : IDisposable
{
    private readonly string _directory = Fixtures.CreateTempDirectory();

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

    // ── Rebuild / List ───────────────────────────────────────────────────

    [Fact]
    public async Task Rebuild_indexes_every_fixture_session()
    {
        using var index = CreateWithRealProviders();

        await index.RebuildAsync(new Progress<IndexProgress>(), default);
        var sessions = await index.ListAsync(SessionFilter.All, default);

        // Claude 2건 + Codex 3건(보관 1건 포함)
        sessions.Should().HaveCount(5);
    }

    [Fact]
    public async Task Rebuild_reports_progress()
    {
        using var index = CreateWithRealProviders();
        var reports = new List<IndexProgress>();

        await index.RebuildAsync(new Progress<IndexProgress>(reports.Add), default);

        // Progress는 비동기로 흘러오므로 마지막 보고만 확인한다.
        await Task.Delay(50);
        reports.Should().NotBeEmpty();
        reports[^1].Total.Should().Be(5);
    }

    [Fact]
    public async Task Excluding_archived_sessions_filters_them_out()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var withArchived = await index.ListAsync(SessionFilter.All, default);
        var withoutArchived = await index.ListAsync(
            SessionFilter.All with { IncludeArchived = false }, default);

        withArchived.Count(s => s.IsArchived).Should().Be(1);
        withoutArchived.Should().HaveCount(withArchived.Count - 1);
        withoutArchived.Should().OnlyContain(s => !s.IsArchived);
    }

    [Fact]
    public async Task Filtering_by_tool_keeps_only_that_tool()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var claude = await index.ListAsync(SessionFilter.All with { Tool = ToolKind.Claude }, default);

        claude.Should().HaveCount(2);
        claude.Should().OnlyContain(s => s.Tool == ToolKind.Claude);
    }

    [Fact]
    public async Task Filtering_by_minimum_size_drops_smaller_sessions()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var all = await index.ListAsync(SessionFilter.All, default);
        var threshold = all.Max(s => s.SizeBytes);

        var large = await index.ListAsync(SessionFilter.All with { MinSizeBytes = threshold }, default);

        large.Should().OnlyContain(s => s.SizeBytes >= threshold);
        large.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Indexed_sessions_keep_their_counts_and_usage()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var session = (await index.ListAsync(SessionFilter.All with { Tool = ToolKind.Claude }, default))
            .Single(s => Path.GetFileName(s.FilePath) == "session-basic.jsonl");

        session.UserMessageCount.Should().Be(2);
        session.AssistantMessageCount.Should().Be(2);
        session.FirstPrompt.Should().Be("더미 질문 1");
    }

    // ── 검색 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_three_character_substring_hits_the_trigram_index()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var hits = await index.SearchAsync("미 질문", default);

        hits.Should().NotBeEmpty();
        hits.Should().OnlyContain(h => h.Message.Text.Contains("미 질문", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_two_character_query_falls_back_to_LIKE()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var hits = await index.SearchAsync("질문", default);

        hits.Should().NotBeEmpty();
        hits.Should().OnlyContain(h => h.Message.Text.Contains("질문", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tool_and_system_messages_are_not_searchable()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        (await index.SearchAsync("도구 결과", default)).Should().BeEmpty();
        (await index.SearchAsync("컴팩션 요약", default)).Should().BeEmpty();
        (await index.SearchAsync("AGENTS 전문", default)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_search_hit_carries_its_session_and_a_snippet()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var hit = (await index.SearchAsync("더미 응답 1", default)).First();

        hit.Session.FilePath.Should().NotBeEmpty();
        hit.Message.Role.Should().Be(MessageRole.Assistant);
        hit.Snippet.Should().Contain("더미 응답 1");
    }

    [Fact]
    public async Task An_empty_query_returns_nothing()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        (await index.SearchAsync("   ", default)).Should().BeEmpty();
    }

    // ── 증분 갱신 ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unchanged_session_is_not_reopened()
    {
        var (provider, index, path) = CreateWithFakeProvider();
        using var owned = index;

        await index.RefreshAsync(default);
        provider.MessageReads.Should().HaveCount(1);

        await index.RefreshAsync(default);

        provider.MessageReads.Should().HaveCount(1, "변화가 없으면 파일을 다시 열지 않는다");
    }

    [Fact]
    public async Task A_grown_session_is_read_from_the_previous_offset()
    {
        var (provider, index, path) = CreateWithFakeProvider();
        using var owned = index;

        await index.RefreshAsync(default);
        var firstSize = provider.Sessions[0].SizeBytes;

        provider.Sessions[0] = provider.Sessions[0] with
        {
            SizeBytes = firstSize + 500,
            ModifiedAt = provider.Sessions[0].ModifiedAt.AddMinutes(1),
        };
        provider.SetMessages(
            path,
            Message(MessageRole.User, "더미 질문 1"),
            Message(MessageRole.Assistant, "더미 응답 1"),
            Message(MessageRole.User, "더미 질문 2"));

        await index.RefreshAsync(default);

        provider.MessageReads.Should().HaveCount(2);
        provider.MessageReads[1].Offset.Should().Be(firstSize);
    }

    [Fact]
    public async Task A_shrunk_session_is_read_from_the_start()
    {
        var (provider, index, path) = CreateWithFakeProvider();
        using var owned = index;

        await index.RefreshAsync(default);

        provider.Sessions[0] = provider.Sessions[0] with
        {
            SizeBytes = 10,
            ModifiedAt = provider.Sessions[0].ModifiedAt.AddMinutes(1),
        };

        await index.RefreshAsync(default);

        provider.MessageReads.Should().HaveCount(2);
        provider.MessageReads[1].Offset.Should().Be(0, "재작성된 파일은 처음부터 다시 읽는다");
    }

    [Fact]
    public async Task Appending_adds_to_the_existing_counts()
    {
        var (provider, index, path) = CreateWithFakeProvider();
        using var owned = index;

        await index.RefreshAsync(default);
        (await index.ListAsync(SessionFilter.All, default))[0].UserMessageCount.Should().Be(1);

        provider.Sessions[0] = provider.Sessions[0] with
        {
            SizeBytes = provider.Sessions[0].SizeBytes + 500,
            ModifiedAt = provider.Sessions[0].ModifiedAt.AddMinutes(1),
        };
        provider.SetMessages(
            path,
            Message(MessageRole.User, "더미 질문 1"),
            Message(MessageRole.Assistant, "더미 응답 1"),
            Message(MessageRole.User, "더미 질문 2"));

        await index.RefreshAsync(default);

        (await index.ListAsync(SessionFilter.All, default))[0].UserMessageCount.Should().Be(2);
    }

    [Fact]
    public async Task A_session_that_disappeared_is_removed_from_the_index()
    {
        var (provider, index, _) = CreateWithFakeProvider();
        using var owned = index;

        await index.RefreshAsync(default);
        provider.Sessions.Clear();

        await index.RefreshAsync(default);

        (await index.ListAsync(SessionFilter.All, default)).Should().BeEmpty();
    }

    // ── 사용량 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Additive_usage_is_summed_per_day()
    {
        var provider = new FakeProvider(ToolKind.Claude, usageIsAdditive: true);
        var path = Path.Combine(_directory, "claude.jsonl");
        provider.Sessions.Add(Session(path, ToolKind.Claude, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        provider.SetMessages(path, Message(MessageRole.User, "더미 질문 1"));
        provider.SetUsage(
            path,
            new UsageDay(new DateOnly(2026, 9, 1), new TokenUsage(10, 1, 0, 0, "claude-opus-5")),
            new UsageDay(new DateOnly(2026, 9, 2), new TokenUsage(20, 2, 0, 0, "claude-opus-5")));

        using var index = new SqliteSessionIndex([provider], DatabasePath());
        await index.RefreshAsync(default);

        var usage = await index.GetUsageAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        usage.Days.Should().HaveCount(2);
        usage.Days[0].Usage.Input.Should().Be(10);
        usage.Days[1].Usage.Input.Should().Be(20);
        usage.ByModel["claude-opus-5"].Input.Should().Be(30);
    }

    [Fact]
    public async Task Non_additive_usage_is_overwritten_per_session()
    {
        var provider = new FakeProvider(ToolKind.Codex, usageIsAdditive: false);
        var path = Path.Combine(_directory, "codex.jsonl");
        var started = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        provider.Sessions.Add(Session(path, ToolKind.Codex, started));
        provider.SetMessages(path, Message(MessageRole.User, "더미 질문 1"));
        provider.SetUsage(path, new UsageDay(default, new TokenUsage(100, 10, 0, 0, "gpt-5-codex")));

        using var index = new SqliteSessionIndex([provider], DatabasePath());
        await index.RefreshAsync(default);

        // 누적값이 커진 채로 다시 들어와도 더해지지 않고 덮어써야 한다.
        provider.Sessions[0] = provider.Sessions[0] with
        {
            SizeBytes = provider.Sessions[0].SizeBytes + 100,
            ModifiedAt = started.AddMinutes(5),
        };
        provider.SetUsage(path, new UsageDay(default, new TokenUsage(250, 25, 0, 0, "gpt-5-codex")));

        await index.RefreshAsync(default);

        var usage = await index.GetUsageAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        usage.Days.Should().HaveCount(1);
        usage.Days[0].Date.Should().Be(new DateOnly(2026, 9, 1));
        usage.Days[0].Usage.Input.Should().Be(250);
    }

    [Fact]
    public async Task Usage_can_be_narrowed_to_one_tool()
    {
        var claude = new FakeProvider(ToolKind.Claude, usageIsAdditive: true);
        var claudePath = Path.Combine(_directory, "claude.jsonl");
        claude.Sessions.Add(Session(claudePath, ToolKind.Claude, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        claude.SetUsage(claudePath, new UsageDay(new DateOnly(2026, 9, 1), new TokenUsage(10, 1, 0, 0, "claude-opus-5")));

        var gemini = new FakeProvider(ToolKind.Gemini, usageIsAdditive: true);
        var geminiPath = Path.Combine(_directory, "gemini.jsonl");
        gemini.Sessions.Add(Session(geminiPath, ToolKind.Gemini, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        gemini.SetUsage(geminiPath, new UsageDay(new DateOnly(2026, 9, 1), new TokenUsage(100, 5, 0, 0, "gemini-2.5-pro")));

        using var index = new SqliteSessionIndex([claude, gemini], DatabasePath());
        await index.RefreshAsync(default);

        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);

        (await index.GetUsageAsync(from, to, default)).Days.Single().Usage.Input.Should().Be(110);
        (await index.GetUsageAsync(from, to, ToolKind.Gemini, default)).Days.Single().Usage.Input.Should().Be(100);
        (await index.GetUsageAsync(from, to, ToolKind.Claude, default)).Days.Single().Usage.Input.Should().Be(10);
        (await index.GetUsageAsync(from, to, ToolKind.Codex, default)).Days.Should().BeEmpty();
    }

    [Fact]
    public async Task Usage_outside_the_requested_range_is_ignored()
    {
        var provider = new FakeProvider();
        var path = Path.Combine(_directory, "claude.jsonl");
        provider.Sessions.Add(Session(path, ToolKind.Claude, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        provider.SetUsage(
            path,
            new UsageDay(new DateOnly(2026, 8, 1), new TokenUsage(5, 5, 0, 0, "m")),
            new UsageDay(new DateOnly(2026, 9, 1), new TokenUsage(7, 7, 0, 0, "m")));

        using var index = new SqliteSessionIndex([provider], DatabasePath());
        await index.RefreshAsync(default);

        var usage = await index.GetUsageAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), default);

        usage.Days.Should().HaveCount(1);
        usage.Days[0].Usage.Input.Should().Be(7);
    }

    [Fact]
    public async Task Real_fixtures_produce_usage_for_both_tools()
    {
        using var index = CreateWithRealProviders();
        await index.RebuildAsync(new Progress<IndexProgress>(), default);

        var usage = await index.GetUsageAsync(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), default);

        usage.ByModel.Should().ContainKey("claude-opus-5");
        usage.ByModel.Should().ContainKey("gpt-5-codex");
        usage.ByModel.Should().ContainKey("gpt-5.1-codex");
    }

    // ── 도우미 ───────────────────────────────────────────────────────────

    private string DatabasePath() => Path.Combine(_directory, $"index-{Guid.NewGuid():N}.db");

    private SqliteSessionIndex CreateWithRealProviders()
    {
        var home = Path.Combine(_directory, "home");
        Directory.CreateDirectory(home);
        Fixtures.CreateClaudeHome(home);
        Fixtures.CreateCodexHome(home);

        return new SqliteSessionIndex(
            [
                new ClaudeProvider(new ProviderHome(home), new FakeProcessProbe()),
                new CodexProvider(new ProviderHome(home)),
            ],
            DatabasePath());
    }

    private (FakeProvider Provider, SqliteSessionIndex Index, string Path) CreateWithFakeProvider()
    {
        var provider = new FakeProvider();
        var path = Path.Combine(_directory, "fake.jsonl");

        provider.Sessions.Add(Session(path, ToolKind.Claude, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
        provider.SetMessages(
            path,
            Message(MessageRole.User, "더미 질문 1"),
            Message(MessageRole.Assistant, "더미 응답 1"));

        return (provider, new SqliteSessionIndex([provider], DatabasePath()), path);
    }

    private static SessionInfo Session(string path, ToolKind tool, DateTimeOffset started) => new(
        tool,
        "fixture-session",
        path,
        @"C:\Fixture\Project",
        started,
        started,
        1000,
        0,
        0,
        null,
        TokenUsage.Zero,
        "1.0.0",
        IsArchived: false,
        IsActive: false);

    private static SessionMessage Message(MessageRole role, string text) =>
        new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), role, text, false);
}
