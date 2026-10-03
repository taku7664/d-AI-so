namespace Daiso.App.Services;

/// <summary>
/// 사람이 머무는 한 자리. 좌측 메뉴 페이지 + 그 페이지의 도구 탭 + (터미널이면) 보고 있던 방.
/// 방은 <c>IRoom</c>이지만 이 클래스는 UI를 모르므로 <c>object</c>로 들고 참조로만 비교한다.
/// </summary>
public sealed record NavigationSpot(string PageTag, int TabIndex, object? Room);

/// <summary>
/// 뒤로/앞으로 이력. 브라우저와 같다 — 새 자리로 옮기면 앞으로 갈 곳은 지워진다.
/// 순수 로직이라 UI를 모른다. 되돌리는 동안에는 기록하지 않는다(<see cref="Restoring"/>). (ARCHITECTURE §6.2)
/// </summary>
public sealed class NavigationHistory
{
    /// <summary>이력 길이 상한. 오래된 것부터 버린다.</summary>
    private const int Limit = 50;

    private readonly List<NavigationSpot> _back = [];
    private readonly List<NavigationSpot> _forward = [];

    private NavigationSpot? _current;
    private int _suppress;

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>되돌리는 동안 기록을 멈춘다. <c>using</c>으로 감싼다.</summary>
    public IDisposable Restoring() => new Suppression(this);

    /// <summary>사람이 옮긴 자리를 적는다. 같은 자리면 아무것도 하지 않는다.</summary>
    public void Record(NavigationSpot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        if (_suppress > 0 || spot == _current)
        {
            return;
        }

        if (_current is not null)
        {
            _back.Add(_current);

            if (_back.Count > Limit)
            {
                _back.RemoveAt(0);
            }
        }

        _current = spot;
        _forward.Clear();
    }

    /// <summary>한 자리 뒤로. 갈 곳이 없으면 null.</summary>
    public NavigationSpot? GoBack() => Move(_back, _forward);

    /// <summary>한 자리 앞으로. 갈 곳이 없으면 null.</summary>
    public NavigationSpot? GoForward() => Move(_forward, _back);

    /// <summary>
    /// 조건에 맞는 자리를 이력에서 지운다. 닫힌 방을 가리키는 자리를 걷어내는 데 쓴다.
    /// 안 그러면 뒤로가기가 이미 없어진 방으로 간다.
    /// </summary>
    public void Forget(Func<NavigationSpot, bool> stale)
    {
        ArgumentNullException.ThrowIfNull(stale);

        _back.RemoveAll(spot => stale(spot));
        _forward.RemoveAll(spot => stale(spot));
    }

    private NavigationSpot? Move(List<NavigationSpot> from, List<NavigationSpot> to)
    {
        if (from.Count == 0)
        {
            return null;
        }

        var target = from[^1];
        from.RemoveAt(from.Count - 1);

        if (_current is not null)
        {
            to.Add(_current);
        }

        _current = target;
        return target;
    }

    private sealed class Suppression : IDisposable
    {
        private readonly NavigationHistory _owner;
        private bool _done;

        internal Suppression(NavigationHistory owner)
        {
            _owner = owner;
            _owner._suppress++;
        }

        public void Dispose()
        {
            if (!_done)
            {
                _done = true;
                _owner._suppress--;
            }
        }
    }
}
