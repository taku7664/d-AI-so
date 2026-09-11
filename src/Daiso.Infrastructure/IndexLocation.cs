using Microsoft.Data.Sqlite;

namespace Daiso.Infrastructure;

/// <summary>
/// 인덱스 파일을 어디에 열지 정한다.
///
/// <para>
/// 설정에 적힌 경로를 쓸 수 없으면 <b>기본 경로로 물러선다</b>. 예전에는 그대로 던졌는데,
/// 그 자리가 DI 를 짜는 도중(창이 뜨기 전)이라 앱이 조용히 죽었다 — 설정 화면을 열 수 없으니
/// 사람이 고칠 길은 손으로 <c>settings.json</c> 을 여는 것뿐이었다
/// (2026-09-11 없는 드라이브 <c>Z:\</c> 로 재현).
/// </para>
///
/// <para>
/// 물러선 사실은 <see cref="Failure"/> 에 남겨 설정 화면이 왜 그랬는지 말하게 한다 —
/// 조용히 다른 곳을 쓰는 것도 거짓말이다.
/// </para>
/// </summary>
/// <param name="Path">실제로 열 경로.</param>
/// <param name="Requested">설정에 적혀 있던 경로. 비워 뒀으면 null.</param>
/// <param name="Failure">물러선 이유. null 이면 설정대로 열었다는 뜻이다.</param>
public sealed record IndexLocation(string Path, string? Requested, string? Failure)
{
    /// <summary>설정값을 보고 열 곳을 정한다. 비어 있으면 기본 경로다.</summary>
    public static IndexLocation Resolve(string? requested)
    {
        var fallback = SqliteSessionIndex.DefaultDatabasePath;
        var wanted = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();

        if (wanted is null)
        {
            return new IndexLocation(fallback, null, null);
        }

        return Probe(wanted) is { } failure
            ? new IndexLocation(fallback, wanted, failure)
            : new IndexLocation(wanted, wanted, null);
    }

    /// <summary>
    /// 이 경로에 인덱스를 열 수 있는가. 열 수 있으면 null, 아니면 이유 한 줄.
    /// 설정 화면이 저장 전에 물어보고, <see cref="Resolve"/> 가 시작할 때 물어본다.
    /// </summary>
    public static string? Probe(string path)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            var directory = System.IO.Path.GetDirectoryName(full);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = full,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
            }.ToString());

            connection.Open();
            return null;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException
            or SqliteException)
        {
            return ex.Message;
        }
    }
}
