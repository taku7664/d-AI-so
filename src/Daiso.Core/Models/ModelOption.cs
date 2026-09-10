namespace Daiso.Core;

/// <summary>
/// 도구가 받는 모델 하나. (ARCHITECTURE §3.2 <c>IProvider.ListModelsAsync</c>)
/// <para>
/// <see cref="Id"/> 는 <c>--model</c> 뒤에 그대로 붙는 값이고, <see cref="Name"/> 은 사람이 읽는 이름이다.
/// <see cref="Description"/> 은 도구가 준 문장 그대로다(대개 영어). 앱이 번역하거나 지어내지 않는다.
/// </para>
/// </summary>
public sealed record ModelOption(string Id, string Name, string? Description = null);
