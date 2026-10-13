using StatsBar.Settings;

namespace StatsBar.Core;

public sealed class DiskReading
{
    public required string Letter { get; init; }
    public string VolumeLabel { get; init; } = "";
    public double ReadBytesPerSecond { get; init; }
    public double WriteBytesPerSecond { get; init; }
    public double BusyPercent { get; init; }
    public ulong CapacityBytes { get; init; }
    public ulong FreeBytes { get; init; }
}

public sealed class NetworkReading
{
    public string AdapterName { get; init; } = "--";
    public double DownBytesPerSecond { get; init; }
    public double UpBytesPerSecond { get; init; }
    public ulong SessionDownBytes { get; init; }
    public ulong SessionUpBytes { get; init; }
    public string LocalAddress { get; init; } = "--";
    public string? PublicAddress { get; init; }
}

public sealed class ProcessReading
{
    public required string Name { get; init; }
    public string ImagePath { get; init; } = "";
    public double CpuPercent { get; init; }
    public ulong WorkingSetBytes { get; init; }
}

public sealed class SampleFrame
{
    public bool HasCpu { get; init; }
    public double CpuPercent { get; init; }
    public double CpuUserPercent { get; init; }
    public double CpuSystemPercent { get; init; }
    public double CpuIdlePercent { get; init; }
    public IReadOnlyList<double> CorePercents { get; init; } = [];
    public double FrequencyMhz { get; init; }
    public TimeSpan Uptime { get; init; }
    public ulong RamTotalBytes { get; init; }
    public ulong RamUsedBytes { get; init; }
    public double RamPercent { get; init; }
    public ulong StandbyBytes { get; init; }
    public ulong ModifiedBytes { get; init; }
    public ulong FreeBytes { get; init; }
    public ulong CompressedBytes { get; init; }
    public bool HasCompressed { get; init; }
    public double PageFilePercent { get; init; }
    public ulong PageFileUsedBytes { get; init; }
    public ulong PageFileTotalBytes { get; init; }
    public bool HasGpu { get; init; }
    public double GpuPercent { get; init; }
    public double GpuCopyPercent { get; init; }
    public double GpuDecodePercent { get; init; }
    public double GpuEncodePercent { get; init; }
    public ulong VramUsedBytes { get; init; }
    public ulong VramTotalBytes { get; init; }
    public bool HasVram { get; init; }
    public double? GpuTemperatureC { get; init; }
    public double? FanPercent { get; init; }
    public double? GraphicsClockMhz { get; init; }
    public double? PowerWatts { get; init; }
    public string GpuName { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public IReadOnlyList<DiskReading> Disks { get; init; } = [];
    public NetworkReading Network { get; init; } = new();
    public IReadOnlyList<ProcessReading> TopCpu { get; init; } = [];
    public IReadOnlyList<ProcessReading> TopMemory { get; init; } = [];
    public bool IncludesProcesses { get; init; }
    public Settings.TemperatureUnit TemperatureUnit { get; init; }
}

public sealed class GaugeModel
{
    public required string Label { get; init; }
    public double Percent { get; init; }
    public required string Center { get; init; }
    public bool Unavailable { get; init; }
}

public sealed class SeriesModel
{
    public required string Name { get; init; }
    public required string ColorKey { get; init; }
    public required IReadOnlyList<double> Values { get; init; }
}

public sealed class DetailModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public required string ColorKey { get; init; }
}

public sealed class BarModel
{
    public required string Label { get; init; }
    public required string Value { get; init; }
    public double Percent { get; init; }
    public required string ColorKey { get; init; }
}

public sealed class ProcessModel
{
    public required string Name { get; init; }
    public string ImagePath { get; init; } = "";
    public required string Value { get; init; }
}

public sealed class ModuleSnapshot
{
    public required ModuleId Id { get; init; }
    public required string Title { get; init; }
    public required string StripLabel { get; init; }
    public required string StripValue { get; init; }
    public double StripLevel { get; init; }
    public string SpeedPrimary { get; init; } = "";
    public string SpeedSecondary { get; init; } = "";
    public IReadOnlyList<double> Sparkline { get; init; } = [];
    public IReadOnlyList<GaugeModel> Gauges { get; init; } = [];
    public IReadOnlyList<SeriesModel> Chart { get; init; } = [];
    public bool StackedChart { get; init; }
    public bool ChartScaleIsPercent { get; init; }
    public IReadOnlyList<DetailModel> Details { get; init; } = [];
    public IReadOnlyList<BarModel> Bars { get; init; } = [];
    public string BarHeader { get; init; } = "";
    public IReadOnlyList<ProcessModel> Processes { get; init; } = [];
    public string ProcessValueHeader { get; init; } = "";
    public string? Note { get; init; }
}

public interface IModuleProvider
{
    ModuleId Id { get; }
    bool WantsDetails { get; set; }
    bool WantsSparkline { get; set; }
    ModuleSnapshot Build(SampleFrame frame);
}
