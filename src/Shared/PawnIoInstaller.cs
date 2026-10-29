using System.IO;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace WinStats.Sensors;

/// <summary>
/// Describes the official PawnIO driver installer that the sensor service uses for CPU temperature.
/// The version and hash are pinned so the app only runs the reviewed release.
/// </summary>
public static class PawnIoInstaller
{
    public const string Version = "2.2.0";
    public const string FileName = "PawnIO_setup-2.2.0.exe";
    public const string DownloadUrl =
        "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string Sha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";

    private const string UninstallRegistryKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(UninstallRegistryKey);
            return key != null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool Verify(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            return string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return false;
        }
    }
}
