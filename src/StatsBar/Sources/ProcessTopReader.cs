using System.IO;
using System.Runtime.InteropServices;

namespace StatsBar.Sources;

public sealed class ProcessTopReader
{
    private const uint SnapProcess = 0x00000002;
    private const uint QueryLimited = 0x1000;
    private const uint QueryInfo = 0x0400;
    private const uint VmRead = 0x0010;
    private readonly Dictionary<uint, ulong> _previousCpu = new();
    private long _previousTick;

    public IReadOnlyList<Core.ProcessReading> TopCpu { get; private set; } = [];
    public IReadOnlyList<Core.ProcessReading> TopMemory { get; private set; } = [];

    public void Sample()
    {
        var now = Environment.TickCount64;
        var elapsed = _previousTick == 0 ? 0d : (now - _previousTick) / 1000d;
        var snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == -1 || snapshot == 0)
            return;

        var seen = new HashSet<uint>();
        var rows = new List<Row>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            var moved = Process32FirstW(snapshot, ref entry);
            while (moved)
            {
                var pid = entry.ProcessId;
                if (pid > 4)
                    Consider(pid, entry.ExeFile, rows, seen);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
                moved = Process32NextW(snapshot, ref entry);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        foreach (var pid in _previousCpu.Keys.Where(pid => !seen.Contains(pid)).ToArray())
            _previousCpu.Remove(pid);

        var cores = Math.Max(1, Environment.ProcessorCount);
        var measured = new List<Core.ProcessReading>(rows.Count);
        foreach (var row in rows)
        {
            var percent = 0d;
            if (elapsed > 0.2 && _previousCpu.TryGetValue(row.Pid, out var previous) && row.CpuTime >= previous)
            {
                var delta = row.CpuTime - previous;
                percent = delta / 10_000_000d / elapsed / cores * 100d;
                if (percent < 0)
                    percent = 0;
                if (percent > 100)
                    percent = 100;
            }

            _previousCpu[row.Pid] = row.CpuTime;
            measured.Add(new Core.ProcessReading
            {
                Name = row.Name,
                ImagePath = row.ImagePath,
                CpuPercent = percent,
                WorkingSetBytes = row.WorkingSet
            });
        }

        _previousTick = now;
        TopCpu = Collapse(measured, byCpu: true);
        TopMemory = Collapse(measured, byCpu: false);
    }

    private static IReadOnlyList<Core.ProcessReading> Collapse(List<Core.ProcessReading> measured, bool byCpu)
    {
        return measured
            .GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new Core.ProcessReading
            {
                Name = group.First().Name,
                ImagePath = group.Select(row => row.ImagePath).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)) ?? "",
                CpuPercent = group.Sum(row => row.CpuPercent),
                WorkingSetBytes = group.Aggregate(0UL, (total, row) => total + row.WorkingSetBytes)
            })
            .OrderByDescending(row => byCpu ? row.CpuPercent : row.WorkingSetBytes)
            .Take(5)
            .ToArray();
    }

    private static void Consider(uint pid, string exe, List<Row> rows, HashSet<uint> seen)
    {
        var process = OpenProcess(QueryLimited | VmRead, false, pid);
        if (process == 0)
            process = OpenProcess(QueryInfo | VmRead, false, pid);
        if (process == 0)
            process = OpenProcess(QueryLimited, false, pid);
        if (process == 0)
            return;
        try
        {
            if (!GetProcessTimes(process, out _, out _, out var kernel, out var user))
                return;
            var memory = new ProcessMemory { Size = (uint)Marshal.SizeOf<ProcessMemory>() };
            if (!GetProcessMemoryInfo(process, ref memory, memory.Size))
                return;
            var path = ImagePath(process);
            var name = string.IsNullOrWhiteSpace(exe) ? Path.GetFileNameWithoutExtension(path) : Path.GetFileNameWithoutExtension(exe);
            if (string.IsNullOrWhiteSpace(name))
                name = exe;
            seen.Add(pid);
            rows.Add(new Row(pid, name, path, (ulong)(kernel + user), (ulong)memory.WorkingSetSize));
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string ImagePath(nint process)
    {
        var buffer = new char[520];
        var size = buffer.Length;
        if (!QueryFullProcessImageName(process, 0, buffer, ref size) || size <= 0)
            return "";
        return new string(buffer, 0, size);
    }

    private readonly record struct Row(uint Pid, string Name, string ImagePath, ulong CpuTime, ulong WorkingSet);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemory
    {
        public uint Size;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, int flags, char[] name, ref int size);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemory counters, uint size);
}
