using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RavenAI.Models;

namespace RavenAI.Services;

/// <summary>
/// Loads/saves <see cref="RavenAISettings"/> to %APPDATA%\raven_ai\settings.json and encrypts the
/// API key at rest using Windows DPAPI (DataProtectionScope.CurrentUser).
///
/// Security invariants:
///   * The plaintext API key is NEVER written to disk or logged.
///   * On disk it is a DPAPI blob (base64) that only the current Windows user can decrypt.
///   * Callers obtain the plaintext only transiently via <see cref="GetAPIKey"/>.
/// </summary>
public sealed class SecureSettingsStore
{
    // Extra entropy mixed into DPAPI so the blob is bound to this app, not just the user.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("raven_ai.ApiKey.v1");

    // PropertyNameCaseInsensitive lets settings files written before the abbreviation-casing
    // rename (e.g. "EncryptedApiKey", "BaseUrl") still bind to the renamed properties
    // (EncryptedAPIKey, BaseURL), which differ only by case.
    private static readonly JsonSerializerOptions JsonOpts =
        new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private readonly string _dir;
    private readonly string _path;

    public SecureSettingsStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "raven_ai");
        _path = Path.Combine(_dir, "settings.json");
    }

    public string SettingsPath => _path;

    public RavenAISettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new RavenAISettings();
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<RavenAISettings>(json, JsonOpts) ?? new RavenAISettings();
        }
        catch
        {
            // Corrupt/unreadable settings must not crash the app; start fresh.
            return new RavenAISettings();
        }
    }

    public void Save(RavenAISettings settings)
    {
        Directory.CreateDirectory(_dir);
        string json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(_path, json);
    }

    /// <summary>
    /// Encrypts <paramref name="plaintextKey"/> with DPAPI and stores it on the settings
    /// object as a base64 blob. Pass null/empty to clear the stored key.
    /// </summary>
    public void SetAPIKey(RavenAISettings settings, string? plaintextKey)
    {
        if (string.IsNullOrEmpty(plaintextKey))
        {
            settings.EncryptedAPIKey = null;
            return;
        }

        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintextKey);
        byte[] cipher = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        Array.Clear(plainBytes, 0, plainBytes.Length); // scrub plaintext from memory promptly
        settings.EncryptedAPIKey = Convert.ToBase64String(cipher);
    }

    /// <summary>
    /// Decrypts and returns the plaintext API key, or null if none is stored / decryption fails.
    /// The returned string should be used immediately and not retained.
    /// </summary>
    public string? GetAPIKey(RavenAISettings settings)
    {
        if (string.IsNullOrEmpty(settings.EncryptedAPIKey)) return null;
        try
        {
            byte[] cipher = Convert.FromBase64String(settings.EncryptedAPIKey);
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
