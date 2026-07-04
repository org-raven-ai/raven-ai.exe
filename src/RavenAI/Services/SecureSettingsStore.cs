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
    // Extra entropy mixed into DPAPI so each blob is bound to this app AND to the specific key
    // slot — an OpenAI key blob can't be decrypted as an Azure Speech key blob and vice versa.
    private static readonly byte[] ApiKeyEntropy = Encoding.UTF8.GetBytes("raven_ai.ApiKey.v1");
    private static readonly byte[] AzureSpeechKeyEntropy = Encoding.UTF8.GetBytes("raven_ai.AzureSpeechKey.v1");

    /// <summary>Encrypts plaintext with DPAPI and returns a base64 blob, scrubbing the plaintext. Null/empty → null.</summary>
    private static string? Protect(string? plaintext, byte[] entropy)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] cipher = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.CurrentUser);
        Array.Clear(plainBytes, 0, plainBytes.Length); // scrub plaintext from memory promptly
        return Convert.ToBase64String(cipher);
    }

    /// <summary>Decrypts a base64 DPAPI blob; null if missing/unreadable. Scrubs the working bytes.</summary>
    private static string? Unprotect(string? blob, byte[] entropy)
    {
        if (string.IsNullOrEmpty(blob)) return null;
        try
        {
            byte[] cipher = Convert.FromBase64String(blob);
            byte[] plain = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.CurrentUser);
            string key = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length);
            return key;
        }
        catch
        {
            return null;
        }
    }

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
        => settings.EncryptedAPIKey = Protect(plaintextKey, ApiKeyEntropy);

    /// <summary>
    /// Decrypts and returns the plaintext API key, or null if none is stored / decryption fails.
    /// The returned string should be used immediately and not retained.
    /// </summary>
    public string? GetAPIKey(RavenAISettings settings)
        => Unprotect(settings.EncryptedAPIKey, ApiKeyEntropy);

    /// <summary>Stores the Azure Speech resource key (DPAPI). Pass null/empty to clear.</summary>
    public void SetAzureSpeechKey(RavenAISettings settings, string? plaintextKey)
        => settings.EncryptedAzureSpeechApiKey = Protect(plaintextKey, AzureSpeechKeyEntropy);

    /// <summary>Decrypts the Azure Speech key, or null if none is stored / decryption fails.</summary>
    public string? GetAzureSpeechKey(RavenAISettings settings)
        => Unprotect(settings.EncryptedAzureSpeechApiKey, AzureSpeechKeyEntropy);
}
