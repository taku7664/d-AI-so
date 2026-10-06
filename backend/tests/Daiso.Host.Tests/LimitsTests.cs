using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Daiso.Host.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Daiso.Host.Tests;

/// <summary>구독 한도 <c>/api/limits</c>: Claude 상태줄 켜고 끄기, 상태줄 명령, Codex 세션 기록 읽기.</summary>
public sealed class LimitsTests : IAsyncLifetime
{
    private const string Input = """{"session_id":"x","rate_limits":{"five_hour":{"used_percentage":23.5,"resets_at":1791700000},"seven_day":{"used_percentage":41.2,"resets_at":1792000000}}}""";

    private RunningHost _host = null!;

    private string SettingsPath => Path.Combine(_host.Home, ".claude", "settings.json");

    public async Task InitializeAsync() => _host = await RunningHost.StartAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Turning_on_without_a_settings_file_creates_it_and_turning_off_removes_it()
    {
        (await SetAsync(true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var command = JsonNode.Parse(File.ReadAllText(SettingsPath))!["statusLine"]!["command"]!.GetValue<string>();
        command.Should().Contain("Daiso.StatusLine.exe").And.Contain("--data");
        (await ClaudeAsync()).Enabled.Should().BeTrue();

        (await SetAsync(false)).StatusCode.Should().Be(HttpStatusCode.OK);

        File.Exists(SettingsPath).Should().BeFalse(because: "DAIso 가 만든 빈 파일은 끄면 지운다");
        (await ClaudeAsync()).Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_off_brings_back_the_users_own_settings()
    {
        const string original = """
            {
              "model": "opus",
              "permissions": { "allow": ["Bash(npm test)"] },
              "statusLine": { "type": "command", "command": "echo mine", "padding": 1 }
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, original);

        await SetAsync(true);

        var on = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        on["model"]!.GetValue<string>().Should().Be("opus", because: "다른 칸은 건드리지 않는다");
        on["statusLine"]!["padding"]!.GetValue<int>().Should().Be(1);
        File.Exists(SettingsPath + ".daiso-backup").Should().BeTrue();

        await SetAsync(false);

        JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(SettingsPath)), JsonNode.Parse(original)).Should().BeTrue(because: "끄면 켜기 전과 같아야 한다");
    }

    [Fact]
    public async Task A_settings_file_that_is_not_an_object_is_left_alone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "[]");

        (await SetAsync(true)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        File.ReadAllText(SettingsPath).Should().Be("[]");
    }

    [Fact]
    public async Task The_registered_command_leaves_limits_that_the_host_reads()
    {
        await SetAsync(true);

        var output = Run(RegisteredCommand(), Input);

        output.Should().BeEmpty(because: "원래 상태줄이 없으면 아무것도 찍지 않는다");
        var claude = await ClaudeAsync();
        claude.At.Should().NotBeNull();
        claude.Windows.Should().BeEquivalentTo([
            new LimitWindow(300, 23.5, DateTimeOffset.FromUnixTimeSeconds(1791700000)),
            new LimitWindow(10080, 41.2, DateTimeOffset.FromUnixTimeSeconds(1792000000)),
        ]);
    }

    [Fact]
    public async Task The_users_own_status_line_still_shows_through()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{ "statusLine": { "type": "command", "command": "echo mine" } }""");
        await SetAsync(true);

        Run(RegisteredCommand(), Input).Trim().Should().Be("mine", because: "감싸도 원래 상태줄이 그대로 보여야 한다");
        (await ClaudeAsync()).Windows.Should().HaveCount(2);
    }

    [Fact]
    public async Task Input_without_limits_keeps_the_last_values()
    {
        await SetAsync(true);
        Run(RegisteredCommand(), Input);
        Run(RegisteredCommand(), """{"session_id":"x"}""");

        (await ClaudeAsync()).Windows.Should().HaveCount(2, because: "첫 응답 전이나 구독자가 아니면 rate_limits 가 안 온다. 마지막 값을 지우지 않는다");
    }

    [Fact]
    public async Task Codex_limits_come_from_its_session_log()
    {
        var folder = Path.Combine(_host.Home, ".codex", "sessions", "2026", "10", "06");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "rollout.jsonl"), """{"timestamp":"2026-10-03T08:40:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":12.0,"window_minutes":10080,"resets_at":1791593939},"secondary":null,"plan_type":"prolite"}}}""" + "\n");

        var codex = (await GetAsync()).Tools.Single(tool => tool.Tool == "codex");

        codex.Source.Should().Be("sessions");
        codex.Plan.Should().Be("prolite");
        codex.Windows.Should().ContainSingle().Which.UsedPercent.Should().Be(12);
    }

    [Fact]
    public async Task Tools_without_a_way_to_read_limits_say_so()
    {
        var antigravity = (await GetAsync()).Tools.Single(tool => tool.Tool == "antigravity");

        antigravity.Source.Should().Be("none");
        antigravity.Windows.Should().BeEmpty();
    }

    private string RegisteredCommand() => _host.App.Services.GetRequiredService<ClaudeStatusLine>().Command;

    /// <summary>Claude 처럼 등록된 명령을 셸로 돌리고 stdin 으로 입력을 준다.</summary>
    private static string Run(string command, string input)
    {
        // cmd 는 /s /c "…" 로 바깥 따옴표만 벗긴다. 안의 따옴표는 명령 그대로 남는다
        using var process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /s /c \"{command}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000).Should().BeTrue();
        process.ExitCode.Should().Be(0, because: "상태줄 명령은 무슨 일이 있어도 0 으로 끝난다");
        return output;
    }

    private async Task<HttpResponseMessage> SetAsync(bool enabled)
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Put, "/api/limits/claude/statusline", _host.Url);
        request.Content = JsonContent.Create(new SetStatusLineRequest(enabled));
        return await client.SendAsync(request);
    }

    private async Task<LimitsResponse> GetAsync()
    {
        using var client = _host.Client();
        using var request = _host.Authed(HttpMethod.Get, "/api/limits");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LimitsResponse>())!;
    }

    private async Task<ToolLimits> ClaudeAsync() => (await GetAsync()).Tools.Single(tool => tool.Tool == "claude");
}
