using System.Text;
using System.Text.Json;
using Daiso.Core;
using Daiso.Providers.Claude;
using Daiso.Providers.Common;
using Daiso.Providers.Manifest;

// 세션 어댑터 참조 구현 (docs/PLUGIN_PLAN.md Stage 5).
//
// 하는 일은 하나다: 줄 단위 JSON 을 읽어 그 도구의 세션 기록을 같은 꼴로 돌려준다.
// 여기서는 앱에 이미 있는 ClaudeProvider 를 감싼다 — 그래야 "매니페스트 + 어댑터로만
// 내장 도구를 재현할 수 있는가"를 실제로 견줄 수 있고, 그것이 Stage 5 의 완료 기준이다.
//
// 플러그인을 만드는 사람은 이 파일을 베껴 가운데(Provider 자리)만 자기 CLI 의 로그 읽기로 바꾸면 된다.
// 언어도 상관없다 — 앱이 보는 것은 stdin/stdout 의 줄뿐이다.
//
// 쓰는 법: Daiso.Adapter.Claude --home <사용자 폴더>

var home = ReadHome(args);
var provider = new ClaudeProvider(new ProviderHome(home), new ProcessProbe());

// 한글이 깨지지 않게 양쪽 다 UTF-8 로 못 박는다. 콘솔 기본 인코딩에 맡기면 PC 마다 달라진다
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

while (await Console.In.ReadLineAsync().ConfigureAwait(false) is { } line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    AdapterRequest? request;

    try
    {
        request = JsonSerializer.Deserialize<AdapterRequest>(line, AdapterProtocol.Json);
    }
    catch (JsonException ex)
    {
        Write(new AdapterResponse(AdapterProtocol.Version, Error: $"읽을 수 없는 요청: {ex.Message}", Done: true));
        continue;
    }

    if (request is null)
    {
        continue;
    }

    try
    {
        await HandleAsync(request).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
    {
        // 어댑터가 죽는 대신 그 요청만 실패한다. 앱은 이 줄을 사람에게 보여 준다
        Write(new AdapterResponse(AdapterProtocol.Version, Error: ex.Message, Done: true));
    }
}

async Task HandleAsync(AdapterRequest request)
{
    switch (request.Op)
    {
        case "hello":
            Write(new AdapterResponse(
                AdapterProtocol.Version,
                Ok: true,
                Name: "claude-reference",
                AppendOnly: provider.AppendOnlySessions,
                Done: true));
            break;

        case "sessions":
            await foreach (var session in provider.EnumerateSessionsAsync(CancellationToken.None).ConfigureAwait(false))
            {
                Write(new AdapterResponse(AdapterProtocol.Version, Session: ToWire(session)));
            }

            Write(new AdapterResponse(AdapterProtocol.Version, Done: true));
            break;

        case "session":
            var scanned = await provider
                .ReadSessionInfoAsync(request.FilePath ?? string.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Write(new AdapterResponse(AdapterProtocol.Version, Session: ToWire(scanned), Done: true));
            break;

        case "messages":
            var path = request.FilePath ?? string.Empty;

            await foreach (var message in provider
                .ReadMessagesAsync(path, request.FromByteOffset ?? 0, CancellationToken.None)
                .ConfigureAwait(false))
            {
                Write(new AdapterResponse(
                    AdapterProtocol.Version,
                    Message: new AdapterMessage(
                        message.At,
                        message.Role.ToString().ToLowerInvariant(),
                        message.Text,
                        message.IsSidechain)));
            }

            Write(new AdapterResponse(
                AdapterProtocol.Version,
                ReadTo: File.Exists(path) ? new FileInfo(path).Length : 0,
                Done: true));
            break;

        default:
            Write(new AdapterResponse(AdapterProtocol.Version, Error: $"모르는 op: {request.Op}", Done: true));
            break;
    }
}

static AdapterSession ToWire(SessionInfo session) => new(
    session.Id,
    session.FilePath,
    session.ProjectPath,
    session.StartedAt,
    session.ModifiedAt,
    session.SizeBytes,
    session.UserMessageCount,
    session.AssistantMessageCount,
    session.FirstPrompt,
    new AdapterUsage(
        session.Usage.Input,
        session.Usage.Output,
        session.Usage.CacheCreate,
        session.Usage.CacheRead,
        session.Usage.Model),
    session.ToolVersion,
    session.IsArchived,
    session.IsActive);

// 한 줄에 하나. 바로 흘려보내지 않으면 앱이 답을 기다리며 멈춘다
static void Write(AdapterResponse response)
{
    Console.Out.WriteLine(JsonSerializer.Serialize(response, AdapterProtocol.Json));
    Console.Out.Flush();
}

static string ReadHome(string[] args)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--home")
        {
            return args[i + 1];
        }
    }

    return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
