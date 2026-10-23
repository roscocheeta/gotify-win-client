using System.IO;
using System.Text.RegularExpressions;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

/// <summary>
/// Validates and clamps configuration/rules that come from untrusted import files.
/// </summary>
public static class ImportValidator
{
    public const long MaxImportFileBytes = 1024 * 1024; // 1 MB
    public const int MaxRules = 500;
    public const int MaxPatternLength = 500;
    public const int MaxNameLength = 100;
    public const int MaxAppFilters = 1000;

    /// <summary>Throws InvalidDataException if the file is too large to be a legitimate export.</summary>
    public static void EnsureFileSizeAllowed(string path)
    {
        var length = new FileInfo(path).Length;
        if (length > MaxImportFileBytes)
            throw new InvalidDataException($"File is too large to import ({length / 1024} KB; limit {MaxImportFileBytes / 1024} KB).");
    }

    public static bool TryValidateRule(NotificationRule rule, out string? error)
    {
        error = null;

        if (rule == null) { error = "Rule is empty."; return false; }
        if (string.IsNullOrEmpty(rule.Pattern)) { error = "Pattern is empty."; return false; }
        if (rule.Pattern.Length > MaxPatternLength) { error = "Pattern is too long."; return false; }
        if (!Enum.IsDefined(typeof(RuleField), rule.TargetField) ||
            !Enum.IsDefined(typeof(RuleOperator), rule.Operator) ||
            !Enum.IsDefined(typeof(RuleAction), rule.Action))
        {
            error = "Unknown field, operator or action.";
            return false;
        }

        if (rule.Operator == RuleOperator.RegexMatch)
        {
            try
            {
                _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50));
            }
            catch (ArgumentException)
            {
                error = "Invalid regular expression.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns only valid rules (capped at MaxRules) with names normalised.
    /// <paramref name="skipped"/> reports how many entries were rejected.
    /// </summary>
    public static List<NotificationRule> SanitizeRules(IEnumerable<NotificationRule>? incoming, out int skipped)
    {
        var result = new List<NotificationRule>();
        skipped = 0;
        if (incoming == null) return result;

        foreach (var rule in incoming)
        {
            if (rule == null || result.Count >= MaxRules || !TryValidateRule(rule, out _))
            {
                skipped++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.Name))
                rule.Name = "Imported Rule";
            else if (rule.Name.Length > MaxNameLength)
                rule.Name = rule.Name[..MaxNameLength];

            if (rule.AppFilterName?.Length > MaxNameLength)
                rule.AppFilterName = rule.AppFilterName[..MaxNameLength];

            result.Add(rule);
        }

        return result;
    }

    /// <summary>Clamps numeric/enum settings of an imported config into the ranges the UI allows.</summary>
    public static void ClampConfig(GotifyClientConfig c)
    {
        c.GlobalMinPriority = Math.Clamp(c.GlobalMinPriority, 0, 10);
        c.MaxHistoryCount = Math.Clamp(c.MaxHistoryCount, 10, 2000);
        if (!Enum.IsDefined(typeof(AppThemeMode), c.Theme)) c.Theme = AppThemeMode.System;

        if (c.Presentation != null)
        {
            var p = c.Presentation;
            p.DisplayDurationSeconds = double.IsFinite(p.DisplayDurationSeconds) ? Math.Clamp(p.DisplayDurationSeconds, 2, 20) : 5;
            p.Width = double.IsFinite(p.Width) ? Math.Clamp(p.Width, 280, 600) : 360;
            p.MaxBodyLines = Math.Clamp(p.MaxBodyLines, 1, 10);
            p.DebounceWindowSeconds = Math.Clamp(p.DebounceWindowSeconds, 3, 60);
            if (!Enum.IsDefined(typeof(NotificationMode), p.Mode)) p.Mode = NotificationMode.CustomHud;
            if (!Enum.IsDefined(typeof(HudPosition), p.Position)) p.Position = HudPosition.BottomRight;
        }

        if (c.Dnd != null)
        {
            var d = c.Dnd;
            d.EmergencyBypassPriority = Math.Clamp(d.EmergencyBypassPriority, 1, 10);
            d.QuietHoursStart = ClampTimeOfDay(d.QuietHoursStart);
            d.QuietHoursEnd = ClampTimeOfDay(d.QuietHoursEnd);
        }
    }

    /// <summary>Keeps only well-formed per-app filters (capped), with clamped priorities.</summary>
    public static Dictionary<long, AppFilterSetting> SanitizeAppFilters(Dictionary<long, AppFilterSetting>? incoming)
    {
        var result = new Dictionary<long, AppFilterSetting>();
        if (incoming == null) return result;

        foreach (var (id, setting) in incoming)
        {
            if (setting == null || result.Count >= MaxAppFilters) continue;
            setting.AppId = id;
            setting.MinPriority = Math.Clamp(setting.MinPriority, 0, 10);
            if (setting.AppName?.Length > MaxNameLength) setting.AppName = setting.AppName[..MaxNameLength];
            // Per-app custom sound paths are machine-specific and could point anywhere; never import them.
            setting.CustomSoundPath = null;
            result[id] = setting;
        }

        return result;
    }

    private static TimeSpan ClampTimeOfDay(TimeSpan t) =>
        t < TimeSpan.Zero || t >= TimeSpan.FromHours(24) ? TimeSpan.Zero : t;
}
