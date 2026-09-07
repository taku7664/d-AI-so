namespace Daiso.Core;

/// <summary>
/// `&lt;!-- daiso:start --&gt;` ~ `&lt;!-- daiso:end --&gt;` 블록만 건드리는 마커 기록기.
/// 블록 밖 내용은 개행 문자 종류까지 바이트 단위로 보존한다. (ARCHITECTURE §7.3)
/// </summary>
public sealed class InstructionMarkerWriter : IInstructionMarkerWriter
{
    /// <summary>블록 시작 마커.</summary>
    public const string StartMarker = "<!-- daiso:start -->";

    /// <summary>블록 끝 마커.</summary>
    public const string EndMarker = "<!-- daiso:end -->";

    private const string Lf = "\n";
    private const string Crlf = "\r\n";

    /// <inheritdoc />
    public string Apply(string? existingContent, string instructionBody)
    {
        ArgumentNullException.ThrowIfNull(instructionBody);

        if (existingContent is null)
        {
            return Block(instructionBody, Lf) + Lf;
        }

        var newline = DetectNewline(existingContent);
        var start = existingContent.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = start < 0
            ? -1
            : existingContent.IndexOf(EndMarker, start + StartMarker.Length, StringComparison.Ordinal);

        // 마커 한 쪽만 있거나 순서가 뒤바뀐 경우는 블록이 없는 것으로 보고 새로 덧붙인다.
        if (start < 0 || end < 0)
        {
            return Append(existingContent, instructionBody, newline);
        }

        var head = existingContent[..(start + StartMarker.Length)];
        var tail = existingContent[end..];

        return head + newline + Normalize(instructionBody, newline) + newline + tail;
    }

    private static string Append(string existingContent, string instructionBody, string newline)
    {
        var separator = existingContent.Length == 0
            ? string.Empty
            : EndsWithNewline(existingContent) ? newline : newline + newline;

        return existingContent + separator + Block(instructionBody, newline) + newline;
    }

    private static string Block(string instructionBody, string newline) =>
        StartMarker + newline + Normalize(instructionBody, newline) + newline + EndMarker;

    /// <summary>본문의 개행을 대상 파일의 개행으로 맞춘다. 본문 끝의 개행은 블록이 붙이므로 제거한다.</summary>
    private static string Normalize(string body, string newline) =>
        body.Replace(Crlf, Lf, StringComparison.Ordinal)
            .TrimEnd('\n')
            .Replace(Lf, newline, StringComparison.Ordinal);

    /// <summary>파일이 이미 쓰고 있는 개행을 따른다. 첫 줄바꿈이 기준이다.</summary>
    private static string DetectNewline(string content)
    {
        var lf = content.IndexOf('\n');
        if (lf < 0)
        {
            return Lf;
        }

        return lf > 0 && content[lf - 1] == '\r' ? Crlf : Lf;
    }

    private static bool EndsWithNewline(string content) => content.EndsWith('\n');
}
