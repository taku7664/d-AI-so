using System.Text;
using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests.Claude;

/// <summary>한 응답이 여러 줄로 남아도 usage 는 한 번만 센다 (ARCHITECTURE §4.1).</summary>
public sealed class ClaudeUsageDedupTests : IDisposable
{
    private readonly ClaudeProvider _provider = new(new ProviderHome(Path.GetTempPath()), new FakeProcessProbe());
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"daiso-claude-{Guid.NewGuid():N}.jsonl");

    private static string Line(string id, string block) =>
        "{\"sessionId\":\"s\",\"type\":\"assistant\",\"timestamp\":\"2026-09-01T00:01:00.000Z\",\"message\":{\"id\":\"" + id +
        "\",\"model\":\"claude-opus-5\",\"content\":[" + block +
        "],\"usage\":{\"input_tokens\":10,\"output_tokens\":2,\"cache_creation_input_tokens\":3,\"cache_read_input_tokens\":100}}}\n";

    private const string Text = "{\"type\":\"text\",\"text\":\"a\"}";
    private const string Tool = "{\"type\":\"tool_use\",\"name\":\"Read\",\"input\":{}}";

    [Fact]
    public async Task Lines_of_one_response_count_once()
    {
        File.WriteAllText(_path, Line("msg_1", Text) + Line("msg_1", Tool) + Line("msg_2", Text));

        var info = await _provider.ReadSessionInfoAsync(_path, default);
        var days = await ListAsync(_provider.ReadUsageAsync(_path, 0, default, default));

        var two = new TokenUsage(20, 4, 6, 200, "claude-opus-5");
        info.Usage.Should().Be(two);
        days.Should().ContainSingle().Which.Usage.Should().Be(two);
    }

    [Fact]
    public async Task Reading_on_from_an_offset_skips_responses_already_counted()
    {
        var first = Line("msg_1", Text);
        File.WriteAllText(_path, first + Line("msg_1", Tool) + Line("msg_2", Text));

        var offset = Encoding.UTF8.GetByteCount(first);
        var days = await ListAsync(_provider.ReadUsageAsync(_path, offset, default, default));

        days.Should().ContainSingle().Which.Usage.Should().Be(new TokenUsage(10, 2, 3, 100, "claude-opus-5"));
    }

    [Fact]
    public async Task Tool_lines_carry_the_tool_name_and_whether_the_result_failed()
    {
        File.WriteAllText(_path, Line("msg_1", Tool) + ResultLine(isError: true));

        var messages = new List<SessionMessage>();
        await foreach (var message in _provider.ReadMessagesAsync(_path, 0, default))
        {
            messages.Add(message);
        }

        messages.Should().HaveCount(2);
        messages[0].ToolName.Should().Be("Read");
        messages[1].Should().Match<SessionMessage>(m => m.Role == MessageRole.Tool && m.ToolName == null && m.IsError);
    }

    private static string ResultLine(bool isError) =>
        "{\"sessionId\":\"s\",\"type\":\"user\",\"timestamp\":\"2026-09-01T00:02:00.000Z\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"is_error\":"
        + (isError ? "true" : "false") + ",\"content\":\"no\"}]}}\n";

    private static async Task<List<UsageDay>> ListAsync(IAsyncEnumerable<UsageDay> source)
    {
        var list = new List<UsageDay>();
        await foreach (var day in source)
        {
            list.Add(day);
        }

        return list;
    }

    public void Dispose() => File.Delete(_path);
}
