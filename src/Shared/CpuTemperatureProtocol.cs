namespace WinStats.Sensors;

public enum CpuTemperatureStatus
{
    Disabled,
    ServiceUnavailable,
    DriverMissing,
    SensorUnavailable,
    Available,
    Error
}

public sealed record CpuTemperatureReading
{
    public double? Celsius { get; init; }
    public string SensorName { get; init; } = "";
    public CpuTemperatureStatus Status { get; init; }
    public DateTimeOffset SampledAtUtc { get; init; }

    public static CpuTemperatureReading Disabled { get; } = new() { Status = CpuTemperatureStatus.Disabled };

    public static CpuTemperatureReading Unavailable(CpuTemperatureStatus status) => new()
    {
        Status = status,
        SampledAtUtc = DateTimeOffset.UtcNow
    };
}

public static class CpuTemperatureProtocol
{
    public const string PipeName = "WinStats.CpuTemperature.v1";
    public const string ServiceName = "WinStatsSensors";
    public const int MaximumMessageBytes = 2048;
    public static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(6);

    public static bool IsValidTemperature(double? value) => value is >= 0 and <= 150 && double.IsFinite(value.Value);

    public static CpuTemperatureReading Validate(CpuTemperatureReading? reading, DateTimeOffset now)
    {
        if (reading == null || !Enum.IsDefined(reading.Status)
            || now - reading.SampledAtUtc > MaximumAge
            || reading.SampledAtUtc - now > TimeSpan.FromSeconds(2)
            || reading.SensorName == null || reading.SensorName.Length > 128
            || (reading.Status == CpuTemperatureStatus.Available && !IsValidTemperature(reading.Celsius)))
            return CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable);

        return reading.Status == CpuTemperatureStatus.Available ? reading : reading with { Celsius = null };
    }
}
