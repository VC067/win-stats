namespace StatsBar.Core;

public sealed class AverageWindow
{
    private readonly double[] _values;
    private int _next;
    private int _count;

    public AverageWindow(int capacity)
    {
        _values = new double[Math.Max(1, capacity)];
    }

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            value = 0;
        _values[_next] = value;
        _next = (_next + 1) % _values.Length;
        if (_count < _values.Length)
            _count++;
    }

    public double Average(int samples)
    {
        if (_count == 0)
            return 0;
        var take = Math.Clamp(samples, 1, _count);
        var sum = 0d;
        var index = (_next - 1 + _values.Length) % _values.Length;
        for (var i = 0; i < take; i++)
        {
            sum += _values[index];
            index = (index - 1 + _values.Length) % _values.Length;
        }

        return sum / take;
    }
}
