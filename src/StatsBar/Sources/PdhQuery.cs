using System.Runtime.InteropServices;

namespace StatsBar.Sources;

public sealed class PdhQuery : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint FormatDouble = 0x00000200;
    private nint _query;

    public PdhQuery()
    {
        var status = PdhOpenQueryW(null, 0, out _query);
        if (status != 0)
            throw new InvalidOperationException($"PdhOpenQuery failed: 0x{status:X8}");
    }

    public bool TryAdd(string path, out nint counter)
    {
        counter = 0;
        if (_query == 0)
            return false;
        var status = PdhAddEnglishCounterW(_query, path, 0, out counter);
        return status == 0 && counter != 0;
    }

    public void Collect()
    {
        if (_query != 0)
            PdhCollectQueryData(_query);
    }

    public bool TryRead(nint counter, out double value)
    {
        value = 0;
        if (counter == 0)
            return false;
        var status = PdhGetFormattedCounterValue(counter, FormatDouble, out _, out var formatted);
        if (status != 0 || formatted.CStatus != 0)
            return false;
        value = formatted.DoubleValue;
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static string[] Expand(string wildcard)
    {
        var chars = new char[8192];
        var size = (uint)chars.Length;
        var pinned = GCHandle.Alloc(chars, GCHandleType.Pinned);
        try
        {
            var status = PdhExpandWildCardPathW(null, wildcard, pinned.AddrOfPinnedObject(), ref size, 0);
            if (status == MoreData && size > chars.Length)
            {
                pinned.Free();
                chars = new char[size];
                pinned = GCHandle.Alloc(chars, GCHandleType.Pinned);
                status = PdhExpandWildCardPathW(null, wildcard, pinned.AddrOfPinnedObject(), ref size, 0);
            }

            if (status != 0 || size == 0)
                return [];
        }
        finally
        {
            if (pinned.IsAllocated)
                pinned.Free();
        }

        var text = new string(chars, 0, Math.Min(chars.Length, (int)size));
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public void Dispose()
    {
        if (_query == 0)
            return;
        PdhCloseQuery(_query);
        _query = 0;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, nuint userData, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nuint userData, out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhFormatted value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhExpandWildCardPathW(string? dataSource, string path, IntPtr expanded, ref uint length, uint flags);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFormatted
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }
}
