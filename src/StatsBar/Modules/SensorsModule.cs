using StatsBar.Core;
using StatsBar.Settings;
using WinStats.Sensors;

namespace StatsBar.Modules;

public sealed class SensorsModule : IModuleProvider
{
    private readonly HistoryBuffer _gpuTemp = new();
    private readonly HistoryBuffer _cpuTemp = new();
    private readonly object _peakGate = new();
    private Peak? _gpuPeak;
    private Peak? _cpuPeak;

    public ModuleId Id => ModuleId.Sensors;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        double? gpuTemperature = frame.GpuTemperatureC is double gpu && double.IsFinite(gpu) ? gpu : null;
        var cpuTemperature = frame.CpuTemperature.Status == CpuTemperatureStatus.Disabled
            ? CpuTemperatureReading.Disabled
            : CpuTemperatureProtocol.Validate(frame.CpuTemperature, DateTimeOffset.UtcNow);
        var cpuTemperatureAvailable = cpuTemperature.Status == CpuTemperatureStatus.Available;
        var cpuCelsius = cpuTemperatureAvailable ? cpuTemperature.Celsius : null;

        if (gpuTemperature != null)
            _gpuTemp.Push(gpuTemperature.Value);
        if (cpuTemperature.Status == CpuTemperatureStatus.Disabled)
            _cpuTemp.Clear();
        if (cpuCelsius is double cpu && double.IsFinite(cpu))
            _cpuTemp.Push(cpu);

        ObservePeaks(gpuTemperature, cpuCelsius, cpuTemperature.Status);

        // Prefer the selected source, but fall back to the other one rather than showing "--".
        double? selectedTemperature;
        HistoryBuffer selectedHistory;
        var selectedIsCpu = false;
        if (frame.SensorTemperatureSource == SensorTemperatureSource.Cpu && cpuCelsius is double cpuPreferred)
        {
            selectedTemperature = cpuPreferred;
            selectedHistory = _cpuTemp;
            selectedIsCpu = true;
        }
        else if (frame.SensorTemperatureSource == SensorTemperatureSource.Cpu && gpuTemperature is double gpuFallback)
        {
            selectedTemperature = gpuFallback;
            selectedHistory = _gpuTemp;
        }
        else if (gpuTemperature is double gpuPreferred)
        {
            selectedTemperature = gpuPreferred;
            selectedHistory = _gpuTemp;
        }
        else if (cpuCelsius is double cpuFallback)
        {
            selectedTemperature = cpuFallback;
            selectedHistory = _cpuTemp;
            selectedIsCpu = true;
        }
        else
        {
            selectedTemperature = null;
            selectedHistory = _cpuTemp;
        }

        var preferredIsCpu = frame.SensorTemperatureSource == SensorTemperatureSource.Cpu;
        var stripLabel = selectedTemperature == null || preferredIsCpu == selectedIsCpu
            ? "TEMP"
            : selectedIsCpu ? "CPU TEMP" : "GPU TEMP";
        var stripValue = FormatText.Temperature(selectedTemperature, frame.TemperatureUnit);
        var stripLevel = selectedTemperature == null ? 0d : Math.Clamp(selectedTemperature.Value / 100d, 0, 1);
        var sparkline = WantsSparkline && selectedTemperature != null ? selectedHistory.ToArray() : [];
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Sensors,
                Title = "Sensors",
                StripLabel = stripLabel,
                StripValue = stripValue,
                StripLevel = stripLevel,
                Sparkline = sparkline
            };
        }

        var gauges = new List<GaugeModel>();
        if (gpuTemperature != null)
        {
            gauges.Add(new GaugeModel
            {
                Label = "GPU",
                Percent = Math.Clamp(gpuTemperature.Value, 0, 100),
                Center = FormatText.Temperature(gpuTemperature, frame.TemperatureUnit)
            });
        }
        if (cpuCelsius != null)
        {
            gauges.Add(new GaugeModel
            {
                Label = "CPU",
                Percent = Math.Clamp(cpuCelsius.Value, 0, 100),
                Center = FormatText.Temperature(cpuCelsius, frame.TemperatureUnit)
            });
        }
        if (frame.FanPercent != null)
        {
            gauges.Add(new GaugeModel
            {
                Label = "Fan",
                Percent = Math.Clamp(frame.FanPercent.Value, 0, 100),
                Center = FormatText.Percent(frame.FanPercent.Value)
            });
        }
        if (frame.PowerWatts != null)
        {
            gauges.Add(new GaugeModel
            {
                Label = "Power",
                Percent = Math.Clamp(frame.PowerWatts.Value, 0, 100),
                Center = FormatText.Watts(frame.PowerWatts)
            });
        }

        var gpuHistory = _gpuTemp.ToArray();
        var cpuHistory = _cpuTemp.ToArray();
        var chart = new List<SeriesModel>();
        if (gpuTemperature != null && gpuHistory.Length > 0)
            chart.Add(new SeriesModel { Name = "GPU", ColorKey = "Sensors", Values = gpuHistory });
        if (cpuCelsius != null && cpuHistory.Length > 0)
            chart.Add(new SeriesModel { Name = "CPU", ColorKey = "Cpu", Values = cpuHistory });

        var details = new List<DetailModel>();
        if (gpuTemperature != null)
        {
            details.Add(new DetailModel
            {
                Label = "GPU temperature",
                Value = FormatText.Temperature(gpuTemperature, frame.TemperatureUnit),
                ColorKey = "Sensors"
            });
        }
        if (cpuCelsius != null)
        {
            details.Add(new DetailModel
            {
                Label = "CPU temperature",
                Value = FormatText.Temperature(cpuCelsius, frame.TemperatureUnit),
                ColorKey = "Cpu"
            });
        }
        if (frame.FanPercent != null)
        {
            details.Add(new DetailModel
            {
                Label = "Fan",
                Value = FormatText.Percent(frame.FanPercent.Value),
                ColorKey = "Sensors"
            });
        }
        if (frame.GraphicsClockMhz != null)
        {
            details.Add(new DetailModel
            {
                Label = "Clock",
                Value = FormatText.Frequency(frame.GraphicsClockMhz.Value),
                ColorKey = "Sensors"
            });
        }
        if (frame.PowerWatts != null)
        {
            details.Add(new DetailModel
            {
                Label = "Power",
                Value = FormatText.Watts(frame.PowerWatts),
                ColorKey = "Sensors"
            });
        }

        var peaks = new List<TemperaturePeakModel>();
        var peakNow = DateTimeOffset.UtcNow;
        lock (_peakGate)
        {
            if (_gpuPeak is { } gpuPeak)
            {
                peaks.Add(new TemperaturePeakModel
                {
                    Label = "GPU",
                    Value = FormatText.Temperature(gpuPeak.Celsius, frame.TemperatureUnit),
                    At = FormatText.PeakTime(gpuPeak.At, peakNow),
                    ColorKey = "Sensors"
                });
            }
            if (_cpuPeak is { } cpuPeak)
            {
                peaks.Add(new TemperaturePeakModel
                {
                    Label = "CPU",
                    Value = FormatText.Temperature(cpuPeak.Celsius, frame.TemperatureUnit),
                    At = FormatText.PeakTime(cpuPeak.At, peakNow),
                    ColorKey = "Cpu"
                });
            }
        }

        return new ModuleSnapshot
        {
            Id = ModuleId.Sensors,
            Title = "Sensors",
            StripLabel = stripLabel,
            StripValue = stripValue,
            StripLevel = stripLevel,
            Sparkline = sparkline,
            Gauges = gauges,
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = false,
            Details = details,
            Peaks = peaks,
            Bars = [],
            BarHeader = "",
            Processes = [],
            ProcessValueHeader = "",
            Note = gauges.Count == 0 && details.Count == 0
                ? "No temperature sensors are available on this PC."
                : null
        };
    }

    public void ResetPeaks()
    {
        lock (_peakGate)
        {
            _gpuPeak = null;
            _cpuPeak = null;
        }
    }

    private void ObservePeaks(double? gpuCelsius, double? cpuCelsius, CpuTemperatureStatus cpuStatus)
    {
        if (cpuStatus == CpuTemperatureStatus.Disabled)
        {
            lock (_peakGate)
                _cpuPeak = null;
        }
        else if (IsPlausibleTemperature(cpuCelsius))
        {
            ObservePeak(ref _cpuPeak, cpuCelsius!.Value);
        }

        if (IsPlausibleTemperature(gpuCelsius))
            ObservePeak(ref _gpuPeak, gpuCelsius!.Value);
    }

    private void ObservePeak(ref Peak? peak, double celsius)
    {
        lock (_peakGate)
        {
            if (peak is null || celsius > peak.Value.Celsius)
                peak = new Peak(celsius, DateTimeOffset.UtcNow);
        }
    }

    private static bool IsPlausibleTemperature(double? value)
    {
        return CpuTemperatureProtocol.IsValidTemperature(value);
    }

    private readonly record struct Peak(double Celsius, DateTimeOffset At);
}
