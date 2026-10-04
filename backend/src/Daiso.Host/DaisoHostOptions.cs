using System.Globalization;
using Daiso.Host.Security;
using Daiso.Infrastructure;
using Daiso.Providers.Manifest;

namespace Daiso.Host;

/// <summary>
/// Host 를 띄울 때 정하는 것. 실제 실행은 <see cref="FromEnvironment"/> 로 만들고,
/// 테스트는 자료 폴더를 임시 폴더로 바꿔 진짜 사용자 폴더를 건드리지 않는다.
/// </summary>
public sealed record DaisoHostOptions
{
    /// <summary>Electron 이 넘기는 토큰. 없으면 Host 가 직접 만든다 (docs/SECURITY.md 2번).</summary>
    public const string TokenVariable = "DAISO_TOKEN";

    /// <summary>Electron 메인의 PID. 있으면 그 프로세스가 끝날 때 Host 도 끝난다.</summary>
    public const string ParentPidVariable = "DAISO_PARENT_PID";

    /// <summary>이번 실행의 토큰.</summary>
    public required string Token { get; init; }

    /// <summary>토큰을 Host 가 직접 만들었는가. Electron 없이 혼자 뜬 경우다.</summary>
    public bool Standalone { get; init; }

    /// <summary>지켜볼 부모 프로세스. null 이면 지켜보지 않는다.</summary>
    public int? ParentPid { get; init; }

    /// <summary>받을 포트. 0 이면 OS 가 고른다 (docs/SECURITY.md 1번).</summary>
    public int Port { get; init; }

    /// <summary><c>settings.json</c>·<c>server.json</c> 이 있는 자료 폴더.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>도구 플러그인 매니페스트 폴더.</summary>
    public required string PluginDirectory { get; init; }

    /// <summary>환경 변수에서 읽는다. 토큰을 읽은 뒤에는 환경 변수를 지운다.</summary>
    public static DaisoHostOptions FromEnvironment()
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);

        // 이 프로세스가 띄우는 터미널(PTY)은 환경 변수를 물려받는다. 그 안에서 도는 셸과 AI 에게 토큰을 넘기지 않는다
        Environment.SetEnvironmentVariable(TokenVariable, null);

        return Create(
            token,
            Environment.GetEnvironmentVariable(ParentPidVariable),
            AppPaths.Root,
            ToolPluginLoader.DefaultDirectory);
    }

    /// <summary>
    /// 환경 변수 값으로 만든다. 테스트는 이걸 바로 부른다 — <see cref="AppPaths.Root"/> 는 처음 물을 때
    /// 옛 자료 폴더를 옮기므로 테스트에서 건드리지 않는다.
    /// </summary>
    internal static DaisoHostOptions Create(string? token, string? parentPid, string dataDirectory, string pluginDirectory)
    {
        var standalone = string.IsNullOrEmpty(token);

        if (!standalone && token!.Length < AccessToken.MinimumLength)
        {
            throw new InvalidOperationException($"{TokenVariable} 가 너무 짧다. {AccessToken.MinimumLength} 자 이상이어야 한다");
        }

        return new DaisoHostOptions
        {
            Token = standalone ? AccessToken.Create() : token!,
            Standalone = standalone,
            ParentPid = ReadPid(parentPid),
            DataDirectory = dataDirectory,
            PluginDirectory = pluginDirectory,
        };
    }

    private static int? ReadPid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : throw new InvalidOperationException($"{ParentPidVariable} 가 PID 가 아니다: {value}");
    }
}
