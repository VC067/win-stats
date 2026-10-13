using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Modules;

public sealed class GpuModule : IModuleProvider
{
    private readonly HistoryBuffer _usage = new();

    public ModuleId Id => ModuleId.Gpu;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        if (frame.HasGpu)
            _usage.Push(frame.GpuPercent);

        var stripValue = FormatText.PercentOrDash(frame.GpuPercent, frame.HasGpu);
        var stripLevel = frame.HasGpu ? frame.GpuPercent / 100d : 0d;
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Gpu,
                Title = "GPU",
                StripLabel = "GPU",
                StripValue = stripValue,
                StripLevel = stripLevel,
                Sparkline = WantsSparkline ? _usage.ToArray() : []
            };
        }

        double? vramPercent = null;
        if (frame.HasVram && frame.VramTotalBytes > 0)
            vramPercent = frame.VramUsedBytes / (double)frame.VramTotalBytes * 100d;

        var gauges = new[]
        {
            new GaugeModel
            {
                Label = "Usage",
                Percent = frame.HasGpu ? Math.Clamp(frame.GpuPercent, 0, 100) : 0,
                Center = stripValue,
                Unavailable = !frame.HasGpu
            },
            new GaugeModel
            {
                Label = "Temp",
                Percent = frame.GpuTemperatureC == null ? 0 : Math.Clamp(frame.GpuTemperatureC.Value, 0, 100),
                Center = FormatText.Temperature(frame.GpuTemperatureC, frame.TemperatureUnit),
                Unavailable = frame.GpuTemperatureC == null
            },
            new GaugeModel
            {
                Label = "VRAM",
                Percent = vramPercent == null ? 0 : Math.Clamp(vramPercent.Value, 0, 100),
                Center = vramPercent == null ? "--" : FormatText.Percent(vramPercent.Value),
                Unavailable = !frame.HasVram || frame.VramTotalBytes == 0
            }
        };

        var chart = new[]
        {
            new SeriesModel { Name = "3D", ColorKey = "Gpu", Values = _usage.ToArray() }
        };

        var details = new[]
        {
            new DetailModel { Label = "3D", Value = FormatText.PercentOrDash(frame.GpuPercent, frame.HasGpu), ColorKey = "Gpu" },
            new DetailModel { Label = "Copy", Value = FormatText.PercentOrDash(frame.GpuCopyPercent, frame.HasGpu), ColorKey = "Gpu" },
            new DetailModel { Label = "Video decode", Value = FormatText.PercentOrDash(frame.GpuDecodePercent, frame.HasGpu), ColorKey = "Gpu" },
            new DetailModel { Label = "Video encode", Value = FormatText.PercentOrDash(frame.GpuEncodePercent, frame.HasGpu), ColorKey = "Gpu" },
            new DetailModel
            {
                Label = "VRAM",
                Value = frame.HasVram ? $"{FormatText.Bytes(frame.VramUsedBytes)} of {FormatText.Bytes(frame.VramTotalBytes)}" : "--",
                ColorKey = "Gpu"
            },
            new DetailModel
            {
                Label = "Fan",
                Value = frame.FanPercent == null ? "--" : FormatText.Percent(frame.FanPercent.Value),
                ColorKey = "Gpu"
            },
            new DetailModel
            {
                Label = "Clock",
                Value = frame.GraphicsClockMhz == null ? "--" : FormatText.Frequency(frame.GraphicsClockMhz.Value),
                ColorKey = "Gpu"
            },
            new DetailModel { Label = "Power", Value = FormatText.Watts(frame.PowerWatts), ColorKey = "Gpu" },
            new DetailModel { Label = "Model", Value = FormatText.OrDash(frame.GpuName), ColorKey = "Gpu" },
            new DetailModel { Label = "Driver", Value = FormatText.OrDash(frame.DriverVersion), ColorKey = "Gpu" }
        };

        return new ModuleSnapshot
        {
            Id = ModuleId.Gpu,
            Title = "GPU",
            StripLabel = "GPU",
            StripValue = stripValue,
            StripLevel = stripLevel,
            Sparkline = _usage.ToArray(),
            Gauges = gauges,
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = true,
            Details = details,
            Bars = [],
            BarHeader = "",
            Processes = [],
            ProcessValueHeader = "",
            Note = frame.HasGpu ? null : "GPU usage is unavailable"
        };
    }
}
