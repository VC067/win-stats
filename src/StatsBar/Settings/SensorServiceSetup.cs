using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;
using WinStats.Sensors;

namespace StatsBar.Settings;

public sealed record SensorSetupResult(bool Succeeded, string Message);

public static class SensorServiceSetup
{
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static string BundledExecutablePath => Path.Combine(
        AppContext.BaseDirectory, "SensorsService", "WinStats.SensorService.exe");

    private static string InstalledExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "WinStats",
        "Sensors",
        "WinStats.SensorService.exe");

    private static string DriverCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinStats",
        "Driver",
        PawnIoInstaller.FileName);

    public static bool IsInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{CpuTemperatureProtocol.ServiceName}");
            if (key == null)
                return false;

            var expected = $"\"{InstalledExecutablePath}\"";
            return string.Equals(key.GetValue("ImagePath") as string, expected, StringComparison.OrdinalIgnoreCase)
                && File.Exists(InstalledExecutablePath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static bool IsDriverInstalled() => PawnIoInstaller.IsInstalled();

    public static async Task<SensorSetupResult> InstallAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string? driverPath = null;
        if (!PawnIoInstaller.IsInstalled())
        {
            progress?.Report("Downloading the sensor driver…");
            driverPath = await PrepareDriverInstallerAsync(cancellationToken).ConfigureAwait(false);
        }

        progress?.Report("Waiting for Windows permission…");
        var arguments = driverPath == null
            ? "--install"
            : $"--install --driver \"{driverPath}\"";
        return await RunAsync(BundledExecutablePath, arguments, "setup").ConfigureAwait(false);
    }

    public static Task<SensorSetupResult> RemoveAsync()
    {
        return RunAsync(File.Exists(InstalledExecutablePath) ? InstalledExecutablePath : BundledExecutablePath,
            "--remove", "removal");
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WinStats");
        return client;
    }

    private static async Task<string?> PrepareDriverInstallerAsync(CancellationToken cancellationToken)
    {
        var path = DriverCachePath;
        if (PawnIoInstaller.Verify(path))
            return path;

        var temporary = path + ".download";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            TryDelete(temporary);

            using var response = await HttpClient.GetAsync(
                PawnIoInstaller.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            if (!PawnIoInstaller.Verify(temporary))
            {
                TryDelete(temporary);
                return null;
            }

            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
            or OperationCanceledException)
        {
            TryDelete(temporary);
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static async Task<SensorSetupResult> RunAsync(string executablePath, string arguments, string operation)
    {
        if (!File.Exists(executablePath))
            throw new FileNotFoundException(
                "The CPU temperature component is missing from this build. Reinstall Win Stats and try again.",
                executablePath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executablePath),
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };

        try
        {
            if (!await Task.Run(() => process.Start()).ConfigureAwait(false))
                throw new InvalidOperationException("Windows could not start CPU temperature setup.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new SensorSetupResult(false, "Windows needs administrator permission for CPU temperature.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Windows could not request permission for CPU temperature: {ex.Message}", ex);
        }

        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode == 5)
            throw new InvalidOperationException("Windows denied permission to update CPU temperature support.");
        if (process.ExitCode == 6)
            throw new InvalidOperationException("Windows could not update the CPU temperature files.");
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"CPU temperature {operation} failed (error {process.ExitCode}).");

        if (operation != "setup")
            return new SensorSetupResult(true, "CPU temperature support was removed.");

        return new SensorSetupResult(true, PawnIoInstaller.IsInstalled()
            ? "CPU temperature is ready."
            : "CPU temperature needs a one-time sensor driver.");
    }
}
