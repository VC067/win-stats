using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;
using WinStats.Sensors;

namespace WinStats.SensorService;

internal static class ServiceInstallation
{
    private const string ExecutableName = "WinStats.SensorService.exe";
    private static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinStats", "Sensors");

    public static int Install(string? driverInstallerPath = null)
    {
        if (!IsAdministrator())
            return 5;

        try
        {
            var destination = Path.GetFullPath(InstallDirectory);
            CheckNoLinks(destination);
            CheckProtectedTree(Path.GetDirectoryName(destination)!);
            CheckServiceIdentity(destination);
            var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Repair must run from the bundled service, not the installed copy.");
            CheckNoLinks(source);
            var files = EnumerateFiles(source).ToArray();
            if (!files.Any(path => Path.GetFileName(path).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase)))
                throw new FileNotFoundException("The bundled service is incomplete.");

            StopIfInstalled();
            ProtectDirectory(Path.GetDirectoryName(destination)!);
            ProtectDirectory(destination);
            foreach (var file in files)
            {
                var target = Path.GetFullPath(Path.Combine(destination, Path.GetRelativePath(source, file)));
                if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Service file escaped the installation directory.");
                CheckNoLinks(target);
                ProtectDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                ProtectFile(target);
            }

            InstallDriver(driverInstallerPath);

            using var installed = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{CpuTemperatureProtocol.ServiceName}");
            if (installed == null)
            {
                RunSc("create", CpuTemperatureProtocol.ServiceName,
                    "binPath=", $"\"{Path.Combine(destination, ExecutableName)}\"",
                    "start=", "auto", "obj=", "LocalSystem", "DisplayName=", "Win Stats CPU Sensors");
            }
            else
            {
                RunSc("config", CpuTemperatureProtocol.ServiceName, "start=", "auto");
            }
            RunSc("description", CpuTemperatureProtocol.ServiceName, "Optional read-only CPU temperature service for Win Stats.");
            using var service = new ServiceController(CpuTemperatureProtocol.ServiceName);
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 5;
        }
        catch (IOException)
        {
            return 6;
        }
    }

    public static int Remove()
    {
        if (!IsAdministrator())
            return 5;
        CheckNoLinks(InstallDirectory);
        CheckServiceIdentity(InstallDirectory);
        using var installed = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{CpuTemperatureProtocol.ServiceName}");
        if (installed == null)
            return 0;
        StopIfInstalled();
        // Files are retained so the running removal executable is never deleted underneath itself.
        RunSc("delete", CpuTemperatureProtocol.ServiceName);
        return 0;
    }

    private static void InstallDriver(string? installerPath)
    {
        if (PawnIoInstaller.IsInstalled())
            return;
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
            return;

        try
        {
            if (!PawnIoInstaller.Verify(installerPath))
            {
                Trace.TraceError("Win Stats sensor service: the downloaded driver installer failed verification.");
                return;
            }

            var directory = Path.Combine(InstallDirectory, "Driver");
            ProtectDirectory(directory);
            var destination = Path.Combine(directory, PawnIoInstaller.FileName);
            CheckNoLinks(destination);
            File.Copy(installerPath, destination, overwrite: true);
            ProtectFile(destination);
            if (!PawnIoInstaller.Verify(destination))
            {
                Trace.TraceError("Win Stats sensor service: the copied driver installer failed verification.");
                return;
            }

            var start = new ProcessStartInfo(destination)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = directory
            };
            start.ArgumentList.Add("-install");
            start.ArgumentList.Add("-silent");

            using var process = Process.Start(start);
            if (process == null)
            {
                Trace.TraceError("Win Stats sensor service: could not start the driver installer.");
                return;
            }

            if (!process.WaitForExit(120_000))
            {
                process.Kill();
                Trace.TraceError("Win Stats sensor service: the driver installer timed out.");
                return;
            }

            for (var attempt = 0; attempt < 60 && !PawnIoInstaller.IsInstalled(); attempt++)
                Thread.Sleep(500);

            if (!PawnIoInstaller.IsInstalled())
                Trace.TraceError("Win Stats sensor service: the driver installer did not register PawnIO.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Trace.TraceError("Win Stats sensor service: driver setup failed: {0}", ex);
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void CheckServiceIdentity(string directory)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{CpuTemperatureProtocol.ServiceName}");
        if (key == null)
            return;
        var expected = $"\"{Path.Combine(directory, ExecutableName)}\"";
        if (!string.Equals(key.GetValue("ImagePath") as string, expected, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(key.GetValue("ObjectName") as string, "LocalSystem", StringComparison.OrdinalIgnoreCase))
            throw new IOException("A different service already uses this name.");
    }

    private static void StopIfInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{CpuTemperatureProtocol.ServiceName}");
        if (key == null)
            return;
        using var service = new ServiceController(CpuTemperatureProtocol.ServiceName);
        if (service.Status == ServiceControllerStatus.Stopped)
            return;
        if (service.Status != ServiceControllerStatus.StopPending)
            service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
    }

    private static void RunSc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "sc.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows service management.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new System.TimeoutException("Windows service management did not respond.");
        }
        if (process.ExitCode != 0)
            throw new Win32Exception(process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
    {
        CheckNoLinks(directory);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            CheckNoLinks(file);
            yield return file;
        }
        foreach (var child in Directory.EnumerateDirectories(directory))
            foreach (var file in EnumerateFiles(child))
                yield return file;
    }

    private static void CheckNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked service installation paths are not supported.");
    }

    private static void CheckProtectedTree(string path)
    {
        if (!Directory.Exists(path))
            return;
        CheckNoLinks(path);
        CheckProtectedAccess(new DirectoryInfo(path).GetAccessControl());
        foreach (var file in Directory.EnumerateFiles(path))
        {
            CheckNoLinks(file);
            CheckProtectedAccess(new FileInfo(file).GetAccessControl());
        }
        foreach (var directory in Directory.EnumerateDirectories(path))
            CheckProtectedTree(directory);
    }

    private static void CheckProtectedAccess(FileSystemSecurity security)
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!administrators.Equals(owner) && !system.Equals(owner))
            throw new IOException("Existing sensor files have an untrusted owner.");
        const FileSystemRights writes = FileSystemRights.WriteData | FileSystemRights.AppendData
            | FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes
            | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles
            | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0
                && (rule.FileSystemRights & writes) != 0
                && !administrators.Equals(rule.IdentityReference) && !system.Equals(rule.IdentityReference))
                throw new IOException("Existing sensor files allow non-administrator modification.");
    }

    private static void ProtectDirectory(string path)
    {
        CheckNoLinks(path);
        var info = new DirectoryInfo(path);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                inheritance, PropagationFlags.None, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (!info.Exists)
            FileSystemAclExtensions.Create(info, security);
        else
            info.SetAccessControl(security);
    }

    private static void ProtectFile(string path)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
}
