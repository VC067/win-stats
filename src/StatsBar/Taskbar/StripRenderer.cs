using System.Runtime.InteropServices;
using StatsBar.Core;
using StatsBar.Settings;
using StatsBar.Ui.Theme;

namespace StatsBar.Taskbar;

public sealed class StripWidget
{
    public ModuleId Id { get; init; }
    public string Label { get; init; } = "";
    public string Value { get; init; } = "--";
    public string SecondaryValue { get; init; } = "";
    public double Level { get; init; }
    public double[] Series { get; init; } = [];
    public StripItemStyle Style { get; set; }
    public bool ShowLabel { get; set; }
    public bool Stacked { get; set; }
    public StripPart[] Parts { get; init; } = [];
}

public readonly record struct StripPart(ModuleId Id, string Label, string Value, double Level);

public sealed class PlacedWidget
{
    public required StripWidget Widget { get; init; }
    public int X { get; init; }
    public int Width { get; init; }
    public PlacedPart[] Parts { get; init; } = [];
}

public readonly record struct PlacedPart(ModuleId Id, int X, int Width);

public readonly record struct StripLayoutResult(PlacedWidget[] Widgets, int Width);

public static class StripComposer
{
    public static List<StripWidget> Compose(AppSettings settings, IReadOnlyList<ModuleSnapshot> snapshots)
    {
        var widgets = new List<StripWidget>();
        foreach (var setting in settings.Modules)
        {
            if (!setting.Visible)
                continue;
            widgets.Add(Single(setting, SnapshotFor(snapshots, setting.Id), settings.Arrangement == StripArrangement.Above));
        }

        if (widgets.Count == 0)
        {
            widgets.Add(new StripWidget
            {
                Id = ModuleId.Cpu,
                Label = "STATS",
                Value = "--",
                Style = StripItemStyle.Mini,
                ShowLabel = true,
                Stacked = false
            });
        }

        return widgets;
    }

    private static StripWidget Single(ModuleSetting setting, ModuleSnapshot snapshot, bool stacked)
    {
        var style = setting.Style == StripItemStyle.ValueOnly ? StripItemStyle.Mini : setting.Style;
        var speed = style == StripItemStyle.Speed
            && (!string.IsNullOrWhiteSpace(snapshot.SpeedPrimary) || !string.IsNullOrWhiteSpace(snapshot.SpeedSecondary));
        return new StripWidget
        {
            Id = snapshot.Id,
            Label = snapshot.StripLabel,
            Value = speed ? Shown(snapshot.SpeedPrimary) : Shown(snapshot.StripValue),
            SecondaryValue = speed ? Shown(snapshot.SpeedSecondary) : "",
            Level = snapshot.StripLevel,
            Series = snapshot.Sparkline.ToArray(),
            Style = style,
            ShowLabel = true,
            Stacked = stacked
        };
    }

    private static ModuleSnapshot SnapshotFor(IReadOnlyList<ModuleSnapshot> snapshots, ModuleId id)
    {
        var found = snapshots.FirstOrDefault(item => item.Id == id);
        return found ?? Placeholder(id);
    }

    private static string Shown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "--" : value;
    }

    private static ModuleSnapshot Placeholder(ModuleId id)
    {
        var series = Enumerable.Range(0, 60).Select(index => 20 + 15 * Math.Sin(index / 8d)).ToArray();
        return id switch
        {
            ModuleId.Ram => Sample(id, "RAM", "42%", 0.42, series),
            ModuleId.Gpu => Sample(id, "GPU", "7%", 0.07, series),
            ModuleId.Disk => Sample(id, "DISK", "4%", 0.04, series, "1.2 MB/s", "80 KB/s"),
            ModuleId.Network => Sample(id, "NET", "1.2 MB/s", 0.12, series, "1.2 MB/s", "80 KB/s"),
            ModuleId.Sensors => Sample(id, "TEMP", "41°", 0.41, series),
            _ => Sample(id, "CPU", "18%", 0.18, series)
        };
    }

    private static ModuleSnapshot Sample(ModuleId id, string label, string value, double level, double[] series, string primary = "", string secondary = "")
    {
        return new ModuleSnapshot
        {
            Id = id,
            Title = label,
            StripLabel = label,
            StripValue = value,
            StripLevel = level,
            SpeedPrimary = primary,
            SpeedSecondary = secondary,
            Sparkline = series
        };
    }
}

public static class StripRenderer
{
    private const int DtCenter = 0x0001;
    private const int DtVCenter = 0x0004;
    private const int DtSingleLine = 0x0020;
    private const int DtCalcRect = 0x0400;
    private const int DtLeft = 0x0000;
    private const int Transparent = 1;

    public static StripLayoutResult Layout(IReadOnlyList<StripWidget> widgets, int height, uint dpi, StripSpacing spacing, int widthCapPixels)
    {
        if (dpi == 0)
            dpi = 96;
        if (height < 8)
            height = 48;
        _ = widthCapPixels;

        return Measure(widgets.Select(Clone).ToList(), height, dpi, spacing);
    }

    public static void Draw(IntPtr dc, IntPtr bits, int width, int height, PlacedWidget[] placed, int hoverIndex, bool lightTaskbar, bool thresholdColors, uint dpi, StripSpacing spacing)
    {
        if (dpi == 0)
            dpi = 96;
        if (width < 2 || height < 2 || placed.Length == 0)
            return;

        SetBkMode(dc, Transparent);
        var fonts = Fonts.Create(dpi, height);
        try
        {
            for (var index = 0; index < placed.Length; index++)
                DrawWidget(dc, bits, width, height, placed[index], fonts, lightTaskbar, thresholdColors, dpi, spacing);

            if (hoverIndex >= 0 && hoverIndex < placed.Length)
                PaintHover(bits, width, height, placed[hoverIndex], lightTaskbar);
        }
        finally
        {
            fonts.Dispose();
        }
    }

    public static void ApplyHitMask(IntPtr bits, int width, int height)
    {
        var count = width * height;
        for (var i = 0; i < count; i++)
        {
            var pixel = Marshal.ReadInt32(bits, i * 4);
            if ((pixel >> 24) != 0)
                continue;
            Marshal.WriteInt32(bits, i * 4, 1 << 24);
        }
    }

    public static System.Windows.Media.Imaging.BitmapSource RenderPreview(IReadOnlyList<StripWidget> widgets, int height, uint dpi, bool lightTaskbar, bool thresholdColors, StripSpacing spacing, int widthCapPixels)
    {
        var layout = Layout(widgets, height, dpi, spacing, widthCapPixels);
        var width = Math.Max(1, layout.Width);
        var pixels = RenderPixels(layout.Widgets, width, height, dpi, lightTaskbar, thresholdColors, spacing);
        CompositeBackdrop(pixels, lightTaskbar);

        var source = System.Windows.Media.Imaging.BitmapSource.Create(
            width,
            height,
            dpi,
            dpi,
            System.Windows.Media.PixelFormats.Pbgra32,
            null,
            pixels,
            width * 4);
        source.Freeze();
        return source;
    }

    private static StripLayoutResult Measure(List<StripWidget> widgets, int height, uint dpi, StripSpacing spacing)
    {
        var metrics = MetricsFor(height, dpi, spacing);
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var fonts = Fonts.Create(dpi, height);
        try
        {
            var placed = new PlacedWidget[widgets.Count];
            var x = metrics.Pad;
            for (var index = 0; index < widgets.Count; index++)
            {
                var widget = widgets[index];
                widget.ShowLabel = true;
                var (width, parts) = MeasureWidget(memory, fonts, widget, metrics, x, height);
                placed[index] = new PlacedWidget
                {
                    Widget = widget,
                    X = x,
                    Width = width,
                    Parts = parts
                };
                x += width + metrics.Gap;
            }

            var total = widgets.Count == 0 ? metrics.Pad * 2 : x - metrics.Gap + metrics.Pad;
            return new StripLayoutResult(placed, Math.Max(metrics.Pad * 2, total));
        }
        finally
        {
            fonts.Dispose();
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private static (int Width, PlacedPart[] Parts) MeasureWidget(IntPtr dc, Fonts fonts, StripWidget widget, Metrics metrics, int origin, int height)
    {
        if (widget.Parts.Length > 0)
        {
            var cursor = origin;
            var parts = new PlacedPart[widget.Parts.Length];
            var separator = widget.Stacked ? metrics.Gap : TextWidth(dc, fonts.Value, " · ");
            for (var index = 0; index < widget.Parts.Length; index++)
            {
                var part = widget.Parts[index];
                var partLabel = TextWidth(dc, fonts.Label, part.Label);
                var partValue = TextWidth(dc, fonts.Value, part.Value);
                var segmentWidth = widget.Stacked
                    ? Math.Max(partLabel, partValue)
                    : partLabel + metrics.Inner + partValue;
                parts[index] = new PlacedPart(part.Id, cursor, segmentWidth);
                cursor += segmentWidth;
                if (index < widget.Parts.Length - 1)
                    cursor += separator;
            }

            return (cursor - origin, parts);
        }

        var valueWidth = TextWidth(dc, fonts.Value, widget.Value);
        var labelWidth = TextWidth(dc, fonts.Label, widget.Label);
        var textWidth = widget.Stacked
            ? Math.Max(valueWidth, labelWidth)
            : labelWidth + metrics.Inner + valueWidth;
        var width = widget.Style switch
        {
            StripItemStyle.LineChart => textWidth + metrics.Inner + metrics.ChartWidth,
            StripItemStyle.Bar => textWidth + metrics.Inner + metrics.BarWidth,
            StripItemStyle.Ring => textWidth + metrics.Inner + metrics.Ring,
            StripItemStyle.Speed => SpeedWidth(dc, fonts, widget, metrics),
            _ => textWidth
        };
        return (width, []);
    }

    private static int SpeedWidth(IntPtr dc, Fonts fonts, StripWidget widget, Metrics metrics)
    {
        var text = Math.Max(TextWidth(dc, fonts.Value, widget.Value), TextWidth(dc, fonts.Value, widget.SecondaryValue));
        var lines = metrics.Dot + metrics.Inner + text;
        var label = TextWidth(dc, fonts.Label, widget.Label);
        return widget.Stacked ? Math.Max(label, lines) : label + metrics.Inner + lines;
    }

    private static byte[] RenderPixels(PlacedWidget[] placed, int width, int height, uint dpi, bool lightTaskbar, bool thresholdColors, StripSpacing spacing)
    {
        var pixels = new byte[width * height * 4];
        var screen = GetDC(IntPtr.Zero);
        var info = new BitmapInfo();
        info.Header.Size = Marshal.SizeOf<BitmapInfoHeader>();
        info.Header.Width = width;
        info.Header.Height = -height;
        info.Header.Planes = 1;
        info.Header.BitCount = 32;
        var dib = CreateDIBSection(screen, ref info, 0, out var bits, IntPtr.Zero, 0);
        var memory = CreateCompatibleDC(screen);
        var previous = SelectObject(memory, dib);
        try
        {
            Draw(memory, bits, width, height, placed, -1, lightTaskbar, thresholdColors, dpi, spacing);
            Marshal.Copy(bits, pixels, 0, pixels.Length);
        }
        finally
        {
            SelectObject(memory, previous);
            DeleteDC(memory);
            DeleteObject(dib);
            ReleaseDC(IntPtr.Zero, screen);
        }

        return pixels;
    }

    private static void DrawWidget(IntPtr dc, IntPtr bits, int width, int height, PlacedWidget placed, Fonts fonts, bool lightTaskbar, bool thresholdColors, uint dpi, StripSpacing spacing)
    {
        var widget = placed.Widget;
        var metrics = MetricsFor(height, dpi, spacing);
        if (widget.Parts.Length > 0)
        {
            DrawCombined(dc, bits, width, height, placed, fonts, lightTaskbar, thresholdColors);
            return;
        }

        var labelInk = LabelInk(lightTaskbar);
        var valueInk = widget.Style == StripItemStyle.Speed
            ? NeutralInk(lightTaskbar)
            : ValueInk(widget.Level, thresholdColors, lightTaskbar);
        var top = Math.Max(0, (height - metrics.LabelHeight - metrics.ValueHeight) / 2);
        var textRight = placed.X + placed.Width;
        if (widget.Style == StripItemStyle.LineChart)
            textRight -= metrics.ChartWidth + metrics.Inner;
        else if (widget.Style == StripItemStyle.Bar)
            textRight -= metrics.BarWidth + metrics.Inner;
        else if (widget.Style == StripItemStyle.Ring)
            textRight -= metrics.Ring + metrics.Inner;

        if (widget.Style == StripItemStyle.Speed)
        {
            DrawSpeed(dc, bits, width, height, placed, fonts, lightTaskbar, metrics);
            return;
        }

        if (widget.Stacked)
        {
            var labelRect = Rect(placed.X, top, textRight, top + metrics.LabelHeight);
            DrawInk(dc, bits, width, height, fonts.Label, widget.Label, labelRect, DtCenter | DtVCenter | DtSingleLine, labelInk);
            var valueRect = Rect(placed.X, top + metrics.LabelHeight, textRight, top + metrics.LabelHeight + metrics.ValueHeight);
            DrawInk(dc, bits, width, height, fonts.Value, widget.Value, valueRect, DtCenter | DtVCenter | DtSingleLine, valueInk);
        }
        else
        {
            var labelWidth = TextWidth(dc, fonts.Label, widget.Label);
            DrawInk(dc, bits, width, height, fonts.Label, widget.Label, Rect(placed.X, 0, placed.X + labelWidth, height), DtLeft | DtVCenter | DtSingleLine, labelInk);
            DrawInk(dc, bits, width, height, fonts.Value, widget.Value, Rect(placed.X + labelWidth + metrics.Inner, 0, textRight, height), DtLeft | DtVCenter | DtSingleLine, valueInk);
        }

        var accent = ModuleColor(widget.Id);
        if (widget.Style == StripItemStyle.LineChart)
            DrawChart(bits, width, height, textRight + metrics.Inner, placed.X + placed.Width, widget.Series, accent);
        else if (widget.Style == StripItemStyle.Bar)
            DrawBar(bits, width, height, textRight + metrics.Inner, metrics.BarWidth, widget.Level, accent);
        else if (widget.Style == StripItemStyle.Ring)
            DrawRing(bits, width, height, textRight + metrics.Inner, metrics.Ring, widget.Level, accent);
    }

    private static void DrawCombined(IntPtr dc, IntPtr bits, int width, int height, PlacedWidget placed, Fonts fonts, bool lightTaskbar, bool thresholdColors)
    {
        var labelInk = LabelInk(lightTaskbar);
        for (var index = 0; index < placed.Parts.Length; index++)
        {
            var part = placed.Widget.Parts[index];
            var box = placed.Parts[index];
            var labelWidth = TextWidth(dc, fonts.Label, part.Label);
            if (placed.Widget.Stacked)
            {
                var top = Math.Max(0, (height - 30) / 2);
                DrawInk(dc, bits, width, height, fonts.Label, part.Label, Rect(box.X, top, box.X + box.Width, top + 14), DtCenter | DtVCenter | DtSingleLine, labelInk);
                DrawInk(dc, bits, width, height, fonts.Value, part.Value, Rect(box.X, top + 14, box.X + box.Width, top + 32), DtCenter | DtVCenter | DtSingleLine, ValueInk(part.Level, thresholdColors, lightTaskbar));
                continue;
            }

            var labelRect = Rect(box.X, 0, box.X + labelWidth, height);
            DrawInk(dc, bits, width, height, fonts.Label, part.Label, labelRect, DtLeft | DtVCenter | DtSingleLine, labelInk);
            var valueRect = Rect(box.X + labelWidth + 4, 0, box.X + box.Width, height);
            DrawInk(dc, bits, width, height, fonts.Value, part.Value, valueRect, DtLeft | DtVCenter | DtSingleLine, ValueInk(part.Level, thresholdColors, lightTaskbar));
            if (index >= placed.Parts.Length - 1)
                continue;
            var separatorRect = Rect(box.X + box.Width, 0, placed.Parts[index + 1].X, height);
            DrawInk(dc, bits, width, height, fonts.Value, "·", separatorRect, DtCenter | DtVCenter | DtSingleLine, labelInk);
        }
    }

    private static void DrawSpeed(IntPtr dc, IntPtr bits, int width, int height, PlacedWidget placed, Fonts fonts, bool lightTaskbar, Metrics metrics)
    {
        var widget = placed.Widget;
        var ink = NeutralInk(lightTaskbar);
        var textX = placed.X;
        var labelWidth = TextWidth(dc, fonts.Label, widget.Label);
        var labelBand = widget.Stacked ? Math.Max(14, height / 3) : 0;
        if (widget.Stacked)
            DrawInk(dc, bits, width, height, fonts.Label, widget.Label, Rect(placed.X, 1, placed.X + placed.Width, labelBand), DtCenter | DtVCenter | DtSingleLine, LabelInk(lightTaskbar));
        else
        {
            DrawInk(dc, bits, width, height, fonts.Label, widget.Label, Rect(placed.X, 0, placed.X + labelWidth, height), DtLeft | DtVCenter | DtSingleLine, LabelInk(lightTaskbar));
            textX += labelWidth + metrics.Inner;
        }

        var rateArea = widget.Stacked ? height - labelBand : height;
        var lineHeight = Math.Max(10, rateArea / 2 - 2);
        var top = widget.Stacked ? labelBand : Math.Max(1, (height - lineHeight * 2) / 2);
        var primary = ModuleColor(widget.Id == ModuleId.Disk ? ModuleId.Disk : ModuleId.Network);
        var secondaryColor = ((byte)0xFF, (byte)0x9F, (byte)0x0A);
        Disc(bits, width, height, textX + metrics.Dot / 2, top + lineHeight / 2, metrics.Dot / 2, primary, 230);
        Disc(bits, width, height, textX + metrics.Dot / 2, top + lineHeight + lineHeight / 2, metrics.Dot / 2, secondaryColor, 230);
        var textLeft = textX + metrics.Dot + metrics.Inner;
        DrawInk(dc, bits, width, height, fonts.Value, widget.Value, Rect(textLeft, top, placed.X + placed.Width, top + lineHeight), DtLeft | DtVCenter | DtSingleLine, ink);
        if (!string.IsNullOrWhiteSpace(widget.SecondaryValue))
            DrawInk(dc, bits, width, height, fonts.Value, widget.SecondaryValue, Rect(textLeft, top + lineHeight, placed.X + placed.Width, top + lineHeight * 2), DtLeft | DtVCenter | DtSingleLine, ink);
    }

    private static void DrawChart(IntPtr bits, int width, int height, int left, int right, double[] series, (byte R, byte G, byte B) color)
    {
        var chartWidth = Math.Max(4, right - left);
        var chartHeight = Math.Max(12, height - 16);
        var top = Math.Max(4, (height - chartHeight) / 2);
        FillRound(bits, width, height, left, top, chartWidth, chartHeight, 4, color, 36);
        if (series.Length < 2)
            return;

        var scale = ChartScale(series);
        var bottom = top + chartHeight - 1;
        var points = new (int X, int Y)[chartWidth];
        for (var column = 0; column < chartWidth; column++)
        {
            var sample = series[(int)Math.Round(column * (series.Length - 1) / (double)(chartWidth - 1))];
            var y = bottom - (int)(Math.Clamp(sample, 0, scale) / scale * (chartHeight - 2));
            points[column] = (left + column, y);
            for (var py = y; py <= bottom; py++)
                Plot(bits, width, height, left + column, py, color.R, color.G, color.B, 110);
        }

        for (var column = 1; column < points.Length; column++)
            Line(bits, width, height, points[column - 1].X, points[column - 1].Y, points[column].X, points[column].Y, color.R, color.G, color.B, 230);
    }

    private static void DrawBar(IntPtr bits, int width, int height, int left, int barWidth, double level, (byte R, byte G, byte B) color)
    {
        var barHeight = Math.Max(12, height - 16);
        var top = Math.Max(4, (height - barHeight) / 2);
        FillRound(bits, width, height, left, top, barWidth, barHeight, 3, color, 48);
        var fill = (int)Math.Round(barHeight * Math.Clamp(level, 0, 1));
        if (fill <= 0)
            return;
        FillRound(bits, width, height, left, top + barHeight - fill, barWidth, fill, 3, color, 230);
    }

    private static void DrawRing(IntPtr bits, int width, int height, int left, int size, double level, (byte R, byte G, byte B) color)
    {
        var cx = left + size / 2;
        var cy = height / 2;
        var radius = Math.Max(6, size / 2 - 1);
        var thickness = Math.Max(2, size / 8);
        var sweep = Math.Clamp(level, 0, 1);
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < radius - thickness || distance > radius)
                    continue;
                var angle = Math.Atan2(dy, dx);
                var clockwise = (angle + Math.PI / 2 + Math.PI * 2) % (Math.PI * 2);
                var on = clockwise <= sweep * Math.PI * 2;
                var ink = on ? color : color;
                Plot(bits, width, height, x, y, ink.R, ink.G, ink.B, on ? (byte)230 : (byte)56);
            }
        }
    }

    private static double ChartScale(double[] series)
    {
        var max = 0d;
        foreach (var value in series)
            max = Math.Max(max, value);
        if (max <= 1.01)
            return 1;
        if (max <= 100)
            return 100;
        return Math.Max(1, max);
    }

    private static void DrawInk(IntPtr dc, IntPtr bits, int width, int height, IntPtr font, string text, NativeRect rect, int format, (byte R, byte G, byte B) ink)
    {
        if (string.IsNullOrEmpty(text) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return;
        var previous = SelectObject(dc, font);
        SetTextColor(dc, 0x00FFFFFF);
        DrawText(dc, text, -1, ref rect, format);
        SelectObject(dc, previous);
        var left = Math.Clamp(rect.Left, 0, width);
        var right = Math.Clamp(rect.Right, 0, width);
        var top = Math.Clamp(rect.Top, 0, height);
        var bottom = Math.Clamp(rect.Bottom, 0, height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var offset = (y * width + x) * 4;
                var pixel = Marshal.ReadInt32(bits, offset);
                var blue = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var red = (pixel >> 16) & 0xFF;
                if (Math.Abs(red - green) > 14 || Math.Abs(green - blue) > 14)
                    continue;
                var coverage = Math.Max(red, Math.Max(green, blue));
                if (coverage < 2)
                    continue;
                Plot(bits, width, height, x, y, ink.R, ink.G, ink.B, (byte)coverage);
            }
        }
    }

    private static int TextWidth(IntPtr dc, IntPtr font, string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var previous = SelectObject(dc, font);
        var rect = new NativeRect { Right = 1, Bottom = 1 };
        DrawText(dc, text, -1, ref rect, DtCalcRect | DtSingleLine);
        SelectObject(dc, previous);
        return Math.Max(0, rect.Right - rect.Left);
    }

    private static void PaintHover(IntPtr bits, int width, int height, PlacedWidget placed, bool lightTaskbar)
    {
        var radius = 8;
        var left = placed.X;
        var right = placed.X + placed.Width;
        var top = 3;
        var bottom = height - 3;
        var ink = lightTaskbar ? (byte)0 : (byte)255;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (!InsideRound(x - left, y - top, right - left, bottom - top, radius))
                    continue;
                Blend(bits, width, height, x, y, ink, ink, ink, 36);
            }
        }
    }

    private static void CompositeBackdrop(byte[] pixels, bool lightTaskbar)
    {
        byte red = lightTaskbar ? (byte)0xF3 : (byte)0x1C;
        byte green = lightTaskbar ? (byte)0xF3 : (byte)0x1C;
        byte blue = lightTaskbar ? (byte)0xF3 : (byte)0x1C;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var srcA = pixels[i + 3];
            if (srcA == 0)
            {
                pixels[i] = blue;
                pixels[i + 1] = green;
                pixels[i + 2] = red;
                pixels[i + 3] = 255;
                continue;
            }

            var inv = 255 - srcA;
            pixels[i] = (byte)Math.Min(255, pixels[i] + blue * inv / 255);
            pixels[i + 1] = (byte)Math.Min(255, pixels[i + 1] + green * inv / 255);
            pixels[i + 2] = (byte)Math.Min(255, pixels[i + 2] + red * inv / 255);
            pixels[i + 3] = 255;
        }
    }

    private static void FillRound(IntPtr bits, int width, int height, int left, int top, int boxWidth, int boxHeight, int radius, (byte R, byte G, byte B) color, byte alpha)
    {
        for (var y = 0; y < boxHeight; y++)
        {
            for (var x = 0; x < boxWidth; x++)
            {
                if (!InsideRound(x, y, boxWidth, boxHeight, radius))
                    continue;
                Plot(bits, width, height, left + x, top + y, color.R, color.G, color.B, alpha);
            }
        }
    }

    private static bool InsideRound(int x, int y, int boxWidth, int boxHeight, int radius)
    {
        if (boxWidth <= 0 || boxHeight <= 0)
            return false;
        var dx = x < radius ? radius - x : x >= boxWidth - radius ? x - (boxWidth - radius - 1) : 0;
        var dy = y < radius ? radius - y : y >= boxHeight - radius ? y - (boxHeight - radius - 1) : 0;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static void Disc(IntPtr bits, int width, int height, int cx, int cy, int radius, (byte R, byte G, byte B) color, byte alpha)
    {
        for (var y = -radius; y <= radius; y++)
        {
            for (var x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radius * radius)
                    continue;
                Plot(bits, width, height, cx + x, cy + y, color.R, color.G, color.B, alpha);
            }
        }
    }

    private static void Line(IntPtr bits, int width, int height, int x0, int y0, int x1, int y1, byte red, byte green, byte blue, byte alpha)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx - dy;
        while (true)
        {
            Plot(bits, width, height, x0, y0, red, green, blue, alpha);
            if (x0 == x1 && y0 == y1)
                break;
            var doubled = 2 * error;
            if (doubled > -dy)
            {
                error -= dy;
                x0 += sx;
            }

            if (doubled < dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void Blend(IntPtr bits, int width, int height, int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return;
        var offset = (y * width + x) * 4;
        var pixel = Marshal.ReadInt32(bits, offset);
        var inv = 255 - alpha;
        var outB = blue * alpha / 255 + (pixel & 0xFF) * inv / 255;
        var outG = green * alpha / 255 + ((pixel >> 8) & 0xFF) * inv / 255;
        var outR = red * alpha / 255 + ((pixel >> 16) & 0xFF) * inv / 255;
        var outA = alpha + ((pixel >> 24) & 0xFF) * inv / 255;
        Marshal.WriteInt32(bits, offset, outB | (outG << 8) | (outR << 16) | (outA << 24));
    }

    private static void Plot(IntPtr bits, int width, int height, int x, int y, byte red, byte green, byte blue, byte alpha)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return;
        var pixel = (blue * alpha / 255)
            | ((green * alpha / 255) << 8)
            | ((red * alpha / 255) << 16)
            | (alpha << 24);
        Marshal.WriteInt32(bits, (y * width + x) * 4, pixel);
    }

    private static (byte R, byte G, byte B) ModuleColor(ModuleId id)
    {
        var color = id switch
        {
            ModuleId.Ram => DesignTokens.Ram,
            ModuleId.Gpu => DesignTokens.Gpu,
            ModuleId.Disk => DesignTokens.Disk,
            ModuleId.Network => DesignTokens.NetworkDown,
            ModuleId.Sensors => DesignTokens.Sensors,
            _ => DesignTokens.Cpu
        };
        return (color.R, color.G, color.B);
    }

    private static (byte R, byte G, byte B) NeutralInk(bool lightTaskbar)
    {
        return lightTaskbar ? ((byte)0x1C, (byte)0x1C, (byte)0x1E) : ((byte)0xFF, (byte)0xFF, (byte)0xFF);
    }

    private static (byte R, byte G, byte B) LabelInk(bool lightTaskbar)
    {
        return lightTaskbar ? ((byte)0x48, (byte)0x48, (byte)0x4D) : ((byte)0xEA, (byte)0xEA, (byte)0xEE);
    }

    private static (byte R, byte G, byte B) ValueInk(double level, bool thresholdColors, bool lightTaskbar)
    {
        if (thresholdColors && level >= 0.9)
            return (0xFF, 0x45, 0x3A);
        if (thresholdColors && level >= 0.7)
            return (0xFF, 0x9F, 0x0A);
        return NeutralInk(lightTaskbar);
    }

    private static Metrics MetricsFor(int height, uint dpi, StripSpacing spacing)
    {
        var scale = dpi / 96d;
        var roomy = spacing == StripSpacing.Comfortable;
        return new Metrics(
            Math.Max(4, (int)Math.Round((roomy ? 10 : 6) * scale)),
            Math.Max(4, (int)Math.Round((roomy ? 14 : 8) * scale)),
            Math.Max(4, (int)Math.Round(4 * scale)),
            Math.Max(12, (int)Math.Round(13 * scale)),
            Math.Max(14, (int)Math.Round(16 * scale)),
            Math.Max(36, (int)Math.Round(44 * scale)),
            Math.Max(6, (int)Math.Round(8 * scale)),
            Math.Min(Math.Max(16, height - 10), Math.Max(18, (int)Math.Round(22 * scale))),
            Math.Max(4, (int)Math.Round(5 * scale)));
    }

    private static StripWidget Clone(StripWidget widget)
    {
        return new StripWidget
        {
            Id = widget.Id,
            Label = widget.Label,
            Value = widget.Value,
            SecondaryValue = widget.SecondaryValue,
            Level = widget.Level,
            Series = widget.Series,
            Style = widget.Style,
            ShowLabel = widget.ShowLabel,
            Stacked = widget.Stacked,
            Parts = widget.Parts
        };
    }

    private static NativeRect Rect(int left, int top, int right, int bottom)
    {
        return new NativeRect { Left = left, Top = top, Right = right, Bottom = bottom };
    }

    private readonly record struct Metrics(int Pad, int Gap, int Inner, int LabelHeight, int ValueHeight, int ChartWidth, int BarWidth, int Ring, int Dot);

    private sealed class Fonts : IDisposable
    {
        public IntPtr Label { get; }
        public IntPtr Value { get; }

        private Fonts(IntPtr label, IntPtr value)
        {
            Label = label;
            Value = value;
        }

        public static Fonts Create(uint dpi, int height)
        {
            var valuePt = height >= (int)Math.Round(56 * (dpi / 96d)) ? 12 : 11;
            return new Fonts(CreateFont(-MulDiv(10, (int)dpi, 72), 600), CreateFont(-MulDiv(valuePt, (int)dpi, 72), 600));
        }

        public void Dispose()
        {
            DeleteObject(Label);
            DeleteObject(Value);
        }

        private static IntPtr CreateFont(int height, int weight)
        {
            return CreateFont(height, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI");
        }

        private static int MulDiv(int number, int numerator, int denominator)
        {
            return (int)Math.Round(number * (numerator / (double)denominator));
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, int color);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr hdc, string text, int count, ref NativeRect rect, int format);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
    }
}
