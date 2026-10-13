using System.IO;
using Microsoft.Win32;
using StatsBar.Settings;

namespace StatsBar.Ui.Theme;

public static class ThemeService
{
    public static bool UseDarkCards(ThemePreference preference)
    {
        return preference switch
        {
            ThemePreference.Dark => true,
            ThemePreference.Light => false,
            _ => !AppsUseLightTheme()
        };
    }

    public static bool TaskbarIsLight()
    {
        return ReadDword("SystemUsesLightTheme") != 0;
    }

    private static bool AppsUseLightTheme()
    {
        var value = ReadDword("AppsUseLightTheme");
        return value != 0;
    }

    private static int ReadDword(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int value ? value : name == "SystemUsesLightTheme" ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return name == "SystemUsesLightTheme" ? 0 : 1;
        }
    }
}
