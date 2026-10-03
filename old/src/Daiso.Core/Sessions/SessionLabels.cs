namespace Daiso.Core;

/// <summary>
/// 값을 모를 때 자리를 채우는 표식. 계층을 넘어 **같은 문자열이어야** 하므로 한 곳에 둔다.
/// <para>
/// 인덱스(<c>SqliteSessionIndex</c>)가 프로젝트·모델이 비었을 때 이 값을 키로 쓰고,
/// 화면은 같은 값을 묶음·비교에 쓴다. 예전에는 네 파일이 각자 같은 문자열을 적어 두어,
/// 한쪽만 고치면 묶음이 조용히 둘로 갈라졌다.
/// </para>
/// <para>
/// <b>번역하지 않는다.</b> 이것은 데이터의 표식이지 화면 문구가 아니다.
/// 화면에 보일 때는 앱이 <c>Common_Unknown</c> 문구로 바꿔 그린다.
/// </para>
/// </summary>
public static class SessionLabels
{
    /// <summary>프로젝트 경로·모델 이름을 모를 때 쓰는 표식.</summary>
    public const string Unknown = "(알 수 없음)";
}
