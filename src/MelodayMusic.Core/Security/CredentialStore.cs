using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Security;

public sealed class StoredCredentials
{
    public string AccountToken { get; set; } = string.Empty;
    public string? AccountName { get; set; }
    public string? AccountUuid { get; set; }
    public string? ServerToken { get; set; }
    public string? ServerId { get; set; }

    /// <summary>The Plex Home member in use when it isn't the signed-in account itself, with their own token.</summary>
    public string? ProfileUuid { get; set; }
    public string? ProfileName { get; set; }
    public string? ProfileToken { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string ActiveToken => string.IsNullOrEmpty(ProfileToken) ? AccountToken : ProfileToken;
}

public interface ICredentialStore
{
    StoredCredentials? Load();
    void Save(StoredCredentials credentials);
    void Clear();
}

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MelodayMusic.Credentials.v1");
    private readonly string _path;
    private readonly ILogger _log;
    private readonly object _gate = new();

    public DpapiCredentialStore(string path, ILogger<DpapiCredentialStore> log)
    {
        _path = path;
        _log = log;
    }

    public StoredCredentials? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var encrypted = File.ReadAllBytes(_path);
                var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                var creds = JsonSerializer.Deserialize<StoredCredentials>(plain);
                return string.IsNullOrEmpty(creds?.AccountToken) ? null : creds;
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
            {
                _log.LogWarning(ex, "Stored credentials could not be read; a new sign-in is required");
                return null;
            }
        }
    }

    public void Save(StoredCredentials credentials)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
            var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, encrypted);
            File.Move(temp, _path, overwrite: true);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try { if (File.Exists(_path)) File.Delete(_path); }
            catch (IOException ex) { _log.LogWarning(ex, "Could not delete stored credentials"); }
        }
    }
}
