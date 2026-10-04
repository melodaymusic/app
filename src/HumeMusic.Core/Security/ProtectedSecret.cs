using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Security;

/// <summary>A single secret (such as a third-party API key) kept between sessions.</summary>
public interface ISecretStore
{
    string? Load();
    void Save(string secret);
    void Clear();
    bool HasSecret { get; }
}

/// <summary>
/// Keeps one secret encrypted with Windows DPAPI (CurrentUser scope), like the Plex credentials:
/// only the signed-in Windows user on this machine can read it, and it is never written in plain text.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedSecret : ISecretStore
{
    private readonly string _path;
    private readonly byte[] _entropy;
    private readonly ILogger _log;
    private readonly object _gate = new();

    public ProtectedSecret(string path, string purpose, ILogger<ProtectedSecret> log)
    {
        _path = path;
        _entropy = Encoding.UTF8.GetBytes("HumeMusic.Secret." + purpose + ".v1");
        _log = log;
    }

    public bool HasSecret => File.Exists(_path);

    public string? Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), _entropy, DataProtectionScope.CurrentUser);
                var secret = Encoding.UTF8.GetString(plain);
                return secret.Length > 0 ? secret : null;
            }
            catch (Exception ex) when (ex is CryptographicException or IOException)
            {
                _log.LogWarning("A stored key could not be read and must be entered again");
                return null;
            }
        }
    }

    public void Save(string secret)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), _entropy, DataProtectionScope.CurrentUser);
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
            catch (IOException ex) { _log.LogWarning(ex, "Could not delete a stored key"); }
        }
    }
}
