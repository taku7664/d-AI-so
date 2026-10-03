namespace Daiso.App.Services;

/// <summary>
/// 화면을 옮긴다. 뷰모델이 셸 창을 직접 붙잡지 않게 하는 통로다.
/// <para>
/// 예전에는 페이지가 <c>(App.MainWindow as ShellWindow)?.NavigateTo(tag)</c> 를 직접 불렀다.
/// 그래서 "이 줄을 눌러 터미널로" 같은 일이 페이지 핸들러여야 했고, 목록 템플릿이 페이지에 묶였다.
/// </para>
/// </summary>
public interface INavigator
{
    /// <summary>좌측 메뉴의 <c>Tag</c> 로 옮긴다. 모르는 값이면 아무 일도 하지 않는다.</summary>
    void Go(string tag);
}

/// <inheritdoc cref="INavigator" />
public sealed class Navigator : INavigator
{
    private Action<string>? _go;

    /// <summary>창이 뜬 뒤 셸이 한 번 부른다.</summary>
    public void Attach(Action<string> go) => _go = go;

    /// <inheritdoc />
    public void Go(string tag) => _go?.Invoke(tag);
}
