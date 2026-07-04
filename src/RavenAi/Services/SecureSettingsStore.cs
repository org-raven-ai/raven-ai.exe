using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RavenAi.Models;

namespace RavenAi.Services;

/// <summary>
/// Loads/saves <see cref="RavenAiSettings"/> to %APPDATA%\raven_ai\settings.json and encrypts the
/// API key at rest using Windows DPAPI (DataProtectionScope.CurrentUser).
///
/// Security invariants:
///   * The plaintext API key is NEVER written to disk or logged.
///   * On disk it is a DPAPI blob (base64) that only the current Windows user can decrypt.
///   * Callers obtain the plaintext only transiently via <see cref="GetApiKey"/>.
/// </summary>
public sealed class SecureSettingsStore
{
    // Extra entropy mixed into DPAPI so the blob is bound to this app, not just the user.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("raven_ai.ApiKey.v1");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _dir;
    private readonly string _path;

    public SecureSettingsStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "raven_ai");
        _path = Path.Combine(_dir, "settings.json");
    }

    public string SettingsPath => _path;

    public RavenAiSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new RavenAiSettings();
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<RavenAiSettings>(json) ?? new RavenAiSettings();
        }
        catch
        {
            // Corrupt/unreadable settings must not crash the app; start fresh.
            return new RavenAiSettings();
        }
    }

    public void Save(RavenAiSettings settings)
    {
        Directory.CreateDirectory(_dir);
        string json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(_path, json);
    }

    /// <summary>
    /// Encrypts <paramref name="plaintextKey"/> with DPAPI and stores it on the settings
    /// object as a base64 blob. Pass null/empty to clear the stored key.
    /// </summary>
    public void SetApiKey(RavenAiSettings settings, string? plaintextKey)
    {
        if (string.IsNullOrEmpty(plaintextKey))
        {
            settings.EncryptedApiKey = null;
            return;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintextKey);
        byte[] cipher = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(plainBytes, 0, plainBytes.Length); // scrub plaintext from memory promptly
        settings.EncryptedApiKey = Convert.ToBase64String(cipher);
    }

    /// <summary>
    /// Decrypts and returns the plaintext API key, or null if none is stored / decryption fails.
    /// The returned string should be used immediately and not retained.
    /// </summary>
    public string? GetApiKey(RavenAiSettings settings)
    {
        if (string.IsNullOrEmpty(settings.EncryptedApiKey)) return null;
        try
        {
            byte[] cipher = Convert.FromBase64String(settings.EncryptedApiKey);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            string key = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return key;
        }
        catch
        {
            return null;
        }
    }
}
