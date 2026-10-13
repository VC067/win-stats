using StatsBar.Settings;

namespace StatsBar.Core;

public static class FormatText
{
    public static string Percent(double value)
    {
        var rounded = Math.Clamp(Math.Round(value), 0, 100);
        return $"{rounded:0}%";
    }

    public static string PercentOrDash(double value, bool available)
    {
        return available ? Percent(value) : "--";
    }

    public static string Bytes(ulong bytes)
    {
        var gigabytes = bytes / 1024d / 1024d / 1024d;
        if (gigabytes >= 1)
            return $"{gigabytes:0.0} GB";

        var megabytes = bytes / 1024d / 1024d;
        return $"{megabytes:0} MB";
    }

    public static string Rate(double bytesPerSecond)
    {
        if (bytesPerSecond < 0 || double.IsNaN(bytesPerSecond))
            bytesPerSecond = 0;
        if (bytesPerSecond < 1024)
            return $"{bytesPerSecond:0} B/s";
        if (bytesPerSecond < 1024d * 1024d)
            return $"{bytesPerSecond / 1024d:0.0} KB/s";
        if (bytesPerSecond < 1024d * 1024d * 1024d)
            return $"{bytesPerSecond / (1024d * 1024d):0.0} MB/s";
        return $"{bytesPerSecond / (1024d * 1024d * 1024d):0.00} GB/s";
    }

    public static string Frequency(double mhz)
    {
        if (mhz <= 0 || double.IsNaN(mhz))
            return "--";
        if (mhz >= 1000)
            return $"{mhz / 1000d:0.00} GHz";
        return $"{mhz:0} MHz";
    }

    public static string Uptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
            uptime = TimeSpan.Zero;
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }

    public static string Temperature(double? celsius, TemperatureUnit unit)
    {
        if (celsius == null || double.IsNaN(celsius.Value))
            return "--";
        var value = unit == TemperatureUnit.Fahrenheit ? celsius.Value * 9d / 5d + 32d : celsius.Value;
        var suffix = unit == TemperatureUnit.Fahrenheit ? "F" : "C";
        return $"{Math.Round(value):0}°{suffix}";
    }

    public static string Watts(double? watts)
    {
        if (watts == null || double.IsNaN(watts.Value))
            return "--";
        return $"{watts.Value:0.0} W";
    }

    public static string OrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value;
    }
}
