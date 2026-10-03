using System.Text;
using Daiso.Providers.Common;

namespace Daiso.Providers.Tests;

/// <summary>fixture 파일 경로와, Provider가 기대하는 폴더 구조를 임시로 꾸미는 도우미.</summary>
internal static class Fixtures
{
    /// <summary>fixture 기준 시각. 인증 fixture의 만료는 2026-12-01이다.</summary>
    internal static readonly DateTimeOffset Now = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    /// <summary>인증 fixture에 박혀 있는 재로그인 만료 시각.</summary>
    internal static readonly DateTimeOffset Expiry = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    internal static string ClaudePath(string name) => Path.Combine(Root, "claude", name);

    internal static string CodexPath(string name) => Path.Combine(Root, "codex", name);

    internal static string ReadClaude(string name) => File.ReadAllText(ClaudePath(name), Encoding.UTF8);

    internal static string ReadCodex(string name) => File.ReadAllText(CodexPath(name), Encoding.UTF8);

    internal static string GeminiPath(string name) => Path.Combine(Root, "gemini", name);

    private static string Root => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>테스트마다 지워지는 임시 폴더.</summary>
    internal static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "daiso-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// `&lt;home&gt;/.claude/projects/&lt;project&gt;/session-basic.jsonl` 구조를 만든다.
    /// 실제 사용자 폴더는 건드리지 않는다.
    /// </summary>
    internal static ProviderHome CreateClaudeHome(string home, int? activePid = null)
    {
        var config = Path.Combine(home, ".claude");
        var project = Path.Combine(config, "projects", "C--Fixture-Project");
        Directory.CreateDirectory(project);

        File.Copy(ClaudePath("session-basic.jsonl"), Path.Combine(project, "session-basic.jsonl"));
        File.Copy(ClaudePath("session-offsets.jsonl"), Path.Combine(project, "session-offsets.jsonl"));

        // 하위 폴더는 무시되어야 한다.
        var nested = Path.Combine(project, "memory");
        Directory.CreateDirectory(nested);
        File.Copy(ClaudePath("session-basic.jsonl"), Path.Combine(nested, "ignored.jsonl"));

        File.Copy(ClaudePath("credentials.json"), Path.Combine(config, ".credentials.json"));
        File.Copy(ClaudePath("claude.json"), Path.Combine(home, ".claude.json"));

        if (activePid is { } pid)
        {
            var sessions = Path.Combine(config, "sessions");
            Directory.CreateDirectory(sessions);
            File.WriteAllText(
                Path.Combine(sessions, $"{pid}.json"),
                $$"""
                {"pid": {{pid}}, "sessionId": "{{ClaudeSessionId}}", "cwd": "C:\\Fixture\\Project"}
                """,
                Encoding.UTF8);
        }

        return new ProviderHome(home);
    }

    /// <summary>`&lt;home&gt;/.codex/sessions/2026/09/01/…` 구조를 만든다.</summary>
    internal static ProviderHome CreateCodexHome(string home)
    {
        var config = Path.Combine(home, ".codex");
        var day = Path.Combine(config, "sessions", "2026", "09", "01");
        var archived = Path.Combine(config, "archived_sessions", "2026", "08", "31");
        Directory.CreateDirectory(day);
        Directory.CreateDirectory(archived);

        File.Copy(
            CodexPath("rollout-legacy.jsonl"),
            Path.Combine(day, "rollout-2026-09-01T00-00-00-01a00000-1111-2222-3333-444444444444.jsonl"));
        File.Copy(
            CodexPath("rollout-modern.jsonl"),
            Path.Combine(day, "rollout-2026-09-01T00-10-00-01a99999-8888-7777-6666-555555555555.jsonl"));
        File.Copy(
            CodexPath("rollout-legacy.jsonl"),
            Path.Combine(archived, "rollout-2026-08-31T00-00-00-01a00000-1111-2222-3333-444444444444.jsonl"));

        File.Copy(CodexPath("auth.json"), Path.Combine(config, "auth.json"));

        return new ProviderHome(home);
    }

    /// <summary>
    /// `&lt;home&gt;/.gemini/tmp/{이름|해시}/chats/session-*.jsonl` 구조를 만든다.
    /// 이름 폴더 하나, 해시 폴더 하나, 그리고 목록에서 빠져야 하는 a2a-server 세션 하나.
    /// </summary>
    internal static ProviderHome CreateGeminiHome(string home)
    {
        var config = Path.Combine(home, ".gemini");
        var named = Path.Combine(config, "tmp", "fixture-project", "chats");
        var hashed = Path.Combine(config, "tmp", GeminiProjectHash, "chats");
        Directory.CreateDirectory(named);
        Directory.CreateDirectory(hashed);

        File.Copy(GeminiPath("session-modern.jsonl"), Path.Combine(named, "session-2026-09-01T01-00-7a1b2c3d.jsonl"));
        File.Copy(GeminiPath("session-modern.jsonl"), Path.Combine(hashed, "session-2026-09-01T01-00-7a1b2c3d.jsonl"));
        File.Copy(GeminiPath("session-a2a.jsonl"), Path.Combine(named, "session-2026-05-25T13-29-a2a-serv.jsonl"));

        File.Copy(GeminiPath("oauth_creds.json"), Path.Combine(config, "oauth_creds.json"));
        File.Copy(GeminiPath("google_accounts.json"), Path.Combine(config, "google_accounts.json"));
        File.Copy(GeminiPath("projects.json"), Path.Combine(config, "projects.json"));

        return new ProviderHome(home);
    }

    /// <summary>projects.json 의 "c:\\fixture\\project" 를 SHA-256 한 값. 해시 폴더 이름으로 쓴다.</summary>
    internal const string GeminiProjectHash = "0bb944d27cd15af3778264ff172604204f9e857f28c43df93754b992a4b14444";

    internal const string ClaudeSessionId = "11111111-2222-3333-4444-555555555555";
    internal const string CodexLegacySessionId = "01a00000-1111-2222-3333-444444444444";
    internal const string CodexModernSessionId = "01a99999-8888-7777-6666-555555555555";
}

/// <summary>지정한 pid만 살아 있다고 답하는 가짜 프로세스 조회.</summary>
internal sealed class FakeProcessProbe(params int[] alivePids) : IProcessProbe
{
    public bool IsAlive(int pid) => alivePids.Contains(pid);
}
