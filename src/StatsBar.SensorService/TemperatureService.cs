using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using WinStats.Sensors;

namespace WinStats.SensorService;

internal sealed class TemperatureService : ServiceBase
{
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private CpuTemperatureReading _latest = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable);
    private long _lastClientTick = long.MinValue;

    public TemperatureService()
    {
        ServiceName = CpuTemperatureProtocol.ServiceName;
        CanStop = true;
        CanShutdown = true;
    }

    protected override void OnStart(string[] args)
    {
        var pipe = CreatePipe();
        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        _worker = Task.Run(() => RunAsync(pipe, token));
    }

    protected override void OnStop()
    {
        _cancellation?.Cancel();
        if (_worker != null && !_worker.Wait(TimeSpan.FromSeconds(10)))
            throw new System.TimeoutException("The CPU sensor worker did not stop in time.");
        _cancellation?.Dispose();
        _cancellation = null;
    }

    protected override void OnShutdown() => OnStop();

    private async Task RunAsync(NamedPipeServerStream server, CancellationToken token)
    {
        var sampling = Task.Run(() => SampleAsync(token));
        try
        {
            // The service owns the name from startup; clients can only read fixed telemetry.
            using var pipe = server;
            while (!token.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                Interlocked.Exchange(ref _lastClientTick, Environment.TickCount64);
                try
                {
                    await CpuTemperaturePipe.SendAsync(pipe, Volatile.Read(ref _latest), token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested)
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            // Cancellation also releases the sampling worker when the pipe fails to initialize.
            _cancellation?.Cancel();
            await sampling.ConfigureAwait(false);
        }
    }

    internal static NamedPipeServerStream CreatePipe()
    {
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(system);
        security.AddAccessRule(new PipeAccessRule(system, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.Read, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));

        return NamedPipeServerStreamAcl.Create(CpuTemperatureProtocol.PipeName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance,
            0, CpuTemperatureProtocol.MaximumMessageBytes, security);
    }

    private async Task SampleAsync(CancellationToken token)
    {
        Computer? computer = null;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var lastClient = Interlocked.Read(ref _lastClientTick);
                if (lastClient != long.MinValue && Environment.TickCount64 - lastClient < 10_000)
                {
                    try
                    {
                        using var driverKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                        if (driverKey == null)
                        {
                            Volatile.Write(ref _latest, CpuTemperatureReading.Unavailable(CpuTemperatureStatus.DriverMissing));
                        }
                        else
                        {
                            if (computer == null)
                            {
                                computer = new Computer { IsCpuEnabled = true };
                                computer.Open();
                            }
                            var sensors = new List<(string Name, double? Celsius)>();
                            foreach (var cpu in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                            {
                                cpu.Update();
                                sensors.AddRange(cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature)
                                    .Select(s => (s.Name, (double?)s.Value)));
                            }
                            var selected = CpuSensorSelection.Select(sensors);
                            Volatile.Write(ref _latest, new CpuTemperatureReading
                            {
                                Celsius = selected.Celsius,
                                SensorName = selected.Name.Length > 128 ? selected.Name[..128] : selected.Name,
                                Status = selected.Celsius.HasValue ? CpuTemperatureStatus.Available : CpuTemperatureStatus.SensorUnavailable,
                                SampledAtUtc = DateTimeOffset.UtcNow
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Volatile.Write(ref _latest, CpuTemperatureReading.Unavailable(CpuTemperatureStatus.Error));
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            computer?.Close();
        }
    }
}
