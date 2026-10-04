namespace Daiso.Host.Tabs;

/// <summary>
/// 탭 하나의 서버 엔드포인트 묶음. 약속은 같은 폴더의 README.md 에 있다.
/// </summary>
public interface ITabEndpoints
{
    /// <summary>화면 쪽 <c>tab.id</c> 와 같다. 소문자와 <c>-</c> 만 쓴다.</summary>
    string Id { get; }

    /// <summary><c>/api/{Id}</c> 아래에 엔드포인트를 단다.</summary>
    void Map(RouteGroupBuilder group);
}
