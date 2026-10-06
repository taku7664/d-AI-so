using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daiso.Core;
using Daiso.Host.Notifications;
using Daiso.Host.Services;
using Daiso.Infrastructure.Pty;
using Daiso.Providers.Common;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>방을 여는 요청.</summary>
/// <param name="Tool">도구 id.</param>
/// <param name="Folder">작업 폴더. 있어야 한다.</param>
/// <param name="ResumePath">이어서 열 세션 파일. 비우면 새 대화.</param>
/// <param name="Model">모델 id. 비우면 도구 설정대로.</param>
/// <param name="Arguments">사람이 적은 옵션 인자. 그대로 붙는다.</param>
/// <param name="Name">방 이름. 비우면 폴더 이름.</param>
public sealed record OpenRoomRequest(string Tool, string Folder, string? ResumePath = null, string? Model = null, string? Arguments = null, string? Name = null);

/// <summary>
/// 터미널 방들. 방을 띄우고, 상태를 모으고, 바뀌면 <c>/ws</c> 로 알린다.
/// <para>
/// 상태는 도구가 알려 준다. DAIso 가 띄운 프로세스에만 훅을 넣고 사용자 설정 파일은 고치지 않는다.
/// Claude 는 <c>--settings {방 설정 파일}</c> 의 hooks, Codex 는 <c>-c notify=[...]</c>.
/// 훅 명령은 <c>Daiso.StatusLine.exe --hook …</c> 이고 <c>{자료 폴더}\rooms\{방}.json</c> 에 상태를 남긴다. 여기서 그 폴더를 지켜본다.
/// </para>
/// <para>
/// 셸로 감싸지 않고 CLI 를 바로 띄운다. 옛 앱은 PowerShell 로 감쌌다가 첫 메시지의 <c>$</c>·백틱이 실행되는 일을 겪었다(옛 앱 779e734).
/// CLI 가 끝나면 방도 끝난다.
/// </para>
/// </summary>
public sealed class RoomService : IHostedService, IDisposable
{
    public const string Topic = "terminal";

    private const int DefaultColumns = 120;
    private const int DefaultRows = 30;

    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly DaisoHostOptions _options;
    private readonly ToolRegistry _tools;
    private readonly NotificationHub _hub;
    private readonly ILogger<RoomService> _logger;
    private FileSystemWatcher? _watcher;

    public RoomService(DaisoHostOptions options, ToolRegistry tools, NotificationHub hub, ILogger<RoomService> logger)
    {
        _options = options;
        _tools = tools;
        _hub = hub;
        _logger = logger;
    }

    private string StateFolder => Path.Combine(_options.DataDirectory, "rooms");

    /// <summary>훅 명령으로 넣는 실행 파일. Host 옆에 놓인다.</summary>
    private static string HookExe => Path.Combine(AppContext.BaseDirectory, "Daiso.StatusLine.exe");

    /// <summary>연 순서대로.</summary>
    public IReadOnlyList<Room> Rooms => [.. _rooms.Values.OrderBy(room => room.StartedAt)];

    public Room? Find(string id) => _rooms.GetValueOrDefault(id);

    /// <summary>방을 띄운다. 도구·폴더가 틀리면 <see cref="ArgumentException"/>.</summary>
    public Room Open(OpenRoomRequest request, SessionInfo? resume)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = Provider(request.Tool) ?? throw new ArgumentException("모르는 도구다");
        if (!Directory.Exists(request.Folder))
        {
            throw new ArgumentException("폴더가 없다");
        }

        var id = Guid.NewGuid().ToString("N");

        // 새 Claude 대화는 세션 id 를 정해 준다. 그러면 말풍선 보기가 첫 메시지부터 기록 파일을 바로 찾는다.
        // 사람이 인자로 세션을 고른 경우(--resume·--continue·--session-id)는 건드리지 않는다
        var sessionId = provider.Kind == ToolKind.Claude && resume is null && !ChoosesSession(request.Arguments)
            ? Guid.NewGuid().ToString()
            : null;

        var arguments = string.Join(' ', new[]
        {
            resume is null ? null : provider.BuildResumeArguments(resume),
            sessionId is null ? null : $"--session-id {sessionId}",
            // 모델을 골랐을 때만 인자의 --model 을 갈아 끼운다. 안 골랐으면 사람이 적은 --model 을 지우지 않는다
            string.IsNullOrWhiteSpace(request.Model) ? request.Arguments : ModelArgument.Apply(request.Arguments, request.Model),
            HookArguments(provider, id),
        }.Where(part => !string.IsNullOrWhiteSpace(part)));

        var session = PtySession.Start(CommandLine(Target(provider), arguments), request.Folder, DefaultColumns, DefaultRows);
        var name = string.IsNullOrWhiteSpace(request.Name) ? Path.GetFileName(request.Folder.TrimEnd('\\', '/')) : request.Name.Trim();
        var room = new Room(id, provider, request.Folder, name, session, DefaultColumns, DefaultRows)
        {
            SessionId = sessionId,
            ResumePath = resume?.FilePath,
        };

        room.Changed += _ => Publish();
        _rooms[id] = room;
        Publish();
        return room;
    }

    public bool Close(string id)
    {
        if (!_rooms.TryRemove(id, out var room))
        {
            return false;
        }

        // 핸들 정리는 2초까지 걸릴 수 있다. 요청을 붙잡지 않는다(옛 앱 c67dbe1)
        _ = Task.Run(room.Dispose);
        TryDelete(Path.Combine(StateFolder, id + ".json"));
        TryDelete(Path.Combine(StateFolder, id + ".settings.json"));
        Publish();
        return true;
    }

    /// <summary>사람이 적은 인자가 이미 세션을 고르는가.</summary>
    private static bool ChoosesSession(string? arguments) =>
        arguments is not null
        && arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(arg => arg is "--session-id" or "--resume" or "-r" or "--continue" or "-c");

    /// <summary>
    /// 바뀐 세션 기록 파일들(<see cref="Services.IndexWatcher"/>). 말풍선 보기가 읽는 기록이거나 아직 기록을 못 찾은 방이 있으면 화면에 알린다.
    /// </summary>
    public void SessionFilesChanged(IReadOnlyCollection<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var touched = _rooms.Values.Any(room =>
            room.Chat.Path is not { } path
                ? paths.Any(changed => changed.StartsWith(room.Provider.SessionsRoot, StringComparison.OrdinalIgnoreCase))
                : paths.Contains(path, StringComparer.OrdinalIgnoreCase));

        if (touched)
        {
            _ = _hub.PublishAsync(new Notification(Topic, "chat"), CancellationToken.None);
        }
    }

    /// <summary>
    /// 띄울 명령 줄. <c>.cmd</c>(npm 래퍼)는 cmd 가 돌린다. 바깥 따옴표만 벗기는 <c>/s /c "…"</c> 라 안의 따옴표는 그대로 간다.
    /// </summary>
    internal static string CommandLine(string target, string arguments)
    {
        var call = $"\"{target}\"{(arguments.Length > 0 ? " " + arguments : string.Empty)}";
        return target.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? $"cmd.exe /d /s /c \"{call}\""
            : call;
    }

    /// <summary>실행 파일 절대 경로. npm 도구는 <c>.cmd</c>, 그 밖에는 PATH 에서 찾은 실행 파일. 못 찾으면 이름 그대로.</summary>
    private static string Target(IProvider provider) =>
        Path.IsPathRooted(provider.LaunchTarget)
            ? provider.LaunchTarget
            : ExecutableLocator.Find(provider.LaunchTarget) ?? provider.LaunchTarget;

    /// <summary>상태 훅 인자. 훅을 넣을 길이 없는 도구는 빈 문자열.</summary>
    private string HookArguments(IProvider provider, string id)
    {
        var data = _options.DataDirectory.Replace('\\', '/');
        var exe = HookExe.Replace('\\', '/');

        if (provider.Kind == ToolKind.Claude)
        {
            string Hook(string state) => $"\"{exe}\" --hook {state} --room {id} --data \"{data}\"";
            JsonObject Entry(string state, string? matcher = null)
            {
                var entry = new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = Hook(state) }) };
                if (matcher is not null)
                {
                    entry["matcher"] = matcher;
                }

                return entry;
            }

            JsonArray Event(string state) => new(Entry(state));

            var settings = new JsonObject
            {
                ["hooks"] = new JsonObject
                {
                    ["UserPromptSubmit"] = Event("run"),
                    ["PostToolUse"] = Event("run"),
                    ["Stop"] = Event("done"),
                    // idle_prompt: 답이 끝나고 한동안(약 60초) 입력이 없다. 로그인 만료 같은 오류로 Stop 이 안 와도 "작업 중"에 머물지 않게 한다
                    ["Notification"] = new JsonArray(Entry("ask", "permission_prompt"), Entry("done", "idle_prompt")),
                },
            };

            var path = Path.Combine(StateFolder, id + ".settings.json");
            Directory.CreateDirectory(StateFolder);
            File.WriteAllText(path, settings.ToJsonString(), new UTF8Encoding(false));
            return $"--settings \"{path.Replace('\\', '/')}\"";
        }

        if (provider.Kind == ToolKind.Codex)
        {
            // TOML 리터럴 문자열('…')은 역슬래시를 그대로 둔다. Codex 는 알림 JSON 을 마지막 인자로 붙인다
            return $"-c \"notify=['{exe}','--hook','codex','--room','{id}','--data','{data}']\"";
        }

        return string.Empty;
    }

    private IProvider? Provider(string tool) =>
        ToolKind.TryParse(tool, out var kind) ? _tools.Tools.FirstOrDefault(provider => provider.Kind == kind) : null;

    // ── 상태 파일 ──

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StateFolder);
        _watcher = new FileSystemWatcher(StateFolder, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        _watcher.Changed += (_, e) => ReadState(e.FullPath);
        _watcher.Created += (_, e) => ReadState(e.FullPath);
        _watcher.Renamed += (_, e) => ReadState(e.FullPath);
        _watcher.EnableRaisingEvents = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void ReadState(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".settings.json", StringComparison.OrdinalIgnoreCase) || Find(Path.GetFileNameWithoutExtension(name)) is not { } room)
        {
            return;
        }

        try
        {
            var record = JsonNode.Parse(File.ReadAllText(path));
            var state = record?["state"]?.GetValue<string>();

            // 말풍선 보기가 읽을 기록. 상태보다 먼저 넣어야 상태 알림을 받은 화면이 새 기록을 읽는다
            if (record?["transcript"]?.GetValue<string>() is { Length: > 0 } transcript)
            {
                room.Transcript = transcript;
            }

            if (record?["thread"]?.GetValue<string>() is { Length: > 0 } thread)
            {
                room.Thread = thread;
            }

            room.SetState(state switch
            {
                "run" => RoomState.Run,
                "done" => RoomState.Done,
                "ask" => RoomState.Ask,
                _ => room.State,
            });
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            // 훅이 쓰는 중이면 다음 알림 때 읽는다
        }
    }

    private void Publish() => _ = _hub.PublishAsync(new Notification(Topic, "changed"), CancellationToken.None);

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "{Path} 를 지우지 못했다", path);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;

        // 앱을 끌 때 방을 한꺼번에 닫는다. 하나씩 닫으면 방마다 2초씩 기다린다(옛 앱 835d63b)
        var rooms = _rooms.Values.ToList();
        _rooms.Clear();
        Parallel.ForEach(rooms, room => room.Dispose());
    }
}
