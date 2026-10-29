using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using StatsBar.Core;
using StatsBar.Modules;
using StatsBar.Settings;
using WinStats.Sensors;
using WinStats.SensorService;
using StatsBar.Sources;

var passed = 0;
var failed = 0;
var pipeCollision = false;

void Check(string name, Action test)
{
    try
    {
        test();
        passed++;
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

async Task CheckAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        passed++;
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        if (ex is PipeCollisionException)
            pipeCollision = true;
    }
}

void Equal<T>(T expected, T actual, string? message = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException(message ?? $"Expected '{expected}', got '{actual}'.");
}

void True(bool value, string message)
{
    if (!value)
        throw new InvalidOperationException(message);
}

CpuTemperatureReading Available(double? celsius, string sensor = "CPU Package", DateTimeOffset? sampled = null) => new()
{
    Celsius = celsius,
    SensorName = sensor,
    Status = CpuTemperatureStatus.Available,
    SampledAtUtc = sampled ?? DateTimeOffset.UtcNow
};

Check("package preference beats hotter cores", () =>
{
    var selected = CpuSensorSelection.Select([("CPU Core #1", 98d), ("CPU Package", 72d)]);
    Equal("CPU Package", selected.Name);
    Equal(72d, selected.Celsius);
});
Check("AMD Tdie preference", () =>
{
    var selected = CpuSensorSelection.Select([("Core #0", 91d), ("Core (Tdie)", 67d)]);
    Equal("Core (Tdie)", selected.Name);
});
Check("hottest CPU core fallback", () =>
{
    var selected = CpuSensorSelection.Select([("CPU Core #0", 54d), ("CPU Core #1", 76d), ("GPU Core", 99d)]);
    Equal("CPU Core #1", selected.Name);
    Equal(76d, selected.Celsius);
});
Check("invalid temperatures are rejected", () =>
{
    var selected = CpuSensorSelection.Select([("CPU Package", double.NaN), ("Core (Tdie)", double.PositiveInfinity), ("Core #1", -1d), ("CPU Core #2", 151d)]);
    Equal("", selected.Name);
    Equal<double?>(null, selected.Celsius);
});
Check("unknown sensor names do not guess a CPU value", () =>
{
    var selected = CpuSensorSelection.Select([("Motherboard CPU", 82d), ("GPU Temperature", 94d), ("System", 50d)]);
    Equal("", selected.Name);
    Equal<double?>(null, selected.Celsius);
});
Check("protocol validates fresh, stale, future, and unknown status readings", () =>
{
    var now = DateTimeOffset.UtcNow;
    Equal(CpuTemperatureStatus.Available, CpuTemperatureProtocol.Validate(Available(42, sampled: now), now).Status);
    Equal(CpuTemperatureStatus.ServiceUnavailable, CpuTemperatureProtocol.Validate(Available(42, sampled: now - TimeSpan.FromSeconds(7)), now).Status);
    Equal(CpuTemperatureStatus.ServiceUnavailable, CpuTemperatureProtocol.Validate(Available(42, sampled: now + TimeSpan.FromSeconds(3)), now).Status);
    var unknown = Available(42) with { Status = (CpuTemperatureStatus)999 };
    Equal(CpuTemperatureStatus.ServiceUnavailable, CpuTemperatureProtocol.Validate(unknown, now).Status);
});
Check("null, unavailable, and invalid available readings clear Celsius", () =>
{
    var now = DateTimeOffset.UtcNow;
    Equal(CpuTemperatureStatus.ServiceUnavailable, CpuTemperatureProtocol.Validate(null, now).Status);
    var unavailable = CpuTemperatureProtocol.Validate(Available(60) with { Status = CpuTemperatureStatus.DriverMissing }, now);
    Equal<double?>(null, unavailable.Celsius);
    Equal(CpuTemperatureStatus.DriverMissing, unavailable.Status);
    var invalid = CpuTemperatureProtocol.Validate(Available(double.NaN), now);
    Equal(CpuTemperatureStatus.ServiceUnavailable, invalid.Status);
    Equal<double?>(null, invalid.Celsius);
});
Check("CPU temperature formats Celsius and Fahrenheit", () =>
{
    Equal("25°C", FormatText.Temperature(25, TemperatureUnit.Celsius));
    Equal("77°F", FormatText.Temperature(25, TemperatureUnit.Fahrenheit));
});
Check("old settings JSON retains new-property defaults", () =>
{
    var settings = JsonSerializer.Deserialize<AppSettings>("{\"Anchor\":1}")!;
    Equal(StripAnchor.Right, settings.Anchor);
    Equal(false, settings.CpuTemperatureEnabled);
    Equal(SensorTemperatureSource.Gpu, settings.SensorTemperatureSource);
});
Check("CPU detail shows temperature only when available", () =>
{
    var cpu = new CpuModule { WantsDetails = true };
    var celsius = cpu.Build(new SampleFrame { CpuTemperature = Available(25), TemperatureUnit = TemperatureUnit.Celsius });
    Equal("25°C", celsius.Details.Single(d => d.Label == "Temperature").Value);
    Equal("CPU Package", celsius.Details.Single(d => d.Label == "Temperature sensor").Value);
    var fahrenheit = cpu.Build(new SampleFrame { CpuTemperature = Available(25), TemperatureUnit = TemperatureUnit.Fahrenheit });
    Equal("77°F", fahrenheit.Details.Single(d => d.Label == "Temperature").Value);
    var disabled = cpu.Build(new SampleFrame());
    True(disabled.Details.All(d => d.Label != "Temperature"), "Disabled CPU temperature added a detail row.");
    Equal(null, disabled.Note);
    var unavailable = cpu.Build(new SampleFrame { CpuTemperature = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable) });
    True(unavailable.Details.All(d => d.Label != "Temperature"), "Unavailable CPU temperature added a detail row.");
    Equal(null, unavailable.Note);
});
Check("sensor strip shows one temperature and falls back to the available source", () =>
{
    var sensors = new SensorsModule();
    var gpu = sensors.Build(new SampleFrame { GpuTemperatureC = 48 });
    Equal("TEMP", gpu.StripLabel);
    Equal("48°C", gpu.StripValue);
    var cpu = sensors.Build(new SampleFrame
    {
        GpuTemperatureC = 48,
        CpuTemperature = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.SensorUnavailable),
        SensorTemperatureSource = SensorTemperatureSource.Cpu
    });
    Equal("GPU TEMP", cpu.StripLabel);
    Equal("48°C", cpu.StripValue);
    var cpuOnly = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(72),
        SensorTemperatureSource = SensorTemperatureSource.Cpu
    });
    Equal("TEMP", cpuOnly.StripLabel);
    Equal("72°C", cpuOnly.StripValue);
    var gpuPreferredCpuOnly = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(73),
        SensorTemperatureSource = SensorTemperatureSource.Gpu
    });
    Equal("CPU TEMP", gpuPreferredCpuOnly.StripLabel);
    Equal("73°C", gpuPreferredCpuOnly.StripValue);
    var none = sensors.Build(new SampleFrame { SensorTemperatureSource = SensorTemperatureSource.Cpu });
    Equal("TEMP", none.StripLabel);
    Equal("--", none.StripValue);
});
Check("missing CPU samples do not append zero and temperature histories stay distinct", () =>
{
    var cpu = new CpuModule { WantsSparkline = true };
    Equal(1, cpu.Build(new SampleFrame { HasCpu = true, CpuPercent = 35 }).Sparkline.Count);
    Equal(1, cpu.Build(new SampleFrame { HasCpu = false }).Sparkline.Count);
    Equal(35d, cpu.Build(new SampleFrame { HasCpu = false }).Sparkline.Single());

    var sensors = new SensorsModule { WantsSparkline = true, WantsDetails = true };
    var both = sensors.Build(new SampleFrame { GpuTemperatureC = 44, CpuTemperature = Available(71), SensorTemperatureSource = SensorTemperatureSource.Cpu });
    Equal(71d, both.Sparkline.Single());
    var sensorsNoCpu = new SensorsModule { WantsSparkline = true, WantsDetails = true };
    var noCpu = sensorsNoCpu.Build(new SampleFrame { GpuTemperatureC = 45, CpuTemperature = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.SensorUnavailable), SensorTemperatureSource = SensorTemperatureSource.Cpu });
    Equal("45°C", noCpu.StripValue);
    Equal(1, noCpu.Sparkline.Count);
    Equal(45d, noCpu.Sparkline.Single());
    Equal(1, noCpu.Chart.Single(s => s.Name == "GPU").Values.Count);
    True(noCpu.Chart.All(s => s.Name != "CPU"), "Unavailable CPU temperature added a chart series.");
    True(noCpu.Gauges.All(g => g.Label != "CPU"), "Unavailable CPU temperature added a gauge.");
    True(noCpu.Details.All(d => d.Label != "CPU temperature"), "Unavailable CPU temperature added a detail row.");
    Equal(null, noCpu.Note);
});
Check("sensors card hides unavailable readings", () =>
{
    var gpuOnly = new SensorsModule { WantsDetails = true }.Build(new SampleFrame { GpuTemperatureC = 48 });
    Equal(1, gpuOnly.Gauges.Count);
    Equal("GPU", gpuOnly.Gauges[0].Label);
    Equal(1, gpuOnly.Details.Count);
    Equal("GPU temperature", gpuOnly.Details[0].Label);
    Equal(null, gpuOnly.Note);

    var none = new SensorsModule { WantsDetails = true }.Build(new SampleFrame());
    Equal(0, none.Gauges.Count);
    Equal(0, none.Details.Count);
    Equal(0, none.Chart.Count);
    True(none.Note?.Contains("No temperature sensors", StringComparison.OrdinalIgnoreCase) == true, "Empty sensor card note was not shown.");
});
Check("sensors card tracks session temperature peaks and resets", () =>
{
    var sensors = new SensorsModule { WantsDetails = true };
    var first = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(55),
        GpuTemperatureC = 40
    });
    Equal(2, first.Peaks.Count);
    Equal("55°C", first.Peaks.Single(p => p.Label == "CPU").Value);
    Equal("40°C", first.Peaks.Single(p => p.Label == "GPU").Value);
    True(first.Peaks.All(p => p.At.Contains(':')), "Peak timestamp was not formatted.");

    var higher = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(72),
        GpuTemperatureC = 42
    });
    Equal("72°C", higher.Peaks.Single(p => p.Label == "CPU").Value);
    Equal("42°C", higher.Peaks.Single(p => p.Label == "GPU").Value);

    var lower = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(60),
        GpuTemperatureC = 38
    });
    Equal("72°C", lower.Peaks.Single(p => p.Label == "CPU").Value);
    Equal("42°C", lower.Peaks.Single(p => p.Label == "GPU").Value);

    sensors.ResetPeaks();
    var afterReset = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(50),
        GpuTemperatureC = 35
    });
    Equal("50°C", afterReset.Peaks.Single(p => p.Label == "CPU").Value);
    Equal("35°C", afterReset.Peaks.Single(p => p.Label == "GPU").Value);
});
Check("disabling CPU temperature clears only the CPU session peak", () =>
{
    var sensors = new SensorsModule { WantsDetails = true };
    var available = sensors.Build(new SampleFrame
    {
        CpuTemperature = Available(80),
        GpuTemperatureC = 50
    });
    Equal("80°C", available.Peaks.Single(p => p.Label == "CPU").Value);
    Equal("50°C", available.Peaks.Single(p => p.Label == "GPU").Value);

    var disabled = sensors.Build(new SampleFrame
    {
        CpuTemperature = CpuTemperatureReading.Disabled,
        GpuTemperatureC = 50
    });
    True(disabled.Peaks.All(p => p.Label != "CPU"), "Disabled CPU temperature kept a CPU peak.");
    Equal("50°C", disabled.Peaks.Single(p => p.Label == "GPU").Value);
});
Check("invalid or unavailable temperatures do not create session peaks", () =>
{
    var invalid = new SensorsModule { WantsDetails = true }.Build(new SampleFrame
    {
        CpuTemperature = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.SensorUnavailable),
        GpuTemperatureC = double.NaN
    });
    Equal(0, invalid.Peaks.Count);
});
Check("session peaks follow the selected temperature unit", () =>
{
    var fahrenheit = new SensorsModule { WantsDetails = true }.Build(new SampleFrame
    {
        CpuTemperature = Available(25),
        TemperatureUnit = TemperatureUnit.Fahrenheit
    });
    Equal("77°F", fahrenheit.Peaks.Single(p => p.Label == "CPU").Value);
});
Check("peak timestamps show time today and date on earlier days", () =>
{
    var now = DateTimeOffset.Now;
    var today = new DateTimeOffset(now.Year, now.Month, now.Day, 14, 5, 0, now.Offset);
    var earlier = today.AddDays(-1);
    Equal("14:05", FormatText.PeakTime(today, now));
    Equal(earlier.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture), FormatText.PeakTime(earlier, now));
});

try
{
    using var reservation = CreateServer();
}
catch (PipeCollisionException ex)
{
    pipeCollision = true;
    failed++;
    Console.Error.WriteLine($"Fake pipe tests aborted: {ex.Message}");
}

if (!pipeCollision)
{
    await CheckAsync("actual service transport delivers before disconnect", async () =>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = CreateServer();
        using var reader = new CpuTemperatureReader { Enabled = true };
        await server.WaitForConnectionAsync(timeout.Token);
        await CpuTemperaturePipe.SendAsync(server, Available(68), timeout.Token);
        await WaitForStatusAsync(reader, CpuTemperatureStatus.Available);
        Equal(68d, reader.Read().Celsius);
        True(!server.IsConnected, "Transport did not release the connection.");
    });

    await CheckAsync("actual service transport bounds a client that never reads", async () =>
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = CreateServer();
        using var client = new NamedPipeClientStream(".", CpuTemperatureProtocol.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        var connecting = client.ConnectAsync(timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        await connecting;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await CpuTemperaturePipe.SendAsync(server, Available(68), timeout.Token).WaitAsync(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Transport unexpectedly drained an unread message.");
        }
        catch (OperationCanceledException)
        {
            True(timer.Elapsed < TimeSpan.FromSeconds(3), "An unread response stalled the transport.");
        }
        True(!server.IsConnected, "Timed-out client was not disconnected.");
    });

    await CheckAsync("reader caches fresh service response", async () =>
    {
        var reading = await ReadFromFakePipeAsync(JsonSerializer.Serialize(Available(63, "CPU Package")) + "\n", CpuTemperatureStatus.Available);
        Equal(63d, reading.Celsius);
        Equal("CPU Package", reading.SensorName);
    });

    if (!pipeCollision)
    {
        await CheckAsync("reader disables immediately and ignores delayed old response after re-enable", async () =>
        {
            using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var first = CreateServer();
            using var reader = new CpuTemperatureReader { Enabled = true };
            await first.WaitForConnectionAsync(serverTimeout.Token);
            reader.Enabled = false;
            Equal(CpuTemperatureStatus.Disabled, reader.Read().Status);
            reader.Enabled = true;
            await WriteLineAsync(first, JsonSerializer.Serialize(Available(99)) + "\n");
            first.Dispose();
            using var second = CreateServer();
            await second.WaitForConnectionAsync(serverTimeout.Token);
            Equal(CpuTemperatureStatus.ServiceUnavailable, reader.Read().Status);
            await WriteLineAsync(second, JsonSerializer.Serialize(CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable)) + "\n");
            Equal(CpuTemperatureStatus.ServiceUnavailable, reader.Read().Status);
        });
    }

    if (!pipeCollision)
    {
        foreach (var (name, payload) in new[]
        {
            ("malformed JSON", "{bad}\n"),
            ("oversized message", new string('x', CpuTemperatureProtocol.MaximumMessageBytes + 4) + "\n"),
            ("EOF without newline", JsonSerializer.Serialize(Available(50))),
            ("stale timestamp", JsonSerializer.Serialize(Available(50, sampled: DateTimeOffset.UtcNow - TimeSpan.FromSeconds(9))) + "\n")
        })
        {
            await CheckAsync($"reader reports service unavailable for {name}", async () =>
            {
                var reading = await ReadFailureAfterAvailableAsync(payload);
                Equal(CpuTemperatureStatus.ServiceUnavailable, reading.Status);
            });
            if (pipeCollision)
                break;
        }
    }

    if (!pipeCollision)
    {
        await CheckAsync("reader read timeout clears cache and later response recovers", async () =>
        {
            using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var first = CreateServer();
            using var reader = new CpuTemperatureReader { Enabled = true };
            await first.WaitForConnectionAsync(serverTimeout.Token);
            await WriteLineAsync(first, JsonSerializer.Serialize(Available(52)) + "\n");
            await WaitForStatusAsync(reader, CpuTemperatureStatus.Available);

            first.Dispose();
            using var stalled = CreateServer();
            await stalled.WaitForConnectionAsync(serverTimeout.Token);
            var stalledAt = DateTime.UtcNow;
            await WaitForStatusAsync(reader, CpuTemperatureStatus.ServiceUnavailable);
            True(DateTime.UtcNow - stalledAt < TimeSpan.FromSeconds(4), "A stalled read did not time out within four seconds.");

            stalled.Dispose();
            using var recovery = CreateServer();
            await recovery.WaitForConnectionAsync(serverTimeout.Token);
            await WriteLineAsync(recovery, JsonSerializer.Serialize(Available(64)) + "\n");
            await WaitForStatusAsync(reader, CpuTemperatureStatus.Available);
            Equal(64d, reader.Read().Celsius);
        });
    }

    if (!pipeCollision)
    {
        await CheckAsync("reader disposal is prompt while service pipe is waiting", async () =>
        {
            using var serverTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var server = CreateServer();
            var reader = new CpuTemperatureReader { Enabled = true };
            await server.WaitForConnectionAsync(serverTimeout.Token);
            var started = DateTime.UtcNow;
            reader.Dispose();
            True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(500), "Reader disposal waited on the pipe.");
            Equal(CpuTemperatureStatus.Disabled, reader.Read().Status);
        });
    }

    if (!pipeCollision)
    {
        await CheckAsync("missing service returns unavailable without throwing or blocking Read", async () =>
        {
            using var reader = new CpuTemperatureReader { Enabled = true };
            var started = DateTime.UtcNow;
            var reading = reader.Read();
            True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(100), "Read blocked while service was missing.");
            Equal(CpuTemperatureStatus.ServiceUnavailable, reading.Status);
            await WaitForStatusAsync(reader, CpuTemperatureStatus.ServiceUnavailable);
        });
    }
}

Console.WriteLine($"{passed} passed, {failed} failed");
if (pipeCollision)
    Console.Error.WriteLine($"Fake pipe tests aborted: named pipe '{CpuTemperatureProtocol.PipeName}' is already bound; no connection was made to the existing service.");
return failed == 0 && !pipeCollision ? 0 : 1;

async Task WaitForStatusAsync(CpuTemperatureReader reader, CpuTemperatureStatus status)
{
    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(4);
    while (DateTime.UtcNow < deadline)
    {
        if (reader.Read().Status == status)
            return;
        await Task.Delay(50);
    }
    throw new TimeoutException($"Reader did not reach {status} within four seconds.");
}

async Task<CpuTemperatureReading> ReadFromFakePipeAsync(string payload, CpuTemperatureStatus expectedStatus)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
    using var server = CreateServer();
    using var reader = new CpuTemperatureReader { Enabled = true };
    await server.WaitForConnectionAsync(timeout.Token);
    await WriteLineAsync(server, payload);
    await WaitForStatusAsync(reader, expectedStatus);
    return reader.Read();
}

async Task<CpuTemperatureReading> ReadFailureAfterAvailableAsync(string failurePayload)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    using var first = CreateServer();
    using var reader = new CpuTemperatureReader { Enabled = true };
    await first.WaitForConnectionAsync(timeout.Token);
    await WriteLineAsync(first, JsonSerializer.Serialize(Available(50)) + "\n");
    await WaitForStatusAsync(reader, CpuTemperatureStatus.Available);
    first.Dispose();
    using var second = CreateServer();
    await second.WaitForConnectionAsync(timeout.Token);
    await WriteLineAsync(second, failurePayload);
    await WaitForStatusAsync(reader, CpuTemperatureStatus.ServiceUnavailable);
    return reader.Read();
}

NamedPipeServerStream CreateServer()
{
    try
    {
        return new NamedPipeServerStream(CpuTemperatureProtocol.PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
    }
    catch (IOException ex)
    {
        throw new PipeCollisionException($"Named pipe '{CpuTemperatureProtocol.PipeName}' is already bound; fake tests will not connect to it.", ex);
    }
}

async Task WriteLineAsync(Stream stream, string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    try
    {
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
    catch (IOException)
    {
    }
}

sealed class PipeCollisionException(string message, Exception innerException) : Exception(message, innerException);
