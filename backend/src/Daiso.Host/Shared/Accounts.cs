using Daiso.Core;
using Daiso.Host.Notifications;
using Daiso.Host.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Daiso.Host.Shared;

/// <summary>로그인 카드 한 줄 부가 정보. 문장은 화면이 <c>Key</c> 로 찾는다.</summary>
/// <param name="Key">문구 키(<c>AuthNote_…</c>).</param>
/// <param name="Argument">문구의 <c>{0}</c>. 토큰 값은 들어오지 않는다.</param>
public sealed record AccountNote(string Key, string? Argument);

/// <summary>저장한 계정 하나.</summary>
public sealed record AccountProfile(string Name, string? AccountLabel, string? Email, DateTimeOffset SavedAt, DateTimeOffset? ExpiresAt);

/// <summary>도구 하나의 로그인 상태. 토큰 값은 어디에도 없다.</summary>
/// <param name="Tool">도구 id.</param>
/// <param name="Installed">CLI 가 깔려 있는가.</param>
/// <param name="State"><c>loggedIn</c> · <c>expiringSoon</c>(7일 안) · <c>expired</c> · <c>missing</c>.</param>
/// <param name="AccountLabel">계정 이름.</param>
/// <param name="Email">이메일.</param>
/// <param name="Plan">요금제.</param>
/// <param name="ExpiresAt">다시 로그인해야 하는 때. 모르면 null.</param>
/// <param name="Notes">부가 정보.</param>
/// <param name="CanSaveProfiles">로그인이 파일에 있어 저장·바꾸기를 할 수 있는가. Windows 자격 증명 관리자에 두는 도구는 못 한다.</param>
/// <param name="Profiles">저장한 계정. 최근 것이 앞.</param>
public sealed record Account(
    string Tool,
    bool Installed,
    string State,
    string? AccountLabel,
    string? Email,
    string? Plan,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<AccountNote> Notes,
    bool CanSaveProfiles,
    IReadOnlyList<AccountProfile> Profiles);

/// <summary>프로필 이름을 받는 요청.</summary>
public sealed record ProfileRequest(string Name);

/// <summary>
/// 위 줄 계정 단추 (docs/ARCHITECTURE.md "탭에 속하지 않는 공용 경로"). 옛 요약 화면의 계정 카드를 옮겼다(<c>DashboardViewModel</c>).
/// 로그인은 앱이 하지 않는다. 새 터미널 창에서 그 도구의 로그인 명령을 띄울 뿐이다.
/// </summary>
public static class AccountsEndpoints
{
    public const string Topic = "accounts";

    public static void MapAccounts(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/accounts").WithTags(Topic);
        group.MapGet("/", ListAsync).WithName("ListAccounts");
        group.MapPost("/{tool}/login", LoginAsync).WithName("Login");
        group.MapPost("/{tool}/profiles", SaveProfileAsync).WithName("SaveProfile");
        group.MapPost("/{tool}/profiles/apply", ApplyProfileAsync).WithName("ApplyProfile");
        group.MapPost("/{tool}/profiles/remove", RemoveProfileAsync).WithName("RemoveProfile");
    }

    internal static async Task<IReadOnlyList<Account>> ListAsync(ToolRegistry tools, IAuthProfileStore profiles, CancellationToken ct)
    {
        var saved = profiles.List();
        var accounts = new List<Account>();

        foreach (var provider in tools.Tools)
        {
            accounts.Add(await DescribeAsync(provider, saved, ct).ConfigureAwait(false));
        }

        return accounts;
    }

    internal static async Task<Account> DescribeAsync(IProvider provider, IReadOnlyList<AuthProfile> saved, CancellationToken ct)
    {
        var installed = await provider.IsInstalledAsync(ct).ConfigureAwait(false);
        AuthStatus status;

        try
        {
            status = await provider.GetAuthStatusAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // 로그인 파일을 못 읽으면 "로그인 정보 없음"으로 보여 준다. 다른 도구 카드까지 막지 않는다
            status = AuthStatus.Missing(provider.Kind);
        }

        return new Account(
            provider.Kind.Id,
            installed,
            status.State switch
            {
                AuthState.LoggedIn => "loggedIn",
                AuthState.ExpiringSoon => "expiringSoon",
                AuthState.Expired => "expired",
                _ => "missing",
            },
            status.AccountLabel,
            status.Email,
            status.Plan,
            status.SessionExpiresAt,
            [.. status.Extras.Select(note => new AccountNote(note.Key, note.Argument))],
            provider.LoginLivesInFiles,
            [.. saved.Where(profile => profile.Tool == provider.Kind)
                .Select(profile => new AccountProfile(profile.Name, profile.AccountLabel, profile.Email, profile.SavedAt, profile.SessionExpiresAt))]);
    }

    /// <summary>새 터미널 창에서 그 도구의 로그인 명령을 띄운다. 이미 로그인돼 있어도 띄운다(계정 바꾸기).</summary>
    private static async Task<Results<NoContent, NotFound>> LoginAsync(string tool, ToolRegistry tools, ITerminalLauncher launcher)
    {
        if (Find(tools, tool) is not { } provider)
        {
            return TypedResults.NotFound();
        }

        await launcher.LaunchAsync(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), provider.LaunchTarget, provider.LoginArguments).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>지금 로그인을 이름 붙여 저장한다. 같은 이름이면 덮어쓴다. 파일은 이 Windows 사용자만 풀 수 있게 암호화된다.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SaveProfileAsync(
        string tool,
        ProfileRequest request,
        ToolRegistry tools,
        IAuthProfileStore profiles,
        NotificationHub hub,
        CancellationToken ct)
    {
        if (Find(tools, tool) is not { } provider)
        {
            return TypedResults.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "보관할 이름을 적어야 한다");
        }

        if (!provider.LoginLivesInFiles)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "이 도구는 로그인을 파일에 두지 않아 저장할 수 없다");
        }

        try
        {
            profiles.Save(request.Name.Trim(), provider, await provider.GetAuthStatusAsync(ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "저장하지 못했다", detail: ex.Message);
        }

        await hub.PublishAsync(new Notification(Topic, "changed"), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>저장한 계정으로 바꾼다. 바꾸기 전에 지금 상태를 자동으로 보관한다(<c>IAuthProfileStore.Apply</c>). 새로 여는 터미널부터 적용된다.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ApplyProfileAsync(
        string tool,
        ProfileRequest request,
        ToolRegistry tools,
        IAuthProfileStore profiles,
        NotificationHub hub,
        CancellationToken ct)
    {
        if (Find(tools, tool) is not { } provider || FindProfile(profiles, provider, request.Name) is not { } profile)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var current = await provider.GetAuthStatusAsync(ct).ConfigureAwait(false);
            profiles.Apply(profile, provider, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "계정을 바꾸지 못했다", detail: ex.Message);
        }

        await hub.PublishAsync(new Notification(Topic, "changed"), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    /// <summary>저장한 계정을 지운다. 지금 로그인은 건드리지 않는다.</summary>
    private static async Task<Results<NoContent, NotFound>> RemoveProfileAsync(
        string tool,
        ProfileRequest request,
        ToolRegistry tools,
        IAuthProfileStore profiles,
        NotificationHub hub,
        CancellationToken ct)
    {
        if (Find(tools, tool) is not { } provider || FindProfile(profiles, provider, request.Name) is not { } profile)
        {
            return TypedResults.NotFound();
        }

        profiles.Remove(profile);
        await hub.PublishAsync(new Notification(Topic, "changed"), ct).ConfigureAwait(false);
        return TypedResults.NoContent();
    }

    private static IProvider? Find(ToolRegistry tools, string id) =>
        ToolKind.TryParse(id, out var kind) ? tools.Tools.FirstOrDefault(provider => provider.Kind == kind) : null;

    private static AuthProfile? FindProfile(IAuthProfileStore profiles, IProvider provider, string name) =>
        profiles.List().FirstOrDefault(profile => profile.Tool == provider.Kind && string.Equals(profile.Name, name, StringComparison.Ordinal));
}
