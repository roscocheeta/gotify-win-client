using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public record FilterResult(FilterDecision Decision, string Reason, bool IsDuplicate = false, int DuplicateCount = 1);

public interface IFilterEngine
{
    FilterResult Evaluate(GotifyMessage message, GotifyClientConfig config, bool isDndActive);
    void ClearDebounceCache();
}
