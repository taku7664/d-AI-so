using System.Text.Json;
using Daiso.Core;
using Daiso.Core.Sessions;
using Daiso.Host.Shared;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>말풍선 한 칸.</summary>
/// <param name="Seq">이 방에서 몇 번째 칸인가. 화면은 마지막으로 받은 번호 뒤만 다시 받는다.</param>
/// <param name="At">기록에 적힌 때.</param>
/// <param name="Kind"><c>me</c>(사람) · <c>ai</c>(답) · <c>tool</c>(도구 호출) · <c>result</c>(도구 결과. 성공·실패만 쓴다).</param>
/// <param name="Text">본문. 사람 글은 도구가 감싼 머리말을 벗긴 것.</param>
/// <param name="Tool">tool 일 때 도구 이름.</param>
/// <param name="Error">result 일 때 실패했는가.</param>
public sealed record ChatItem(long Seq, DateTimeOffset At, string Kind, string Text, string? Tool, bool Error);

/// <summary>말풍선 목록.</summary>
/// <param name="Found">이 방의 세션 기록을 찾았는가. 첫 메시지를 보내기 전에는 기록이 없을 수 있다(Codex 는 첫 메시지 때 만든다).</param>
/// <param name="Last">지금까지의 마지막 칸 번호. 다음에 <c>after</c> 로 보낸다.</param>
/// <param name="Items"><c>after</c> 뒤의 칸들. 처음 받을 때는 끝에서 <see cref="RoomChat.FirstPage"/>개까지.</param>
public sealed record ChatResponse(bool Found, long Last, IReadOnlyList<ChatItem> Items);

/// <summary>
/// 방 하나의 말풍선. 화면 글자를 읽지 않고 CLI 가 쓰는 세션 기록(JSONL)을 도구의 파서(<see cref="IProvider.ReadMessagesAsync"/>)로 읽는다
/// (docs/DECISIONS.md "말풍선은 세션 기록에서"). 읽은 곳(바이트)을 기억했다가 늘어난 만큼만 이어 읽는다.
/// <para>
/// 쓰는 중인 마지막 줄을 반만 읽지 않도록, 파일이 줄바꿈으로 끝날 때만 읽는다. 읽는 사이 새 줄이 붙어 두 번 읽히는 것은
/// 최근 칸과 같으면 버려서 막는다.
/// </para>
/// </summary>
public sealed class RoomChat
{
    /// <summary>처음 받을 때 보내는 칸 수. 수만 줄짜리 세션도 화면은 끝부분부터 그린다.</summary>
    public const int FirstPage = 300;

    /// <summary>들고 있는 칸 수. 넘치면 앞에서 버린다.</summary>
    private const int Keep = 2000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ChatItem> _items = [];
    private readonly Queue<string> _recentKeys = new();
    private readonly HashSet<string> _recent = new(StringComparer.Ordinal);
    private string? _path;
    private long _offset;
    private long _seq;

    public string? Path => _path;

    public Task<ChatResponse> ReadAsync(Room room, long after, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(room);
        return ReadAsync(room.Provider, Resolve(room), after, ct);
    }

    /// <summary>기록 파일을 정한 뒤의 일. 시험이 방 없이 부른다.</summary>
    internal async Task<ChatResponse> ReadAsync(IProvider provider, string? path, long after, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (path is null)
            {
                return new ChatResponse(false, _seq, []);
            }

            if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
            {
                // 다른 기록으로 넘어갔다(/clear 로 새 세션 등). 처음부터 다시 읽는다. 번호는 이어 간다
                _path = path;
                _offset = 0;
                _items.Clear();
                _recent.Clear();
                _recentKeys.Clear();
            }

            await ReadMoreAsync(provider, path, ct).ConfigureAwait(false);

            var items = after <= 0
                ? _items.Skip(Math.Max(0, _items.Count - FirstPage)).ToList()
                : _items.Where(item => item.Seq > after).ToList();
            return new ChatResponse(true, _seq, items);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReadMoreAsync(IProvider provider, string path, CancellationToken ct)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= _offset || !EndsWithNewline(path, info.Length))
        {
            return;
        }

        var end = info.Length;
        await foreach (var message in provider.ReadMessagesAsync(path, _offset, ct).ConfigureAwait(false))
        {
            if (message.IsSidechain || Map(message) is not { } item)
            {
                continue;
            }

            var key = $"{item.At:O}|{item.Kind}|{item.Text.Length}|{item.Text.GetHashCode(StringComparison.Ordinal)}";
            if (!_recent.Add(key))
            {
                continue;
            }

            _recentKeys.Enqueue(key);
            if (_recentKeys.Count > 500)
            {
                _recent.Remove(_recentKeys.Dequeue());
            }

            _items.Add(item with { Seq = ++_seq });
        }

        _offset = end;
        if (_items.Count > Keep)
        {
            _items.RemoveRange(0, _items.Count - Keep);
        }
    }

    private static ChatItem? Map(SessionMessage message)
    {
        switch (message.Role)
        {
            case MessageRole.User:
                var body = PromptNoise.Body(message.Text);
                return PromptNoise.IsNoise(body) ? null : new ChatItem(0, message.At, "me", body.Trim(), null, false);
            case MessageRole.Assistant:
                return string.IsNullOrWhiteSpace(message.Text) ? null : new ChatItem(0, message.At, "ai", message.Text.Trim(), null, false);
            case MessageRole.Tool when message.ToolName is { } name:
                return new ChatItem(0, message.At, "tool", SessionTitle.Clean(message.Text), name, false);
            case MessageRole.Tool:
                return new ChatItem(0, message.At, "result", string.Empty, null, message.IsError);
            default:
                return null; // 시스템·대화 요약 줄은 말풍선으로 보이지 않는다
        }
    }

    private static bool EndsWithNewline(string path, long length)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(length - 1, SeekOrigin.Begin);
            return stream.ReadByte() == '\n';
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// 이 방의 세션 기록 파일. 훅이 알려 준 경로 → (Claude) 방을 열 때 정한 세션 id → (Codex) 알림의 세션 id →
    /// (Codex) 방을 연 뒤 그 폴더에서 새로 생긴 기록 → 이어서 연 세션 파일 순서로 찾는다.
    /// </summary>
    internal static string? Resolve(Room room)
    {
        if (room.Transcript is { } told && File.Exists(told))
        {
            return told;
        }

        var root = room.Provider.SessionsRoot;
        if (Directory.Exists(root))
        {
            if (room.SessionId is { } id)
            {
                foreach (var folder in Directory.EnumerateDirectories(root))
                {
                    var file = System.IO.Path.Combine(folder, id + ".jsonl");
                    if (File.Exists(file))
                    {
                        return file;
                    }
                }
            }

            if (room.Provider.Kind == ToolKind.Codex)
            {
                if (room.Thread is { } thread
                    && Directory.EnumerateFiles(root, $"*{thread}.jsonl", SearchOption.AllDirectories).FirstOrDefault() is { } byThread)
                {
                    return byThread;
                }

                if (NewCodexRollout(room, root) is { } fresh)
                {
                    return fresh;
                }
            }
        }

        return room.ResumePath is { } resume && File.Exists(resume) ? resume : null;
    }

    /// <summary>방을 연 뒤 새로 생긴 Codex 기록 가운데 첫 줄(session_meta)의 폴더가 이 방 폴더인 것. 방을 연 때와 가장 가까운 것.</summary>
    private static string? NewCodexRollout(Room room, string root)
    {
        var since = room.StartedAt.UtcDateTime.AddSeconds(-5);
        var folder = ProjectGroups.Trim(room.Folder);

        return new DirectoryInfo(root)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .Where(file => file.CreationTimeUtc >= since)
            .OrderBy(file => file.CreationTimeUtc)
            .FirstOrDefault(file => string.Equals(CodexCwd(file.FullName), folder, StringComparison.OrdinalIgnoreCase))
            ?.FullName;
    }

    private static string? CodexCwd(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            using var first = JsonDocument.Parse(reader.ReadLine() ?? "{}");
            return first.RootElement.TryGetProperty("payload", out var payload)
                && payload.ValueKind == JsonValueKind.Object
                && payload.TryGetProperty("cwd", out var cwd)
                && cwd.ValueKind == JsonValueKind.String
                ? ProjectGroups.Trim(cwd.GetString()!)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
