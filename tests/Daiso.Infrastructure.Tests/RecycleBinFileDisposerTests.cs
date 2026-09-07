using Daiso.Core;
using Daiso.Providers.Tests;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.5 — 실행 중 세션은 거부하고, 나머지는 휴지통으로.</summary>
public sealed class RecycleBinFileDisposerTests : IDisposable
{
    private readonly string _directory = Fixtures.CreateTempDirectory();
    private readonly RecycleBinFileDisposer _disposer = new();

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

    [Fact]
    public async Task An_active_session_is_skipped_with_a_reason()
    {
        var session = CreateFile("active.jsonl", isActive: true);

        var result = await _disposer.MoveToRecycleBinAsync([session]);

        result.Deleted.Should().BeEmpty();
        result.Skipped.Should().ContainSingle()
            .Which.Should().Be((session.FilePath, "실행 중인 세션이다"));
        File.Exists(session.FilePath).Should().BeTrue("실행 중 세션 파일은 그대로 있어야 한다");
    }

    [Fact]
    public async Task An_idle_session_file_is_moved_to_the_recycle_bin()
    {
        var session = CreateFile("idle.jsonl", isActive: false);

        var result = await _disposer.MoveToRecycleBinAsync([session]);

        result.Deleted.Should().ContainSingle().Which.Should().Be(session.FilePath);
        result.Skipped.Should().BeEmpty();
        File.Exists(session.FilePath).Should().BeFalse();
    }

    [Fact]
    public async Task A_missing_file_is_skipped_with_a_reason()
    {
        var session = Session(Path.Combine(_directory, "gone.jsonl"), isActive: false);

        var result = await _disposer.MoveToRecycleBinAsync([session]);

        result.Deleted.Should().BeEmpty();
        result.Skipped.Should().ContainSingle().Which.Reason.Should().Be("파일이 없다");
    }

    [Fact]
    public async Task Active_and_idle_sessions_are_reported_separately()
    {
        var active = CreateFile("mixed-active.jsonl", isActive: true);
        var idle = CreateFile("mixed-idle.jsonl", isActive: false);

        var result = await _disposer.MoveToRecycleBinAsync([active, idle]);

        result.Deleted.Should().ContainSingle().Which.Should().Be(idle.FilePath);
        result.Skipped.Should().ContainSingle().Which.Path.Should().Be(active.FilePath);
    }

    [Fact]
    public async Task Permanent_deletion_also_refuses_active_sessions()
    {
        var session = CreateFile("permanent-active.jsonl", isActive: true);

        var result = await _disposer.DeletePermanentlyAsync([session]);

        result.Deleted.Should().BeEmpty();
        File.Exists(session.FilePath).Should().BeTrue();
    }

    [Fact]
    public async Task Permanent_deletion_removes_an_idle_file()
    {
        var session = CreateFile("permanent-idle.jsonl", isActive: false);

        var result = await _disposer.DeletePermanentlyAsync([session]);

        result.Deleted.Should().ContainSingle();
        File.Exists(session.FilePath).Should().BeFalse();
    }

    private SessionInfo CreateFile(string name, bool isActive)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "{\"type\":\"user\"}\n");
        return Session(path, isActive);
    }

    private static SessionInfo Session(string path, bool isActive) => new(
        ToolKind.Claude,
        Path.GetFileNameWithoutExtension(path),
        path,
        @"C:\Fixture\Project",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        0,
        0,
        0,
        null,
        TokenUsage.Zero,
        null,
        IsArchived: false,
        isActive);
}
