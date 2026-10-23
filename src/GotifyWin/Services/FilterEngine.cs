using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public class FilterEngine : IFilterEngine
{
    private class DebounceEntry
    {
        public DateTime LastSeenUtc { get; set; }
        public int Count { get; set; }
    }

    private const int MaxDebounceEntries = 1000;
    private const int MaxRegexCacheEntries = 200;
    private readonly Dictionary<string, DebounceEntry> _debounceCache = new();
    private readonly object _lock = new();

    // SEC-11: Thread-safe cache of precompiled regular expressions to avoid recompilation overhead
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    public void ClearDebounceCache()
    {
        lock (_lock)
        {
            _debounceCache.Clear();
        }
    }

    public FilterResult Evaluate(GotifyMessage message, GotifyClientConfig config, bool isDndActive)
    {
        // 1. App-level enablement check
        if (config.AppFilters.TryGetValue(message.AppId, out var appSetting))
        {
            if (!appSetting.Enabled)
            {
                return new FilterResult(FilterDecision.Drop, $"App '{appSetting.AppName}' is disabled in settings.");
            }

            if (message.Priority < appSetting.MinPriority)
            {
                return new FilterResult(FilterDecision.SilentLog,
                    $"Message priority ({message.Priority}) is below app threshold ({appSetting.MinPriority}).");
            }
        }
        else if (message.Priority < config.GlobalMinPriority)
        {
            return new FilterResult(FilterDecision.SilentLog,
                $"Message priority ({message.Priority}) is below global threshold ({config.GlobalMinPriority}).");
        }

        // 2. Custom User Rules Check (Executed before debounce so explicit Drops discard immediately)
        foreach (var rule in config.Rules.Where(r => r.Enabled))
        {
            if (MatchesRule(message, rule))
            {
                switch (rule.Action)
                {
                    case RuleAction.Drop:
                        return new FilterResult(FilterDecision.Drop, $"Matched rule '{rule.Name}' (Action: Drop).");
                    case RuleAction.SilentLog:
                        message.FilteredByRuleName = rule.Name;
                        return new FilterResult(FilterDecision.SilentLog, $"Matched rule '{rule.Name}' (Action: SilentLog).");
                    case RuleAction.AlwaysNotify:
                        // AlwaysNotify forces desktop notification and bypasses DND
                        return new FilterResult(FilterDecision.Notify, $"Matched rule '{rule.Name}' (Action: AlwaysNotify).");
                }
            }
        }

        // 3. Debounce / Anti-Flap Check (SEC-05 & SEC-07)
        if (config.Presentation.EnableDebounce && config.Presentation.DebounceWindowSeconds > 0)
        {
            // SEC-07: Null-safe title and body trimming
            var title = message.Title?.Trim() ?? string.Empty;
            var body = message.Message?.Trim() ?? string.Empty;

            // SEC-05: Hash-based key to prevent heap bloat and unbounded string concatenation
            var debounceKey = $"{message.AppId}:{title.GetHashCode():X8}:{body.GetHashCode():X8}";
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                // SEC-05: Only prune when cache approaches threshold rather than scanning on every single message
                if (_debounceCache.Count > MaxDebounceEntries)
                {
                    var expiryThreshold = now.AddSeconds(-config.Presentation.DebounceWindowSeconds * 2);
                    var staleKeys = _debounceCache.Where(kv => kv.Value.LastSeenUtc < expiryThreshold).Select(kv => kv.Key).Take(300).ToList();
                    foreach (var k in staleKeys)
                    {
                        _debounceCache.Remove(k);
                    }

                    if (_debounceCache.Count > MaxDebounceEntries)
                    {
                        var oldest = _debounceCache.OrderBy(kv => kv.Value.LastSeenUtc).Take(200).Select(kv => kv.Key).ToList();
                        foreach (var k in oldest)
                        {
                            _debounceCache.Remove(k);
                        }
                    }
                }

                if (_debounceCache.TryGetValue(debounceKey, out var entry))
                {
                    var elapsed = (now - entry.LastSeenUtc).TotalSeconds;
                    if (elapsed <= config.Presentation.DebounceWindowSeconds)
                    {
                        entry.Count++;
                        entry.LastSeenUtc = now;
                        message.DuplicateCount = entry.Count;
                        message.FilteredByRuleName = "Anti-Flap Debounce";
                        return new FilterResult(
                            FilterDecision.SilentLog,
                            $"Debounced duplicate #{entry.Count} within {config.Presentation.DebounceWindowSeconds}s window.",
                            IsDuplicate: true,
                            DuplicateCount: entry.Count
                        );
                    }
                    else
                    {
                        entry.Count = 1;
                        entry.LastSeenUtc = now;
                    }
                }
                else
                {
                    _debounceCache[debounceKey] = new DebounceEntry { LastSeenUtc = now, Count = 1 };
                }
            }
        }

        // 4. Do Not Disturb / Quiet Hours Check
        if (isDndActive)
        {
            // Check Emergency Breakthrough
            if (config.Dnd.EnableEmergencyBypass && message.Priority >= config.Dnd.EmergencyBypassPriority)
            {
                return new FilterResult(FilterDecision.Notify,
                    $"Emergency breakthrough: Priority ({message.Priority}) >= Bypass threshold ({config.Dnd.EmergencyBypassPriority}).");
            }

            // Check if app has DND override
            if (appSetting != null && appSetting.OverrideGlobalDnd)
            {
                return new FilterResult(FilterDecision.Notify,
                    $"App '{appSetting.AppName}' has DND override enabled.");
            }

            message.FilteredByRuleName = "Do Not Disturb";
            return new FilterResult(FilterDecision.SilentLog, "Suppressed by active Do Not Disturb or Quiet Hours.");
        }

        // 5. Default Pass
        return new FilterResult(FilterDecision.Notify, "Passed all filter gates.");
    }

    public static bool MatchesRule(GotifyMessage message, NotificationRule rule)
    {
        // 1. Check App Scope: if rule is scoped to a specific app, it must match
        if (rule.AppFilterId.HasValue && rule.AppFilterId.Value > 0)
        {
            if (message.AppId != rule.AppFilterId.Value)
                return false;
        }
        else if (!string.IsNullOrWhiteSpace(rule.AppFilterName) &&
                 !string.Equals(rule.AppFilterName, "All Apps", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(message.ApplicationName, rule.AppFilterName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (string.IsNullOrEmpty(rule.Pattern))
            return false;

        var targetText = rule.TargetField switch
        {
            RuleField.Title => message.Title ?? string.Empty,
            RuleField.Message => message.Message ?? string.Empty,
            RuleField.AppName => message.ApplicationName ?? string.Empty,
            _ => string.Empty
        };

        var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        try
        {
            return rule.Operator switch
            {
                RuleOperator.Contains => targetText.IndexOf(rule.Pattern, comparison) >= 0,
                RuleOperator.DoesNotContain => targetText.IndexOf(rule.Pattern, comparison) < 0,
                RuleOperator.StartsWith => targetText.StartsWith(rule.Pattern, comparison),
                RuleOperator.EndsWith => targetText.EndsWith(rule.Pattern, comparison),
                RuleOperator.ExactMatch => string.Equals(targetText, rule.Pattern, comparison),
                RuleOperator.RegexMatch => MatchesRegexCached(targetText, rule.Pattern, rule.CaseSensitive),
                _ => false
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // Invalid regex pattern, skip rule safely
            return false;
        }
    }

    // SEC-11: Pre-compiled regex with 50ms strict timeout to prevent ReDoS CPU exhaustion
    private static bool MatchesRegexCached(string input, string pattern, bool caseSensitive)
    {
        var cacheKey = $"{pattern}:{(caseSensitive ? "1" : "0")}";

        // SEC: Bound the cache so imported/edited rules can't grow memory without limit
        if (RegexCache.Count >= MaxRegexCacheEntries && !RegexCache.ContainsKey(cacheKey))
        {
            RegexCache.Clear();
        }

        var regex = RegexCache.GetOrAdd(cacheKey, k =>
        {
            try
            {
                // Not RegexOptions.Compiled: compilation emits IL per pattern and is costly for rarely-hit rules
                return new Regex(
                    pattern,
                    caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                    TimeSpan.FromMilliseconds(50)
                );
            }
            catch
            {
                return null;
            }
        });

        if (regex == null) return false;

        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
