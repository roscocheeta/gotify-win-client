using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Microsoft.Win32;
using GotifyWin.Models.Config;

namespace GotifyWin.Services;

public interface IThemeManager
{
    void ApplyTheme(AppThemeMode mode);
    bool IsDarkThemeActive { get; }
    event EventHandler<bool>? ThemeChanged;
}

public class ThemeManager : IThemeManager
{
    private AppThemeMode _currentMode = AppThemeMode.System;
    private bool _isDarkActive = true;

    public bool IsDarkThemeActive => _isDarkActive;
    public event EventHandler<bool>? ThemeChanged;

    public ThemeManager()
    {
        try
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (_currentMode == AppThemeMode.System && e.Category == UserPreferenceCategory.General)
                {
                    Application.Current?.Dispatcher?.Invoke(() => ApplyTheme(AppThemeMode.System));
                }
            };
        }
        catch { }
    }

    public void ApplyTheme(AppThemeMode mode)
    {
        _currentMode = mode;
        bool useDark;

        switch (mode)
        {
            case AppThemeMode.Dark:
                useDark = true;
                break;
            case AppThemeMode.Light:
                useDark = false;
                break;
            case AppThemeMode.System:
            default:
                useDark = !IsWindowsLightTheme();
                break;
        }

        _isDarkActive = useDark;

        if (Application.Current != null)
        {
            var res = Application.Current.Resources;

            if (useDark)
            {
                // Dark Palette (Fluent Slate)
                res["ThemeBackground"] = ToBrush("#0B1120");
                res["ThemeHeaderBackground"] = ToBrush("#0F172A");
                res["ThemeCardBackground"] = ToBrush("#1E293B");
                res["ThemeCardInnerBackground"] = ToBrush("#0F172A");
                res["ThemeCardBorder"] = ToBrush("#334155");
                res["ThemeTextPrimary"] = ToBrush("#F8FAFC");
                res["ThemeTextSecondary"] = ToBrush("#94A3B8");
                res["ThemeTextMuted"] = ToBrush("#64748B");
                res["ThemeInputBackground"] = ToBrush("#0F172A");
                res["ThemeInputBorder"] = ToBrush("#334155");
                res["ThemePopupBackground"] = ToBrush("#1E293B");
                res["ThemeItemHover"] = ToBrush("#334155");
                res["ThemeBrandAccent"] = ToBrush("#6EC1F8");
                res["ThemePrimaryButton"] = ToBrush("#0A58CA");
                res["ThemePrimaryButtonHover"] = ToBrush("#053285");
                res["ThemeBadge"] = ToBrush("#FF5C39");
                res["ThemeSecondaryButtonBg"] = ToBrush("#1E293B");
                res["ThemeSecondaryButtonBorder"] = ToBrush("#334155");
                res["ThemeSecondaryButtonText"] = ToBrush("#F8FAFC");
                res["ThemeNavHover"] = ToBrush("#1E293B");
                res["ThemeNavChecked"] = ToBrush("#1E293B");
            }
            else
            {
                // Light Palette (Fluent Clean White)
                res["ThemeBackground"] = ToBrush("#F8FAFC");
                res["ThemeHeaderBackground"] = ToBrush("#FFFFFF");
                res["ThemeCardBackground"] = ToBrush("#FFFFFF");
                res["ThemeCardInnerBackground"] = ToBrush("#F1F5F9");
                res["ThemeCardBorder"] = ToBrush("#E2E8F0");
                res["ThemeTextPrimary"] = ToBrush("#0F172A");
                res["ThemeTextSecondary"] = ToBrush("#475569");
                res["ThemeTextMuted"] = ToBrush("#94A3B8");
                res["ThemeInputBackground"] = ToBrush("#FFFFFF");
                res["ThemeInputBorder"] = ToBrush("#CBD5E1");
                res["ThemePopupBackground"] = ToBrush("#FFFFFF");
                res["ThemeItemHover"] = ToBrush("#F1F5F9");
                res["ThemeBrandAccent"] = ToBrush("#0A58CA");
                res["ThemePrimaryButton"] = ToBrush("#0A58CA");
                res["ThemePrimaryButtonHover"] = ToBrush("#053285");
                res["ThemeBadge"] = ToBrush("#FF5C39");
                res["ThemeSecondaryButtonBg"] = ToBrush("#F1F5F9");
                res["ThemeSecondaryButtonBorder"] = ToBrush("#CBD5E1");
                res["ThemeSecondaryButtonText"] = ToBrush("#0F172A");
                res["ThemeNavHover"] = ToBrush("#F1F5F9");
                res["ThemeNavChecked"] = ToBrush("#E2E8F0");
            }
        }

        ThemeChanged?.Invoke(this, _isDarkActive);
    }

    public static bool IsWindowsLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0
            );

            if (value is int intVal && intVal == 1)
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }
}
