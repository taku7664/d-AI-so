using Daiso.Core;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.3 — 방이 활성 세션 파일을 잡고 새로 붙은 메시지만 흘린다.</summary>
public sealed class SessionTailTests
{
    [Fact]
    public async Task Finds_the_projects_recent_file_and_streams_only_new_messages()
    {
        var root = Directory.CreateTempSubdirectory("daiso-tail-");
        var project = @"C:\work\proj";
        var file = Path.Combine(root.FullName, "session-a.jsonl");
        await File.WriteAllTextAsync(file, "seed");

        var provider = new FakeProvider(ToolKind.Gemini) { SessionsRoot = root.FullName };
        provider.Sessions.Add(Session(file, project));
        provider.SetMessages(file, Msg("첫 질문", MessageRole.User));

        var batches = new List<IReadOnlyList<SessionMessage>>();
        using var tail = new SessionTail(provider, project, DateTimeOffset.UtcNow.AddSeconds(-1), TimeSpan.FromMilliseconds(100));
        tail.MessagesAppended += b => { lock (batches) { batches.Add(b); } };
        tail.Start();

        await WaitUntil(() => { lock (batches) { return batches.Sum(b => b.Count) == 1; } });

        // 한 턴 더: 답과 다음 질문이 붙는다
        provider.SetMessages(file, Msg("첫 질문", MessageRole.User), Msg("첫 답", MessageRole.Assistant), Msg("둘째 질문", MessageRole.User));
        await File.WriteAllTextAsync(file, "seed-grown-larger");   // 크기·수정시각 변화

        await WaitUntil(() => { lock (batches) { return batches.Sum(b => b.Count) == 3; } });

        lock (batches)
        {
            batches.SelectMany(b => b).Select(m => m.Text)
                .Should().Equal("첫 질문", "첫 답", "둘째 질문");
            batches[1].Should().HaveCount(2, because: "이미 보낸 첫 질문은 다시 흘리지 않는다");
        }

        root.Delete(recursive: true);
    }

    [Fact]
    public async Task A_file_older_than_the_room_start_is_ignored()
    {
        var root = Directory.CreateTempSubdirectory("daiso-tail-old-");
        var project = @"C:\work\proj";
        var file = Path.Combine(root.FullName, "old.jsonl");
        await File.WriteAllTextAsync(file, "x");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10));

        var provider = new FakeProvider(ToolKind.Gemini) { SessionsRoot = root.FullName };
        provider.Sessions.Add(Session(file, project));
        provider.SetMessages(file, Msg("옛 대화", MessageRole.User));

        var seen = 0;
        using var tail = new SessionTail(provider, project, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(100));
        tail.MessagesAppended += b => Interlocked.Add(ref seen, b.Count);
        tail.Start();

        await Task.Delay(600);
        seen.Should().Be(0);
        tail.ActiveFile.Should().BeNull();

        root.Delete(recursive: true);
    }

    private static SessionInfo Session(string file, string project) =>
        new(ToolKind.Gemini, Path.GetFileNameWithoutExtension(file), file, project, default, default, 0, 0, 0, null, TokenUsage.Zero, null, false, false);

    private static SessionMessage Msg(string text, MessageRole role) =>
        new(DateTimeOffset.UtcNow, role, text, false);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("조건이 10초 안에 참이 되지 않았다");
            }

            await Task.Delay(50);
        }
    }
}
