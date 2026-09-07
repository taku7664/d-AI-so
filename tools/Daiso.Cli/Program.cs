using System.Text;
using Daiso.Core;
using Daiso.Infrastructure;
using Daiso.Providers.Claude;
using Daiso.Providers.Codex;

namespace Daiso.Cli;

/// <summary>Stage 1 검증용 CLI. UI 없이 Core·Providers·Infrastructure를 직접 호출한다.</summary>
internal static class Program
{
    private const string Usage = """
        daiso — d-AI-so 검증용 CLI

        사용법:
          daiso auth
          daiso auth save <이름> --tool claude|codex
          daiso auth use <이름> --tool claude|codex
          daiso auth list
          daiso auth remove <이름> --tool claude|codex
          daiso sessions [--tool claude|codex] [--include-archived]
          daiso search <query>
          daiso refresh
          daiso usage --days N
          daiso rules render <path>
          daiso rules roundtrip <path>
          daiso rules install <projectDir>
          daiso rules migrate <projectDir> [--to claude|codex] [--apply]
          daiso doctor <dir> [--tool claude|codex]
          daiso export <sessionId> <out.md>
        """;

    internal static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0)
        {
            Console.WriteLine(Usage);
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "auth" => await Commands.AuthAsync(args).ConfigureAwait(false),
                "sessions" => await Commands.SessionsAsync(args).ConfigureAwait(false),
                "search" => await Commands.SearchAsync(args).ConfigureAwait(false),
                "refresh" => await Commands.RefreshAsync().ConfigureAwait(false),
                "usage" => await Commands.UsageAsync(args).ConfigureAwait(false),
                "rules" => await Commands.RulesAsync(args).ConfigureAwait(false),
                "doctor" => await Commands.DoctorAsync(args).ConfigureAwait(false),
                "export" => await Commands.ExportAsync(args).ConfigureAwait(false),
                "-h" or "--help" or "help" => Print(Usage, 0),
                _ => Print($"알 수 없는 명령: {args[0]}\n\n{Usage}", 1),
            };
        }
        catch (RuleParseException ex)
        {
            Console.Error.WriteLine($"규칙 오류 (line {ex.Line}, column {ex.Column}): {ex.Detail}");
            return 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"실패: {ex.Message}");
            return 4;
        }
    }

    private static int Print(string message, int exitCode)
    {
        if (exitCode == 0)
        {
            Console.WriteLine(message);
        }
        else
        {
            Console.Error.WriteLine(message);
        }

        return exitCode;
    }
}

/// <summary>CLI 하위 명령 구현.</summary>
internal static class Commands
{
    private static IReadOnlyList<IProvider> Providers { get; } =
        [new ClaudeProvider(), new CodexProvider()];

    internal static async Task<int> AuthAsync(string[] args)
    {
        if (args.Length > 1)
        {
            return await AuthProfileAsync(args).ConfigureAwait(false);
        }

        foreach (var provider in Providers)
        {
            var installed = await provider.IsInstalledAsync(default).ConfigureAwait(false);
            var status = await provider.GetAuthStatusAsync(default).ConfigureAwait(false);

            Console.WriteLine($"[{provider.Kind}]");
            Console.WriteLine($"  설치: {(installed ? "있음" : "없음")}");
            Console.WriteLine($"  상태: {status.State}");
            Console.WriteLine($"  계정: {status.AccountLabel ?? "-"}");
            Console.WriteLine($"  이메일: {status.Email ?? "-"}");
            Console.WriteLine($"  재로그인 필요 시각: {status.SessionExpiresAt?.ToString("u") ?? "-"}");

            foreach (var extra in status.Extras)
            {
                Console.WriteLine($"  - {extra}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>로그인 프로필. 여러 계정을 오갈 때 지금 상태를 이름 붙여 두고 되돌린다. (ARCHITECTURE §5.7)</summary>
    private static async Task<int> AuthProfileAsync(string[] args)
    {
        var store = new AuthProfileStore();
        var sub = args[1];

        if (string.Equals(sub, "list", StringComparison.Ordinal))
        {
            var profiles = store.List();
            Console.WriteLine($"프로필 {profiles.Count}개");

            foreach (var profile in profiles)
            {
                Console.WriteLine(
                    $"  {profile.Tool,-6} {profile.Name,-20} {profile.AccountLabel ?? "-"}"
                    + $"  (저장 {profile.SavedAt.ToLocalTime():yyyy-MM-dd HH:mm})");
            }

            return 0;
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine("사용법: daiso auth save|use|remove <이름> --tool claude|codex");
            return 2;
        }

        var name = args[2];
        var kind = ToolOption(args) ?? ToolKind.Claude;
        var provider = Providers.First(item => item.Kind == kind);

        switch (sub)
        {
            case "save":
                var status = await provider.GetAuthStatusAsync(default).ConfigureAwait(false);
                var saved = store.Save(name, provider, status);
                Console.WriteLine($"저장했다: [{saved.Tool}] {saved.Name} — {saved.AccountLabel ?? "계정 정보 없음"}");
                return 0;

            case "use":
                var target = store.List().FirstOrDefault(item =>
                    item.Tool == kind && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

                if (target is null)
                {
                    Console.Error.WriteLine($"그런 프로필이 없다: [{kind}] {name}");
                    return 4;
                }

                store.Apply(target, provider, await provider.GetAuthStatusAsync(CancellationToken.None));
                Console.WriteLine($"되돌렸다: [{kind}] {name}. 새로 여는 터미널부터 적용된다");
                return 0;

            case "remove":
                var doomed = store.List().FirstOrDefault(item =>
                    item.Tool == kind && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

                if (doomed is null)
                {
                    Console.Error.WriteLine($"그런 프로필이 없다: [{kind}] {name}");
                    return 4;
                }

                store.Remove(doomed);
                Console.WriteLine($"지웠다: [{kind}] {name}");
                return 0;

            default:
                Console.Error.WriteLine($"알 수 없는 auth 하위 명령: {sub}");
                return 2;
        }
    }

    internal static async Task<int> SessionsAsync(string[] args)
    {
        using var index = await OpenIndexAsync().ConfigureAwait(false);

        var filter = SessionFilter.All with
        {
            Tool = ToolOption(args),
            IncludeArchived = args.Contains("--include-archived", StringComparer.Ordinal),
        };

        var sessions = await index.ListAsync(filter, default).ConfigureAwait(false);

        Console.WriteLine($"세션 {sessions.Count}건");

        foreach (var session in sessions.Take(50))
        {
            Console.WriteLine(
                $"  {session.Tool,-6} {session.ModifiedAt:yyyy-MM-dd HH:mm} "
                + $"{Size(session.SizeBytes),9} U{session.UserMessageCount,-4} A{session.AssistantMessageCount,-4} "
                + $"{session.ProjectPath ?? "(알 수 없음)"}");
            Console.WriteLine($"         {session.Id}  {session.FirstPrompt ?? string.Empty}");
        }

        if (sessions.Count > 50)
        {
            Console.WriteLine($"  … 그 밖에 {sessions.Count - 50}건");
        }

        return 0;
    }

    /// <summary>인덱스를 갱신한다. 앱 없이 확인할 때 쓴다.</summary>
    internal static async Task<int> RefreshAsync()
    {
        using var index = await OpenIndexAsync().ConfigureAwait(false);

        await index.RefreshAsync(default).ConfigureAwait(false);

        var sessions = await index.ListAsync(SessionFilter.All, default).ConfigureAwait(false);
        Console.WriteLine($"갱신 완료. 세션 {sessions.Count}건");

        return 0;
    }

    internal static async Task<int> SearchAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("검색어가 필요하다");
            return 2;
        }

        using var index = await OpenIndexAsync().ConfigureAwait(false);
        var hits = await index.SearchAsync(args[1], default).ConfigureAwait(false);

        Console.WriteLine($"'{args[1]}' 결과 {hits.Count}건");

        foreach (var group in hits.GroupBy(h => h.Session.ProjectPath ?? "(알 수 없음)").Take(20))
        {
            Console.WriteLine($"  {group.Key}");

            foreach (var hit in group.Take(5))
            {
                Console.WriteLine($"    [{hit.Message.Role}] {Single(hit.Snippet)}");
            }
        }

        return 0;
    }

    internal static async Task<int> UsageAsync(string[] args)
    {
        var days = 7;
        var index = Array.IndexOf(args, "--days");

        if (index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var parsed))
        {
            days = Math.Max(1, parsed);
        }

        using var sessionIndex = await OpenIndexAsync().ConfigureAwait(false);
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = to.AddDays(-(days - 1));

        var usage = await sessionIndex.GetUsageAsync(from, to, default).ConfigureAwait(false);

        Console.WriteLine($"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd} 사용량");
        Console.WriteLine();
        Console.WriteLine("  일별");

        foreach (var day in usage.Days)
        {
            Console.WriteLine($"    {day.Date:yyyy-MM-dd}  총 {day.Usage.Total,12:N0}"
                + $"  (in {day.Usage.Input:N0} / out {day.Usage.Output:N0})");
        }

        Console.WriteLine();
        Console.WriteLine("  프로젝트별 상위 10");

        foreach (var entry in usage.ByProject.OrderByDescending(e => e.Value.Total).Take(10))
        {
            Console.WriteLine($"    {entry.Value.Total,12:N0}  {entry.Key}");
        }

        Console.WriteLine();
        Console.WriteLine("  모델별");

        foreach (var entry in usage.ByModel.OrderByDescending(e => e.Value.Total))
        {
            Console.WriteLine($"    {entry.Value.Total,12:N0}  {entry.Key}");
        }

        return 0;
    }

    internal static Task<int> RulesAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("사용법: daiso rules render|roundtrip|install|migrate <경로>");
            return Task.FromResult(2);
        }

        var service = new RuleFileService();
        var path = args[2];

        switch (args[1])
        {
            case "render":
                Console.WriteLine(new MarkdownRuleRenderer().Render(service.Load(path)));
                return Task.FromResult(0);

            case "roundtrip":
                return Task.FromResult(Roundtrip(path));

            case "install":
                service.EnsureInstruction(path, Providers);
                Console.WriteLine($"OK  {path} 에 지시문 블록을 넣었다");

                foreach (var provider in Providers)
                {
                    Console.WriteLine($"  {Path.Combine(path, provider.RulesFileName)}");
                }

                return Task.FromResult(0);

            case "migrate":
                return Task.FromResult(Migrate(path, args));

            default:
                Console.Error.WriteLine($"알 수 없는 rules 하위 명령: {args[1]}");
                return Task.FromResult(2);
        }
    }

    /// <summary>CLAUDE.md ↔ AGENTS.md 비교·복사. `--apply` 없이는 쓰지 않는다. (REQUIREMENTS §7)</summary>
    private static int Migrate(string projectDir, string[] args)
    {
        var directory = Path.GetFullPath(projectDir);
        var service = new InstructionMigrationService(Providers);
        var plan = service.Plan(directory);

        Console.WriteLine($"[migrate] {directory}");
        Console.WriteLine($"  CLAUDE.md: {(plan.Claude.Exists ? "있음" : "없음")}"
            + $"   AGENTS.md: {(plan.Codex.Exists ? "있음" : "없음")}");

        foreach (var note in plan.Notes)
        {
            Console.WriteLine($"  · {note}");
        }

        if (!plan.CanMigrate)
        {
            return 0;
        }

        Console.WriteLine("  diff (왼쪽 CLAUDE.md, 오른쪽 AGENTS.md, 마커 블록 제외)");

        foreach (var line in plan.Diff)
        {
            var sign = line.Kind switch
            {
                DiffKind.Removed => '<',
                DiffKind.Added => '>',
                _ => ' ',
            };

            Console.WriteLine($"    {line.LeftLine?.ToString() ?? "",4} {line.RightLine?.ToString() ?? "",4} {sign} {line.Text}");
        }

        var direction = DirectionOption(args) ?? plan.Suggested;

        if (direction is null)
        {
            Console.Error.WriteLine("  양쪽이 다르다. --to claude 또는 --to codex 로 방향을 정해라");
            return 2;
        }

        var apply = args.Contains("--apply", StringComparer.Ordinal);
        var result = service.Apply(directory, direction.Value, dryRun: !apply);
        var targetName = Providers.First(p => p.Kind == result.Target).RulesFileName;

        foreach (var warning in result.Warnings)
        {
            Console.WriteLine($"  경고: {warning}");
        }

        Console.WriteLine(apply
            ? $"  OK  {targetName} 를 {direction} 방향으로 갱신했다 ({result.Content.Length:N0}자)"
            : $"  미리보기 ({direction}, {result.Content.Length:N0}자). 실제로 쓰려면 --apply");

        return 0;
    }

    /// <summary>`--to claude|codex` → 대상 도구 기준 방향.</summary>
    private static MigrationDirection? DirectionOption(string[] args)
    {
        var index = Array.IndexOf(args, "--to");

        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        return args[index + 1].ToLowerInvariant() switch
        {
            "codex" or "agents" => MigrationDirection.ClaudeToCodex,
            "claude" => MigrationDirection.CodexToClaude,
            _ => null,
        };
    }

    internal static async Task<int> DoctorAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("사용법: daiso doctor <dir> [--tool claude|codex]");
            return 2;
        }

        var directory = Path.GetFullPath(args[1]);
        ToolKind[] tools = ToolOption(args) is { } selected
            ? [selected]
            : [ToolKind.Claude, ToolKind.Codex];
        var inspector = new ContextInspector(Providers);

        foreach (var tool in tools)
        {
            var report = await inspector.InspectAsync(tool, directory, default).ConfigureAwait(false);

            Console.WriteLine($"[{tool}] {directory}");
            Console.WriteLine($"  총 글자 수: {report.TotalChars:N0}");
            Console.WriteLine("  파일 (로드 순서)");

            foreach (var file in report.Files)
            {
                Console.WriteLine($"    {file.Order,2}. {(file.Exists ? "O" : "-")} "
                    + $"{file.Content.Length,8:N0}자  [{file.Kind}] {file.Path}");
            }

            Console.WriteLine($"  중복 줄 {report.Duplicates.Count}건");

            foreach (var duplicate in report.Duplicates.Take(10))
            {
                Console.WriteLine($"    \"{Single(duplicate.NormalizedText)}\"");
                Console.WriteLine($"      {string.Join(", ", duplicate.Files.Select(Path.GetFileName))}");
            }

            Console.WriteLine($"  충돌 후보 {report.Conflicts.Count}건");

            foreach (var conflict in report.Conflicts.Take(10))
            {
                Console.WriteLine($"    {conflict.Reason}");
                Console.WriteLine($"      A {Path.GetFileName(conflict.FileA)}: {Single(conflict.LineA)}");
                Console.WriteLine($"      B {Path.GetFileName(conflict.FileB)}: {Single(conflict.LineB)}");
            }

            Console.WriteLine();
        }

        return 0;
    }

    internal static async Task<int> ExportAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("사용법: daiso export <sessionId> <out.md>");
            return 2;
        }

        using var index = await OpenIndexAsync().ConfigureAwait(false);
        var sessions = await index.ListAsync(SessionFilter.All, default).ConfigureAwait(false);

        var session = sessions.FirstOrDefault(s =>
            string.Equals(s.Id, args[1], StringComparison.OrdinalIgnoreCase));

        if (session is null)
        {
            Console.Error.WriteLine($"세션을 찾을 수 없다: {args[1]}");
            return 5;
        }

        await new MarkdownSessionExporter(Providers)
            .ExportMarkdownAsync(session, args[2], ExportOptions.Default, default)
            .ConfigureAwait(false);

        Console.WriteLine($"OK  {args[2]} ({new FileInfo(args[2]).Length:N0} 바이트)");
        return 0;
    }

    private static int Roundtrip(string path)
    {
        var serializer = new RulePresetSerializer();
        var source = File.ReadAllText(path, Encoding.UTF8);

        var once = serializer.Serialize(serializer.Parse(source));
        var twice = serializer.Serialize(serializer.Parse(once));

        if (string.Equals(once, twice, StringComparison.Ordinal))
        {
            Console.WriteLine("OK  라운드트립이 안정적이다");
            return 0;
        }

        Console.Error.WriteLine("FAIL  두 번째 직렬화 결과가 다르다");
        return 6;
    }

    /// <summary>인덱스를 열고, 비어 있으면 한 번 만든다.</summary>
    private static async Task<SqliteSessionIndex> OpenIndexAsync()
    {
        var index = new SqliteSessionIndex(Providers, SqliteSessionIndex.DefaultDatabasePath);

        var existing = await index.ListAsync(SessionFilter.All, default).ConfigureAwait(false);
        if (existing.Count == 0)
        {
            Console.WriteLine("인덱스를 만든다 …");
            await index.RebuildAsync(
                new Progress<IndexProgress>(),
                default).ConfigureAwait(false);
        }
        else
        {
            await index.RefreshAsync(default).ConfigureAwait(false);
        }

        return index;
    }

    private static ToolKind? ToolOption(string[] args)
    {
        var index = Array.IndexOf(args, "--tool");

        if (index < 0 || index + 1 >= args.Length)
        {
            return null;
        }

        return args[index + 1].ToLowerInvariant() switch
        {
            "claude" => ToolKind.Claude,
            "codex" => ToolKind.Codex,
            _ => null,
        };
    }

    /// <summary>여러 줄 텍스트를 한 줄로 눌러 목록 출력에 쓴다.</summary>
    private static string Single(string text)
    {
        var flattened = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return flattened.Length <= 120 ? flattened : flattened[..120] + "…";
    }

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        _ => $"{bytes / (1024.0 * 1024):N1} MB",
    };
}
