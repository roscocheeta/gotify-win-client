using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface IConfigService
{
    GotifyClientConfig Config { get; }
    void Load();
    void Save();
    string ConfigFilePath { get; }
    bool HasDecryptionFailed { get; }
}
