using System.Diagnostics;

namespace Daiso.Host;

/// <summary>
/// 부모(Electron 메인)가 끝나면 Host 도 끝낸다. 고아 서버를 남기지 않는다 (docs/SECURITY.md "서버를 띄우는 순서" 4).
/// 부모 PID 를 받지 않았으면(혼자 띄웠으면) 아무것도 하지 않는다.
/// </summary>
public sealed class ParentWatcher : BackgroundService
{
    private readonly DaisoHostOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ParentWatcher> _logger;

    public ParentWatcher(DaisoHostOptions options, IHostApplicationLifetime lifetime, ILogger<ParentWatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.ParentPid is not { } pid)
        {
            return;
        }

        Process parent;

        try
        {
            parent = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // 이미 없다. 띄워 준 쪽이 사라졌으니 바로 끝낸다
            _logger.LogWarning("부모 프로세스 {Pid} 가 이미 없다. 끝낸다", pid);
            _lifetime.StopApplication();
            return;
        }

        using (parent)
        {
            try
            {
                await parent.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        _logger.LogInformation("부모 프로세스 {Pid} 가 끝났다. 따라 끝낸다", pid);
        _lifetime.StopApplication();
    }
}
