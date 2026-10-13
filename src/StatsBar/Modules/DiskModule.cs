using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Modules;

public sealed class DiskModule : IModuleProvider
{
    private readonly Dictionary<string, HistoryBuffer> _read = new();
    private readonly Dictionary<string, HistoryBuffer> _write = new();
    private readonly Dictionary<string, HistoryBuffer> _busy = new();

    public ModuleId Id => ModuleId.Disk;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        foreach (var disk in frame.Disks)
        {
            if (!_read.TryGetValue(disk.Letter, out var read))
            {
                read = new HistoryBuffer();
                _read[disk.Letter] = read;
            }
            if (!_write.TryGetValue(disk.Letter, out var write))
            {
                write = new HistoryBuffer();
                _write[disk.Letter] = write;
            }
            if (!_busy.TryGetValue(disk.Letter, out var busy))
            {
                busy = new HistoryBuffer();
                _busy[disk.Letter] = busy;
            }
            read.Push(disk.ReadBytesPerSecond);
            write.Push(disk.WriteBytesPerSecond);
            busy.Push(disk.BusyPercent);
        }

        if (frame.Disks.Count == 0)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Disk,
                Title = "Disk",
                StripLabel = "DISK",
                StripValue = "--",
                StripLevel = 0,
                Sparkline = [],
                Gauges = [],
                Chart = [],
                StackedChart = false,
                ChartScaleIsPercent = false,
                Details = [],
                Bars = [],
                BarHeader = "",
                Processes = [],
                ProcessValueHeader = "",
                Note = "No fixed drives"
            };
        }

        var busiest = frame.Disks[0];
        foreach (var disk in frame.Disks)
        {
            if (disk.BusyPercent > busiest.BusyPercent)
                busiest = disk;
        }

        DiskReading? systemDrive = null;
        foreach (var disk in frame.Disks)
        {
            if (disk.Letter == "C")
            {
                systemDrive = disk;
                break;
            }
        }
        systemDrive ??= frame.Disks[0];

        var stripValue = FormatText.Percent(busiest.BusyPercent);
        var stripLevel = busiest.BusyPercent / 100d;
        var sparkline = WantsSparkline || WantsDetails
            ? (_busy.TryGetValue(busiest.Letter, out var busiestBusy) ? busiestBusy.ToArray() : Array.Empty<double>())
            : [];
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Disk,
                Title = "Disk",
                StripLabel = "DISK",
                StripValue = stripValue,
                StripLevel = stripLevel,
                SpeedPrimary = FormatText.Rate(busiest.ReadBytesPerSecond),
                SpeedSecondary = FormatText.Rate(busiest.WriteBytesPerSecond),
                Sparkline = sparkline
            };
        }

        var chart = new[]
        {
            new SeriesModel
            {
                Name = "Read",
                ColorKey = "Disk",
                Values = _read.TryGetValue(systemDrive.Letter, out var readHist) ? readHist.ToArray() : Array.Empty<double>()
            },
            new SeriesModel
            {
                Name = "Write",
                ColorKey = "NetworkUp",
                Values = _write.TryGetValue(systemDrive.Letter, out var writeHist) ? writeHist.ToArray() : Array.Empty<double>()
            }
        };

        var bars = frame.Disks.Select(disk =>
        {
            var used = disk.CapacityBytes - disk.FreeBytes;
            var label = string.IsNullOrEmpty(disk.VolumeLabel)
                ? $"{disk.Letter}:"
                : $"{disk.Letter}: {disk.VolumeLabel}";
            var percent = disk.CapacityBytes == 0 ? 0d : used / (double)disk.CapacityBytes * 100d;
            return new BarModel
            {
                Label = label,
                Value = $"{FormatText.Bytes(used)} of {FormatText.Bytes(disk.CapacityBytes)}",
                Percent = percent,
                ColorKey = "Disk"
            };
        }).ToArray();

        var details = new List<DetailModel>();
        foreach (var disk in frame.Disks)
        {
            details.Add(new DetailModel { Label = $"{disk.Letter}: read", Value = FormatText.Rate(disk.ReadBytesPerSecond), ColorKey = "Disk" });
            details.Add(new DetailModel { Label = $"{disk.Letter}: write", Value = FormatText.Rate(disk.WriteBytesPerSecond), ColorKey = "Disk" });
            details.Add(new DetailModel { Label = $"{disk.Letter}: busy", Value = FormatText.Percent(disk.BusyPercent), ColorKey = "Disk" });
        }

        return new ModuleSnapshot
        {
            Id = ModuleId.Disk,
            Title = "Disk",
            StripLabel = "DISK",
            StripValue = stripValue,
            StripLevel = stripLevel,
            SpeedPrimary = FormatText.Rate(busiest.ReadBytesPerSecond),
            SpeedSecondary = FormatText.Rate(busiest.WriteBytesPerSecond),
            Sparkline = sparkline,
            Gauges = [],
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = false,
            Details = details,
            Bars = bars,
            BarHeader = "Capacity",
            Processes = [],
            ProcessValueHeader = "",
            Note = null
        };
    }
}
