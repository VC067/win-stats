using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Modules;

public sealed class NetworkModule : IModuleProvider
{
    private readonly HistoryBuffer _down = new();
    private readonly HistoryBuffer _up = new();

    public ModuleId Id => ModuleId.Network;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        var net = frame.Network;
        _down.Push(net.DownBytesPerSecond);
        _up.Push(net.UpBytesPerSecond);

        const double scale = 10d * 1024d * 1024d;
        var stripValue = FormatText.Rate(net.DownBytesPerSecond);
        var stripLevel = Math.Clamp(net.DownBytesPerSecond / scale, 0, 1);
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Network,
                Title = "Network",
                StripLabel = "NET",
                StripValue = stripValue,
                StripLevel = stripLevel,
                SpeedPrimary = FormatText.Rate(net.DownBytesPerSecond),
                SpeedSecondary = FormatText.Rate(net.UpBytesPerSecond),
                Sparkline = WantsSparkline ? _down.ToArray() : []
            };
        }

        var gauges = new[]
        {
            new GaugeModel
            {
                Label = "Down",
                Percent = Math.Clamp(net.DownBytesPerSecond / scale * 100d, 0, 100),
                Center = FormatText.Rate(net.DownBytesPerSecond),
                Unavailable = false
            },
            new GaugeModel
            {
                Label = "Up",
                Percent = Math.Clamp(net.UpBytesPerSecond / scale * 100d, 0, 100),
                Center = FormatText.Rate(net.UpBytesPerSecond),
                Unavailable = false
            }
        };

        var chart = new[]
        {
            new SeriesModel { Name = "Down", ColorKey = "NetworkDown", Values = _down.ToArray() },
            new SeriesModel { Name = "Up", ColorKey = "NetworkUp", Values = _up.ToArray() }
        };

        var publicAddress = string.IsNullOrEmpty(net.PublicAddress) ? "Off" : net.PublicAddress;

        var details = new[]
        {
            new DetailModel { Label = "Down", Value = FormatText.Rate(net.DownBytesPerSecond), ColorKey = "NetworkDown" },
            new DetailModel { Label = "Up", Value = FormatText.Rate(net.UpBytesPerSecond), ColorKey = "NetworkUp" },
            new DetailModel { Label = "Adapter", Value = FormatText.OrDash(net.AdapterName), ColorKey = "NetworkDown" },
            new DetailModel { Label = "Local address", Value = net.LocalAddress, ColorKey = "NetworkDown" },
            new DetailModel { Label = "Public address", Value = publicAddress, ColorKey = "NetworkDown" },
            new DetailModel { Label = "Session down", Value = FormatText.Bytes(net.SessionDownBytes), ColorKey = "NetworkDown" },
            new DetailModel { Label = "Session up", Value = FormatText.Bytes(net.SessionUpBytes), ColorKey = "NetworkDown" }
        };

        return new ModuleSnapshot
        {
            Id = ModuleId.Network,
            Title = "Network",
            StripLabel = "NET",
            StripValue = stripValue,
            StripLevel = stripLevel,
            SpeedPrimary = FormatText.Rate(net.DownBytesPerSecond),
            SpeedSecondary = FormatText.Rate(net.UpBytesPerSecond),
            Sparkline = _down.ToArray(),
            Gauges = gauges,
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = false,
            Details = details,
            Bars = [],
            BarHeader = "",
            Processes = [],
            ProcessValueHeader = "",
            Note = null
        };
    }
}
