using System.Text.RegularExpressions;
using GotifyWin.Models;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface INotificationPatternAnalyzer
{
    List<RuleRecommendation> Analyze(IEnumerable<GotifyMessage> messages, GotifyClientConfig config, int threshold = 3);
}

public class NotificationPatternAnalyzer : INotificationPatternAnalyzer
{
    private static readonly Regex UrlRegex = new(@"https?://\S+", RegexOptions.Compiled);
    private static readonly Regex IpRegex = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(?::\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"\b\d{4}[-/]\d{2}[-/]\d{2}(?:[T\s]\d{2}:\d{2}(?::\d{2})?(?:\.\d+)?)?\b", RegexOptions.Compiled);
    private static readonly Regex TimeRegex = new(@"\b\d{1,2}:\d{2}(?::\d{2})?\s*(?:am|pm|AM|PM)?\b", RegexOptions.Compiled);
    private static readonly Regex HashGuidRegex = new(@"\b[0-9a-fA-F-]{8,36}\b", RegexOptions.Compiled);
    private static readonly Regex NumberWithUnitRegex = new(@"\b\d+(?:\.\d+)?\s*(?:ms|s|m|h|B|KB|MB|GB|TB|%|p|k|fps)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CodecRegex = new(@"\b(?:x264|x265|h264|h265|hevc|av1|hdr|web-dl|remux)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled);
    private static readonly Regex QuotedRegex = new(@"[""'‘“][^""'’”]{2,}[""'’”]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly HashSet<string> ErrorKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "fail", "failed", "down", "offline", "critical", "fatal",
        "panic", "alert", "corrupt", "emergency", "breach", "unhealthy", "warning", "degraded"
    };

    public List<RuleRecommendation> Analyze(IEnumerable<GotifyMessage> messages, GotifyClientConfig config, int threshold = 3)
    {
        var recommendations = new List<RuleRecommendation>();
        var messageList = messages.Where(m => m.Id > 0).ToList();

        if (messageList.Count < threshold)
            return recommendations;

        // Group messages by AppId first
        var appGroups = messageList.GroupBy(m => m.AppId);

        foreach (var appGroup in appGroups)
        {
            var appId = appGroup.Key;
            var appName = appGroup.First().DisplayAppName;

            // 1. Check Title-based clusters
            var titleCandidates = appGroup
                .Where(m => !string.IsNullOrWhiteSpace(m.Title))
                .GroupBy(m => MaskVariables(m.Title))
                .Where(g => g.Count() >= threshold);

            var handledTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cluster in titleCandidates)
            {
                var clusterMessages = cluster.ToList();

                // Safety: Skip if any high priority (>= 7) or error keywords present
                if (IsSafetyViolation(clusterMessages))
                    continue;

                // Skip if already covered by an existing enabled rule
                if (IsAlreadyFiltered(clusterMessages, config))
                    continue;

                // Determine pattern: if title is constant, use original title; otherwise extract common prefix
                var rawTitles = clusterMessages.Select(m => m.Title.Trim()).Distinct().ToList();
                string pattern;
                RuleOperator op = RuleOperator.Contains;

                if (rawTitles.Count == 1)
                {
                    pattern = rawTitles[0];
                }
                else
                {
                    var commonPrefix = GetLongestCommonPrefix(rawTitles);
                    pattern = commonPrefix.Length >= 4 ? commonPrefix.Trim() : cluster.Key.Replace("<VAR>", "").Trim();
                    if (string.IsNullOrWhiteSpace(pattern))
                        pattern = rawTitles[0];
                }

                handledTitles.Add(pattern);

                recommendations.Add(new RuleRecommendation
                {
                    AppId = appId,
                    AppName = appName,
                    Title = $"Mute routine {appName} notifications",
                    Description = $"Detected repetitive pattern '{pattern}' in message titles.",
                    SuggestedField = RuleField.Title,
                    SuggestedOperator = op,
                    SuggestedPattern = pattern,
                    SuggestedAction = RuleAction.SilentLog,
                    MatchCount = clusterMessages.Count,
                    SampleMessages = clusterMessages.Take(10).ToList()
                });
            }

            // 2. Check Body-based clusters for messages where Title is empty or unclustered
            var bodyCandidates = appGroup
                .Where(m => string.IsNullOrWhiteSpace(m.Title) || !handledTitles.Any(t => m.Title.Contains(t, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(m => MaskVariables(m.Message))
                .Where(g => g.Count() >= threshold);

            foreach (var cluster in bodyCandidates)
            {
                var clusterMessages = cluster.ToList();

                if (IsSafetyViolation(clusterMessages))
                    continue;

                if (IsAlreadyFiltered(clusterMessages, config))
                    continue;

                var rawBodies = clusterMessages.Select(m => m.Message.Trim()).Distinct().ToList();
                string pattern;
                if (rawBodies.Count == 1)
                {
                    pattern = rawBodies[0];
                }
                else
                {
                    var commonPrefix = GetLongestCommonPrefix(rawBodies);
                    pattern = commonPrefix.Length >= 5 ? commonPrefix.Trim() : cluster.Key.Replace("<VAR>", "").Trim();
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        pattern = rawBodies[0].Length > 30 ? rawBodies[0][..30] : rawBodies[0];
                    }
                }

                recommendations.Add(new RuleRecommendation
                {
                    AppId = appId,
                    AppName = appName,
                    Title = $"Mute repetitive {appName} messages",
                    Description = $"Detected repetitive pattern '{pattern}' in message content.",
                    SuggestedField = RuleField.Message,
                    SuggestedOperator = RuleOperator.Contains,
                    SuggestedPattern = pattern,
                    SuggestedAction = RuleAction.SilentLog,
                    MatchCount = clusterMessages.Count,
                    SampleMessages = clusterMessages.Take(10).ToList()
                });
            }
        }

        return recommendations;
    }

    public static string MaskVariables(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var result = UrlRegex.Replace(text, "<URL>");
        result = IpRegex.Replace(result, "<IP>");
        result = DateRegex.Replace(result, "<DATE>");
        result = TimeRegex.Replace(result, "<TIME>");
        result = HashGuidRegex.Replace(result, "<ID>");
        result = NumberWithUnitRegex.Replace(result, "<NUM>");
        result = CodecRegex.Replace(result, "<CODEC>");
        result = NumberRegex.Replace(result, "<NUM>");
        result = QuotedRegex.Replace(result, "<STR>");
        result = WhitespaceRegex.Replace(result, " ").Trim();

        return result;
    }

    private static bool IsSafetyViolation(List<GotifyMessage> messages)
    {
        // 1. Any message priority >= 7 must never be recommended for muting
        if (messages.Any(m => m.Priority >= 7))
            return true;

        // 2. Check for negative / error keywords in title or body
        foreach (var msg in messages)
        {
            var combined = $"{msg.Title} {msg.Message}";
            foreach (var word in ErrorKeywords)
            {
                if (Regex.IsMatch(combined, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAlreadyFiltered(List<GotifyMessage> messages, GotifyClientConfig config)
    {
        if (config.Rules.Count == 0)
            return false;

        var activeRules = config.Rules.Where(r => r.Enabled).ToList();
        if (activeRules.Count == 0)
            return false;

        // Check if any rule already matches this cluster's messages
        int matchCount = 0;
        foreach (var msg in messages)
        {
            foreach (var rule in activeRules)
            {
                if (FilterEngine.MatchesRule(msg, rule))
                {
                    matchCount++;
                    break;
                }
            }
        }

        // If >= 80% of the cluster is already matched by active rules, skip recommendation
        return matchCount >= (messages.Count * 0.8);
    }

    private static string GetLongestCommonPrefix(List<string> strings)
    {
        if (strings.Count == 0) return string.Empty;
        if (strings.Count == 1) return strings[0];

        var prefix = strings[0];
        for (int i = 1; i < strings.Count; i++)
        {
            int j = 0;
            while (j < prefix.Length && j < strings[i].Length &&
                   char.ToLowerInvariant(prefix[j]) == char.ToLowerInvariant(strings[i][j]))
            {
                j++;
            }
            prefix = prefix[..j];
            if (string.IsNullOrEmpty(prefix)) break;
        }

        return prefix;
    }
}
