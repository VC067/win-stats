using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StatsBar.Core;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Controls;

public sealed class ProcessList : StackPanel
{
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public void SetProcesses(IReadOnlyList<ProcessModel> rows, string valueHeader, ThemePalette palette)
    {
        Children.Clear();
        if (rows.Count == 0)
            return;

        var header = new Grid { Height = 20 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var headerLeft = new TextBlock
        {
            Text = "Name",
            FontSize = DesignTokens.HeaderSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Muted),
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerRight = new TextBlock
        {
            Text = valueHeader,
            FontSize = DesignTokens.HeaderSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Muted),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(headerLeft, 0);
        Grid.SetColumn(headerRight, 1);
        header.Children.Add(headerLeft);
        header.Children.Add(headerRight);
        Children.Add(header);

        foreach (var row in rows.Take(5))
        {
            var grid = new Grid { Height = 28 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(row.ImagePath))
            {
                var icon = GetIcon(row.ImagePath);
                if (icon != null)
                {
                    left.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
            }

            var name = new TextBlock
            {
                Text = row.Name,
                FontSize = 13,
                Foreground = new SolidColorBrush(palette.Text),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            left.Children.Add(name);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            var value = new TextBlock
            {
                Text = row.Value,
                FontSize = 13,
                Foreground = new SolidColorBrush(palette.Text),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            Grid.SetColumn(value, 1);
            grid.Children.Add(value);

            Children.Add(grid);
        }
    }

    private static ImageSource? GetIcon(string path)
    {
        if (IconCache.TryGetValue(path, out var cached))
            return cached;

        ImageSource? result = null;
        var info = new ShFileInfo();
        var flags = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<ShFileInfo>(), 0x100 | 0x1);
        if (flags != 0 && info.IconHandle != 0)
        {
            try
            {
                var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    info.IconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(16, 16));
                bitmap.Freeze();
                result = bitmap;
            }
            catch
            {
                result = null;
            }
            DestroyIcon(info.IconHandle);
        }

        IconCache[path] = result;
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint IconHandle;
        public int Icon;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);
}
