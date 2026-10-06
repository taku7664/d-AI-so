using System.Globalization;
using System.Text;
using System.Text.Json;
using Daiso.Core;

namespace Daiso.Providers.Codex;

/// <summary>
/// Codex 세션 기록에 남는 구독 한도를 읽는다. CLI 가 응답을 받을 때마다 <c>token_count</c> 이벤트에 붙여 적는다
/// (0.153.x, 2026-10-06 확인).
/// <code>
/// { "timestamp": "…Z", "type": "event_msg",
///   "payload": { "type": "token_count", "rate_limits": {
///     "primary":   { "used_percent": 58.0, "window_minutes": 10080, "resets_at": 1791089763 },
///     "secondary": null, "plan_type": "prolite" } } }
/// </code>
/// <para>
/// <b>문서에 없는 내부 형식</b>이라 모양이 바뀌면 null 이다. 세션을 이어서 열면 옛 파일에 새 줄이 붙으므로
/// 파일 수정 시각만으로 고르지 않고, 최근 파일 몇 개의 마지막 한도 줄 중 <c>timestamp</c> 가 가장 늦은 것을 쓴다.
/// </para>
/// </summary>
public static class CodexRateLimits
{
    /// <summary>뒤에서부터 읽을 양. 한도 줄은 응답마다 붙으므로 끝 근처에 있다.</summary>
    private const int TailBytes = 512 * 1024;

    private const string Marker = "\"rate_limits\"";

    /// <summary>최근에 바뀐 세션 파일 <paramref name="recentFiles"/> 개에서 가장 늦은 한도를 찾는다. 없으면 null.</summary>
    public static RateLimitSnapshot? ReadLatest(string sessionsRoot, int recentFiles = 8)
    {
        if (!Directory.Exists(sessionsRoot))
        {
            return null;
        }

        IEnumerable<FileInfo> files;

        try
        {
            files = new DirectoryInfo(sessionsRoot)
                .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(recentFiles)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        RateLimitSnapshot? latest = null;

        foreach (var file in files)
        {
            if (LastIn(file.FullName) is { } snapshot && (latest is null || snapshot.At > latest.At))
            {
                latest = snapshot;
            }
        }

        return latest;
    }

    /// <summary>한 줄을 읽는다. 한도가 없거나 모양이 다르면 null.</summary>
    public static RateLimitSnapshot? Parse(string line)
    {
        if (!line.Contains(Marker, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (!root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("rate_limits", out var limits)
                || limits.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("timestamp", out var stamp)
                || !DateTimeOffset.TryParse(stamp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            var windows = new[] { "primary", "secondary" }
                .Select(name => limits.TryGetProperty(name, out var window) ? Window(window) : null)
                .OfType<RateLimitWindow>()
                .OrderBy(window => window.WindowMinutes)
                .ToList();

            if (windows.Count == 0)
            {
                return null;
            }

            var plan = limits.TryGetProperty("plan_type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;

            return new RateLimitSnapshot(at, windows, plan);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RateLimitWindow? Window(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percent", out var used) || used.ValueKind != JsonValueKind.Number
            || !window.TryGetProperty("window_minutes", out var minutes) || minutes.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        DateTimeOffset? resets = window.TryGetProperty("resets_at", out var at) && at.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64())
            : null;

        return new RateLimitWindow(minutes.GetInt32(), used.GetDouble(), resets);
    }

    /// <summary>파일 끝부분에서 한도가 든 마지막 줄을 찾는다. CLI 가 쓰는 중이어도 읽을 수 있게 쓰기 공유로 연다.</summary>
    private static RateLimitSnapshot? LastIn(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - TailBytes);
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = reader.ReadToEnd().Split('\n');

            // 중간부터 읽었으면 첫 줄은 잘렸을 수 있다. 뒤에서부터 본다
            for (var i = lines.Length - 1; i >= (start > 0 ? 1 : 0); i--)
            {
                if (Parse(lines[i].TrimEnd('\r')) is { } snapshot)
                {
                    return snapshot;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 못 읽는 파일은 건너뛴다
        }

        return null;
    }
}
