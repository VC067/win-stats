using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using StatsBar.Core;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Controls;

public sealed class BarList : StackPanel
{
    public void SetBars(IReadOnlyList<BarModel> bars, Color text, Color muted, Color track)
    {
        Children.Clear();
        if (bars.Count == 0)
            return;

        foreach (var bar in bars)
        {
            var grid = new Grid
            {
                Height = 16,
                Margin = new Thickness(0, 0, 0, 4)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });

            var label = new TextBlock
            {
                Text = bar.Label,
                FontSize = 11,
                Foreground = new SolidColorBrush(muted),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            var barHost = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 6 };
            var trackRect = new Border
            {
                Background = new SolidColorBrush(track),
                Height = 6,
                CornerRadius = new CornerRadius(3)
            };
            var fillWidth = Math.Clamp(bar.Percent, 0, 100) / 100.0;
            var fill = new Border
            {
                Background = new SolidColorBrush(DesignTokens.ForKey(bar.ColorKey)),
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = double.NaN,
                CornerRadius = new CornerRadius(3)
            };
            barHost.Loaded += (_, _) =>
            {
                fill.Width = Math.Max(0, barHost.ActualWidth * fillWidth);
            };
            barHost.SizeChanged += (_, _) =>
            {
                fill.Width = Math.Max(0, barHost.ActualWidth * fillWidth);
            };
            barHost.Children.Add(trackRect);
            barHost.Children.Add(fill);
            Grid.SetColumn(barHost, 1);
            grid.Children.Add(barHost);

            var value = new TextBlock
            {
                Text = bar.Value,
                FontSize = 11,
                Foreground = new SolidColorBrush(text),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetColumn(value, 2);
            grid.Children.Add(value);

            Children.Add(grid);
        }
    }
}
