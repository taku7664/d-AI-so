using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Host.Services;
using Daiso.Host.Shared;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Daiso.Host.Tabs.Terminal;

/// <summary>방 하나.</summary>
/// <param name="Id">방 id.</param>
/// <param name="Tool">도구 id.</param>
/// <param name="Folder">작업 폴더.</param>
/// <param name="Name">방 이름.</param>
/// <param name="State"><c>idle</c> · <c>run</c> · <c>done</c> · <c>ask</c>(허락 기다림, Claude 만) · <c>exited</c>.</param>
/// <param name="Unseen">답이 끝났는데 아직 안 봤다.</param>
/// <param name="StartedAt">연 때.</param>
/// <param name="DoneAt">마지막으로 답이 끝난 때.</param>
/// <param name="ExitCode">프로세스가 끝났으면 종료 코드.</param>
/// <param name="Project">
/// 이 방이 속한 프로젝트 경로. 워크트리·Claude 임시 폴더는 원래 프로젝트다(<see cref="ProjectGroups"/>).
/// 방을 연 폴더에 세션이 아직 없어도 프로젝트 방 목록에 묶인다.
/// </param>
public sealed record RoomInfo(string Id, string Tool, string Folder, string Name, string State, bool Unseen, DateTimeOffset StartedAt, DateTimeOffset? DoneAt, int? ExitCode, string Project);

/// <summary>이름을 바꾸는 요청.</summary>
public sealed record RoomNameRequest(string Name);

/// <summary>말풍선 입력칸에서 보내는 글.</summary>
/// <param name="Text">여러 줄이어도 한 메시지로 들어간다.</param>
public sealed record SendRequest(string Text);

/// <summary>폴더를 가리키는 요청.</summary>
public sealed record FolderRequest(string Path);

/// <summary>
/// 터미널 탭 (docs/ROADMAP.md Stage 6). 방 목록·열기·닫기·이름, 그리고 방 화면을 잇는 WebSocket <c>/api/terminal/rooms/{id}/pty</c>.
/// WebSocket 도 보안 미들웨어를 지난다(쿠키 + Origin).
/// </summary>
public sealed class TerminalEndpoints : ITabEndpoints
{
    public string Id => "terminal";

    public void Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/rooms", ListAsync).WithName("ListRooms");
        group.MapPost("/rooms", OpenAsync).WithName("OpenRoom");
        group.MapPost("/rooms/{id}/close", (string id, RoomService rooms) => rooms.Close(id) ? Results.NoContent() : Results.NotFound()).WithName("CloseRoom");
        group.MapPost("/rooms/{id}/seen", Seen).WithName("MarkRoomSeen");
        group.MapPut("/rooms/{id}/name", Rename).WithName("RenameRoom");
        group.Map("/rooms/{id}/pty", PtyAsync).ExcludeFromDescription();
        group.MapGet("/models", ModelsAsync).WithName("ListModels");
        group.MapGet("/rooms/{id}/chat", ChatAsync).WithName("GetRoomChat");
        group.MapPost("/rooms/{id}/send", SendAsync).WithName("SendToRoom");
        group.MapPost("/rooms/{id}/image", PasteImageAsync).WithName("PasteImageToRoom");
        group.MapGet("/commands", ListCommands).WithName("ListCommands");
        group.MapPost("/external", ExternalAsync).WithName("OpenExternalTerminal");
        group.MapPost("/open-folder", OpenFolder).WithName("OpenFolder");
    }

    private static async Task<List<RoomInfo>> ListAsync(RoomService rooms, ProjectCatalog catalog, CancellationToken ct)
    {
        var known = await KnownProjectsAsync(catalog, ct).ConfigureAwait(false);
        return [.. rooms.Rooms.Select(room => Info(room, known))];
    }

    private static async Task<IReadOnlyList<string>> KnownProjectsAsync(ProjectCatalog catalog, CancellationToken ct) =>
        [.. (await catalog.ListAsync(ct).ConfigureAwait(false)).Select(project => project.Path)];

    internal static RoomInfo Info(Room room, IReadOnlyList<string> knownProjects)
    {
        // Codex 워크트리는 같은 이름의 프로젝트를 찾아 묶으므로 아는 프로젝트들과 같이 넘긴다
        var folder = ProjectGroups.Trim(room.Folder);
        var project = ProjectGroups.Map(knownProjects.Append(folder)).GetValueOrDefault(folder, folder);

        return new(
            room.Id,
            room.Provider.Kind.Id,
            room.Folder,
            room.Name,
            room.State.ToString().ToLowerInvariant(),
            room.Unseen,
            room.StartedAt,
            room.DoneAt,
            room.ExitCode,
            project);
    }

    private static async Task<Results<Ok<RoomInfo>, ProblemHttpResult>> OpenAsync(
        OpenRoomRequest request,
        RoomService rooms,
        IServiceProvider services,
        ISettingsStore settings,
        ProjectCatalog catalog,
        CancellationToken ct)
    {
        SessionInfo? resume = null;
        if (!string.IsNullOrEmpty(request.ResumePath))
        {
            var all = await services.GetRequiredService<ISessionIndex>().ListAsync(SessionFilter.All, ct).ConfigureAwait(false);
            resume = all.FirstOrDefault(session => string.Equals(session.FilePath, request.ResumePath, StringComparison.OrdinalIgnoreCase));
            if (resume is null)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "이어서 열 세션이 목록에 없다");
            }
        }

        try
        {
            var room = rooms.Open(request, resume);
            AppSettings.Remember(settings.Current.RecentFolders, request.Folder);
            settings.Save();
            return TypedResults.Ok(Info(room, await KnownProjectsAsync(catalog, ct).ConfigureAwait(false)));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "방을 열지 못했다", detail: ex.Message);
        }
    }

    /// <summary>말풍선. <paramref name="after"/> 뒤의 칸만 준다. 0 이면 끝부분 한 쪽.</summary>
    /// <param name="id">방 id.</param>
    /// <param name="rooms">방들.</param>
    /// <param name="ct">요청이 끊기면 멈춘다.</param>
    /// <param name="after">화면이 마지막으로 받은 칸 번호.</param>
    private static async Task<Results<Ok<ChatResponse>, NotFound>> ChatAsync(string id, RoomService rooms, CancellationToken ct, long after = 0)
    {
        if (rooms.Find(id) is not { } room)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await room.Chat.ReadAsync(room, after, ct).ConfigureAwait(false));
    }

    /// <summary>말풍선 입력칸의 글을 그 방 CLI 의 입력 줄에 넣고 Enter 를 친다.</summary>
    private static async Task<IResult> SendAsync(string id, SendRequest request, RoomService rooms, CancellationToken ct)
    {
        if (rooms.Find(id) is not { } room)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxSend)
        {
            return Results.BadRequest();
        }

        await room.SendAsync(request.Text, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>
    /// 그 도구의 그림 붙이기 키를 CLI 에 보내고, CLI 가 입력 줄에 첨부 표시를 그릴 때까지 기다린다.
    /// CLI 가 시스템 클립보드의 그림을 스스로 읽어 첨부한다(Claude Code 는 Alt+V, Codex 는 Ctrl+V — 옛 앱 0694861).
    /// 그림을 클립보드에 올리는 일은 화면이 먼저 한다. 키가 없는 도구나 끝난 방은 409, 제때 첨부되지 않으면 504.
    /// </summary>
    private static async Task<IResult> PasteImageAsync(string id, RoomService rooms, CancellationToken ct)
    {
        if (rooms.Find(id) is not { } room)
        {
            return Results.NotFound();
        }

        if (room.State == RoomState.Exited || string.IsNullOrEmpty(room.Provider.ImagePasteKeys))
        {
            return Results.Conflict();
        }

        return await room.PasteImageAsync(ImageWait, ct).ConfigureAwait(false)
            ? Results.NoContent()
            : Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }

    /// <summary>그림 하나를 첨부하기를 기다리는 시간. Claude 는 PowerShell 로 클립보드를 읽어 1~2초 걸린다.</summary>
    private static readonly TimeSpan ImageWait = TimeSpan.FromSeconds(8);

    /// <summary>보낼 글의 최대 길이. 붙여 넣은 큰 파일 내용이 터미널을 멈추지 않게.</summary>
    private const int MaxSend = 100_000;

    /// <summary>/ 목록. 도구 기본 명령과 내 명령·스킬, 이 프로젝트의 명령·스킬.</summary>
    private static IReadOnlyList<CommandGroup> ListCommands(string tool, ToolRegistry tools, string? folder = null)
    {
        if (!ToolKind.TryParse(tool, out var kind) || tools.Tools.FirstOrDefault(provider => provider.Kind == kind) is not { } found)
        {
            return [];
        }

        return Commands.For(found, folder);
    }

    private static IResult Seen(string id, RoomService rooms)
    {
        if (rooms.Find(id) is not { } room)
        {
            return Results.NotFound();
        }

        room.MarkSeen();
        return Results.NoContent();
    }

    private static IResult Rename(string id, RoomNameRequest request, RoomService rooms)
    {
        if (rooms.Find(id) is not { } room || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.NotFound();
        }

        room.Name = request.Name.Trim();
        room.MarkSeen();
        return Results.NoContent();
    }

    /// <summary>모델 목록. 도구가 알려 주는 만큼만. 모르면 빈 목록(도구 설정대로).</summary>
    private static async Task<IReadOnlyList<ModelOption>> ModelsAsync(string tool, ToolRegistry tools, CancellationToken ct)
    {
        if (!ToolKind.TryParse(tool, out var kind) || tools.Tools.FirstOrDefault(provider => provider.Kind == kind) is not { } found)
        {
            return [];
        }

        return await found.ListModelsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>바깥 터미널 창으로 연다. 상태 훅은 넣지 않는다(방이 아니다).</summary>
    private static async Task<IResult> ExternalAsync(OpenRoomRequest request, ToolRegistry tools, ITerminalLauncher launcher, IServiceProvider services, CancellationToken ct)
    {
        if (!ToolKind.TryParse(request.Tool, out var kind) || tools.Tools.FirstOrDefault(provider => provider.Kind == kind) is not { } provider || !Directory.Exists(request.Folder))
        {
            return Results.BadRequest();
        }

        var arguments = request.Arguments;
        if (!string.IsNullOrEmpty(request.ResumePath))
        {
            var all = await services.GetRequiredService<ISessionIndex>().ListAsync(SessionFilter.All, ct).ConfigureAwait(false);
            if (all.FirstOrDefault(session => string.Equals(session.FilePath, request.ResumePath, StringComparison.OrdinalIgnoreCase)) is { } session)
            {
                arguments = $"{provider.BuildResumeArguments(session)} {arguments}".Trim();
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            arguments = ModelArgument.Apply(arguments, request.Model);
        }

        await launcher.LaunchAsync(request.Folder, provider.LaunchTarget, arguments ?? string.Empty).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>탐색기로 폴더를 연다. 있는 폴더만.</summary>
    private static IResult OpenFolder(FolderRequest request)
    {
        if (!Directory.Exists(request.Path))
        {
            return Results.NotFound();
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { request.Path }, UseShellExecute = false })?.Dispose();
        return Results.NoContent();
    }

    // ── 방 화면 ──

    /// <summary>
    /// 방 화면. 서버 → 화면은 바이너리(콘솔 출력 바이트 그대로)와 텍스트 <c>{"t":"exit","code":n}</c>.
    /// 화면 → 서버는 텍스트 <c>{"t":"in","d":"…"}</c> · <c>{"t":"resize","c":120,"r":30}</c>.
    /// 붙자마자 지난 출력을 한 번에 보낸다. 화면은 받기 전에 <c>reset</c> 해 둔다.
    /// </summary>
    private static async Task PtyAsync(HttpContext context, string id, RoomService rooms)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (rooms.Find(id) is not { } room)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var ct = context.RequestAborted;
        var (snapshot, live) = room.Output.Subscribe();

        try
        {
            if (snapshot.Length > 0)
            {
                await socket.SendAsync(snapshot, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
            }

            var pump = PumpAsync(socket, room, live.Reader, ct);
            await ReceiveAsync(socket, room, ct).ConfigureAwait(false);
            room.Output.Unsubscribe(live);
            await pump.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // 화면이 닫혔다
        }
        finally
        {
            room.Output.Unsubscribe(live);
        }
    }

    private static async Task PumpAsync(WebSocket socket, Room room, System.Threading.Channels.ChannelReader<byte[]> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await socket.SendAsync(chunk, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
            }

            if (room.State == RoomState.Exited && socket.State == WebSocketState.Open)
            {
                var exit = JsonSerializer.SerializeToUtf8Bytes(new { t = "exit", code = room.ExitCode });
                await socket.SendAsync(exit, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
        {
            // 화면이 닫혔다
        }
    }

    private static async Task ReceiveAsync(WebSocket socket, Room room, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            Handle(room, Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            message.SetLength(0);
        }
    }

    private static void Handle(Room room, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "in" when root.TryGetProperty("d", out var data) && data.GetString() is { Length: > 0 } input:
                    room.Write(input);
                    break;
                case "resize" when root.TryGetProperty("c", out var c) && root.TryGetProperty("r", out var r):
                    room.Resize(Math.Clamp(c.GetInt32(), 10, 500), Math.Clamp(r.GetInt32(), 5, 300));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // 모르는 메시지는 버린다
        }
    }
}
