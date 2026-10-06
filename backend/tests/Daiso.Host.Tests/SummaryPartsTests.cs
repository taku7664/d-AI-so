using System.Diagnostics;
using System.Net.Http.Json;
using Daiso.Host.Services;
using Daiso.Host.Shared;
using Daiso.Host.Tabs.Dashboard;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Daiso.Host.Tests;

/// <summary>요약을 이루는 조각: 사람이 치지 않은 줄 가리기, 워크트리 읽기, 세션 폴더를 지켜보다 바로 반영하기.</summary>
public sealed class SummaryPartsTests
{
    [Theory]
    [InlineData("<task-notification>\n<task-id>b1</task-id>")]
    [InlineData("<system-reminder>\nThe user started this session")]
    [InlineData("<command-name>/compact</command-name>")]
    [InlineData("This session is being continued from a previous conversation that ran out of context.")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Lines_the_tool_put_in_the_users_place_are_noise(string? text) => PromptNoise.IsNoise(text).Should().BeTrue();

    [Theory]
    [InlineData("야이씨 장난하냐")]
    [InlineData("if (a < b) 왜 안 돼?")]
    [InlineData("< 이건 꺾쇠로 시작한 질문")]
    public void What_a_person_typed_is_not_noise(string text) => PromptNoise.IsNoise(text).Should().BeFalse();

    [Fact]
    public void Codex_wrapping_is_peeled_off_and_a_cut_off_wrapping_is_noise()
    {
        const string Wrapped = """
            # Context from my IDE setup:

            ## Open tabs:
            - a.cs

            ## My request for Codex:
            이거 왜 안 돼?
            """;
        const string CutOff = """
            # Context from my IDE setup:

            ## Open tabs:
            - a.cs (200자에서 잘림)
            """;

        PromptNoise.Body(Wrapped).Should().Be("이거 왜 안 돼?");
        PromptNoise.Body("그냥 질문").Should().Be("그냥 질문");
        PromptNoise.IsNoise(PromptNoise.Body(CutOff)).Should().BeTrue();
    }

    [Fact]
    public void Worktree_listing_is_parsed_block_by_block()
    {
        var entries = WorktreeReader.ParseList(
            "worktree C:/r\nHEAD 1\nbranch refs/heads/main\n\nworktree C:/r/.claude/worktrees/x\nHEAD 2\nbranch refs/heads/claude/x\n\nworktree C:/r/w2\nHEAD 3\ndetached\n");

        entries.Select(entry => (entry.Path, entry.Branch)).Should().Equal(
            ("C:/r", "main"),
            ("C:/r/.claude/worktrees/x", "claude/x"),
            ("C:/r/w2", (string?)null));
    }

    [Fact]
    public async Task Worktrees_show_branch_changes_and_distance_from_main()
    {
        if (Git() is not { } git)
        {
            return; // git 이 없는 PC 에서는 이 시험을 건너뛴다. 응답의 GitMissing 은 그때 true 다
        }

        var root = Path.Combine(Path.GetTempPath(), "daiso-host-tests", "wt-" + Guid.NewGuid().ToString("N"));
        var repo = Path.Combine(root, "repo");
        var tree = Path.Combine(root, "tree");
        Directory.CreateDirectory(repo);
        try
        {
            Run(git, repo, "init", "-q", "-b", "main");
            Run(git, repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "first");
            Run(git, repo, "worktree", "add", "-q", "-b", "feature", tree);
            Run(git, tree, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "on feature");
            File.WriteAllText(Path.Combine(tree, "new.txt"), "x");

            var reader = new WorktreeReader(NullLogger<WorktreeReader>.Instance);
            var result = await reader.ReadAsync([repo], default);

            result.GitMissing.Should().BeFalse();
            var item = result.Worktrees.Should().ContainSingle(because: "메인 작업 폴더는 넣지 않는다").Subject;
            item.Branch.Should().Be("feature");
            item.Changes.Should().Be(1);
            item.BaseBranch.Should().Be("main");
            (item.Ahead, item.Behind).Should().Be((1, 0));
            item.LastCommit.Should().Be("on feature");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // git 이 읽기 전용으로 둔 객체 파일이 남을 수 있다. 임시 폴더라 그대로 둔다
            }
        }
    }

    [Fact]
    public async Task A_session_written_while_the_app_runs_shows_up_without_a_refresh_button()
    {
        var folder = string.Empty;
        await using var host = await RunningHost.StartAsync(home: home =>
        {
            folder = Path.Combine(home, ".claude", "projects", "C--Live");
            Directory.CreateDirectory(folder);
        });
        var index = host.App.Services.GetRequiredService<IndexService>();
        await index.Current.WaitAsync(TimeSpan.FromSeconds(30));

        File.WriteAllText(
            Path.Combine(folder, "live.jsonl"),
            """{"sessionId": "live-1", "cwd": "C:\\Live", "type": "user", "timestamp": "2026-10-07T00:00:00.000Z", "message": {"role": "user", "content": "방금 친 질문"}}""" + "\n");

        var watch = Stopwatch.StartNew();
        DashboardResponse? summary = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(20))
        {
            using var client = host.Client();
            using var request = host.Authed(HttpMethod.Get, "/api/dashboard");
            using var response = await client.SendAsync(request);
            summary = await response.Content.ReadFromJsonAsync<DashboardResponse>();
            if (summary!.Recent.Count > 0)
            {
                break;
            }

            await Task.Delay(300);
        }

        summary!.Recent.Should().ContainSingle().Which.LastPrompt.Should().Be("방금 친 질문");
        watch.Elapsed.Should().BeLessThan(IndexWatcher.MaxWait + TimeSpan.FromSeconds(5));
    }

    private static string? Git() => Daiso.Providers.Common.ExecutableLocator.Find("git");

    private static void Run(string git, string folder, params string[] args)
    {
        var info = new ProcessStartInfo(git) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(folder);
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info)!;
        process.WaitForExit();
        process.ExitCode.Should().Be(0, because: process.StandardError.ReadToEnd());
    }
}
