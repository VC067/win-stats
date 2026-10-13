using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using StatsBar.Core;
using StatsBar.Settings;

namespace StatsBar.Sources;

public sealed class MachineProbe : IDisposable
{
    private readonly PdhQuery? _query;
    private readonly NvmlReader _nvml = new();
    private readonly ProcessTopReader _processes = new();
    private readonly List<CoreCounter> _cores = new();
    private readonly List<EngineCounter> _engines = new();
    private readonly List<nint> _vram = new();
    private readonly List<DiskCounter> _disks = new();
    private readonly List<NicCounter> _nics = new();
    private nint _frequency;
    private nint _standbyCore;
    private nint _standbyNormal;
    private nint _standbyReserve;
    private nint _modified;
    private nint _free;
    private nint _compressed;
    private nint _pagePercent;
    private bool _countersReady;
    private bool _hasCpuTimes;
    private ulong _idle;
    private ulong _kernel;
    private ulong _user;
    private ulong _sessionDown;
    private ulong _sessionUp;
    private long _lastTick;
    private long _publicIpTick = -120_000;
    private string? _publicIp;
    private int _publicIpBusy;

    public TemperatureUnit TemperatureUnit { get; set; } = TemperatureUnit.Celsius;

    public MachineProbe()
    {
        try
        {
            _query = new PdhQuery();
        }
        catch (InvalidOperationException)
        {
            _query = null;
        }
    }

    public SampleFrame Capture(bool includeProcesses, bool publicIp)
    {
        EnsureCounters();
        _query?.Collect();
        var cpu = ReadCpu();
        var memory = ReadMemory();
        var gpu = ReadGpu();
        var disks = ReadDisks();
        var network = ReadNetwork(publicIp);
        IReadOnlyList<ProcessReading> topCpu = [];
        IReadOnlyList<ProcessReading> topMemory = [];
        if (includeProcesses)
        {
            try
            {
                _processes.Sample();
                topCpu = _processes.TopCpu;
                topMemory = _processes.TopMemory;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        return new SampleFrame
        {
            HasCpu = cpu.HasValue,
            CpuPercent = cpu.Percent,
            CpuUserPercent = cpu.User,
            CpuSystemPercent = cpu.System,
            CpuIdlePercent = cpu.Idle,
            CorePercents = cpu.Cores,
            FrequencyMhz = Read(_frequency),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            RamTotalBytes = memory.Total,
            RamUsedBytes = memory.Used,
            RamPercent = memory.Percent,
            StandbyBytes = memory.Standby,
            ModifiedBytes = memory.Modified,
            FreeBytes = memory.Free,
            CompressedBytes = memory.Compressed,
            HasCompressed = memory.HasCompressed,
            PageFilePercent = memory.PagePercent,
            PageFileUsedBytes = memory.PageUsed,
            PageFileTotalBytes = memory.PageTotal,
            HasGpu = gpu.HasGpu,
            GpuPercent = gpu.Percent,
            GpuCopyPercent = gpu.Copy,
            GpuDecodePercent = gpu.Decode,
            GpuEncodePercent = gpu.Encode,
            VramUsedBytes = gpu.VramUsed,
            VramTotalBytes = gpu.VramTotal,
            HasVram = gpu.HasVram,
            GpuTemperatureC = gpu.Temperature,
            FanPercent = gpu.Fan,
            GraphicsClockMhz = gpu.Clock,
            PowerWatts = gpu.Power,
            GpuName = gpu.Name,
            DriverVersion = gpu.Driver,
            Disks = disks,
            Network = network,
            TopCpu = topCpu,
            TopMemory = topMemory,
            IncludesProcesses = includeProcesses,
            TemperatureUnit = TemperatureUnit
        };
    }

    public void Dispose()
    {
        _query?.Dispose();
        _nvml.Dispose();
    }

    private void EnsureCounters()
    {
        if (_countersReady || _query == null)
            return;
        _countersReady = true;
        _query.TryAdd(@"\Processor Information(_Total)\Processor Frequency", out _frequency);
        _query.TryAdd(@"\Memory\Standby Cache Core Bytes", out _standbyCore);
        _query.TryAdd(@"\Memory\Standby Cache Normal Priority Bytes", out _standbyNormal);
        _query.TryAdd(@"\Memory\Standby Cache Reserve Bytes", out _standbyReserve);
        _query.TryAdd(@"\Memory\Modified Page List Bytes", out _modified);
        _query.TryAdd(@"\Memory\Free & Zero Page List Bytes", out _free);
        if (!_query.TryAdd(@"\Memory\Compressed Bytes", out _compressed))
            _query.TryAdd(@"\Memory\Compressed Memory", out _compressed);
        _query.TryAdd(@"\Paging File(_Total)\% Usage", out _pagePercent);

        foreach (var path in PdhQuery.Expand(@"\Processor Information(*)\% Processor Time"))
        {
            var instance = InstanceName(path);
            if (instance.Contains("_Total", StringComparison.OrdinalIgnoreCase))
                continue;
            if (_query.TryAdd(path, out var counter))
                _cores.Add(new CoreCounter(instance, counter));
        }

        foreach (var path in PdhQuery.Expand(@"\GPU Engine(*)\Utilization Percentage"))
        {
            var instance = InstanceName(path);
            var kind = EngineKind(instance);
            if (kind.Length == 0 || !_query.TryAdd(path, out var counter))
                continue;
            _engines.Add(new EngineCounter(kind, counter));
        }

        foreach (var path in PdhQuery.Expand(@"\GPU Adapter Memory(*)\Dedicated Usage"))
        {
            if (_query.TryAdd(path, out var counter))
                _vram.Add(counter);
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;
            var letter = drive.Name.TrimEnd('\\');
            _query.TryAdd($@"\LogicalDisk({letter})\Disk Read Bytes/sec", out var read);
            _query.TryAdd($@"\LogicalDisk({letter})\Disk Write Bytes/sec", out var write);
            _query.TryAdd($@"\LogicalDisk({letter})\% Disk Time", out var busy);
            _query.TryAdd($@"\LogicalDisk({letter})\% Idle Time", out var idle);
            _disks.Add(new DiskCounter(letter, drive.VolumeLabel, read, write, busy, idle));
        }

        foreach (var path in PdhQuery.Expand(@"\Network Interface(*)\Bytes Received/sec"))
        {
            var name = InstanceName(path);
            if (IsIgnoredNic(name))
                continue;
            var sentPath = path.Replace("Bytes Received/sec", "Bytes Sent/sec", StringComparison.Ordinal);
            _query.TryAdd(path, out var down);
            _query.TryAdd(sentPath, out var up);
            _nics.Add(new NicCounter(name, down, up));
        }

        _query.Collect();
    }

    private CpuRead ReadCpu()
    {
        var cores = new List<double>();
        foreach (var core in _cores.OrderBy(item => item.Instance, StringComparer.Ordinal))
        {
            if (_query != null && _query.TryRead(core.Counter, out var value))
                cores.Add(Math.Clamp(value, 0, 100));
        }

        if (!GetSystemTimes(out var idleFile, out var kernelFile, out var userFile))
            return new CpuRead(false, 0, 0, 0, 0, cores);

        var idle = ToUInt64(idleFile);
        var kernel = ToUInt64(kernelFile);
        var user = ToUInt64(userFile);
        var has = false;
        var percent = 0d;
        var userPercent = 0d;
        var systemPercent = 0d;
        var idlePercent = 0d;
        if (_hasCpuTimes)
        {
            var idleDelta = idle - _idle;
            var kernelDelta = kernel - _kernel;
            var userDelta = user - _user;
            var total = kernelDelta + userDelta;
            if (total > 0)
            {
                has = true;
                var busy = total > idleDelta ? total - idleDelta : 0;
                percent = busy * 100d / total;
                userPercent = userDelta * 100d / total;
                systemPercent = (kernelDelta > idleDelta ? kernelDelta - idleDelta : 0) * 100d / total;
                idlePercent = idleDelta * 100d / total;
            }
        }

        _idle = idle;
        _kernel = kernel;
        _user = user;
        _hasCpuTimes = true;
        return new CpuRead(has, percent, userPercent, systemPercent, idlePercent, cores);
    }

    private MemoryRead ReadMemory()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(ref status))
            return new MemoryRead(0, 0, 0, 0, 0, 0, 0, false, 0, 0, 0);

        var used = status.TotalPhys > status.AvailPhys ? status.TotalPhys - status.AvailPhys : 0;
        var standby = (ulong)Math.Max(0, Read(_standbyCore)) + (ulong)Math.Max(0, Read(_standbyNormal)) + (ulong)Math.Max(0, Read(_standbyReserve));
        var modified = (ulong)Math.Max(0, Read(_modified));
        var free = (ulong)Math.Max(0, Read(_free));
        var compressedValue = 0d;
        var hasCompressed = _compressed != 0 && _query != null && _query.TryRead(_compressed, out compressedValue);
        var compressed = hasCompressed ? (ulong)Math.Max(0, compressedValue) : 0;
        var pagePercent = Math.Clamp(Read(_pagePercent), 0, 100);
        var pageTotal = status.TotalPageFile > status.TotalPhys ? status.TotalPageFile - status.TotalPhys : 0;
        var pageUsed = (ulong)(pageTotal * (pagePercent / 100d));
        return new MemoryRead(status.TotalPhys, used, status.MemoryLoad, standby, modified, free, compressed, hasCompressed, pagePercent, pageUsed, pageTotal);
    }

    private GpuRead ReadGpu()
    {
        var threeD = MaxEngine("3D");
        var copy = MaxEngine("Copy");
        var decode = MaxEngine("Decode");
        var encode = MaxEngine("Encode");
        var hasGpu = _engines.Count > 0;
        var percent = threeD ?? copy ?? decode ?? encode ?? 0;
        ulong vramUsed = 0;
        foreach (var counter in _vram)
            vramUsed += (ulong)Math.Max(0, Read(counter));
        var hasVram = _nvml.TryMemory(out var nvmlUsed, out var nvmlTotal);
        if (hasVram)
            vramUsed = nvmlUsed;
        return new GpuRead(
            hasGpu,
            percent,
            copy ?? 0,
            decode ?? 0,
            encode ?? 0,
            vramUsed,
            hasVram ? nvmlTotal : 0,
            hasVram,
            _nvml.TemperatureC(),
            _nvml.FanPercent(),
            _nvml.GraphicsClockMhz(),
            _nvml.PowerWatts(),
            _nvml.Name,
            _nvml.DriverVersion);
    }

    private double? MaxEngine(string kind)
    {
        double? max = null;
        foreach (var engine in _engines)
        {
            if (!string.Equals(engine.Kind, kind, StringComparison.Ordinal) || _query == null || !_query.TryRead(engine.Counter, out var value))
                continue;
            value = Math.Clamp(value, 0, 100);
            max = max == null ? value : Math.Max(max.Value, value);
        }

        return max;
    }

    private IReadOnlyList<DiskReading> ReadDisks()
    {
        var readings = new List<DiskReading>();
        foreach (var disk in _disks)
        {
            ulong capacity = 0;
            ulong free = 0;
            try
            {
                var info = new DriveInfo(disk.Letter + "\\");
                if (info.IsReady)
                {
                    capacity = (ulong)Math.Max(0, info.TotalSize);
                    free = (ulong)Math.Max(0, info.AvailableFreeSpace);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            readings.Add(new DiskReading
            {
                Letter = disk.Letter.TrimEnd(':'),
                VolumeLabel = disk.Label,
                ReadBytesPerSecond = Math.Max(0, Read(disk.Read)),
                WriteBytesPerSecond = Math.Max(0, Read(disk.Write)),
                BusyPercent = DiskBusy(disk),
                CapacityBytes = capacity,
                FreeBytes = free
            });
        }

        return readings;
    }

    private NetworkReading ReadNetwork(bool publicIp)
    {
        NicCounter? busiest = null;
        var best = -1d;
        var down = 0d;
        var up = 0d;
        foreach (var nic in _nics)
        {
            var nicDown = Math.Max(0, Read(nic.Down));
            var nicUp = Math.Max(0, Read(nic.Up));
            var total = nicDown + nicUp;
            if (total > best)
            {
                best = total;
                busiest = nic;
                down = nicDown;
                up = nicUp;
            }
        }

        var now = Environment.TickCount64;
        var elapsed = _lastTick == 0 ? 0d : (now - _lastTick) / 1000d;
        _lastTick = now;
        if (elapsed is > 0 and < 5)
        {
            _sessionDown += (ulong)(down * elapsed);
            _sessionUp += (ulong)(up * elapsed);
        }

        if (publicIp && now - _publicIpTick > 600_000)
            RequestPublicIp();

        return new NetworkReading
        {
            AdapterName = busiest == null ? "--" : DisplayNic(busiest.Value.Name),
            DownBytesPerSecond = down,
            UpBytesPerSecond = up,
            SessionDownBytes = _sessionDown,
            SessionUpBytes = _sessionUp,
            LocalAddress = LocalIpv4(busiest == null ? "" : DisplayNic(busiest.Value.Name)),
            PublicAddress = publicIp ? _publicIp : null
        };
    }

    private void RequestPublicIp()
    {
        if (Interlocked.CompareExchange(ref _publicIpBusy, 1, 0) != 0)
            return;
        _publicIpTick = Environment.TickCount64;
        _ = Task.Run(async () =>
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var text = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                if (text.Length is > 0 and < 64)
                    _publicIp = text;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _publicIpBusy, 0);
            }
        });
    }

    private double Read(nint counter)
    {
        return _query != null && _query.TryRead(counter, out var value) ? value : 0;
    }

    private static string InstanceName(string path)
    {
        var open = path.IndexOf('(');
        var close = path.LastIndexOf(')');
        if (open < 0 || close <= open)
            return path;
        return path.Substring(open + 1, close - open - 1);
    }

    private static string EngineKind(string instance)
    {
        if (instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
            return "3D";
        if (instance.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase))
            return "Copy";
        if (instance.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase))
            return "Decode";
        if (instance.Contains("engtype_VideoEncode", StringComparison.OrdinalIgnoreCase))
            return "Encode";
        return "";
    }

    private static bool IsIgnoredNic(string name)
    {
        return name.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Isatap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Teredo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Pseudo", StringComparison.OrdinalIgnoreCase)
            || name.Contains("WAN Miniport", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayNic(string name)
    {
        return name.Replace("[R]", "(R)", StringComparison.Ordinal).Replace("[r]", "(r)", StringComparison.Ordinal);
    }

    private double DiskBusy(DiskCounter disk)
    {
        if (disk.Idle != 0 && _query != null && _query.TryRead(disk.Idle, out var idle))
            return Math.Clamp(100d - idle, 0, 100);
        return Math.Clamp(Read(disk.Busy), 0, 100);
    }

    private static string LocalIpv4(string adapterName)
    {
        try
        {
            NetworkInterface? fallback = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || IsVirtualNic(nic))
                    continue;
                var address = FirstIpv4(nic);
                if (address == null)
                    continue;
                fallback ??= nic;
                if (adapterName.Length > 0 && NamesMatch(nic.Description, adapterName))
                    return address;
            }

            return fallback == null ? "--" : FirstIpv4(fallback) ?? "--";
        }
        catch (NetworkInformationException)
        {
            return "--";
        }
    }

    private static bool IsVirtualNic(NetworkInterface nic)
    {
        var text = nic.Description + " " + nic.Name;
        return text.Contains("vEthernet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Virtual", StringComparison.OrdinalIgnoreCase)
            || text.Contains("WSL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);
    }

    private static bool NamesMatch(string description, string adapterName)
    {
        var left = description.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
        var right = adapterName.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal);
        return left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstIpv4(NetworkInterface nic)
    {
        foreach (var address in nic.GetIPProperties().UnicastAddresses)
        {
            if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                return address.Address.ToString();
        }

        return null;
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High) << 32 | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        }
    }

    private readonly record struct CoreCounter(string Instance, nint Counter);
    private readonly record struct EngineCounter(string Kind, nint Counter);
    private readonly record struct DiskCounter(string Letter, string Label, nint Read, nint Write, nint Busy, nint Idle);
    private readonly record struct NicCounter(string Name, nint Down, nint Up);

    private readonly record struct CpuRead(bool HasValue, double Percent, double User, double System, double Idle, List<double> Cores);

    private readonly record struct MemoryRead(
        ulong Total,
        ulong Used,
        double Percent,
        ulong Standby,
        ulong Modified,
        ulong Free,
        ulong Compressed,
        bool HasCompressed,
        double PagePercent,
        ulong PageUsed,
        ulong PageTotal);

    private readonly record struct GpuRead(
        bool HasGpu,
        double Percent,
        double Copy,
        double Decode,
        double Encode,
        ulong VramUsed,
        ulong VramTotal,
        bool HasVram,
        double? Temperature,
        double? Fan,
        double? Clock,
        double? Power,
        string Name,
        string Driver);
}
