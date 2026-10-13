using System.Windows;
using System.Windows.Media;
using StatsBar.Core;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Controls;

public sealed class AreaChart : FrameworkElement
{
    private IReadOnlyList<SeriesModel> _series = [];
    private bool _stacked;
    private bool _scaleIsPercent;
    private Color _lineFallback;
    private Color _track;
    private Color _muted;

    public AreaChart()
    {
        Height = 64;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public void SetPalette(Color lineFallback, Color track, Color muted)
    {
        _lineFallback = lineFallback;
        _track = track;
        _muted = muted;
        InvalidateVisual();
    }

    public void SetSeries(IReadOnlyList<SeriesModel> series, bool stacked, bool scaleIsPercent)
    {
        _series = series;
        _stacked = stacked;
        _scaleIsPercent = scaleIsPercent;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_series.Count == 0)
            return;

        var pointCount = _series.Max(s => s.Values.Count);
        if (pointCount < 2)
            return;

        var max = _scaleIsPercent || _stacked ? 100.0 : Math.Max(1.0, _series.SelectMany(s => s.Values).DefaultIfEmpty(0).Max());

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        double Y(double value)
        {
            var t = value / max;
            return h - t * h;
        }

        double X(int index)
        {
            return index * w / (pointCount - 1);
        }

        var gridY = Y(max * 0.5);
        var gridPen = new Pen(new SolidColorBrush(_track), 1);
        gridPen.Freeze();
        dc.DrawLine(gridPen, new Point(0, gridY), new Point(w, gridY));

        if (_stacked)
        {
            var running = new double[pointCount];
            foreach (var series in _series)
            {
                var color = DesignTokens.ForKey(series.ColorKey);
                var fillColor = Color.FromArgb(0x29, color.R, color.G, color.B);
                var upperPoints = new List<Point>(pointCount);
                var lowerPoints = new List<Point>(pointCount);
                for (var i = 0; i < pointCount; i++)
                {
                    var v = i < series.Values.Count ? series.Values[i] : 0;
                    var lower = running[i];
                    var upper = lower + v;
                    running[i] = upper;
                    upperPoints.Add(new Point(X(i), Y(upper)));
                    lowerPoints.Add(new Point(X(i), Y(lower)));
                }

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(upperPoints[0], true, true);
                    for (var i = 1; i < upperPoints.Count; i++)
                        ctx.LineTo(upperPoints[i], true, false);
                    for (var i = lowerPoints.Count - 1; i >= 0; i--)
                        ctx.LineTo(lowerPoints[i], true, false);
                }
                geometry.Freeze();
                dc.DrawGeometry(new SolidColorBrush(fillColor), null, geometry);

                var strokePen = new Pen(new SolidColorBrush(color), 1.5);
                strokePen.Freeze();
                var polyline = new StreamGeometry();
                using (var ctx = polyline.Open())
                {
                    ctx.BeginFigure(upperPoints[0], false, false);
                    for (var i = 1; i < upperPoints.Count; i++)
                        ctx.LineTo(upperPoints[i], true, false);
                }
                polyline.Freeze();
                dc.DrawGeometry(null, strokePen, polyline);

                var last = upperPoints[^1];
                dc.DrawEllipse(new SolidColorBrush(color), null, last, 2, 2);
            }
        }
        else
        {
            foreach (var series in _series)
            {
                if (series.Values.Count < 2)
                    continue;

                var color = DesignTokens.ForKey(series.ColorKey);
                var count = series.Values.Count;
                var points = new List<Point>(count);
                for (var i = 0; i < count; i++)
                {
                    var x = count > 1 ? i * w / (count - 1) : 0;
                    points.Add(new Point(x, Y(series.Values[i])));
                }

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(points[0], true, true);
                    for (var i = 1; i < points.Count; i++)
                        ctx.LineTo(points[i], true, false);
                    ctx.LineTo(new Point(points[^1].X, h), true, false);
                    ctx.LineTo(new Point(points[0].X, h), true, false);
                }
                geometry.Freeze();
                var fill = new LinearGradientBrush(
                    Color.FromArgb(0x66, color.R, color.G, color.B),
                    Color.FromArgb(0x00, color.R, color.G, color.B),
                    new Point(0, 0),
                    new Point(0, 1));
                fill.Freeze();
                dc.DrawGeometry(fill, null, geometry);

                var strokePen = new Pen(new SolidColorBrush(color), 2);
                strokePen.Freeze();
                var polyline = new StreamGeometry();
                using (var ctx = polyline.Open())
                {
                    ctx.BeginFigure(points[0], false, false);
                    for (var i = 1; i < points.Count; i++)
                        ctx.LineTo(points[i], true, false);
                }
                polyline.Freeze();
                dc.DrawGeometry(null, strokePen, polyline);

                var last = points[^1];
                dc.DrawEllipse(new SolidColorBrush(color), null, last, 2, 2);
            }
        }
    }
}
