using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using StatsBar.Core;
using StatsBar.Settings;
using StatsBar.Ui.Controls;
using StatsBar.Ui.Theme;

namespace StatsBar.Ui.Cards;

public sealed class ModuleCardWindow : Window
{
    public event Action? SettingsRequested;
    internal bool KeepOpenForDiagnostics { get; set; }

    private readonly StackPanel _stack;
    private readonly Border _shell;
    private readonly ScrollViewer _scroller;
    private long _openedTick;
    private bool _lastDark;
    private nint _hwnd;

    public ModuleCardWindow()
    {
        Title = "Win Stats";
        Width = DesignTokens.CardWidth;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(360, SystemParameters.WorkArea.Height - 24);
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        FontFamily = new FontFamily(DesignTokens.FontFamilyName);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = new SolidColorBrush(ThemePalette.For(false).Card);

        _stack = new StackPanel();
        _scroller = new ScrollViewer
        {
            Content = _stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        _shell = new Border
        {
            Padding = new Thickness(DesignTokens.CardPadding),
            CornerRadius = new CornerRadius(DesignTokens.CornerRadius),
            Child = _scroller
        };
        Content = _shell;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyDwm();
        };
    }

    public void LimitHeight(double dips)
    {
        MaxHeight = dips;
        _scroller.MaxHeight = Math.Max(160, dips - DesignTokens.CardPadding * 2);
    }

    public void NoteOpened()
    {
        _openedTick = Environment.TickCount64;
    }

    public void Apply(ModuleSnapshot snapshot, bool dark)
    {
        _lastDark = dark;
        var palette = ThemePalette.For(dark);
        var cardBrush = new SolidColorBrush(palette.Card);
        Background = cardBrush;
        _shell.Background = cardBrush;
        _shell.CornerRadius = new CornerRadius(0);
        _stack.Children.Clear();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var moduleColorKey = ModuleKey(snapshot.Id);
        var icon = new TextBlock
        {
            FontFamily = new FontFamily(DesignTokens.IconFontFamilyName),
            FontSize = 16,
            Text = DesignTokens.IconFor(snapshot.Id),
            Foreground = new SolidColorBrush(DesignTokens.ForKey(moduleColorKey)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var title = new TextBlock
        {
            Text = snapshot.Title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Text),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerLeft.Children.Add(icon);
        headerLeft.Children.Add(title);
        Grid.SetColumn(headerLeft, 0);
        header.Children.Add(headerLeft);

        var settingsButton = new Button
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6),
            Cursor = Cursors.Hand,
            Focusable = false,
            Template = GearTemplate(),
            Content = new TextBlock
            {
                FontFamily = new FontFamily(DesignTokens.IconFontFamilyName),
                Text = DesignTokens.IconSettings,
                FontSize = 14,
                Foreground = new SolidColorBrush(palette.Muted)
            }
        };
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        Grid.SetColumn(settingsButton, 1);
        header.Children.Add(settingsButton);
        _stack.Children.Add(header);

        if (snapshot.Gauges.Count > 0)
        {
            var hero = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, DesignTokens.SectionGap, 0, 0)
            };
            for (var i = 0; i < snapshot.Gauges.Count; i++)
            {
                var gauge = snapshot.Gauges[i];
                var accent = GaugeAccent(snapshot.Id, i);
                var ring = new RingGauge
                {
                    Percent = gauge.Percent,
                    Center = gauge.Center,
                    Label = gauge.Label,
                    Accent = accent,
                    Track = palette.Track,
                    Text = palette.Text,
                    Muted = palette.Muted,
                    Unavailable = gauge.Unavailable
                };
                hero.Children.Add(ring);
            }
            _stack.Children.Add(hero);
        }

        if (snapshot.Chart.Any(s => s.Values.Count > 0))
        {
            _stack.Children.Add(SectionHeader("History", palette));
            var chart = new AreaChart();
            chart.SetPalette(palette.Text, palette.Track, palette.Muted);
            chart.SetSeries(snapshot.Chart, snapshot.StackedChart, snapshot.ChartScaleIsPercent);
            chart.Margin = new Thickness(0, 4, 0, 0);
            _stack.Children.Add(chart);
        }

        if (snapshot.Details.Count > 0)
        {
            _stack.Children.Add(SectionHeader("Details", palette));
            var details = new DetailList();
            details.SetRows(snapshot.Details, palette);
            _stack.Children.Add(details);
        }

        if (snapshot.Bars.Count > 0)
        {
            if (!string.IsNullOrEmpty(snapshot.BarHeader))
                _stack.Children.Add(SectionHeader(snapshot.BarHeader, palette));
            var bars = new BarList();
            bars.SetBars(snapshot.Bars, palette.Text, palette.Muted, palette.Track);
            _stack.Children.Add(bars);
        }

        if (snapshot.Processes.Count > 0)
        {
            _stack.Children.Add(SectionHeader("Processes", palette));
            var processes = new ProcessList();
            processes.SetProcesses(snapshot.Processes, snapshot.ProcessValueHeader, palette);
            _stack.Children.Add(processes);
        }

        if (!string.IsNullOrEmpty(snapshot.Note))
        {
            _stack.Children.Add(new TextBlock
            {
                Text = snapshot.Note,
                FontSize = 12,
                Foreground = new SolidColorBrush(palette.Muted),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        if (_hwnd != 0)
            ApplyDwm();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.Escape)
            return;
        Hide();
        e.Handled = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!IsVisible || KeepOpenForDiagnostics)
            return;

        var openedTick = _openedTick;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || IsActive || KeepOpenForDiagnostics)
                return;
            if (Environment.TickCount64 - openedTick < 400)
                return;
            Hide();
        });
    }

    private static TextBlock SectionHeader(string text, ThemePalette palette)
    {
        return new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = DesignTokens.HeaderSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(palette.Muted),
            Margin = new Thickness(0, DesignTokens.SectionGap, 0, 0)
        };
    }

    private static string ModuleKey(ModuleId id)
    {
        return id switch
        {
            ModuleId.Ram => "Ram",
            ModuleId.Gpu => "Gpu",
            ModuleId.Disk => "Disk",
            ModuleId.Network => "NetworkDown",
            ModuleId.Sensors => "Sensors",
            _ => "Cpu"
        };
    }

    private static Color GaugeAccent(ModuleId id, int index)
    {
        if (id == ModuleId.Network)
        {
            if (index == 0)
                return DesignTokens.ForKey("NetworkDown");
            if (index == 1)
                return DesignTokens.ForKey("NetworkUp");
        }

        return DesignTokens.ForKey(ModuleKey(id));
    }

    private static ControlTemplate GearTemplate()
    {
        return (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Bd" Background="Transparent" CornerRadius="6" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="Bd" Property="Background" Value="#22FFFFFF"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Bd" Property="Background" Value="#33FFFFFF"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private void ApplyDwm()
    {
        var corner = 2;
        DwmSetWindowAttribute(_hwnd, 33, ref corner, sizeof(int));
        var backdrop = 3;
        DwmSetWindowAttribute(_hwnd, 38, ref backdrop, sizeof(int));
        var dark = _lastDark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, 20, ref dark, sizeof(int));
        var card = ThemePalette.For(_lastDark).Card;
        var border = card.R | (card.G << 8) | (card.B << 16);
        DwmSetWindowAttribute(_hwnd, 34, ref border, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int attrSize);
}
