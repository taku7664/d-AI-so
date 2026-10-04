using System.Text;
using System.Text.Json;

namespace Daiso.Host.Security;

/// <summary>
/// <c>server.json</c> 에 담는 것. 같은 사용자의 AI 가 이걸 읽고 크롬 탭으로 화면을 연다 (docs/SECURITY.md 7번).
/// </summary>
/// <param name="Url">서버 주소. 끝에 <c>/</c> 가 없다.</param>
/// <param name="OpenUrl">처음 열 때 쓰는 주소. 토큰이 붙어 있다.</param>
/// <param name="Token">이번 실행의 토큰.</param>
/// <param name="Pid">이 파일을 쓴 Host 프로세스.</param>
/// <param name="StartedAt">뜬 시각.</param>
public sealed record ServerInfo(string Url, string OpenUrl, string Token, int Pid, DateTimeOffset StartedAt);

/// <summary><c>server.json</c> 쓰기·지우기.</summary>
public static class ServerInfoFile
{
    /// <summary>자료 폴더 안의 파일 이름.</summary>
    public const string FileName = "server.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>임시 파일에 다 쓰고 자리를 바꾼다. 읽는 쪽이 반쪽 파일을 보지 않게.</summary>
    public static void Write(string path, ServerInfo info)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(info);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(info, Options), Utf8NoBom);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>읽는다. 없거나 깨졌으면 null.</summary>
    public static ServerInfo? Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ServerInfo>(File.ReadAllText(path, Encoding.UTF8), Options)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// <b>이 프로세스가 쓴 것일 때만</b> 지운다. Host 를 둘 띄웠다가 먼저 띄운 쪽을 끄면,
    /// 나중 것이 쓴 파일까지 지워서 AI 가 살아 있는 서버를 못 찾게 된다.
    /// </summary>
    public static void DeleteIfOwned(string path, int pid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Read(path) is not { } info || info.Pid != pid)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 못 지워도 다음 실행이 덮어쓴다. 끄는 길을 막지 않는다
        }
    }
}
