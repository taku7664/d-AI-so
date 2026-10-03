using Daiso.Core;
using Microsoft.VisualBasic.FileIO;

namespace Daiso.Infrastructure;

/// <summary>
/// 세션 파일을 휴지통으로 보낸다. 실행 중 세션은 거부한다. (ARCHITECTURE §5.5, §7.2)
/// </summary>
public sealed class RecycleBinFileDisposer : IFileDisposer
{
    private const string ActiveReason = "실행 중인 세션이다";
    private const string MissingReason = "파일이 없다";

    /// <inheritdoc />
    public Task<DisposeResult> MoveToRecycleBinAsync(IEnumerable<SessionInfo> sessions) =>
        Task.FromResult(Dispose(sessions, RecycleOption.SendToRecycleBin));

    /// <inheritdoc />
    public Task<DisposeResult> DeletePermanentlyAsync(IEnumerable<SessionInfo> sessions) =>
        Task.FromResult(Dispose(sessions, RecycleOption.DeletePermanently));

    private static DisposeResult Dispose(IEnumerable<SessionInfo> sessions, RecycleOption option)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var deleted = new List<string>();
        var skipped = new List<(string Path, string Reason)>();

        foreach (var session in sessions)
        {
            if (session.IsActive)
            {
                skipped.Add((session.FilePath, ActiveReason));
                continue;
            }

            if (!File.Exists(session.FilePath))
            {
                skipped.Add((session.FilePath, MissingReason));
                continue;
            }

            try
            {
                FileSystem.DeleteFile(session.FilePath, UIOption.OnlyErrorDialogs, option);
                deleted.Add(session.FilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                skipped.Add((session.FilePath, ex.Message));
            }
        }

        return new DisposeResult(deleted, skipped);
    }
}
