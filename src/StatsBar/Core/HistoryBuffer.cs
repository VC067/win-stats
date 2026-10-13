namespace StatsBar.Core;

public sealed class HistoryBuffer
{
    public const int Capacity = 60;
    private readonly double[] _values = new double[Capacity];
    private int _next;
    private int _count;

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            value = 0;
        _values[_next] = value;
        _next = (_next + 1) % Capacity;
        if (_count < Capacity)
            _count++;
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        var start = _count == Capacity ? _next : 0;
        for (var i = 0; i < _count; i++)
            result[i] = _values[(start + i) % Capacity];
        return result;
    }
}
