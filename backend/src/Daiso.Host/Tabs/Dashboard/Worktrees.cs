using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Daiso.Providers.Common;

namespace Daiso.Host.Tabs.Dashboard;

/// <summary>워크트리 하나. 저장소의 메인 작업 폴더는 넣지 않는다.</summary>
/// <param name="Repo">저장소 이름(폴더 이름).</param>
/// <param name="RepoPath">저장소 메인 작업 폴더.</param>
/// <param name="Path">워크트리 폴더.</param>
/// <param name="Branch">브랜치. 분리된 HEAD 면 null.</param>
/// <param name="Exists">폴더가 아직 있는가. git 에는 남았는데 폴더를 지운 경우 false.</param>
/// <param name="Changes">커밋 안 한 변경 파일 수(새 파일 포함). 못 읽었으면 null.</param>
/// <param name="BaseBranch">앞뒤를 잰 기준 브랜치(origin 의 기본 브랜치, 없으면 main·master). 못 찾았으면 null.</param>
/// <param name="Ahead">기준 브랜치에 없는 이 워크트리의 커밋 수.</param>
/// <param name="Behind">이 워크트리에 없는 기준 브랜치의 커밋 수.</param>
/// <param name="LastCommitAt">마지막 커밋 시각.</param>
/// <param name="LastCommit">마지막 커밋 제목.</param>
public sealed record WorktreeItem(
    string Repo,
    string RepoPath,
    string Path,
    string? Branch,
    bool Exists,
    int? Changes,
    string? BaseBranch,
    int? Ahead,
    int? Behind,
    DateTimeOffset? LastCommitAt,
    string? LastCommit);

/// <summary>워크트리 목록.</summary>
/// <param name="Worktrees">마지막 커밋이 최근인 것부터.</param>
/// <param name="GitMissing">git 을 찾지 못했다. 그러면 목록은 비어 있다.</param>
public sealed record WorktreesResponse(IReadOnlyList<WorktreeItem> Worktrees, bool GitMissing);

/// <summary>
/// 프로젝트 폴더들의 git 워크트리를 읽는다(docs/DECISIONS.md "요약은 지금 상황"). git 명령을 그대로 부른다.
/// <para>
/// <c>GIT_OPTIONAL_LOCKS=0</c> 으로 부른다. <c>git status</c> 는 보통 인덱스 파일을 고쳐 쓰려고 잠그는데,
/// 그 워크트리에서 에이전트가 커밋하는 중이면 잠금이 부딪친다. 읽기만 하는 앱이 남의 작업을 막으면 안 된다.
/// </para>
/// <para>
/// 프로젝트 마흔 곳이면 git 을 백 번 넘게 띄운다. 저장소마다 <see cref="CacheFor"/> 동안 결과를 들고 있고, 동시에 <see cref="Parallel"/> 개까지만 띄운다.
/// </para>
/// </summary>
public sealed class WorktreeReader
{
    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);
    private const int Parallel = 4;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<WorktreeItem> Items)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _slots = new(Parallel);
    private readonly ILogger<WorktreeReader> _logger;
    private readonly Func<string?> _findGit;

    public WorktreeReader(ILogger<WorktreeReader> logger)
        : this(logger, () => ExecutableLocator.Find("git"))
    {
    }

    internal WorktreeReader(ILogger<WorktreeReader> logger, Func<string?> findGit)
    {
        _logger = logger;
        _findGit = findGit;
    }

    /// <summary>이 폴더들이 속한 저장소들의 워크트리. 폴더가 저장소가 아니면 건너뛴다.</summary>
    public async Task<WorktreesResponse> ReadAsync(IEnumerable<string> folders, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (_findGit() is not { } git)
        {
            return new WorktreesResponse([], GitMissing: true);
        }

        var roots = await Task.WhenAll(folders
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(folder => RepoRootAsync(git, folder, ct))).ConfigureAwait(false);

        var lists = await Task.WhenAll(roots
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(root => RepoAsync(git, root, ct))).ConfigureAwait(false);

        return new WorktreesResponse(
            [.. lists.SelectMany(list => list).OrderByDescending(item => item.LastCommitAt ?? DateTimeOffset.MinValue)],
            GitMissing: false);
    }

    /// <summary>메인 작업 폴더. 워크트리 안에서 물어도 메인 쪽을 준다(공통 git 폴더의 부모).</summary>
    private async Task<string?> RepoRootAsync(string git, string folder, CancellationToken ct)
    {
        var common = (await RunAsync(git, folder, ct, "rev-parse", "--path-format=absolute", "--git-common-dir").ConfigureAwait(false))?.Trim();
        if (string.IsNullOrEmpty(common))
        {
            return null;
        }

        var full = System.IO.Path.GetFullPath(common);
        return string.Equals(System.IO.Path.GetFileName(full), ".git", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetDirectoryName(full)
            : null; // bare 저장소. 메인 작업 폴더가 없다
    }

    private async Task<IReadOnlyList<WorktreeItem>> RepoAsync(string git, string root, CancellationToken ct)
    {
        if (_cache.TryGetValue(root, out var hit) && DateTimeOffset.UtcNow - hit.At < CacheFor)
        {
            return hit.Items;
        }

        var listing = await RunAsync(git, root, ct, "worktree", "list", "--porcelain").ConfigureAwait(false);
        if (listing is null)
        {
            return [];
        }

        var entries = ParseList(listing);
        var main = entries.FirstOrDefault();
        var others = entries.Skip(1).Where(entry => !entry.Bare).ToList();
        var baseBranch = others.Count > 0 ? await BaseBranchAsync(git, root, ct).ConfigureAwait(false) : null;
        var repo = System.IO.Path.GetFileName(root.TrimEnd('\\', '/'));

        var items = await Task.WhenAll(others.Select(entry => ItemAsync(git, repo, root, entry, baseBranch, ct))).ConfigureAwait(false);
        _cache[root] = (DateTimeOffset.UtcNow, items);
        return items;
    }

    private async Task<WorktreeItem> ItemAsync(string git, string repo, string root, Entry entry, string? baseBranch, CancellationToken ct)
    {
        var path = System.IO.Path.GetFullPath(entry.Path);
        if (!Directory.Exists(path))
        {
            return new WorktreeItem(repo, root, path, entry.Branch, false, null, baseBranch, null, null, null, null);
        }

        var status = RunAsync(git, path, ct, "status", "--porcelain");
        var log = RunAsync(git, path, ct, "log", "-1", "--format=%cI%x1f%s");
        var counts = baseBranch is null ? Task.FromResult<string?>(null) : RunAsync(git, path, ct, "rev-list", "--left-right", "--count", $"HEAD...{baseBranch}");
        await Task.WhenAll(status, log, counts).ConfigureAwait(false);

        int? changes = status.Result is { } s ? s.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length : null;
        DateTimeOffset? at = null;
        string? subject = null;
        if (log.Result?.Trim() is { Length: > 0 } line && line.Split('\x1f', 2) is [var when, var title])
        {
            at = DateTimeOffset.TryParse(when, out var parsed) ? parsed : null;
            subject = title;
        }

        int? ahead = null, behind = null;
        if (counts.Result?.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries) is [var left, var right]
            && int.TryParse(left, out var a) && int.TryParse(right, out var b))
        {
            ahead = a;
            behind = b;
        }

        return new WorktreeItem(repo, root, path, entry.Branch, true, changes, baseBranch, ahead, behind, at, subject);
    }

    /// <summary>origin 의 기본 브랜치. 없으면 로컬 main, master 순서.</summary>
    private async Task<string?> BaseBranchAsync(string git, string root, CancellationToken ct)
    {
        if ((await RunAsync(git, root, ct, "symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD").ConfigureAwait(false))?.Trim() is { Length: > 0 } remote)
        {
            return remote;
        }

        foreach (var name in new[] { "main", "master" })
        {
            if (await RunAsync(git, root, ct, "rev-parse", "--verify", "--quiet", $"refs/heads/{name}").ConfigureAwait(false) is { Length: > 0 })
            {
                return name;
            }
        }

        return null;
    }

    internal sealed record Entry(string Path, string? Branch, bool Bare);

    /// <summary><c>git worktree list --porcelain</c>. 빈 줄로 나뉜 덩어리마다 <c>worktree</c>·<c>branch</c>·<c>bare</c> 줄을 읽는다.</summary>
    internal static List<Entry> ParseList(string text)
    {
        var entries = new List<Entry>();
        string? path = null, branch = null;
        var bare = false;

        foreach (var raw in text.Split('\n').Append(string.Empty))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (path is not null)
                {
                    entries.Add(new Entry(path, branch, bare));
                }

                path = branch = null;
                bare = false;
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var name = line["branch ".Length..];
                branch = name.StartsWith("refs/heads/", StringComparison.Ordinal) ? name["refs/heads/".Length..] : name;
            }
            else if (line == "bare")
            {
                bare = true;
            }
        }

        return entries;
    }

    /// <summary>git 을 부른다. 실패·시간 초과면 null.</summary>
    private async Task<string?> RunAsync(string git, string folder, CancellationToken ct, params string[] args)
    {
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var info = new ProcessStartInfo(git)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            info.ArgumentList.Add("-C");
            info.ArgumentList.Add(folder);
            foreach (var arg in args)
            {
                info.ArgumentList.Add(arg);
            }

            info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            info.Environment["GIT_TERMINAL_PROMPT"] = "0";

            using var process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CommandTimeout);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            _ = process.StandardError.ReadToEndAsync(timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                _logger.LogDebug("git {Args} 이 {Folder} 에서 시간을 넘겼다", string.Join(' ', args), folder);
                return null;
            }

            return process.ExitCode == 0 ? await output.ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            _logger.LogDebug(ex, "git 을 부르지 못했다");
            return null;
        }
        finally
        {
            _slots.Release();
        }
    }
}
