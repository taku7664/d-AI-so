using System.Runtime.InteropServices;

namespace Daiso.Providers.Common;

/// <summary>
/// Windows 자격 증명 관리자에 그 이름의 항목이 있는지만 본다. (ARCHITECTURE §4.5 인증)
/// <para>
/// 토큰을 파일이 아니라 자격 증명 관리자에 넣는 도구(Antigravity CLI)의 로그인 여부를 판정하는 유일한 신호다.
/// </para>
/// </summary>
public interface ICredentialProbe
{
    /// <summary>그 대상 이름의 일반 자격 증명이 있는가.</summary>
    bool Exists(string target);
}

/// <summary>
/// `CredReadW`(advapi32)로 존재만 확인한다.
/// <para>
/// <b>비밀 값은 읽지 않는다.</b> 이 API 에 "있는지만 묻기"가 없어 구조체가 잠깐 메모리에 오지만,
/// <c>CredentialBlob</c> 포인터는 건드리지 않고 곧바로 <c>CredFree</c> 로 놓는다.
/// 앱은 이 도구의 토큰을 화면·로그·파일 어디에도 쓰지 않는다.
/// </para>
/// </summary>
public sealed class WindowsCredentialProbe : ICredentialProbe
{
    /// <summary>CRED_TYPE_GENERIC.</summary>
    private const uint Generic = 1;

    /// <inheritdoc />
    /// <remarks>
    /// Windows 가 아니면 판정하지 않고 false 다. 형식에 <c>SupportedOSPlatform</c> 을 붙이는 대신 여기서 막는 이유:
    /// 이 형식을 쓰는 제공자는 플랫폼 표시가 없는 net8.0 이라, 붙이면 부르는 쪽마다 표시를 번지게 해야 한다.
    /// </remarks>
    public bool Exists(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || !OperatingSystem.IsWindows())
        {
            return false;
        }

        var handle = IntPtr.Zero;

        try
        {
            return CredReadW(target, Generic, 0, out handle);
        }
        catch (DllNotFoundException)
        {
            // Windows 가 아닌 곳(테스트 러너 등)에서는 판정하지 않는다
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CredFree(handle);
            }
        }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
