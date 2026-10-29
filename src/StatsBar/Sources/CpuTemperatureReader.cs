using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using WinStats.Sensors;

namespace StatsBar.Sources;

public sealed class CpuTemperatureReader : IDisposable
{
    private readonly object _gate = new();
    private readonly byte[] _readBuffer = new byte[CpuTemperatureProtocol.MaximumMessageBytes];
    private CpuTemperatureReading _cached = CpuTemperatureReading.Disabled;
    private CancellationTokenSource? _pollCancellation;
    private bool _enabled;
    private bool _disposed;
    private long _generation;

    public bool Enabled
    {
        get
        {
            lock (_gate)
                return _enabled;
        }
        set
        {
            lock (_gate)
            {
                if (_disposed || _enabled == value)
                    return;

                _enabled = value;
                _generation++;
                if (!value)
                {
                    _cached = CpuTemperatureReading.Disabled;
                    if (_pollCancellation is { } pollCancellation)
                        _ = CancelAndObserveAsync(pollCancellation);
                    _pollCancellation = null;
                    return;
                }

                var cancellation = new CancellationTokenSource();
                _pollCancellation = cancellation;
                var generation = _generation;
                _cached = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable);
                _ = Task.Run(() => PollAsync(generation, cancellation));
            }
        }
    }

    public CpuTemperatureReading Read()
    {
        lock (_gate)
        {
            if (!_enabled || _disposed)
                return CpuTemperatureReading.Disabled;

            var reading = CpuTemperatureProtocol.Validate(_cached, DateTimeOffset.UtcNow);
            if (reading.Status == CpuTemperatureStatus.ServiceUnavailable && _cached.Status == CpuTemperatureStatus.Available)
                _cached = reading;
            return reading;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _enabled = false;
            _generation++;
            _cached = CpuTemperatureReading.Disabled;
            if (_pollCancellation is { } pollCancellation)
                _ = CancelAndObserveAsync(pollCancellation);
            _pollCancellation = null;
        }
    }

    private async Task PollAsync(long generation, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                CpuTemperatureReading reading;
                try
                {
                    reading = await ReadFromServiceAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or JsonException or ArgumentException or OperationCanceledException)
                {
                    reading = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable);
                }

                lock (_gate)
                {
                    if (_enabled && !_disposed && generation == _generation)
                        _cached = CpuTemperatureProtocol.Validate(reading, DateTimeOffset.UtcNow);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            SetUnavailable(generation);
        }
        finally
        {
            await CancelAndObserveAsync(cancellation).ConfigureAwait(false);
            lock (_gate)
            {
                if (ReferenceEquals(_pollCancellation, cancellation))
                    _pollCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void SetUnavailable(long generation)
    {
        lock (_gate)
        {
            if (_enabled && !_disposed && generation == _generation)
                _cached = CpuTemperatureReading.Unavailable(CpuTemperatureStatus.ServiceUnavailable);
        }
    }

    private static async Task CancelAndObserveAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private async Task<CpuTemperatureReading> ReadFromServiceAsync(CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", CpuTemperatureProtocol.PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));

        try
        {
            using var connectDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            connectDeadline.CancelAfter(TimeSpan.FromMilliseconds(300));
            await pipe.ConnectAsync(connectDeadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }

        var bytes = _readBuffer;
        var length = 0;
        while (true)
        {
            int read;
            try
            {
                read = await pipe.ReadAsync(bytes.AsMemory(length, bytes.Length - length), deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException();
            }
            if (read == 0)
                throw new ArgumentException("CPU sensor message is missing its line terminator.");

            var newline = Array.IndexOf(bytes, (byte)'\n', length, read);
            if (newline >= 0)
            {
                length = newline;
                break;
            }

            length += read;
            if (length == bytes.Length)
                throw new ArgumentException("CPU sensor message exceeds the maximum size.");
        }

        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (length > 0 && bytes[length - 1] == (byte)'\r')
            length--;

        var reading = JsonSerializer.Deserialize<CpuTemperatureReading>(bytes.AsSpan(0, length));
        return CpuTemperatureProtocol.Validate(reading, DateTimeOffset.UtcNow);
    }
}
