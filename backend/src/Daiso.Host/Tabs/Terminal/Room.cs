using Daiso.Core;
using Daiso.Infrastructure.Pty;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>
/// 터미널 방 하나: AI CLI 프로세스 하나와 그 출력. 앱이 꺼지면 같이 끝난다(프로세스는 Job 오브젝트에 묶여 있다).
/// </summary>
public sealed class Room : IDisposable
{
    private readonly Lock _gate = new();
    private readonly PtySession _session;
    private int _columns;
    private int _rows;

    internal Room(string id, IProvider provider, string folder, string name, PtySession session, int columns, int rows)
    {
        Id = id;
        Provider = provider;
        Folder = folder;
        Name = name;
        _session = session;
        _columns = columns;
        _rows = rows;
        StartedAt = DateTimeOffset.Now;
        State = RoomState.Idle;

        _session.OutputReceived += Output.Append;
        _session.Exited += code =>
        {
            ExitCode = code;
            SetState(RoomState.Exited);
            Output.Complete();
        };
    }

    /// <summary>32자 16진수. 상태 파일 이름으로도 쓴다.</summary>
    public string Id { get; }

    public IProvider Provider { get; }

    public string Folder { get; }

    /// <summary>사람이 붙이는 이름. 같은 폴더의 방 둘을 이것으로 가른다.</summary>
    public string Name { get; set; }

    public DateTimeOffset StartedAt { get; }

    public RoomOutput Output { get; } = new();

    public RoomState State { get; private set; }

    /// <summary>마지막으로 답이 끝난 때.</summary>
    public DateTimeOffset? DoneAt { get; private set; }

    /// <summary>사람이 마지막으로 이 방을 본 때.</summary>
    public DateTimeOffset? SeenAt { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>(Claude) 방을 열 때 정해 준 세션 id. 세션 기록 파일 이름이 된다. 이어서 연 방·사람이 인자로 정한 방은 null.</summary>
    public string? SessionId { get; init; }

    /// <summary>이어서 연 세션 파일.</summary>
    public string? ResumePath { get; init; }

    /// <summary>(Claude) 훅이 마지막으로 알려 준 세션 기록 파일.</summary>
    public string? Transcript { get; set; }

    /// <summary>(Codex) 알림이 알려 준 세션(스레드) id.</summary>
    public string? Thread { get; set; }

    /// <summary>말풍선 보기.</summary>
    public RoomChat Chat { get; } = new();

    /// <summary>답이 끝났는데 아직 안 봤다.</summary>
    public bool Unseen => State == RoomState.Done && DoneAt is { } done && (SeenAt is not { } seen || seen < done);

    /// <summary>상태가 바뀌면 부른다. 서버가 화면에 알린다.</summary>
    public event Action<Room>? Changed;

    /// <summary>
    /// 말풍선 입력칸의 글을 CLI 입력 줄에 붙여 넣고 Enter 를 친다. 붙여넣기(bracketed paste)로 넣어야 여러 줄이 한 메시지가 된다
    /// (2026-10-07 Claude·Codex 로 확인). 붙여넣기를 받아들일 틈을 조금 둔다.
    /// </summary>
    public async Task SendAsync(string text, CancellationToken ct)
    {
        Write(PasteStart + text.Replace("\r\n", "\n", StringComparison.Ordinal) + PasteEnd);
        await Task.Delay(PasteSettle, ct).ConfigureAwait(false);
        Write("\r");
    }

    private const string PasteStart = "\u001b[200~";
    private const string PasteEnd = "\u001b[201~";
    private static readonly TimeSpan PasteSettle = TimeSpan.FromMilliseconds(300);

    /// <summary>키 입력을 보낸다. Enter 가 들어 있으면 일을 시작한 것으로 본다(훅이 없는 도구도 "작업 중"이 보이게).</summary>
    public void Write(string text)
    {
        _session.Write(text);

        if (text.Contains('\r', StringComparison.Ordinal) && State is RoomState.Idle or RoomState.Done or RoomState.Ask)
        {
            SetState(RoomState.Run);
        }
    }

    /// <summary>크기가 같으면 보내지 않는다. 같은 크기를 다시 보내면 ConPTY 가 다시 그려 줄이 겹쳤다(옛 앱 eb521fa).</summary>
    public void Resize(int columns, int rows)
    {
        lock (_gate)
        {
            if (columns == _columns && rows == _rows)
            {
                return;
            }

            _columns = columns;
            _rows = rows;
        }

        _session.Resize(columns, rows);
    }

    public void MarkSeen()
    {
        SeenAt = DateTimeOffset.Now;
        Changed?.Invoke(this);
    }

    public void SetState(RoomState state)
    {
        lock (_gate)
        {
            if (State == RoomState.Exited || State == state)
            {
                return;
            }

            State = state;
            if (state == RoomState.Done)
            {
                DoneAt = DateTimeOffset.Now;
            }
        }

        Changed?.Invoke(this);
    }

    public void Dispose()
    {
        _session.OutputReceived -= Output.Append;
        _session.Dispose();
        Output.Complete();
    }
}

/// <summary>방 상태.</summary>
public enum RoomState
{
    /// <summary>띄웠고 아직 아무것도 안 시켰다.</summary>
    Idle,

    /// <summary>일하는 중.</summary>
    Run,

    /// <summary>답이 끝나고 입력을 기다린다.</summary>
    Done,

    /// <summary>도구 사용 허락을 기다린다. Claude 만 안다.</summary>
    Ask,

    /// <summary>프로세스가 끝났다.</summary>
    Exited,
}
