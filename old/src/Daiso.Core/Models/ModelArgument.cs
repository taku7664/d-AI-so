using System.Text.RegularExpressions;

namespace Daiso.Core;

/// <summary>
/// 인자 줄 안의 <c>--model 값</c>을 읽고 바꾼다. 세 도구가 모두 같은 플래그를 받는다
/// (<c>claude --help</c> 2.1.266 · <c>codex --help</c> 0.153.4 · <c>agy --help</c> 1.1.28, 2026-09-10 확인).
///
/// <para>
/// 모델 고르기는 인자 칸을 <b>대신 고쳐 쓴다</b>. 고른 값을 따로 들고 있다가 실행 때 붙이면
/// 사람이 칸에 직접 적은 <c>--model</c> 과 겹쳐 어느 쪽이 이기는지 알 수 없다 —
/// 칸이 곧 실행될 인자여야 미리보기와 실제가 어긋나지 않는다.
/// </para>
/// </summary>
public static partial class ModelArgument
{
    public const string Flag = "--model";

    /// <summary>인자 줄에 적힌 모델. 여러 번 적혔으면 마지막 것(CLI 가 그렇게 읽는다). 없거나 값이 비었으면 null.</summary>
    public static string? Read(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            return null;
        }

        string? found = null;

        foreach (Match match in Pattern().Matches(arguments))
        {
            found = match.Groups["value"] is { Success: true, Length: > 0 } value ? value.Value.Trim('"') : null;
        }

        return found;
    }

    /// <summary>
    /// <c>--model</c> 을 전부 지우고, <paramref name="model"/> 이 있으면 끝에 하나만 붙인다.
    /// 다른 인자와 그 안의 빈칸은 건드리지 않는다.
    /// </summary>
    public static string Apply(string? arguments, string? model)
    {
        var rest = Pattern().Replace(arguments ?? string.Empty, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(model))
        {
            return rest;
        }

        var token = $"{Flag} {Quote(model.Trim())}";

        return rest.Length == 0 ? token : $"{rest} {token}";
    }

    /// <summary>
    /// 흔한 글자만 있으면 그대로, 아니면 큰따옴표로 감싼다. <c>claude-fable-5-1[1m]</c> 의 대괄호는
    /// PowerShell 이 다르게 읽을 수 있어 감싼다 (셸 감싸기는 <c>TerminalCommandBuilder</c> 가 따옴표를 이스케이프한다).
    /// </summary>
    private static string Quote(string model) =>
        SafeValue().IsMatch(model) ? model : $"\"{model.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";

    /// <summary>
    /// 앞의 빈칸까지 한 덩어리. 값은 따옴표 묶음이거나 <c>-</c> 로 시작하지 않는 낱말이다 —
    /// <c>--model --sandbox</c> 처럼 값이 빠졌으면 다음 플래그를 값으로 먹지 않는다.
    /// 뒤에는 빈칸이나 끝이 와야 해서 <c>--models</c> 같은 다른 플래그는 걸리지 않는다.
    /// </summary>
    [GeneratedRegex(@"(?:^|\s+)--model(?:(?:=|\s+)(?<value>""[^""]*""|[^\s""-][^\s""]*))?(?=\s|$)")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^[A-Za-z0-9._:/@+-]+$")]
    private static partial Regex SafeValue();
}
