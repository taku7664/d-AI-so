using System.Globalization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Daiso.Host.Security;

/// <summary>
/// 서버가 뜨면 주소를 알리고, 꺼지면 거둔다 (docs/SECURITY.md "서버를 띄우는 순서").
/// <list type="bullet">
/// <item>표준 출력 <c>DAISO_LISTENING http://127.0.0.1:{port}</c> — Electron 이 이 줄을 기다린다</item>
/// <item>혼자 떴으면 <c>DAISO_OPEN {토큰이 붙은 주소}</c> 도 — 사람이 그 주소를 크롬으로 연다</item>
/// <item><c>server.json</c> — AI 가 읽는다. 꺼질 때 지운다</item>
/// </list>
/// </summary>
public sealed class ServerAnnouncer : IHostedService
{
    /// <summary>Electron 이 찾는 줄 머리.</summary>
    public const string ListeningPrefix = "DAISO_LISTENING ";

    /// <summary>혼자 떴을 때 열 주소를 알리는 줄 머리.</summary>
    public const string OpenPrefix = "DAISO_OPEN ";

    private readonly IServer _server;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly DaisoHostOptions _options;
    private readonly ILogger<ServerAnnouncer> _logger;
    private readonly int _pid = Environment.ProcessId;

    public ServerAnnouncer(IServer server, IHostApplicationLifetime lifetime, DaisoHostOptions options, ILogger<ServerAnnouncer> logger)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _server = server;
        _lifetime = lifetime;
        _options = options;
        _logger = logger;
    }

    /// <summary><c>server.json</c> 경로.</summary>
    public string InfoPath => Path.Combine(_options.DataDirectory, ServerInfoFile.FileName);

    /// <summary>뜬 주소. 서버가 뜨기 전에는 null.</summary>
    public string? Url { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 포트는 Kestrel 이 실제로 연 뒤에야 안다. 호스티드 서비스는 서버보다 먼저 시작하므로 다 뜬 시점에 건다
        _lifetime.ApplicationStarted.Register(Announce);
        _lifetime.ApplicationStopping.Register(() => ServerInfoFile.DeleteIfOwned(InfoPath, _pid));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Announce()
    {
        var address = _server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault()
            ?? throw new InvalidOperationException("서버 주소를 알 수 없다");
        var port = new Uri(address).Port;

        Url = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}");
        var openUrl = $"{Url}/?{LocalOnlyMiddleware.TokenQuery}={Uri.EscapeDataString(_options.Token)}";

        // 표준 출력이 먼저다. Electron 은 이 줄을 기다린다 — 파일을 못 썼다고 줄까지 안 나가면 창이 끝없이 빈다
        Console.Out.WriteLine(ListeningPrefix + Url);

        if (_options.Standalone)
        {
            Console.Out.WriteLine(OpenPrefix + openUrl);
        }

        Console.Out.Flush();

        try
        {
            ServerInfoFile.Write(InfoPath, new ServerInfo(Url, openUrl, _options.Token, _pid, DateTimeOffset.Now));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 화면은 그대로 쓸 수 있다. AI 가 크롬 탭으로 여는 길만 막힌다
            _logger.LogError(ex, "{Path} 를 쓰지 못했다", InfoPath);
        }
    }
}
