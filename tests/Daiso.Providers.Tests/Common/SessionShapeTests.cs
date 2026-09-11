using System.Globalization;
using System.Text;
using Daiso.Core;
using Daiso.Providers.Antigravity;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Common;

/// <summary>
/// 특성화 테스트 — <b>지금 나오는 값을 그대로 못 박는다</b> (docs/PLUGIN_PLAN.md Stage 0).
/// <para>
/// 플러그인 작업은 <c>ToolKind</c> 를 문자열 id 로 바꾸고(저장소 둘을 이관한다), 표시 정보를 옮기고,
/// 세션 읽기를 바깥 프로세스로 뺄 수 있게 만든다. 그 과정에서 <b>값이 바뀌면 안 된다</b>.
/// 여기가 그것을 잡는 그물이다 — 뒤 단계는 전부 "이 스냅샷이 그대로인가"로 검사된다.
/// </para>
/// <para>
/// 스냅샷은 <c>Snapshots/*.txt</c> 다. 일부러 바꾼 것이면 파일을 고쳐 함께 커밋한다.
/// 파일이 없으면 테스트가 실제 값을 적어 두고 실패한다 — 처음 만들 때 한 번만 그렇다.
/// </para>
/// </summary>
public sealed class SessionShapeTests : IDisposable
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

    [Fact]
    public Task Claude_session_shape_is_unchanged() =>
        Verify("claude", new ClaudeProvider(Fixtures.CreateClaudeHome(_home), new FakeProcessProbe()));

    [Fact]
    public Task Codex_session_shape_is_unchanged() =>
        Verify("codex", new CodexProvider(Fixtures.CreateCodexHome(_home)));

    [Fact]
    public Task Antigravity_session_shape_is_unchanged() =>
        Verify("antigravity", new AntigravityProvider(Fixtures.CreateGeminiHome(_home)));

    /// <summary>도구 하나가 표본 폴더에서 내놓는 것 전부를 한 장의 글로 만들어 스냅샷과 견준다.</summary>
    private static async Task Verify(string name, IProvider provider)
    {
        var actual = await Render(provider);
        var path = SnapshotPath(name);

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, Encoding.UTF8);

            Assert.Fail(
                $"스냅샷이 없어 지금 값을 적어 뒀다: {path}{Environment.NewLine}" +
                "내용을 눈으로 확인하고 커밋한 뒤 다시 돌려라.");
        }

        var expected = Normalize(await File.ReadAllTextAsync(path, Encoding.UTF8));

        Normalize(actual).Should().Be(
            expected,
            because: "세션에서 읽어 내는 값은 플러그인 작업 내내 그대로여야 한다 (docs/PLUGIN_PLAN.md Stage 0)");
    }

    /// <summary>
    /// 세션 메타 + 본문을 훑은 메타 + 메시지 전부를 한 줄씩 적는다.
    /// <b>파일 경로·크기는 적지 않는다</b> — 임시 폴더 이름과 줄바꿈 꼴에 따라 달라져 스냅샷이 못 쓰게 된다.
    /// </summary>
    private static async Task<string> Render(IProvider provider)
    {
        var builder = new StringBuilder();
        var sessions = new List<SessionInfo>();

        await foreach (var session in provider.EnumerateSessionsAsync(CancellationToken.None))
        {
            sessions.Add(session);
        }

        builder.Append("tool=").Append(provider.Kind).Append('\n');
        builder.Append("appendOnly=").Append(provider.AppendOnlySessions).Append('\n');
        builder.Append("sessions=").Append(sessions.Count).Append('\n');

        foreach (var session in sessions.OrderBy(session => session.FilePath, StringComparer.Ordinal))
        {
            var scanned = await provider.ReadSessionInfoAsync(session.FilePath, CancellationToken.None);

            builder.Append("\n── ").Append(Path.GetFileName(session.FilePath)).Append(" ──\n");
            AppendMeta(builder, "enumerated", session);
            AppendMeta(builder, "scanned", scanned);
            builder.Append("resume=").Append(provider.BuildResumeArguments(session)).Append('\n');

            await foreach (var message in provider.ReadMessagesAsync(session.FilePath, 0, CancellationToken.None))
            {
                builder
                    .Append("  msg ")
                    .Append(Instant(message.At))
                    .Append(' ')
                    .Append(message.Role)
                    .Append(message.IsSidechain ? " [sidechain] " : " ")
                    .Append(Flatten(message.Text))
                    .Append('\n');
            }
        }

        return builder.ToString();
    }

    private static void AppendMeta(StringBuilder builder, string label, SessionInfo session)
    {
        builder
            .Append(label)
            .Append(": id=").Append(session.Id)
            .Append(" project=").Append(session.ProjectPath ?? "<none>")
            .Append(" started=").Append(Instant(session.StartedAt))
            .Append(" user=").Append(session.UserMessageCount)
            .Append(" assistant=").Append(session.AssistantMessageCount)
            .Append(" usage=").Append(session.Usage.Input).Append('/')
            .Append(session.Usage.Output).Append('/')
            .Append(session.Usage.CacheCreate).Append('/')
            .Append(session.Usage.CacheRead)
            .Append(" model=").Append(session.Usage.Model ?? "<none>")
            .Append(" version=").Append(session.ToolVersion ?? "<none>")
            .Append(" archived=").Append(session.IsArchived)
            .Append(" firstPrompt=").Append(Flatten(session.FirstPrompt))
            .Append('\n');
    }

    /// <summary>시각은 UTC 로 적는다. 표본을 읽는 PC 의 시간대가 스냅샷을 흔들면 안 된다.</summary>
    private static string Instant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>본문의 줄바꿈을 한 줄로 접는다. 길면 자른다 — 여기서 보는 것은 내용이 아니라 모양이다.</summary>
    private static string Flatten(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "<none>";
        }

        var single = text.ReplaceLineEndings(" ").Trim();

        return single.Length > 120 ? single[..120] + "…" : single;
    }

    /// <summary>줄바꿈 꼴(CRLF/LF)은 비교에서 뺀다. git 이 작업 트리에서 바꾼다.</summary>
    private static string Normalize(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static string SnapshotPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "snapshots", name + ".txt");
}
