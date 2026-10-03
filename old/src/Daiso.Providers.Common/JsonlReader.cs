using System.Runtime.CompilerServices;
using System.Text;

namespace Daiso.Providers.Common;

/// <summary>
/// jsonl 세션 파일을 스트리밍으로 읽는다. 파일이 수십 MB일 수 있으므로 전체를 메모리에 올리지 않는다.
/// 인코딩은 항상 UTF-8로 명시한다. (ARCHITECTURE §4, §7.6)
/// </summary>
public static class JsonlReader
{
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// <paramref name="fromByteOffset"/>부터 줄 단위로 읽는다. jsonl은 append-only라 오프셋 이어 읽기가 안전하다.
    /// 빈 줄은 건너뛴다.
    /// </summary>
    public static async IAsyncEnumerable<string> ReadLinesAsync(
        string path,
        long fromByteOffset,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = Open(path);

        if (fromByteOffset > 0)
        {
            // 파일이 오프셋만큼 자라지 않았으면 새로 읽을 것이 없다.
            if (fromByteOffset >= stream.Length)
            {
                yield break;
            }

            stream.Seek(fromByteOffset, SeekOrigin.Begin);
        }

        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: fromByteOffset == 0,
            BufferSize);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    /// <summary>파일 앞부분에서 최대 <paramref name="maxLines"/>줄만 읽는다. 메타 조회용.</summary>
    public static async Task<IReadOnlyList<string>> ReadHeadLinesAsync(
        string path,
        int maxLines,
        CancellationToken ct = default)
    {
        var lines = new List<string>(maxLines);

        await foreach (var line in ReadLinesAsync(path, 0, ct).ConfigureAwait(false))
        {
            lines.Add(line);
            if (lines.Count >= maxLines)
            {
                break;
            }
        }

        return lines;
    }

    private static FileStream Open(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        BufferSize,
        useAsync: true);
}
