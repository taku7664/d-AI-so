using System.Threading.Channels;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>
/// 방의 콘솔 출력. 다시 붙는 화면에 지난 출력을 통째로 돌려주고 그 뒤 출력을 이어 흘린다.
/// 옛 <c>OutputReplayBuffer</c>(old/src/Daiso.Infrastructure/Pty)와 같은 규칙이고, 화면을 여럿 받는다(Electron 창과 크롬 탭이 같은 방을 같이 볼 수 있다).
/// <para>
/// <b>스냅숏과 구독을 한 잠금 안에서 한다.</b> 그 사이에 온 덩어리가 두 번 그려지거나 빠지지 않는다(옛 앱 350ace2).
/// 덩어리는 UTF-8 글자 중간에서 잘릴 수 있다. 여기서는 바이트 그대로 두고 화면(xterm)이 이어 붙인다.
/// </para>
/// </summary>
public sealed class RoomOutput
{
    /// <summary>쌓아 둘 최대 바이트. 넘으면 오래된 것부터 버린다. 옛 앱과 같다.</summary>
    public const int MaxBytes = 8 * 1024 * 1024;

    private readonly Lock _gate = new();
    private readonly LinkedList<byte[]> _chunks = new();
    private readonly List<Channel<byte[]>> _readers = [];
    private int _bytes;
    private bool _completed;

    public void Append(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_gate)
        {
            _chunks.AddLast(chunk);
            _bytes += chunk.Length;
            while (_bytes > MaxBytes && _chunks.First is { } first)
            {
                _bytes -= first.Value.Length;
                _chunks.RemoveFirst();
            }

            foreach (var reader in _readers)
            {
                reader.Writer.TryWrite(chunk);
            }
        }
    }

    /// <summary>프로세스가 끝났다. 붙어 있는 화면에 더 올 것이 없다고 알린다.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            foreach (var reader in _readers)
            {
                reader.Writer.TryComplete();
            }
        }
    }

    /// <summary>지난 출력과, 그 뒤로 올 출력을 읽는 통로. 다 보면 <see cref="Unsubscribe"/> 한다.</summary>
    public (byte[] Snapshot, Channel<byte[]> Live) Subscribe()
    {
        var live = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

        lock (_gate)
        {
            var snapshot = new byte[_bytes];
            var at = 0;
            foreach (var chunk in _chunks)
            {
                chunk.CopyTo(snapshot, at);
                at += chunk.Length;
            }

            if (_completed)
            {
                live.Writer.TryComplete();
            }
            else
            {
                _readers.Add(live);
            }

            return (snapshot, live);
        }
    }

    /// <summary>지난 출력 없이 앞으로 올 출력만 읽는 통로. 다 보면 <see cref="Unsubscribe"/> 한다.</summary>
    public Channel<byte[]> Follow()
    {
        var live = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

        lock (_gate)
        {
            if (_completed)
            {
                live.Writer.TryComplete();
            }
            else
            {
                _readers.Add(live);
            }
        }

        return live;
    }

    /// <summary>마지막 <paramref name="bytes"/> 바이트. 덩어리 경계에서 자르므로 조금 더 올 수 있다.</summary>
    public byte[] Tail(int bytes)
    {
        lock (_gate)
        {
            var picked = new List<byte[]>();
            var total = 0;
            for (var node = _chunks.Last; node is not null && total < bytes; node = node.Previous)
            {
                picked.Add(node.Value);
                total += node.Value.Length;
            }

            picked.Reverse();
            return [.. picked.SelectMany(chunk => chunk)];
        }
    }

    public void Unsubscribe(Channel<byte[]> live)
    {
        lock (_gate)
        {
            _readers.Remove(live);
        }

        live.Writer.TryComplete();
    }
}
