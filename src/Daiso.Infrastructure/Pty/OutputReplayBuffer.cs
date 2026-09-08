namespace Daiso.Infrastructure.Pty;

/// <summary>
/// 방의 콘솔 출력을 되돌리기용으로 쌓아 두고, 화면(싱크) 하나에 흘려 주는 순수 버퍼. (ARCHITECTURE §5.3)
/// 화면은 하나뿐이고 탭마다 같은 화면을 다른 방에 다시 가리키므로, 붙을 때 지난 화면을 통째로 되돌리고
/// 그 뒤 덩어리를 이어 보낸다. 스냅샷과 구독을 한 잠금 안에서 하여, 되돌리는 사이에 온 덩어리가
/// 두 번 그려지거나 빠지지 않게 한다. 스레드 안전. UI 디스패처 옮기기는 부르는 쪽이 한다.
/// </summary>
public sealed class OutputReplayBuffer
{
    /// <summary>기본 상한 8MB. 넘으면 앞(오래된 스크롤백)을 버린다. 긴 세션도 화면 복원에 충분하다.</summary>
    public const int DefaultMaxBytes = 8 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly LinkedList<byte[]> _chunks = new();
    private readonly int _maxBytes;
    private int _bytes;
    private Action<byte[]>? _sink;

    public OutputReplayBuffer(int maxBytes = DefaultMaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBytes, 0);
        _maxBytes = maxBytes;
    }

    /// <summary>쌓여 있는 바이트 수.</summary>
    public int Length
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>
    /// 덩어리를 버퍼에 넣고, 붙어 있는 싱크가 있으면 돌려준다(부르는 쪽이 자기 스레드에서 sink(chunk)를 한다).
    /// 싱크가 없으면 null. 버퍼 넣기와 싱크 잡기가 한 잠금 안이라 <see cref="Attach"/>의 스냅샷과 겹쳐도 정확히 한 번만 간다.
    /// </summary>
    public Action<byte[]>? Append(byte[] chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        lock (_gate)
        {
            _chunks.AddLast(chunk);
            _bytes += chunk.Length;

            while (_bytes > _maxBytes && _chunks.First is { } first)
            {
                _bytes -= first.Value.Length;
                _chunks.RemoveFirst();
            }

            return _sink;
        }
    }

    /// <summary>싱크를 붙이고, 지금까지 쌓인 덩어리를 순서대로 돌려준다. 부르는 쪽이 그대로 싱크에 흘린다.</summary>
    public byte[][] Attach(Action<byte[]> sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            _sink = sink;
            return [.. _chunks];
        }
    }

    /// <summary>그 싱크가 지금 붙은 것이면 떼어 낸다. 다른 방으로 옮겨 갈 때.</summary>
    public void Detach(Action<byte[]> sink)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_sink, sink))
            {
                _sink = null;
            }
        }
    }

    /// <summary>다 비운다. 방을 닫을 때.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _chunks.Clear();
            _bytes = 0;
            _sink = null;
        }
    }
}
