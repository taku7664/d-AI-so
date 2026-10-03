namespace Daiso.Core;

/// <summary>
/// 웹 주소를 기본 브라우저로 연다. (ARCHITECTURE §5.3)
/// <para>
/// 설치가 셸 명령 하나로 끝나지 않는 도구(Antigravity CLI)의 <see cref="IProvider.InstallUri"/>를 여는 데 쓴다.
/// 앱이 원격 스크립트를 내려받아 돌리는 대신, 공식 안내 페이지를 열어 사람이 판단하게 한다.
/// </para>
/// </summary>
public interface IUriOpener
{
    /// <summary>그 주소를 연다. `http`·`https`만 받는다.</summary>
    void Open(string uri);
}
