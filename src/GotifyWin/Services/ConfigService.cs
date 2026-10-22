using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class ConfigService : IConfigService
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("GotifyWin.Desktop.Client.Entropy.v1");
    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly object _lock = new();

    public GotifyClientConfig Config { get; private set; } = new();

    public string ConfigFilePath => _configFilePath;

    public ConfigService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _configFilePath = customPath;
            _configDirectory = Path.GetDirectoryName(customPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }
        else
        {
            _configDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GotifyWin"
            );
            _configFilePath = Path.Combine(_configDirectory, "config.json");
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Config = new GotifyClientConfig();
                    Save();
                    return;
                }

                var json = File.ReadAllText(_configFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var loaded = JsonSerializer.Deserialize<GotifyClientConfig>(json, options);
                if (loaded != null)
                {
                    Config = loaded;

                    // Unprotect token if encrypted (SEC-03)
                    if (Config.ClientToken.StartsWith("enc:", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var cipherBytes = Convert.FromBase64String(Config.ClientToken[4..]);
                            byte[] plainBytes;
                            try
                            {
                                // Try with entropy first
                                plainBytes = ProtectedData.Unprotect(cipherBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                            }
                            catch
                            {
                                // Fallback without entropy for backwards compatibility
                                plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                            }
                            Config.ClientToken = Encoding.UTF8.GetString(plainBytes);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ConfigService] DPAPI decryption failed: {ex.Message}");
                            // Clear token so we don't attempt requests with encrypted garbage
                            Config.ClientToken = string.Empty;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Load error: {ex.Message}");
                Config = new GotifyClientConfig();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_configDirectory))
                {
                    Directory.CreateDirectory(_configDirectory);
                }

                // Create a clone to persist with DPAPI protected token
                var persistConfig = JsonSerializer.Deserialize<GotifyClientConfig>(
                    JsonSerializer.Serialize(Config)
                ) ?? new GotifyClientConfig();

                // SEC-03: Protect token with DPAPI and user-specific entropy
                if (!string.IsNullOrEmpty(persistConfig.ClientToken) &&
                    !persistConfig.ClientToken.StartsWith("enc:", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var plainBytes = Encoding.UTF8.GetBytes(persistConfig.ClientToken);
                        var cipherBytes = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
                        persistConfig.ClientToken = "enc:" + Convert.ToBase64String(cipherBytes);
                    }
                    catch (CryptographicException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ConfigService] DPAPI encryption failed: {ex.Message}");
                        throw new InvalidOperationException("Failed to securely encrypt client token with DPAPI.", ex);
                    }
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(persistConfig, options);

                // SEC-10: Atomic write to prevent config corruption/loss on power failure or crash
                var tempPath = _configFilePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _configFilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConfigService] Save error: {ex.Message}");
                throw;
            }
        }
    }
}
