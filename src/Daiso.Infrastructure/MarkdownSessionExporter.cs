using System.Globalization;
using System.Text;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 세션을 시간순 마크다운으로 내보낸다. 도구 호출은 접힌 코드블록으로 넣는다. (REQUIREMENTS §7)
/// </summary>
public sealed class MarkdownSessionExporter : ISessionExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IReadOnlyList<IProvider> _providers;

    public MarkdownSessionExporter(IEnumerable<IProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
    }

    /// <inheritdoc />
    public async Task ExportMarkdownAsync(
        SessionInfo session,
        string outputPath,
        ExportOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        var provider = _providers.FirstOrDefault(p => p.Kind == session.Tool)
            ?? throw new InvalidOperationException($"{session.Tool} Provider가 등록되지 않았다");

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream, Utf8NoBom) { NewLine = "\n" };

        await WriteHeaderAsync(writer, session).ConfigureAwait(false);

        await foreach (var message in provider.ReadMessagesAsync(session.FilePath, 0, ct).ConfigureAwait(false))
        {
            if (!Include(message, options))
            {
                continue;
            }

            await WriteMessageAsync(writer, message).ConfigureAwait(false);
        }
    }

    private static bool Include(SessionMessage message, ExportOptions options)
    {
        if (message.IsSidechain && !options.IncludeSidechain)
        {
            return false;
        }

        return message.Role switch
        {
            MessageRole.Tool => options.IncludeToolCalls,
            MessageRole.System => options.IncludeSystem,
            _ => true,
        };
    }

    private static async Task WriteHeaderAsync(StreamWriter writer, SessionInfo session)
    {
        await writer.WriteLineAsync($"# {session.Tool} 세션 {session.Id}").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync($"- 프로젝트: {session.ProjectPath ?? "(알 수 없음)"}").ConfigureAwait(false);
        await writer.WriteLineAsync($"- 시작: {Iso(session.StartedAt)}").ConfigureAwait(false);
        await writer.WriteLineAsync($"- 수정: {Iso(session.ModifiedAt)}").ConfigureAwait(false);
        await writer.WriteLineAsync(
            $"- 메시지: 사용자 {session.UserMessageCount}, 어시스턴트 {session.AssistantMessageCount}")
            .ConfigureAwait(false);

        if (session.ToolVersion is { } version)
        {
            await writer.WriteLineAsync($"- 도구 버전: {version}").ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync("---").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task WriteMessageAsync(StreamWriter writer, SessionMessage message)
    {
        switch (message.Role)
        {
            case MessageRole.Tool:
                await writer.WriteLineAsync("<details><summary>도구 호출</summary>").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("```").ConfigureAwait(false);
                await writer.WriteLineAsync(message.Text).ConfigureAwait(false);
                await writer.WriteLineAsync("```").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync("</details>").ConfigureAwait(false);
                break;

            default:
                await writer.WriteLineAsync($"**{Label(message.Role)}:**").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteLineAsync(message.Text).ConfigureAwait(false);
                break;
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
    }

    private static string Label(MessageRole role) => role switch
    {
        MessageRole.User => "User",
        MessageRole.Assistant => "Assistant",
        MessageRole.System => "System",
        _ => "Tool",
    };

    private static string Iso(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
