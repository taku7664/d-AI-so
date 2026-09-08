using Daiso.Infrastructure.Pty;

namespace Daiso.Infrastructure.Tests;

/// <summary>ARCHITECTURE §5.3 — 방 출력 되돌리기 버퍼. 붙을 때 지난 화면을 통째로, 그 뒤는 이어서, 중복·누락 없이.</summary>
public sealed class OutputReplayBufferTests
{
    [Fact]
    public void Attach_replays_everything_so_far_and_later_chunks_go_to_the_sink()
    {
        var buffer = new OutputReplayBuffer();
        buffer.Append([1]).Should().BeNull(because: "아직 붙은 화면이 없다");
        buffer.Append([2, 3]);

        var received = new List<byte[]>();
        Action<byte[]> sink = received.Add;

        foreach (var chunk in buffer.Attach(sink))
        {
            sink(chunk);
        }

        buffer.Append([4])!.Invoke([4]);

        received.Should().BeEquivalentTo(new[] { new byte[] { 1 }, new byte[] { 2, 3 }, new byte[] { 4 } }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Detach_stops_delivery_only_for_the_sink_that_is_attached()
    {
        var buffer = new OutputReplayBuffer();
        Action<byte[]> first = _ => { };
        Action<byte[]> second = _ => { };

        buffer.Attach(first);
        buffer.Attach(second);
        buffer.Detach(first);

        buffer.Append([1]).Should().BeSameAs(second, because: "옛 싱크를 떼어도 지금 붙은 싱크는 남는다");

        buffer.Detach(second);
        buffer.Append([2]).Should().BeNull();
    }

    [Fact]
    public void Old_chunks_are_dropped_from_the_front_when_the_cap_is_exceeded()
    {
        var buffer = new OutputReplayBuffer(maxBytes: 5);
        buffer.Append([1, 1]);
        buffer.Append([2, 2]);
        buffer.Append([3, 3]);

        buffer.Length.Should().Be(4);
        buffer.Attach(_ => { }).Should().BeEquivalentTo(new[] { new byte[] { 2, 2 }, new byte[] { 3, 3 } }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void Clear_empties_the_buffer_and_forgets_the_sink()
    {
        var buffer = new OutputReplayBuffer();
        buffer.Attach(_ => { });
        buffer.Append([9]);

        buffer.Clear();

        buffer.Length.Should().Be(0);
        buffer.Append([1]).Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_appends_during_attach_are_delivered_exactly_once()
    {
        var buffer = new OutputReplayBuffer();
        var delivered = new List<int>();
        var gate = new object();
        Action<byte[]> sink = chunk => { lock (gate) { delivered.Add(chunk[0]); } };

        var producer = Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
            {
                buffer.Append([(byte)(i % 256)])?.Invoke([(byte)(i % 256)]);
            }
        });

        var snapshot = buffer.Attach(sink);
        foreach (var chunk in snapshot)
        {
            sink(chunk);
        }

        await producer;

        // 스냅샷에 들어갔거나 싱크로 갔거나 정확히 한 번. 개수가 같으면 중복·누락이 없다
        delivered.Count.Should().Be(200);
    }
}
