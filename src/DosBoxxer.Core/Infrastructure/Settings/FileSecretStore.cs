using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Settings;

/// <summary>
/// Cross platform secret store.
///
/// Secrets are kept in a JSON dictionary that is encrypted with AES-GCM. The 256 bit key lives
/// in a separate file next to it; on Unix both files are created with mode 0600 so only the
/// current user can read them.
///
/// This protects the secrets against casual inspection and against being picked up by backup
/// or sync tools that only look at <c>settings.json</c>. It is *not* equivalent to an OS
/// keychain: a process running as the same user can read the key file. Because every caller
/// goes through <see cref="ISecretStore"/>, a DPAPI / libsecret / macOS Keychain backend can
/// be dropped in later without changing anything else.
/// </summary>
public sealed class FileSecretStore : ISecretStore
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
    };

    private readonly string _secretsFile;
    private readonly string _keyFile;
    private readonly ILogger<FileSecretStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private Dictionary<string, string>? _cache;

    public FileSecretStore(IAppPaths paths, ILogger<FileSecretStore> logger)
    {
        _secretsFile = paths.SecretsFile;
        _keyFile = Path.Combine(Path.GetDirectoryName(paths.SecretsFile)!, "secrets.key");
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = LoadUnsafe();
            return store.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var store = LoadUnsafe();

            if (string.IsNullOrEmpty(value))
            {
                store.Remove(key);
            }
            else
            {
                store[key] = value;
            }

            PersistUnsafe(store);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        SetAsync(key, null, cancellationToken);

    private Dictionary<string, string> LoadUnsafe()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            if (!File.Exists(_secretsFile) || !File.Exists(_keyFile))
            {
                return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var key = File.ReadAllBytes(_keyFile);
            if (key.Length != KeySizeBytes)
            {
                _logger.LogWarning("Secret key file has an unexpected size, ignoring stored secrets");
                return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var payload = Convert.FromBase64String(File.ReadAllText(_secretsFile).Trim());
            if (payload.Length <= NonceSizeBytes + TagSizeBytes)
            {
                return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var nonce = payload.AsSpan(0, NonceSizeBytes);
            var tag = payload.AsSpan(NonceSizeBytes, TagSizeBytes);
            var cipher = payload.AsSpan(NonceSizeBytes + TagSizeBytes);
            var plain = new byte[cipher.Length];

            using (var aes = new AesGcm(key, TagSizeBytes))
            {
                aes.Decrypt(nonce, cipher, tag, plain);
            }

            var json = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);

            var restored = JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions);
            return _cache = restored is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(restored, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or FormatException or JsonException or UnauthorizedAccessException)
        {
            // Never log the exception message verbatim together with any secret material.
            _logger.LogError("Stored secrets could not be decrypted ({Type}); they have to be entered again", ex.GetType().Name);
            return _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private void PersistUnsafe(Dictionary<string, string> store)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_secretsFile)!);

            if (store.Count == 0)
            {
                if (File.Exists(_secretsFile))
                {
                    File.Delete(_secretsFile);
                }

                _cache = store;
                return;
            }

            var key = EnsureKey();
            var json = JsonSerializer.Serialize(store, SerializerOptions);
            var plain = Encoding.UTF8.GetBytes(json);

            var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
            var tag = new byte[TagSizeBytes];
            var cipher = new byte[plain.Length];

            using (var aes = new AesGcm(key, TagSizeBytes))
            {
                aes.Encrypt(nonce, plain, cipher, tag);
            }

            CryptographicOperations.ZeroMemory(plain);

            var payload = new byte[NonceSizeBytes + TagSizeBytes + cipher.Length];
            nonce.CopyTo(payload, 0);
            tag.CopyTo(payload, NonceSizeBytes);
            cipher.CopyTo(payload, NonceSizeBytes + TagSizeBytes);

            File.WriteAllText(_secretsFile, Convert.ToBase64String(payload));
            RestrictPermissions(_secretsFile);

            _cache = store;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Secrets could not be written ({Type})", ex.GetType().Name);
        }
    }

    private byte[] EnsureKey()
    {
        if (File.Exists(_keyFile))
        {
            var existing = File.ReadAllBytes(_keyFile);
            if (existing.Length == KeySizeBytes)
            {
                return existing;
            }
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        File.WriteAllBytes(_keyFile, key);
        RestrictPermissions(_keyFile);
        return key;
    }

    private void RestrictPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // NTFS inherits the user profile ACL; %APPDATA% is already user private.
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _logger.LogWarning("Could not restrict permissions of {Path}", path);
        }
    }
}
