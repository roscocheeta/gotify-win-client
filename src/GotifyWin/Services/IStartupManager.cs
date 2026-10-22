namespace GotifyWin.Services;

public interface IStartupManager
{
    bool IsAutoStartEnabled();
    void SetAutoStart(bool enable);
}
