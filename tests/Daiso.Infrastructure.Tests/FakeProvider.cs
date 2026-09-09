using System.Runtime.CompilerServices;
using Daiso.Core;

namespace Daiso.Infrastructure.Tests;

/// <summary>
/// 증분 인덱싱 테스트용 Provider. 세션 목록을 직접 지정하고, 어떤 오프셋으로 읽혔는지 기록한다.
/// </summary>
internal sealed class FakeProvider : IProvider, IUsageReader
{
    private readonly Dictionary<string, List<SessionMessage>> _messages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<UsageDay>> _usage = new(StringComparer.OrdinalIgnoreCase);

    internal FakeProvider(ToolKind kind = ToolKind.Claude, bool usageIsAdditive = true)
    {
        Kind = kind;
        UsageIsAdditive = usageIsAdditive;
    }

    /// <summary>열거할 세션. 테스트가 자유롭게 바꾼다.</summary>
    internal List<SessionInfo> Sessions { get; } = [];

    /// <summary>로그인 파일. 프로필 저장·전환 테스트가 채운다.</summary>
    internal List<AuthFile> Credentials { get; } = [];

    /// <summary>`ReadMessagesAsync`가 호출된 (경로, 오프셋) 기록.</summary>
    internal List<(string Path, long Offset)> MessageReads { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<AuthFile> AuthFiles => Credentials;

    /// <inheritdoc />
    public ToolKind Kind { get; }

    /// <inheritdoc />
    public string SessionsRoot { get; set; } = Path.GetTempPath();

    /// <inheritdoc />
    public bool UsageIsAdditive { get; }

    /// <inheritdoc />
    public string ExecutableName => "fake";

    public string InstallCommand => "npm install -g fake";

    /// <inheritdoc />
    public bool AppendOnlySessions => true;

    /// <inheritdoc />
    public string RulesFileName => "FAKE.md";

    internal void SetMessages(string filePath, params SessionMessage[] messages) =>
        _messages[filePath] = [.. messages];

    internal void SetUsage(string filePath, params UsageDay[] days) =>
        _usage[filePath] = [.. days];

    /// <inheritdoc />
    public IReadOnlyList<string> ContextFilePatterns(string projectDir) => [];

    /// <inheritdoc />
    public Task<bool> IsInstalledAsync(CancellationToken ct) => Task.FromResult(true);

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct) =>
        Task.FromResult(AuthStatus.Missing(Kind));

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionInfo> EnumerateSessionsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var session in Sessions.ToList())
        {
            await Task.Yield();
            yield return session;
        }
    }

    /// <inheritdoc />
    public Task<SessionInfo> ReadSessionInfoAsync(string filePath, CancellationToken ct) =>
        Task.FromResult(Sessions.First(s =>
            string.Equals(s.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));

    /// <inheritdoc />
    public async IAsyncEnumerable<SessionMessage> ReadMessagesAsync(
        string filePath,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct)
    {
        MessageReads.Add((filePath, fromByteOffset));

        if (!_messages.TryGetValue(filePath, out var messages))
        {
            yield break;
        }

        // 오프셋이 0이면 전부, 그 밖에는 "새로 붙은 것"만 흘려보낸다.
        var slice = fromByteOffset == 0 ? messages : messages.Skip(messages.Count - 1);

        foreach (var message in slice)
        {
            await Task.Yield();
            yield return message;
        }
    }

    /// <inheritdoc />
    /// <summary>테스트 대역.</summary>
    public string ImagePasteKeys => "\x16";

    public string BuildResumeArguments(SessionInfo session) => $"resume {session.Id}";

    /// <inheritdoc />
    public async IAsyncEnumerable<UsageDay> ReadUsageAsync(
        string filePath,
        long fromByteOffset,
        DateOnly sessionDate,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_usage.TryGetValue(filePath, out var days))
        {
            yield break;
        }

        foreach (var day in days)
        {
            await Task.Yield();
            yield return day with { Date = day.Date == default ? sessionDate : day.Date };
        }
    }
}
