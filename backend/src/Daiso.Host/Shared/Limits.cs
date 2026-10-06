using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daiso.Core;
using Daiso.Host.Notifications;
using Daiso.Providers.Codex;
using Daiso.Providers.Common;

namespace Daiso.Host.Shared;

/// <summary>한도 창 하나.</summary>
/// <param name="WindowMinutes">창의 길이(분). 300 이면 5시간, 10080 이면 7일.</param>
/// <param name="UsedPercent">쓴 비율. 0~100.</param>
/// <param name="ResetsAt">다시 차는 때. 지났으면 화면이 "초기화됨 · 0%"로 보여 준다.</param>
public sealed record LimitWindow(int WindowMinutes, double UsedPercent, DateTimeOffset? ResetsAt);

/// <summary>도구 하나의 구독 한도.</summary>
/// <param name="Tool">도구 id.</param>
/// <param name="Source"><c>statusline</c>(Claude 상태줄) · <c>sessions</c>(Codex 세션 기록) · <c>none</c>(읽을 길이 없는 도구).</param>
/// <param name="Enabled">Claude 만: 상태줄 등록이 켜져 있는가. 다른 도구는 null.</param>
/// <param name="At">값이 적힌 때. 아직 받은 적이 없으면 null.</param>
/// <param name="Windows">창 목록. 짧은 창부터.</param>
/// <param name="Plan">요금제 이름. 모르면 null.</param>
public sealed record ToolLimits(string Tool, string Source, bool? Enabled, DateTimeOffset? At, IReadOnlyList<LimitWindow> Windows, string? Plan);

/// <summary>모든 도구의 구독 한도. 도구 목록 순서.</summary>
public sealed record LimitsResponse(IReadOnlyList<ToolLimits> Tools);

/// <summary>Claude 상태줄 등록을 켜고 끄는 요청.</summary>
/// <param name="Enabled">켤지.</param>
public sealed record SetStatusLineRequest(bool Enabled);

/// <summary>
/// Claude 상태줄 등록. 사용자 설정 파일 <c>~/.claude/settings.json</c> 의 <c>statusLine</c> 을 DAIso 상태줄(<c>Daiso.StatusLine.exe</c>)로 바꾸고,
/// 끄면 원래대로 돌린다 (docs/DECISIONS.md "구독 한도는 도구가 남긴 파일로만 읽는다").
/// <list type="bullet">
/// <item>켜기 전 설정 파일을 <c>settings.json.daiso-backup</c> 으로 한 벌 복사해 둔다</item>
/// <item>원래 <c>statusLine</c> 은 <c>{자료 폴더}\limits\claude-statusline-original.json</c> 에 둔다. 상태줄 명령이 그 명령을 같은 입력으로 돌려 출력을 그대로 넘긴다(감싸기)</item>
/// <item>설정 파일의 다른 칸은 건드리지 않는다. 다만 주석과 줄 바꿈 모양은 다시 쓰면서 사라진다</item>
/// </list>
/// </summary>
public sealed class ClaudeStatusLine
{
    public const string Folder = "limits";
    public const string LimitsFile = "claude.json";
    public const string OriginalFile = "claude-statusline-original.json";
    public const string ExeName = "Daiso.StatusLine.exe";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private readonly string _data;
    private readonly string _exe;
    private readonly Lock _gate = new();

    public ClaudeStatusLine(DaisoHostOptions options, ProviderHome home)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(home);

        _data = options.DataDirectory;
        _exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        SettingsPath = home.Combine(".claude", "settings.json");
    }

    /// <summary>Claude 사용자 설정 파일.</summary>
    public string SettingsPath { get; }

    /// <summary>상태줄이 남기는 한도 파일.</summary>
    public string LimitsPath => Path.Combine(_data, Folder, LimitsFile);

    private string OriginalPath => Path.Combine(_data, Folder, OriginalFile);

    private string BackupPath => SettingsPath + ".daiso-backup";

    /// <summary>Claude 에 넣는 명령. Git Bash 와 cmd 둘 다 읽게 경로를 / 로 쓰고 따옴표로 감싼다.</summary>
    public string Command => $"\"{_exe.Replace('\\', '/')}\" --data \"{_data.Replace('\\', '/')}\"";

    /// <summary>지금 설정 파일의 상태줄이 DAIso 것인가.</summary>
    public bool IsEnabled() =>
        ReadSettings() is { } settings
        && settings["statusLine"]?["command"]?.GetValue<string>() is { } command
        && command.Contains(ExeName, StringComparison.OrdinalIgnoreCase);

    public void Enable()
    {
        lock (_gate)
        {
            if (IsEnabled())
            {
                return;
            }

            var existed = File.Exists(SettingsPath);
            var settings = ReadSettings() ?? new JsonObject();

            if (existed)
            {
                File.Copy(SettingsPath, BackupPath, overwrite: true);
            }

            // 원래 상태줄(없으면 null)과 파일이 있었는지를 남겨 끌 때 그대로 돌린다
            var original = new JsonObject
            {
                ["hadFile"] = existed,
                ["statusLine"] = settings["statusLine"]?.DeepClone(),
            };
            WriteAtomic(OriginalPath, original.ToJsonString(Indented));

            var ours = new JsonObject { ["type"] = "command", ["command"] = Command };
            if (settings["statusLine"]?["padding"] is { } padding)
            {
                ours["padding"] = padding.DeepClone();
            }

            settings["statusLine"] = ours;
            WriteSettings(settings);
        }
    }

    public void Disable()
    {
        lock (_gate)
        {
            if (!IsEnabled())
            {
                return;
            }

            var original = File.Exists(OriginalPath) ? JsonNode.Parse(File.ReadAllText(OriginalPath), documentOptions: Lenient) : null;
            var settings = ReadSettings() ?? new JsonObject();

            if (original?["statusLine"] is { } line)
            {
                settings["statusLine"] = line.DeepClone();
            }
            else
            {
                settings.Remove("statusLine");
            }

            // DAIso 가 만든 파일이고 이제 빈 파일이면 지운다
            if (original?["hadFile"]?.GetValue<bool>() == false && settings.Count == 0)
            {
                File.Delete(SettingsPath);
            }
            else if (SameAsBackup(settings))
            {
                // 켠 사이에 다른 칸을 안 바꿨으면 켜기 전 파일을 바이트 그대로 돌린다. 줄 바꿈·들여쓰기까지 같다
                File.Copy(BackupPath, SettingsPath, overwrite: true);
                File.Delete(BackupPath);
            }
            else
            {
                // 켠 사이에 사용자가 다른 칸을 바꿨다. 그 변경은 살리고 상태줄만 되돌린다
                WriteSettings(settings);
            }

            File.Delete(OriginalPath);
        }
    }

    /// <summary>상태줄이 남긴 마지막 한도. 아직 없거나 모양이 다르면 null.</summary>
    public RateLimitSnapshot? ReadLimits()
    {
        try
        {
            if (!File.Exists(LimitsPath))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(LimitsPath));
            if (root?["at"]?.GetValue<string>() is not { } at || root["rate_limits"] is not JsonObject limits)
            {
                return null;
            }

            var windows = new List<RateLimitWindow>();
            foreach (var (name, minutes) in new[] { ("five_hour", 300), ("seven_day", 10080) })
            {
                if (limits[name] is JsonObject window && window["used_percentage"] is JsonValue used)
                {
                    DateTimeOffset? resets = window["resets_at"] is JsonValue reset ? DateTimeOffset.FromUnixTimeSeconds(reset.GetValue<long>()) : null;
                    windows.Add(new RateLimitWindow(minutes, used.GetValue<double>(), resets));
                }
            }

            return new RateLimitSnapshot(DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), windows, null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>되돌린 설정이 켜기 전 사본과 내용이 같은가.</summary>
    private bool SameAsBackup(JsonObject settings)
    {
        if (!File.Exists(BackupPath))
        {
            return false;
        }

        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(BackupPath), documentOptions: Lenient), settings);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 설정 파일을 쓴다. 지금 파일의 줄 바꿈(LF·CRLF)과 끝 줄 바꿈을 따른다. Claude 는 LF 에 끝 줄 바꿈을 붙여 쓴다.
    /// </summary>
    private void WriteSettings(JsonObject settings)
    {
        var current = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
        var newline = current?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
        var text = settings.ToJsonString(Indented).Replace("\n", newline, StringComparison.Ordinal);

        if (current is null || current.EndsWith('\n'))
        {
            text += newline;
        }

        WriteAtomic(SettingsPath, text);
    }

    private JsonObject? ReadSettings()
    {
        if (!File.Exists(SettingsPath))
        {
            return null;
        }

        return JsonNode.Parse(File.ReadAllText(SettingsPath), documentOptions: Lenient) as JsonObject
            ?? throw new InvalidOperationException($"{SettingsPath} 가 JSON 객체가 아니다. 손대지 않는다");
    }

    private static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".daiso.tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }
}

/// <summary>
/// 도구마다 구독 한도를 모은다. Claude 는 상태줄 파일, Codex 는 세션 기록, 그 밖의 도구는 읽을 길이 없다.
/// </summary>
public sealed class LimitsService(ClaudeStatusLine claude, Services.ToolRegistry tools, ProviderHome home)
{
    /// <summary>알림의 <c>tab</c> 자리에 넣는 이름.</summary>
    public const string Topic = "limits";

    public string CodexSessions => home.Combine(".codex", "sessions");

    public LimitsResponse Read() =>
        new([.. tools.Tools.Select(tool => tool.Kind == ToolKind.Claude
            ? From(tool.Kind, "statusline", claude.IsEnabled(), claude.ReadLimits())
            : tool.Kind == ToolKind.Codex
                ? From(tool.Kind, "sessions", null, CodexRateLimits.ReadLatest(CodexSessions))
                : new ToolLimits(tool.Kind.Id, "none", null, null, [], null))]);

    private static ToolLimits From(ToolKind kind, string source, bool? enabled, RateLimitSnapshot? snapshot) =>
        new(kind.Id, source, enabled, snapshot?.At,
            snapshot?.Windows.Select(window => new LimitWindow(window.WindowMinutes, window.UsedPercent, window.ResetsAt)).ToList() ?? [],
            snapshot?.Plan);
}

/// <summary>
/// 한도 파일이 바뀌면 화면에 알린다. Claude 상태줄 파일, Claude 설정 파일(켜고 끈 것), Codex 세션 기록을 지켜본다.
/// 여러 번 바뀌어도 1초에 한 번만 알린다.
/// </summary>
public sealed class LimitsWatcher(LimitsService limits, ClaudeStatusLine claude, NotificationHub hub, ILogger<LimitsWatcher> logger) : IHostedService, IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => _ = hub.PublishAsync(new Notification(LimitsService.Topic, "changed"), CancellationToken.None));
        // 자기 자료 폴더만 만든다. 사용자 홈의 .claude·.codex 는 없으면 지켜보지 않는다(만들지 않는다)
        Directory.CreateDirectory(Path.GetDirectoryName(claude.LimitsPath)!);
        Watch(Path.GetDirectoryName(claude.LimitsPath)!, ClaudeStatusLine.LimitsFile, subdirectories: false);
        Watch(Path.GetDirectoryName(claude.SettingsPath)!, "settings.json", subdirectories: false);
        Watch(limits.CodexSessions, "*.jsonl", subdirectories: true);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    private void Watch(string folder, string filter, bool subdirectories)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder, filter)
            {
                IncludeSubdirectories = subdirectories,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => _timer?.Change(Quiet, Timeout.InfiniteTimeSpan);
            watcher.Created += (_, _) => _timer?.Change(Quiet, Timeout.InfiniteTimeSpan);
            watcher.Renamed += (_, _) => _timer?.Change(Quiet, Timeout.InfiniteTimeSpan);
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 못 지켜봐도 탭을 열 때 읽는다
            logger.LogWarning(ex, "{Folder} 를 지켜보지 못한다", folder);
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        _timer?.Dispose();
        _timer = null;
    }
}
