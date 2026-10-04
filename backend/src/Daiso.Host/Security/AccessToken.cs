using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Daiso.Host.Security;

/// <summary>실행마다 새로 만드는 토큰 (docs/SECURITY.md 2번).</summary>
public static class AccessToken
{
    /// <summary>받아 주는 가장 짧은 토큰. 32바이트를 base64url 로 쓰면 43자다.</summary>
    public const int MinimumLength = 32;

    /// <summary>32바이트 난수를 base64url 로.</summary>
    public static string Create() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>걸리는 시간으로 토큰을 알아낼 수 없게 비교한다.</summary>
    public static bool Matches(string expected, string? actual)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (actual is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
    }
}
