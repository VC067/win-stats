using System.Windows.Media;

namespace StatsBar.Ui.Theme;

public static class DesignTokens
{
    public const double CardWidth = 320;
    public const double CardPadding = 16;
    public const double SectionGap = 12;
    public const double RowHeight = 24;
    public const double CornerRadius = 16;
    public const double HeroSize = 22;
    public const double HeaderSize = 11;
    public const double RowSize = 13;
    public const string FontFamilyName = "Segoe UI Variable";
    public const string IconFontFamilyName = "Segoe Fluent Icons";

    public const string IconCpu = "\uE9F5";
    public const string IconRam = "\uE964";
    public const string IconGpu = "\uE7F4";
    public const string IconDisk = "\uEDA2";
    public const string IconNetwork = "\uE839";
    public const string IconSensors = "\uE9CA";
    public const string IconSettings = "\uE713";

    public static readonly Color Cpu = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color Ram = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color Gpu = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Disk = Color.FromRgb(0xBF, 0x5A, 0xF2);
    public static readonly Color NetworkDown = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color NetworkUp = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Sensors = Color.FromRgb(0xFF, 0xB4, 0x00);
    public static readonly Color User = Color.FromRgb(0x0A, 0x84, 0xFF);
    public static readonly Color System = Color.FromRgb(0x64, 0xD2, 0xFF);
    public static readonly Color ThresholdOk = Color.FromRgb(0x30, 0xD1, 0x58);
    public static readonly Color ThresholdWarn = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color ThresholdHot = Color.FromRgb(0xFF, 0x45, 0x3A);

    public static Color ForKey(string key)
    {
        return key switch
        {
            "Ram" => Ram,
            "Gpu" => Gpu,
            "Disk" => Disk,
            "NetworkDown" => NetworkDown,
            "NetworkUp" => NetworkUp,
            "Sensors" => Sensors,
            "System" => System,
            "Neutral" => Color.FromRgb(0x8E, 0x8E, 0x93),
            _ => Cpu
        };
    }

    public static Color Threshold(double percent)
    {
        if (percent >= 90)
            return ThresholdHot;
        if (percent >= 70)
            return ThresholdWarn;
        return ThresholdOk;
    }

    public static string IconFor(global::StatsBar.Settings.ModuleId id)
    {
        return id switch
        {
            global::StatsBar.Settings.ModuleId.Ram => IconRam,
            global::StatsBar.Settings.ModuleId.Gpu => IconGpu,
            global::StatsBar.Settings.ModuleId.Disk => IconDisk,
            global::StatsBar.Settings.ModuleId.Network => IconNetwork,
            global::StatsBar.Settings.ModuleId.Sensors => IconSensors,
            _ => IconCpu
        };
    }
}

public sealed class ThemePalette
{
    public required Color Card { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Hairline { get; init; }
    public required Color Track { get; init; }
    public required bool Dark { get; init; }

    public static ThemePalette For(bool dark)
    {
        if (dark)
        {
            return new ThemePalette
            {
                Dark = true,
                Card = Color.FromRgb(0x2C, 0x2C, 0x2E),
                Text = Color.FromRgb(0xF5, 0xF5, 0xF7),
                Muted = Color.FromRgb(0xC7, 0xC7, 0xCC),
                Hairline = Color.FromRgb(0x3A, 0x3A, 0x3C),
                Track = Color.FromRgb(0x3A, 0x3A, 0x3C)
            };
        }

        return new ThemePalette
        {
            Dark = false,
            Card = Color.FromRgb(0xFF, 0xFF, 0xFF),
            Text = Color.FromRgb(0x1C, 0x1C, 0x1E),
            Muted = Color.FromRgb(0x6E, 0x6E, 0x73),
            Hairline = Color.FromRgb(0xE5, 0xE5, 0xEA),
            Track = Color.FromRgb(0xE5, 0xE5, 0xEA)
        };
    }
}
