using System.IO.Pipes;
using System.Text.Json;
using WinStats.Sensors;

namespace WinStats.SensorService;

public static class CpuTemperaturePipe
{
    public static async Task SendAsync(NamedPipeServerStream pipe, CpuTemperatureReading reading, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(1));
        Task? drain = null;
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(reading);
            if (json.Length + 1 > CpuTemperatureProtocol.MaximumMessageBytes)
                throw new InvalidDataException("CPU temperature message is too large.");
            await pipe.WriteAsync(json, deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync(new byte[] { (byte)'\n' }, deadline.Token).ConfigureAwait(false);

            // FlushAsync does not wait for a named-pipe client to consume buffered bytes.
            drain = Task.Run(pipe.WaitForPipeDrain);
            await drain.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        finally
        {
            // Disconnect also releases a synchronous drain if a client stops reading.
            if (pipe.IsConnected)
                pipe.Disconnect();
            if (drain != null)
            {
                try
                {
                    await drain.ConfigureAwait(false);
                }
                catch (IOException)
                {
                }
            }
        }
    }
}
