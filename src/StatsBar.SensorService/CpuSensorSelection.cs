using WinStats.Sensors;

namespace WinStats.SensorService;

public static class CpuSensorSelection
{
    public static (string Name, double? Celsius) Select(IEnumerable<(string Name, double? Celsius)> sensors)
    {
        var available = sensors.Where(s => CpuTemperatureProtocol.IsValidTemperature(s.Celsius)).ToArray();
        string[] preferred = ["CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "CPU (Tctl/Tdie)", "CPU Die (average)", "Core Max"];
        foreach (var name in preferred)
        {
            var matches = available.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 0)
                return matches.MaxBy(s => s.Celsius);
        }

        var cores = available.Where(s => s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase)
            || s.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)).ToArray();
        return cores.Length > 0 ? cores.MaxBy(s => s.Celsius) : ("", null);
    }
}
