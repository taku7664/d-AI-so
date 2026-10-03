using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daiso.Core;

namespace Daiso.Infrastructure;

/// <summary>
/// 로그인 상태를 이름 붙여 보관하고 되돌린다. (ARCHITECTURE §5.7)
///
/// - 인증 파일은 **DPAPI(현재 Windows 사용자)** 로 암호화해 둔다. 다른 계정·다른 PC에서는 풀리지 않는다
/// - 표시용 정보(`meta.json`)에는 계정 이름·이메일·시각만 담는다. **토큰 값은 담지 않는다**
/// - 되돌리기 전에 지금 상태를 자동으로 보관해 되돌린 것을 되돌릴 수 있게 한다
/// </summary>
public sealed class AuthProfileStore : IAuthProfileStore
{
    /// <summary>되돌리기 직전 상태를 담아 두는 이름.</summary>
    public const string PreviousProfileName = "직전 상태";

    private const string MetaFileName = "meta.json";
    private const string EncryptedSuffix = ".dpapi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _root;

    public AuthProfileStore()
        : this(DefaultRoot)
    {
    }

    public AuthProfileStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = root;
    }

    /// <summary>보관 위치를 바꾸는 환경 변수. 검증할 때 실제 폴더를 건드리지 않으려고 쓴다.</summary>
    public const string RootVariable = "DAISO_PROFILES_DIR";

    /// <summary>기본 보관 위치. <see cref="RootVariable"/>이 있으면 그 값을 쓴다.</summary>
    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } overridden
            ? overridden
            : AppPaths.Combine("profiles");

    /// <inheritdoc />
    public IReadOnlyList<AuthProfile> List()
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var profiles = new List<AuthProfile>();

        foreach (var directory in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories))
        {
            var meta = Path.Combine(directory, MetaFileName);

            if (File.Exists(meta) && ReadMeta(meta) is { } profile)
            {
                profiles.Add(profile);
            }
        }

        return [.. profiles.OrderByDescending(profile => profile.SavedAt)];
    }

    /// <summary>
    /// 로그인이 파일에 없는 도구는 여기서 막는다. 프로필은 파일을 복사했다 되돌리는 것이라,
    /// 자격 증명 관리자에 있는 로그인은 <b>복사해도 계정이 바뀌지 않는다</b>.
    /// 화면이 이미 단추를 감추지만, 계약을 아는 쪽에서 한 번 더 막는다 — 조용히 되는 척하는 것보다 낫다.
    /// </summary>
    private static void Refuse(IProvider provider)
    {
        if (!provider.LoginLivesInFiles)
        {
            throw new InvalidOperationException(
                $"{provider.Kind} 는 로그인을 파일에 두지 않아 계정을 보관·전환할 수 없다");
        }
    }

    /// <inheritdoc />
    public AuthProfile Save(string name, IProvider provider, AuthStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(status);

        Refuse(provider);

        var profile = new AuthProfile(
            name.Trim(),
            provider.Kind,
            status.AccountLabel,
            status.Email,
            DateTimeOffset.UtcNow,
            status.SessionExpiresAt);

        var directory = DirectoryFor(profile.Tool, profile.Name);
        Directory.CreateDirectory(directory);

        var stored = 0;

        foreach (var file in provider.AuthFiles)
        {
            if (!File.Exists(file.Path))
            {
                if (file.Required)
                {
                    throw new InvalidOperationException(
                        $"로그인 파일이 없어 저장할 수 없다: {Path.GetFileName(file.Path)}");
                }

                continue;
            }

            Protect(file.Path, Path.Combine(directory, Path.GetFileName(file.Path) + EncryptedSuffix));
            stored++;
        }

        if (stored == 0)
        {
            throw new InvalidOperationException("보관할 로그인 파일이 없다");
        }

        File.WriteAllText(
            Path.Combine(directory, MetaFileName),
            JsonSerializer.Serialize(new MetaRow(
                profile.Name,
                profile.Tool.ToString(),
                profile.AccountLabel,
                profile.Email,
                profile.SavedAt.ToString("O", CultureInfo.InvariantCulture),
                profile.SessionExpiresAt?.ToString("O", CultureInfo.InvariantCulture)),
                JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return profile;
    }

    /// <inheritdoc />
    public void Apply(AuthProfile profile, IProvider provider, AuthStatus? current)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(provider);

        Refuse(provider);

        var directory = DirectoryFor(profile.Tool, profile.Name);

        if (!Directory.Exists(directory))
        {
            throw new InvalidOperationException($"보관된 프로필이 없다: {profile.Name}");
        }

        // 파일을 먼저 다 읽어 둔다. "직전 상태"를 되돌리는 경우 백업이 원본을 덮어쓰기 때문이다.
        var payload = new List<(string Target, byte[] Bytes)>();

        foreach (var file in provider.AuthFiles)
        {
            var source = Path.Combine(directory, Path.GetFileName(file.Path) + EncryptedSuffix);

            if (File.Exists(source))
            {
                payload.Add((file.Path, Decrypt(source)));
            }
        }

        if (payload.Count == 0)
        {
            throw new InvalidOperationException($"프로필에 되돌릴 파일이 없다: {profile.Name}");
        }

        // 지금 상태를 "직전 상태"로 남긴다. 되돌리기를 한 번 더 누르면 서로 맞바뀐다.
        BackupCurrent(provider, current);

        foreach (var (target, bytes) in payload)
        {
            var targetDirectory = Path.GetDirectoryName(target);

            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllBytes(target, bytes);
            Array.Clear(bytes);
        }
    }

    /// <inheritdoc />
    public void Remove(AuthProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var directory = DirectoryFor(profile.Tool, profile.Name);

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>지금 로그인 파일을 "직전 상태"로 남긴다. 로그인이 없으면 조용히 넘어간다.</summary>
    private void BackupCurrent(IProvider provider, AuthStatus? current)
    {
        var required = provider.AuthFiles.FirstOrDefault(file => file.Required);

        if (required is null || !File.Exists(required.Path))
        {
            return;
        }

        var directory = DirectoryFor(provider.Kind, PreviousProfileName);
        Directory.CreateDirectory(directory);

        foreach (var file in provider.AuthFiles.Where(file => File.Exists(file.Path)))
        {
            Protect(file.Path, Path.Combine(directory, Path.GetFileName(file.Path) + EncryptedSuffix));
        }

        File.WriteAllText(
            Path.Combine(directory, MetaFileName),
            JsonSerializer.Serialize(new MetaRow(
                PreviousProfileName,
                provider.Kind.ToString(),
                current?.AccountLabel,
                current?.Email,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                current?.SessionExpiresAt?.ToString("O", CultureInfo.InvariantCulture)),
                JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void Protect(string sourcePath, string targetPath)
    {
        var plain = File.ReadAllBytes(sourcePath);
        var sealedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

        Array.Clear(plain);
        File.WriteAllBytes(targetPath, sealedBytes);
    }

    private static byte[] Decrypt(string sourcePath) =>
        ProtectedData.Unprotect(
            File.ReadAllBytes(sourcePath),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

    private string DirectoryFor(ToolKind tool, string name) =>
        Path.Combine(_root, tool.ToString(), SafeName(name));

    /// <summary>이름을 폴더 이름으로 쓸 수 있게 다듬는다.</summary>
    private static string SafeName(string name) =>
        string.Join("_", name.Trim().Split(Path.GetInvalidFileNameChars()));

    private static AuthProfile? ReadMeta(string path)
    {
        try
        {
            var row = JsonSerializer.Deserialize<MetaRow>(File.ReadAllText(path, Encoding.UTF8));

            // 옛 meta.json 에는 enum 이름("Claude")이 적혀 있다. TryParse 가 대소문자를 안 가려 그대로 읽힌다.
            // 폴더 이름도 "Claude" 인데 Windows 파일 이름은 대소문자를 안 가려 "claude" 로 찾아도 같은 폴더다
            if (row is null || !ToolKind.TryParse(row.Tool, out var tool))
            {
                return null;
            }

            return new AuthProfile(
                row.Name,
                tool,
                row.AccountLabel,
                row.Email,
                DateTimeOffset.Parse(row.SavedAt, CultureInfo.InvariantCulture),
                row.SessionExpiresAt is { Length: > 0 } expires
                    ? DateTimeOffset.Parse(expires, CultureInfo.InvariantCulture)
                    : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException)
        {
            // 망가진 메타는 목록에서 빼고 넘어간다. 하나가 깨져도 나머지는 보여야 한다.
            return null;
        }
    }

    /// <summary>meta.json 한 줄. 토큰 값은 여기 담지 않는다.</summary>
    private sealed record MetaRow(
        string Name,
        string Tool,
        string? AccountLabel,
        string? Email,
        string SavedAt,
        string? SessionExpiresAt);
}
