using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace StatsBar.Sources;

public sealed class NvmlReader : IDisposable
{
    private nint _library;
    private nint _device;
    private bool _ready;
    private InitDelegate? _shutdown;

    public bool Available => _ready;
    public string Name { get; private set; } = "";
    public string DriverVersion { get; private set; } = "";

    public NvmlReader()
    {
        var path = Path.Combine(Environment.SystemDirectory, "nvml.dll");
        if (!File.Exists(path))
            return;

        _library = LoadLibrary(path);
        if (_library == 0)
            return;

        var init = Get<InitDelegate>("nvmlInit_v2");
        _shutdown = Get<InitDelegate>("nvmlShutdown");
        var count = Get<CountDelegate>("nvmlDeviceGetCount_v2");
        var handle = Get<HandleDelegate>("nvmlDeviceGetHandleByIndex_v2");
        var name = Get<TextDelegate>("nvmlDeviceGetName");
        var driver = Get<DriverDelegate>("nvmlSystemGetDriverVersion");
        if (init == null || count == null || handle == null || name == null || driver == null || init() != 0)
            return;

        if (count(out var devices) != 0 || devices == 0 || handle(0, out _device) != 0)
            return;

        Name = ReadText(buffer => name(_device, buffer, (uint)buffer.Length));
        DriverVersion = ReadDriver(driver);
        _ready = _device != 0 && Name.Length > 0;
    }

    public double? TemperatureC()
    {
        var fn = Get<SensorDelegate>("nvmlDeviceGetTemperature");
        if (!_ready || fn == null || fn(_device, 0, out var temperature) != 0)
            return null;
        return temperature;
    }

    public double? FanPercent()
    {
        var fn = Get<UintOutDelegate>("nvmlDeviceGetFanSpeed");
        if (!_ready || fn == null || fn(_device, out var speed) != 0)
            return null;
        return speed;
    }

    public double? GraphicsClockMhz()
    {
        var fn = Get<SensorDelegate>("nvmlDeviceGetClockInfo");
        if (!_ready || fn == null || fn(_device, 0, out var mhz) != 0)
            return null;
        return mhz;
    }

    public double? PowerWatts()
    {
        var fn = Get<UintOutDelegate>("nvmlDeviceGetPowerUsage");
        if (!_ready || fn == null || fn(_device, out var milliwatts) != 0)
            return null;
        return milliwatts / 1000d;
    }

    public bool TryMemory(out ulong used, out ulong total)
    {
        used = 0;
        total = 0;
        var fn = Get<MemoryDelegate>("nvmlDeviceGetMemoryInfo");
        if (!_ready || fn == null)
            return false;
        var memory = new NvmlMemory();
        if (fn(_device, ref memory) != 0 || memory.Total == 0)
            return false;
        used = memory.Used;
        total = memory.Total;
        return true;
    }

    public void Dispose()
    {
        if (_library == 0)
            return;
        try
        {
            _shutdown?.Invoke();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        FreeLibrary(_library);
        _library = 0;
        _ready = false;
    }

    private T? Get<T>(string entry) where T : Delegate
    {
        if (_library == 0)
            return null;
        var address = GetProcAddress(_library, entry);
        if (address == 0)
            return null;
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    private static string ReadText(Func<byte[], int> read)
    {
        var buffer = new byte[96];
        if (read(buffer) != 0)
            return "";
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
            length = buffer.Length;
        return Encoding.ASCII.GetString(buffer, 0, length).Trim();
    }

    private string ReadDriver(DriverDelegate driver)
    {
        var buffer = new byte[80];
        if (driver(buffer, (uint)buffer.Length) != 0)
            return "";
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
            length = buffer.Length;
        return Encoding.ASCII.GetString(buffer, 0, length).Trim();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountDelegate(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleDelegate(uint index, out nint device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TextDelegate(nint device, byte[] name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DriverDelegate(byte[] version, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SensorDelegate(nint device, int sensor, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UintOutDelegate(nint device, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MemoryDelegate(nint device, ref NvmlMemory memory);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern nint GetProcAddress(nint module, string name);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(nint module);
}
