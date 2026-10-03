using System.Diagnostics;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 셸에 맡겨 기본 브라우저로 주소를 연다(`UseShellExecute = true`).
/// <para>
/// `http`·`https`가 아닌 것은 열지 않는다. 주소는 제공자가 코드에 박아 둔 값이지만,
/// 여기가 프로세스를 띄우는 자리이므로 스킴 검사는 이 문 앞에서 한다 — `file:`·`ms-settings:` 같은 것이
/// 이 길로 흘러들면 브라우저가 아니라 다른 프로그램이 열린다.
/// </para>
/// </summary>
public sealed class ShellUriOpener : IUriOpener
{
    /// <inheritdoc />
    public void Open(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("웹 주소(http·https)만 열 수 있다", nameof(uri));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = parsed.AbsoluteUri,
            UseShellExecute = true,
        };

        Process.Start(startInfo)?.Dispose();
    }
}
