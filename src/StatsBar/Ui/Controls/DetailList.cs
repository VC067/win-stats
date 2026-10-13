using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using StatsBar.Core;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Controls;

public sealed class DetailList : StackPanel
{
    public void SetRows(IReadOnlyList<DetailModel> rows, ThemePalette palette)
    {
        Children.Clear();
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var grid = new Grid { Height = DesignTokens.RowHeight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(DesignTokens.ForKey(row.ColorKey)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var label = new TextBlock
            {
                Text = row.Label,
                FontSize = DesignTokens.RowSize,
                Foreground = new SolidColorBrush(palette.Muted),
                VerticalAlignment = VerticalAlignment.Center
            };
            left.Children.Add(dot);
            left.Children.Add(label);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var value = new TextBlock
            {
                Text = row.Value,
                FontSize = DesignTokens.RowSize,
                Foreground = new SolidColorBrush(palette.Text),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextAlignment = TextAlignment.Right
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            if (i < rows.Count - 1)
            {
                var line = new Rectangle
                {
                    Height = 1,
                    Fill = new SolidColorBrush(palette.Hairline),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                grid.Children.Add(line);
            }

            Children.Add(grid);
        }
    }
}
