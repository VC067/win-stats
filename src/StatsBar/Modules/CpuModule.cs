using StatsBar.Core;
using StatsBar.Settings;
using WinStats.Sensors;

namespace StatsBar.Modules;

public sealed class CpuModule : IModuleProvider
{
    private readonly HistoryBuffer _cpu = new();
    private readonly HistoryBuffer _user = new();
    private readonly HistoryBuffer _system = new();
    private readonly AverageWindow _average = new(900);

    public ModuleId Id => ModuleId.Cpu;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        if (frame.HasCpu)
        {
            _cpu.Push(frame.CpuPercent);
            _user.Push(frame.CpuUserPercent);
            _system.Push(frame.CpuSystemPercent);
            _average.Push(frame.CpuPercent);
        }

        var stripValue = FormatText.PercentOrDash(frame.CpuPercent, frame.HasCpu);
        var stripLevel = frame.HasCpu ? frame.CpuPercent / 100d : 0d;
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Cpu,
                Title = "CPU",
                StripLabel = "CPU",
                StripValue = stripValue,
                StripLevel = stripLevel,
                Sparkline = WantsSparkline ? _cpu.ToArray() : []
            };
        }

        var gauges = new[]
        {
            new GaugeModel
            {
                Label = "Usage",
                Percent = frame.HasCpu ? Math.Clamp(frame.CpuPercent, 0, 100) : 0,
                Center = stripValue,
                Unavailable = !frame.HasCpu
            }
        };

        var chart = new[]
        {
            new SeriesModel { Name = "User", ColorKey = "User", Values = _user.ToArray() },
            new SeriesModel { Name = "System", ColorKey = "System", Values = _system.ToArray() }
        };

        var cpuTemperature = frame.CpuTemperature.Status == CpuTemperatureStatus.Disabled
            ? CpuTemperatureReading.Disabled
            : CpuTemperatureProtocol.Validate(frame.CpuTemperature, DateTimeOffset.UtcNow);
        var cpuTemperatureAvailable = cpuTemperature.Status == CpuTemperatureStatus.Available;
        var details = new List<DetailModel>
        {
            new() { Label = "User", Value = FormatText.PercentOrDash(frame.CpuUserPercent, frame.HasCpu), ColorKey = "Cpu" },
            new() { Label = "System", Value = FormatText.PercentOrDash(frame.CpuSystemPercent, frame.HasCpu), ColorKey = "System" },
            new() { Label = "Idle", Value = FormatText.PercentOrDash(frame.CpuIdlePercent, frame.HasCpu), ColorKey = "Neutral" },
            new() { Label = "Frequency", Value = FormatText.Frequency(frame.FrequencyMhz), ColorKey = "Cpu" },
            new() { Label = "Uptime", Value = FormatText.Uptime(frame.Uptime), ColorKey = "Cpu" },
            new() { Label = "Average 1 min", Value = frame.HasCpu ? FormatText.Percent(_average.Average(60)) : "--", ColorKey = "Cpu" },
            new() { Label = "Average 5 min", Value = frame.HasCpu ? FormatText.Percent(_average.Average(300)) : "--", ColorKey = "Cpu" },
            new() { Label = "Average 15 min", Value = frame.HasCpu ? FormatText.Percent(_average.Average(900)) : "--", ColorKey = "Cpu" }
        };
        if (cpuTemperatureAvailable)
        {
            details.Add(new DetailModel
            {
                Label = "Temperature",
                Value = FormatText.Temperature(cpuTemperature.Celsius, frame.TemperatureUnit),
                ColorKey = "Sensors"
            });
            if (!string.IsNullOrWhiteSpace(cpuTemperature.SensorName))
                details.Add(new DetailModel { Label = "Temperature sensor", Value = cpuTemperature.SensorName, ColorKey = "Neutral" });
        }

        var bars = new List<BarModel>();
        for (var i = 0; i < frame.CorePercents.Count; i++)
        {
            var core = frame.CorePercents[i];
            bars.Add(new BarModel
            {
                Label = $"Core {i}",
                Value = FormatText.Percent(core),
                Percent = core,
                ColorKey = "Cpu"
            });
        }

        ProcessModel[] processes;
        string processValueHeader;
        if (frame.IncludesProcesses)
        {
            processes = frame.TopCpu.Select(p => new ProcessModel
            {
                Name = p.Name,
                ImagePath = p.ImagePath,
                Value = FormatText.Percent(p.CpuPercent)
            }).ToArray();
            processValueHeader = "Usage";
        }
        else
        {
            processes = [];
            processValueHeader = "";
        }

        return new ModuleSnapshot
        {
            Id = ModuleId.Cpu,
            Title = "CPU",
            StripLabel = "CPU",
            StripValue = stripValue,
            StripLevel = stripLevel,
            Sparkline = _cpu.ToArray(),
            Gauges = gauges,
            Chart = chart,
            StackedChart = true,
            ChartScaleIsPercent = true,
            Details = details,
            Bars = bars,
            BarHeader = "Cores",
            Processes = processes,
            ProcessValueHeader = processValueHeader
        };
    }
}
