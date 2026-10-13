using System.IO;
using Microsoft.Win32;

namespace StatsBar.Settings;

public static class StartupRegistration
{
    private const string ValueName = "Win Stats";
    private const string LegacyValueName = "StatsBar";

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null)
                return;
            key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
        }
    }
}
