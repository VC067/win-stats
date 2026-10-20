using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using StatsBar.Settings;

namespace StatsBar.Diagnostics;

internal sealed class BenchmarkSession : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly List<Reading> _readings = [];
    private readonly string _path;
    private readonly Action _openDetails;
    private readonly Func<ModuleId?> _activePanel;
    private readonly Action _shutdown;
    private TimeSpan _lastCpu;
    private double _lastSeconds;
    private bool _opened;

    public BenchmarkSession(string path, Action openDetails, Func<ModuleId?> activePanel, Action shutdown)
    {
        _path = Path.GetFullPath(path);
        _openDetails = openDetails;
        _activePanel = activePanel;
        _shutdown = shutdown;
        _lastCpu = _process.TotalProcessorTime;
        _timer.Tick += Tick;
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs args)
    {
        _process.Refresh();
        var seconds = _clock.Elapsed.TotalSeconds;
        var cpu = _process.TotalProcessorTime;
        var percent = (cpu - _lastCpu).TotalSeconds / (seconds - _lastSeconds) / Environment.ProcessorCount * 100;
        _lastCpu = cpu;
        _lastSeconds = seconds;
        var panel = _activePanel();
        if (seconds >= 15 && seconds < 45)
            _readings.Add(new Reading(panel == null ? "idle" : "idle-interrupted", seconds, percent, _process.PrivateMemorySize64, _process.WorkingSet64, panel?.ToString()));
        if (seconds >= 45 && !_opened)
        {
            _opened = true;
            _openDetails();
        }
        if (seconds >= 50 && seconds < 80)
            _readings.Add(new Reading(panel == ModuleId.Cpu ? "cpu-panel" : "cpu-panel-interrupted", seconds, percent, _process.PrivateMemorySize64, _process.WorkingSet64, panel?.ToString()));
        if (seconds < 80)
            return;
        _timer.Stop();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new
            {
                capturedAtUtc = DateTime.UtcNow,
                windows = Environment.OSVersion.VersionString,
                logicalProcessors = Environment.ProcessorCount,
                runtime = Environment.Version.ToString(),
                cpuDefinition = "Percent of total logical CPU capacity; process CPU time divided by wall time and logical processor count.",
                conditions = "Default CPU/RAM/GPU strip, 1-second sampling, 15-second warm-up, 30 seconds idle then 5-second panel warm-up and 30 seconds CPU panel. Diagnostic mode keeps the panel visible after focus changes; each sample records the active panel. Existing working-set trim enabled. No settings or startup changes saved.",
                readings = _readings
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            _shutdown();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Tick;
        _process.Dispose();
    }

    private sealed record Reading(string Phase, double Seconds, double CpuPercent, long PrivateBytes, long WorkingSetBytes, string? ActivePanel);
}
