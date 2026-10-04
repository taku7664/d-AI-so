using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Daiso.Host.Notifications;

/// <summary>
/// 공용 WebSocket <c>/ws</c> 에 실어 보내는 한 줄. 무엇이 바뀌었는지만 알리고 바뀐 데이터는 싣지 않는다.
/// 화면은 이걸 받으면 그 데이터를 다시 요청한다 (docs/ARCHITECTURE.md "화면과 서버가 주고받는 법").
/// </summary>
/// <param name="Tab">탭 id. 화면 쪽 <c>tab.id</c> 와 같다.</param>
/// <param name="Kind">무슨 일인가. 지금은 <c>changed</c> 하나다.</param>
public sealed record Notification(string Tab, string Kind);

/// <summary>
/// <c>/ws</c> 에 붙은 화면 전부에 알림을 보낸다. 크롬 탭과 Electron 창이 같이 붙어 있을 수 있다.
/// </summary>
public sealed class NotificationHub
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();

    /// <summary>지금 붙어 있는 화면 수.</summary>
    public int Count => _clients.Count;

    /// <summary>모두에게 보낸다. 보내다 끊긴 쪽은 빼고 나머지에는 계속 보낸다.</summary>
    public async Task PublishAsync(Notification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var payload = JsonSerializer.SerializeToUtf8Bytes(notification, Options);

        foreach (var (id, client) in _clients)
        {
            if (!await client.TrySendAsync(payload, ct).ConfigureAwait(false))
            {
                _clients.TryRemove(id, out _);
            }
        }
    }

    /// <summary>
    /// 받은 WebSocket 을 목록에 넣고 닫힐 때까지 붙들고 있는다. 화면이 보내는 것은 읽고 버린다 —
    /// 이 통로는 서버 → 화면 한 방향이다. 읽기를 돌려야 닫힘을 알 수 있다.
    /// </summary>
    public async Task HoldAsync(WebSocket socket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(socket);

        var id = Guid.NewGuid();
        using var client = new Client(socket);
        _clients[id] = client;

        try
        {
            var buffer = new byte[1024];

            while (socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await client.CloseAsync(ct).ConfigureAwait(false);
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // 화면이 그냥 사라졌거나 서버가 꺼지는 중이다
        }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }

    /// <summary>WebSocket 하나. 한 소켓에 두 번 동시에 보내면 안 되므로 보내기를 줄 세운다.</summary>
    private sealed class Client(WebSocket socket) : IDisposable
    {
        private readonly SemaphoreSlim _sending = new(1, 1);

        public async Task<bool> TrySendAsync(byte[] payload, CancellationToken ct)
        {
            await _sending.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (socket.State != WebSocketState.Open)
                {
                    return false;
                }

                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
                return true;
            }
            catch (WebSocketException)
            {
                return false;
            }
            finally
            {
                _sending.Release();
            }
        }

        public async Task CloseAsync(CancellationToken ct)
        {
            await _sending.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct).ConfigureAwait(false);
            }
            finally
            {
                _sending.Release();
            }
        }

        public void Dispose() => _sending.Dispose();
    }
}
