using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests;

/// <summary>
/// 20MB 세션을 순회하며 피크 메모리가 파일 크기의 2배를 넘지 않는지 본다. (ARCHITECTURE §8)
/// fixture를 저장소에 두지 않고 테스트 실행 중에 만든다.
/// </summary>
[Trait("Category", "Slow")]
public sealed class StreamingTests : IDisposable
{
    private const long TargetBytes = 20L * 1024 * 1024;

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

    [Fact]
    public async Task A_twenty_megabyte_session_streams_without_loading_the_whole_file()
    {
        var path = Path.Combine(_directory, "big.jsonl");
        var written = WriteLargeSession(path);
        written.Should().BeGreaterThanOrEqualTo(TargetBytes);

        var provider = new ClaudeProvider(new ProviderHome(_directory), new FakeProcessProbe());

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var peak = before;
        var count = 0;

        await foreach (var message in provider.ReadMessagesAsync(path, 0, default))
        {
            count++;

            if (count % 200 == 0)
            {
                peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));
            }

            message.Text.Should().NotBeEmpty();
        }

        count.Should().BeGreaterThan(0);
        (peak - before).Should().BeLessThan(written * 2);
    }

    [Fact]
    public async Task Reading_the_session_info_of_a_large_file_stays_bounded()
    {
        var path = Path.Combine(_directory, "big-info.jsonl");
        var written = WriteLargeSession(path);

        var provider = new ClaudeProvider(new ProviderHome(_directory), new FakeProcessProbe());

        GC.Collect();
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var info = await provider.ReadSessionInfoAsync(path, default);

        var after = GC.GetTotalMemory(forceFullCollection: false);

        info.SizeBytes.Should().Be(written);
        info.UserMessageCount.Should().BeGreaterThan(0);
        (after - before).Should().BeLessThan(written * 2);
    }

    /// <summary>사용자·어시스턴트 줄을 번갈아 써서 목표 크기를 넘긴다.</summary>
    private static long WriteLargeSession(string path)
    {
        var filler = new string('가', 2000);

        using (var writer = new StreamWriter(path, append: false, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            var index = 0;

            while (writer.BaseStream.Length < TargetBytes)
            {
                writer.WriteLine(UserLine(index, filler));
                writer.WriteLine(AssistantLine(index, filler));
                writer.Flush();
                index++;
            }
        }

        return new FileInfo(path).Length;
    }

    private static string UserLine(int index, string filler) => JsonSerializer.Serialize(new
    {
        type = "user",
        sessionId = "big-fixture",
        timestamp = "2026-09-01T00:00:00.000Z",
        cwd = @"C:\Fixture\Project",
        version = "2.0.0",
        message = new { role = "user", content = $"더미 질문 {index} {filler}" },
    });

    private static string AssistantLine(int index, string filler) => JsonSerializer.Serialize(new
    {
        type = "assistant",
        sessionId = "big-fixture",
        timestamp = "2026-09-01T00:00:01.000Z",
        cwd = @"C:\Fixture\Project",
        version = "2.0.0",
        message = new
        {
            role = "assistant",
            model = "claude-opus-5",
            content = new[] { new { type = "text", text = $"더미 응답 {index} {filler}" } },
            usage = new
            {
                input_tokens = 1,
                output_tokens = 1,
                cache_creation_input_tokens = 0,
                cache_read_input_tokens = 0,
            },
        },
    });
}
