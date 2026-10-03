using System.Diagnostics;
using Daiso.Core;
using Daiso.Providers.Common;

namespace Daiso.Infrastructure;

/// <summary>
/// <see cref="TerminalCommandBuilder"/>가 만든 명령을 새 창으로 실행한다. (ARCHITECTURE §5.3)
/// </summary>
public sealed class WindowsTerminalLauncher : ITerminalLauncher
{
    private readonly TerminalCommandBuilder _builder;

    public WindowsTerminalLauncher()
        : this(new TerminalCommandBuilder(ExecutableLocator.ExistsOnPath))
    {
    }

    public WindowsTerminalLauncher(TerminalCommandBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public Task LaunchAsync(string workingDir, string command, string arguments)
    {
        var built = _builder.Build(workingDir, command, arguments);

        var startInfo = new ProcessStartInfo(built.FileName, built.Arguments)
        {
            WorkingDirectory = workingDir,
            UseShellExecute = true,
        };

        Process.Start(startInfo)?.Dispose();

        return Task.CompletedTask;
    }
}
