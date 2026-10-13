using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Controls;

public sealed class RingGauge : FrameworkElement
{
    private readonly TextBlock _centerText;
    private readonly TextBlock _labelText;

    public double Percent { get; set; }
    public string Center { get; set; } = "";
    public string Label { get; set; } = "";
    public Color Accent { get; set; }
    public Color Track { get; set; }
    public Color Text { get; set; }
    public Color Muted { get; set; }
    public bool Unavailable { get; set; }

    public RingGauge()
    {
        Width = 96;
        Height = 112;
        SnapsToDevicePixels = true;

        _centerText = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.FontFamilyName),
            FontWeight = FontWeights.SemiBold,
            FontSize = 16,
            TextAlignment = TextAlignment.Center
        };
        Typography.SetNumeralAlignment(_centerText, FontNumeralAlignment.Tabular);

        _labelText = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.FontFamilyName),
            FontSize = 11,
            TextAlignment = TextAlignment.Center
        };

        AddVisualChild(_centerText);
        AddVisualChild(_labelText);
        AddLogicalChild(_centerText);
        AddLogicalChild(_labelText);
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _centerText : _labelText;

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncText();
        _centerText.Measure(availableSize);
        _labelText.Measure(availableSize);
        return new Size(96, 112);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        SyncText();
        var cx = finalSize.Width / 2;
        const double top = 4;
        const double diameter = 72;
        var cy = top + diameter / 2;
        _centerText.Arrange(new Rect(
            cx - _centerText.DesiredSize.Width / 2,
            cy - _centerText.DesiredSize.Height / 2,
            _centerText.DesiredSize.Width,
            _centerText.DesiredSize.Height));
        _labelText.Arrange(new Rect(
            cx - _labelText.DesiredSize.Width / 2,
            top + diameter + 8,
            _labelText.DesiredSize.Width,
            _labelText.DesiredSize.Height));
        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        SyncText();
        var cx = ActualWidth / 2;
        const double top = 4;
        const double diameter = 72;
        const double radius = diameter / 2;
        var cy = top + radius;

        var trackPen = new Pen(new SolidColorBrush(Track), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        trackPen.Freeze();
        dc.DrawEllipse(null, trackPen, new Point(cx, cy), radius, radius);

        if (!Unavailable && Percent > 0)
        {
            var sweep = Math.Min(Percent, 100) / 100.0 * 360.0;
            if (sweep >= 360)
            {
                var accentPen = new Pen(new SolidColorBrush(Accent), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                accentPen.Freeze();
                dc.DrawEllipse(null, accentPen, new Point(cx, cy), radius, radius);
            }
            else if (sweep > 0)
            {
                var startRadians = -Math.PI / 2;
                var endRadians = startRadians + sweep * Math.PI / 180.0;
                var startPoint = new Point(cx + radius * Math.Cos(startRadians), cy + radius * Math.Sin(startRadians));
                var endPoint = new Point(cx + radius * Math.Cos(endRadians), cy + radius * Math.Sin(endRadians));
                var arc = new StreamGeometry();
                using (var ctx = arc.Open())
                {
                    ctx.BeginFigure(startPoint, false, false);
                    ctx.ArcTo(endPoint, new Size(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
                }
                arc.Freeze();
                var accentPen = new Pen(new SolidColorBrush(Accent), 6)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                accentPen.Freeze();
                dc.DrawGeometry(null, accentPen, arc);
            }
        }
    }

    private void SyncText()
    {
        _centerText.Text = Center;
        _centerText.Foreground = new SolidColorBrush(Text);
        _labelText.Text = Label;
        _labelText.Foreground = new SolidColorBrush(Muted);
    }
}
