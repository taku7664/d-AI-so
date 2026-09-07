using Daiso.Core;

namespace Daiso.App.Services;

/// <summary>
/// 앱이 이미 아는 프로젝트 폴더 목록. 세션 인덱스와 최근 폴더를 합친다.
/// 폴더를 고를 때 OS 대화상자로 헤매지 않게 하려는 것이다. (ARCHITECTURE §6.2)
/// </summary>
public sealed class KnownProjects
{
    /// <summary>목록에 담는 최대 개수. 최근에 손댄 것부터.</summary>
    private const int Limit = 20;

    private readonly IndexService _indexService;
    private readonly ISettingsStore _settings;

    public KnownProjects(IndexService indexService, ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(indexService);
        ArgumentNullException.ThrowIfNull(settings);

        _indexService = indexService;
        _settings = settings;
    }

    /// <summary>
    /// 최근 폴더 → 세션이 있는 프로젝트 순서로 합친다.
    /// 사라진 폴더는 넣지 않는다 (골라도 할 수 있는 게 없다).
    /// </summary>
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var ordered = new List<string>(_settings.Current.RecentFolders);

        try
        {
            var sessions = await _indexService.Index
                .ListAsync(SessionFilter.All, ct)
                .ConfigureAwait(true);

            ordered.AddRange(sessions
                .Where(session => session.ProjectPath is { Length: > 0 })
                .GroupBy(session => session.ProjectPath!)
                .OrderByDescending(group => group.Max(session => session.ModifiedAt))
                .Select(group => group.Key));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            // 인덱스를 못 읽으면 최근 폴더만으로도 쓸 수 있다.
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(Limit);

        foreach (var path in ordered)
        {
            if (result.Count == Limit)
            {
                break;
            }

            if (seen.Add(path) && Directory.Exists(path))
            {
                result.Add(path);
            }
        }

        return result;
    }
}
