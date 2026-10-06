using System.Data.Common;
using Daiso.Core;
using Daiso.Host.Services;

namespace Daiso.Host.Shared;

/// <summary>프로젝트 하나. 위 줄 프로젝트 선택기와 "모든 프로젝트" 요약이 쓴다.</summary>
/// <param name="Name">폴더 이름. Claude 임시 폴더 묶음은 정해 둔 이름.</param>
/// <param name="Path">전체 경로. 프로젝트를 가리키는 열쇠로 쓴다.</param>
/// <param name="SessionCount">인덱스에 있는 세션 수. 묶인 워크트리 것까지 센다. 최근 폴더에만 있으면 0.</param>
/// <param name="LastActivity">마지막 세션이 바뀐 때. 세션이 없으면 null.</param>
/// <param name="Exists">폴더가 아직 있는가. 없으면 세션만 남은 것이다.</param>
/// <param name="Members">이 프로젝트로 묶은 폴더들(자기 포함). 워크트리·임시 폴더가 여기 들어온다 (<see cref="ProjectGroups"/>).</param>
public sealed record ProjectItem(string Name, string Path, int SessionCount, DateTimeOffset? LastActivity, bool Exists, IReadOnlyList<string> Members);

/// <summary>도구 하나. 화면이 도구 칩과 글자 표시를 그린다.</summary>
/// <param name="Id">도구 id. 사용량 등에서 도구를 고를 때 이 값을 보낸다.</param>
/// <param name="Title">이름.</param>
/// <param name="Initial">한 글자 표시.</param>
/// <param name="Order">표시 순서. 작은 것이 앞.</param>
/// <param name="Colors">색. 하나면 단색, 둘 이상이면 그 순서의 그라데이션.</param>
public sealed record ToolItem(string Id, string Title, string Initial, int Order, IReadOnlyList<string> Colors);

/// <summary>프로젝트 목록과 지금 고른 프로젝트.</summary>
/// <param name="Projects">마지막 작업이 최근인 것부터.</param>
/// <param name="Current">지금 프로젝트의 경로. null 이면 "모든 프로젝트".</param>
public sealed record ProjectsResponse(IReadOnlyList<ProjectItem> Projects, string? Current);

/// <summary>지금 프로젝트를 바꾸는 요청.</summary>
/// <param name="Path">목록에 있는 경로. null 이면 "모든 프로젝트".</param>
public sealed record SetCurrentProjectRequest(string? Path);

/// <summary>
/// 아는 프로젝트를 모은다. 세션 인덱스의 프로젝트 폴더와 최근에 터미널을 연 폴더를 합친다.
/// 옛 <c>KnownProjects</c>(old/src/Daiso.App/Services) 와 같은 출처지만, 사라진 폴더도 "폴더 없음"으로 남기고 세션 수를 붙인다.
/// </summary>
public sealed class ProjectCatalog
{
    private readonly IServiceProvider _services;
    private readonly ISettingsStore _settings;

    /// <param name="services">
    /// 인덱스는 여기서 그때그때 꺼낸다. 인덱스는 만들 때 DB 를 열기 때문에, 생성자로 받으면 DB 가 깨졌을 때
    /// 이 클래스조차 못 만들어 최근 폴더로 물러설 수 없다.
    /// </param>
    /// <param name="settings">최근 폴더를 읽는다.</param>
    public ProjectCatalog(IServiceProvider services, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        _services = services;
        _settings = settings;
    }

    /// <summary>목록을 만든다. 인덱스를 못 읽으면 최근 폴더만으로 만든다.</summary>
    public async Task<IReadOnlyList<ProjectItem>> ListAsync(CancellationToken ct)
    {
        var byPath = new Dictionary<string, (int Count, DateTimeOffset? Last)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var index = _services.GetRequiredService<ISessionIndex>();
            var sessions = await index.ListAsync(SessionFilter.All, ct).ConfigureAwait(false);

            foreach (var group in sessions
                .Where(session => session.ProjectPath is { Length: > 0 })
                .GroupBy(session => Normalize(session.ProjectPath!), StringComparer.OrdinalIgnoreCase))
            {
                byPath[group.Key] = (group.Count(), group.Max(session => session.ModifiedAt));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or DbException)
        {
            // 인덱스를 못 읽어도 최근 폴더로는 고를 수 있다. DB 가 깨진 경우도 여기로 온다(2026-10-06 사용자 index.db-wal 손상으로 재현)
        }

        foreach (var folder in _settings.Current.RecentFolders)
        {
            byPath.TryAdd(Normalize(folder), (0, null));
        }

        // 워크트리·임시 폴더를 원래 프로젝트로 묶는다
        var groups = ProjectGroups.Map(byPath.Keys);

        return byPath
            .GroupBy(pair => groups[pair.Key], StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProjectItem(
                ProjectGroups.LabelFor(group.Key) ?? (System.IO.Path.GetFileName(group.Key) is { Length: > 0 } name ? name : group.Key),
                group.Key,
                group.Sum(pair => pair.Value.Count),
                group.Max(pair => pair.Value.Last),
                Directory.Exists(group.Key),
                [.. group.Select(pair => pair.Key).Append(group.Key).Distinct(StringComparer.OrdinalIgnoreCase)]))
            .OrderByDescending(item => item.LastActivity ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 프로젝트로 좁힐 때 넣을 폴더들. 묶인 워크트리·임시 폴더까지 넣는다. 프로젝트를 안 골랐으면 null(모든 프로젝트).
    /// 목록에 없는 경로면 그 경로 하나만 쓴다.
    /// </summary>
    public async Task<IReadOnlySet<string>?> ScopeAsync(string? project, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(project))
        {
            return null;
        }

        var key = Normalize(project);
        var item = (await ListAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(entry => string.Equals(entry.Path, key, StringComparison.OrdinalIgnoreCase));

        return new HashSet<string>(item?.Members ?? [key], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>세션이 이 범위에 드는가. 범위가 null 이면 모두 든다.</summary>
    public static bool InScope(IReadOnlySet<string>? scope, string? projectPath) =>
        scope is null || (projectPath is not null && scope.Contains(Normalize(projectPath)));

    /// <summary>끝의 구분자를 뗀다. 같은 폴더가 <c>C:\a</c> 와 <c>C:\a\</c> 로 두 번 나오지 않게.</summary>
    private static string Normalize(string path) =>
        path.Length > 3 ? path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) : path;
}
