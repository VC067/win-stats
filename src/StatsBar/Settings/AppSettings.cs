namespace StatsBar.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

public enum StripItemStyle
{
    Mini,
    ValueOnly,
    LineChart,
    Bar,
    Ring,
    Speed
}

public enum StripDensity
{
    Minimal,
    Balanced,
    Detailed
}

public enum StripSpacing
{
    Compact,
    Comfortable
}

public enum StripArrangement
{
    Beside,
    Above
}

public enum ModuleId
{
    Cpu,
    Ram,
    Gpu,
    Disk,
    Network,
    Sensors
}

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}

public enum SensorTemperatureSource
{
    Gpu,
    Cpu
}

public sealed class ModuleSetting
{
    public ModuleId Id { get; set; }

    public bool Visible { get; set; } = true;

    public StripItemStyle Style { get; set; } = StripItemStyle.Mini;
}

public sealed class AppSettings
{
    public StripAnchor Anchor { get; set; } = StripAnchor.Left;

    public int NudgePixels { get; set; }

    public int MonitorIndex { get; set; }

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public bool CpuTemperatureEnabled { get; set; }

    public SensorTemperatureSource SensorTemperatureSource { get; set; } = SensorTemperatureSource.Gpu;

    public bool PublicIp { get; set; }

    public bool StartWithWindows { get; set; }

    public StripDensity Density { get; set; } = StripDensity.Minimal;

    public bool ShowLabels { get; set; } = true;

    public bool ThresholdColors { get; set; } = true;

    public StripSpacing Spacing { get; set; } = StripSpacing.Compact;

    public StripArrangement Arrangement { get; set; } = StripArrangement.Beside;

    public bool CombinedItem { get; set; } = true;

    public List<ModuleId> CombinedModules { get; set; } = [ModuleId.Cpu, ModuleId.Ram, ModuleId.Gpu];

    public int WidthCapPixels { get; set; }

    public List<ModuleSetting> Modules { get; set; } = CreateDefaultModules();

    public static List<ModuleSetting> CreateDefaultModules()
    {
        return
        [
            new ModuleSetting { Id = ModuleId.Cpu, Visible = true, Style = StripItemStyle.Mini },
            new ModuleSetting { Id = ModuleId.Ram, Visible = true, Style = StripItemStyle.Mini },
            new ModuleSetting { Id = ModuleId.Gpu, Visible = true, Style = StripItemStyle.Mini },
            new ModuleSetting { Id = ModuleId.Disk, Visible = false, Style = StripItemStyle.Speed },
            new ModuleSetting { Id = ModuleId.Network, Visible = false, Style = StripItemStyle.Speed },
            new ModuleSetting { Id = ModuleId.Sensors, Visible = false, Style = StripItemStyle.Mini }
        ];
    }

    public void ApplyDensity(StripDensity density)
    {
        Density = density;
        ShowLabels = true;
        CombinedItem = false;
        switch (density)
        {
            case StripDensity.Balanced:
                Spacing = StripSpacing.Compact;
                SetModules(visible: true, cpu: StripItemStyle.Mini, disk: StripItemStyle.Speed, sensors: StripItemStyle.Mini);
                break;
            case StripDensity.Detailed:
                Spacing = StripSpacing.Comfortable;
                SetModules(visible: true, cpu: StripItemStyle.LineChart, disk: StripItemStyle.Speed, sensors: StripItemStyle.Mini);
                break;
            default:
                Spacing = StripSpacing.Compact;
                foreach (var module in Modules)
                {
                    module.Visible = module.Id is ModuleId.Cpu or ModuleId.Ram or ModuleId.Gpu;
                    module.Style = StripItemStyle.Mini;
                }
                break;
        }
    }

    private void SetModules(bool visible, StripItemStyle cpu, StripItemStyle disk, StripItemStyle sensors)
    {
        foreach (var module in Modules)
        {
            module.Visible = visible;
            module.Style = module.Id switch
            {
                ModuleId.Disk or ModuleId.Network => disk,
                ModuleId.Sensors => sensors,
                _ => cpu
            };
        }
    }
}
