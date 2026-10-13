using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Modules;

public sealed class SensorsModule : IModuleProvider
{
    private readonly HistoryBuffer _temp = new();

    public ModuleId Id => ModuleId.Sensors;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        if (frame.GpuTemperatureC != null)
            _temp.Push(frame.GpuTemperatureC.Value);

        var stripValue = FormatText.Temperature(frame.GpuTemperatureC, frame.TemperatureUnit);
        var stripLevel = frame.GpuTemperatureC == null ? 0d : Math.Clamp(frame.GpuTemperatureC.Value / 100d, 0, 1);
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Sensors,
                Title = "Sensors",
                StripLabel = "TEMP",
                StripValue = stripValue,
                StripLevel = stripLevel,
                Sparkline = WantsSparkline ? _temp.ToArray() : []
            };
        }

        var gauges = new[]
        {
            new GaugeModel
            {
                Label = "GPU",
                Percent = frame.GpuTemperatureC == null ? 0 : Math.Clamp(frame.GpuTemperatureC.Value, 0, 100),
                Center = stripValue,
                Unavailable = frame.GpuTemperatureC == null
            },
            new GaugeModel
            {
                Label = "Fan",
                Percent = frame.FanPercent == null ? 0 : Math.Clamp(frame.FanPercent.Value, 0, 100),
                Center = frame.FanPercent == null ? "--" : FormatText.Percent(frame.FanPercent.Value),
                Unavailable = frame.FanPercent == null
            },
            new GaugeModel
            {
                Label = "Power",
                Percent = frame.PowerWatts == null ? 0 : Math.Clamp(frame.PowerWatts.Value, 0, 100),
                Center = FormatText.Watts(frame.PowerWatts),
                Unavailable = frame.PowerWatts == null
            }
        };

        var chart = new[]
        {
            new SeriesModel { Name = "GPU", ColorKey = "Sensors", Values = _temp.ToArray() }
        };

        var details = new[]
        {
            new DetailModel { Label = "GPU temperature", Value = stripValue, ColorKey = "Sensors" },
            new DetailModel
            {
                Label = "Fan",
                Value = frame.FanPercent == null ? "--" : FormatText.Percent(frame.FanPercent.Value),
                ColorKey = "Sensors"
            },
            new DetailModel
            {
                Label = "Clock",
                Value = frame.GraphicsClockMhz == null ? "--" : FormatText.Frequency(frame.GraphicsClockMhz.Value),
                ColorKey = "Sensors"
            },
            new DetailModel { Label = "Power", Value = FormatText.Watts(frame.PowerWatts), ColorKey = "Sensors" },
            new DetailModel { Label = "CPU temperature", Value = "Needs a driver", ColorKey = "Neutral" }
        };

        return new ModuleSnapshot
        {
            Id = ModuleId.Sensors,
            Title = "Sensors",
            StripLabel = "TEMP",
            StripValue = stripValue,
            StripLevel = stripLevel,
            Sparkline = _temp.ToArray(),
            Gauges = gauges,
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = true,
            Details = details,
            Bars = [],
            BarHeader = "",
            Processes = [],
            ProcessValueHeader = "",
            Note = "CPU temperature needs a driver"
        };
    }
}
