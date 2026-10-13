using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Modules;

public sealed class RamModule : IModuleProvider
{
    private readonly HistoryBuffer _ram = new();

    public ModuleId Id => ModuleId.Ram;
    public bool WantsDetails { get; set; }
    public bool WantsSparkline { get; set; }

    public ModuleSnapshot Build(SampleFrame frame)
    {
        if (frame.RamTotalBytes > 0)
            _ram.Push(frame.RamPercent);

        var hasRam = frame.RamTotalBytes > 0;
        var stripValue = hasRam ? FormatText.Percent(frame.RamPercent) : "--";
        var stripLevel = hasRam ? frame.RamPercent / 100d : 0d;
        if (!WantsDetails)
        {
            return new ModuleSnapshot
            {
                Id = ModuleId.Ram,
                Title = "Memory",
                StripLabel = "RAM",
                StripValue = stripValue,
                StripLevel = stripLevel,
                Sparkline = WantsSparkline ? _ram.ToArray() : []
            };
        }

        var gauges = new[]
        {
            new GaugeModel
            {
                Label = "Memory",
                Percent = hasRam ? Math.Clamp(frame.RamPercent, 0, 100) : 0,
                Center = stripValue,
                Unavailable = !hasRam
            }
        };

        var chart = new[]
        {
            new SeriesModel { Name = "Memory", ColorKey = "Ram", Values = _ram.ToArray() }
        };

        var details = new[]
        {
            new DetailModel { Label = "In use", Value = FormatText.Bytes(frame.RamUsedBytes), ColorKey = "Ram" },
            new DetailModel { Label = "Standby", Value = FormatText.Bytes(frame.StandbyBytes), ColorKey = "Ram" },
            new DetailModel { Label = "Modified", Value = FormatText.Bytes(frame.ModifiedBytes), ColorKey = "Ram" },
            new DetailModel { Label = "Free", Value = FormatText.Bytes(frame.FreeBytes), ColorKey = "Ram" },
            new DetailModel { Label = "Compressed", Value = frame.HasCompressed ? FormatText.Bytes(frame.CompressedBytes) : "--", ColorKey = "Ram" },
            new DetailModel { Label = "Total", Value = FormatText.Bytes(frame.RamTotalBytes), ColorKey = "Ram" },
            new DetailModel
            {
                Label = "Page file",
                Value = $"{FormatText.Percent(frame.PageFilePercent)} ({FormatText.Bytes(frame.PageFileUsedBytes)} of {FormatText.Bytes(frame.PageFileTotalBytes)})",
                ColorKey = "Ram"
            }
        };

        ProcessModel[] processes;
        string processValueHeader;
        if (frame.IncludesProcesses)
        {
            processes = frame.TopMemory.Select(p => new ProcessModel
            {
                Name = p.Name,
                ImagePath = p.ImagePath,
                Value = FormatText.Bytes(p.WorkingSetBytes)
            }).ToArray();
            processValueHeader = "Memory";
        }
        else
        {
            processes = [];
            processValueHeader = "";
        }

        return new ModuleSnapshot
        {
            Id = ModuleId.Ram,
            Title = "Memory",
            StripLabel = "RAM",
            StripValue = stripValue,
            StripLevel = stripLevel,
            Sparkline = _ram.ToArray(),
            Gauges = gauges,
            Chart = chart,
            StackedChart = false,
            ChartScaleIsPercent = true,
            Details = details,
            Bars = [],
            BarHeader = "",
            Processes = processes,
            ProcessValueHeader = processValueHeader,
            Note = null
        };
    }
}
