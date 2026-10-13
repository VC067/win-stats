using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using StatsBar.Core;
using StatsBar.Modules;
using StatsBar.Settings;
using StatsBar.Sources;
using StatsBar.Taskbar;
using StatsBar.Ui.Cards;
using StatsBar.Ui.Settings;
using StatsBar.Ui.Theme;

namespace StatsBar;

public partial class App : Application
{
    private Mutex? _mutex;
    private DispatcherTimer? _placementTimer;
    private Timer? _sampleTimer;
    private Timer? _trimTimer;
    private StripHost? _strip;
    private ModuleCardWindow? _card;
    private AppSettings _settings = new();
    private readonly MachineProbe _probe = new();
    private readonly List<IModuleProvider> _modules =
    [
        new CpuModule(),
        new RamModule(),
        new GpuModule(),
        new DiskModule(),
        new NetworkModule(),
        new SensorsModule()
    ];
    private ModuleSnapshot[] _latest = [];
    private ModuleId? _openModule;
    private int _cardAnchorLeft;
    private int _cardAnchorTop;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--probe"))
        {
            WriteProbe();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--card-shot"))
        {
            SaveCardShot();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--settings-shot"))
        {
            SaveSettingsShot();
            Shutdown();
            return;
        }

        if (e.Args.Contains("--strip-shot"))
        {
            SaveStripShots();
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, @"Local\WinStats.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load();
        _probe.TemperatureUnit = _settings.TemperatureUnit;
        if (_settings.StartWithWindows)
            StartupRegistration.Apply(true);

        if (!StripHost.TryCreate(out _strip, out var error) || _strip == null)
        {
            MessageBox.Show(
                $"Win Stats could not attach to the taskbar. Win32 error {error}.",
                "Win Stats",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _strip.CurrentAnchor = _settings.Anchor;
        _strip.ItemActivated += OpenModule;
        _strip.SettingsRequested += OpenSettings;
        _strip.QuitRequested += Shutdown;
        _strip.AnchorChosen += ChooseAnchor;
        _strip.TaskbarRecreated += () => Dispatcher.BeginInvoke(MaintainPlacement);
        Publish(BuildAll(includeProcesses: false));

        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _placementTimer.Tick += (_, _) => MaintainPlacement();
        _placementTimer.Start();

        _sampleTimer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _trimTimer = new Timer(_ => TrimWorkingSet(), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        _placementTimer?.Stop();
        _sampleTimer?.Dispose();
        _trimTimer?.Dispose();
        _probe.Dispose();
        _strip?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void Sample()
    {
        try
        {
            var snapshots = BuildAll(CardWantsProcesses());
            Dispatcher.BeginInvoke(() => Publish(snapshots));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
        }
    }

    private ModuleSnapshot[] BuildAll(bool includeProcesses)
    {
        foreach (var module in _modules)
        {
            module.WantsDetails = _openModule == module.Id;
            var setting = _settings.Modules.FirstOrDefault(item => item.Id == module.Id);
            module.WantsSparkline = module.WantsDetails
                || setting is { Visible: true, Style: StripItemStyle.LineChart };
        }
        var frame = _probe.Capture(includeProcesses, _settings.PublicIp);
        return _modules.Select(module => module.Build(frame)).ToArray();
    }

    private bool CardWantsProcesses()
    {
        return _openModule is ModuleId.Cpu or ModuleId.Ram && _card is { IsVisible: true };
    }

    private void Publish(ModuleSnapshot[] snapshots)
    {
        if (_strip == null)
            return;
        _latest = snapshots;
        var widgets = StripComposer.Compose(_settings, snapshots);
        _strip.CurrentAnchor = _settings.Anchor;
        _strip.Update(widgets, ThemeService.TaskbarIsLight(), _settings.ThresholdColors, _settings.Spacing, _settings.WidthCapPixels);
        MaintainPlacement();
        if (_card is { IsVisible: true } && _openModule is ModuleId open)
        {
            var snapshot = snapshots.FirstOrDefault(item => item.Id == open);
            if (snapshot != null)
            {
                _card.Apply(snapshot, ThemeService.UseDarkCards(_settings.Theme));
                PositionCard(open);
            }
        }
    }

    private void OpenModule(ModuleId id)
    {
        if (_strip == null)
            return;
        EnsureCard();
        if (_card == null)
            return;
        if (_card.IsVisible && _openModule == id)
        {
            _card.Hide();
            _openModule = null;
            _trimTimer?.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
            return;
        }

        _openModule = id;
        Publish(BuildAll(includeProcesses: false));
        var snapshot = _latest.FirstOrDefault(item => item.Id == id);
        if (snapshot == null)
        {
            _openModule = null;
            return;
        }
        _card.NoteOpened();
        _card.Opacity = 0;
        _card.Apply(snapshot, ThemeService.UseDarkCards(_settings.Theme));
        _card.Show();
        _card.UpdateLayout();
        PositionCard(id);
        _card.Opacity = 1;
        _card.Activate();
    }

    private void PositionCard(ModuleId id)
    {
        if (_card == null || _strip == null)
            return;
        if (!_strip.TryGetItemScreenRect(id, out var left, out var top, out var right, out var bottom))
            return;

        _card.UpdateLayout();
        var scale = VisualTreeHelper.GetDpi(_card).DpiScaleX;
        if (scale <= 0)
            scale = 1;

        var work = SystemParameters.WorkArea;
        var room = TaskbarEmbedder.IsTaskbarAtTop()
            ? work.Bottom - bottom / scale - 16
            : top / scale - work.Top - 16;
        _card.LimitHeight(Math.Max(280, room));
        _card.UpdateLayout();
        var itemWidth = (right - left) / scale;
        var cardLeft = left / scale + itemWidth / 2 - _card.ActualWidth / 2;
        if (cardLeft + _card.ActualWidth > work.Right)
            cardLeft = work.Right - _card.ActualWidth - 8;
        if (cardLeft < work.Left)
            cardLeft = work.Left + 8;

        var cardTop = TaskbarEmbedder.IsTaskbarAtTop()
            ? bottom / scale + 8
            : top / scale - _card.ActualHeight - 8;
        if (cardTop < work.Top + 8)
            cardTop = work.Top + 8;
        if (cardTop + _card.ActualHeight > work.Bottom - 8)
            cardTop = work.Bottom - _card.ActualHeight - 8;

        _card.Left = cardLeft;
        _card.Top = cardTop;
        _cardAnchorLeft = left;
        _cardAnchorTop = top;
    }

    private void ChooseAnchor(StripAnchor anchor)
    {
        _settings.Anchor = anchor;
        SettingsStore.Save(_settings);
        if (_strip != null)
            _strip.CurrentAnchor = anchor;
        MaintainPlacement();
        if (_card is { IsVisible: true } && _openModule is ModuleId id)
            PositionCard(id);
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _latest);
        window.Applied += ApplySettings;
        window.ShowDialog();
    }

    private void ApplySettings()
    {
        SettingsStore.Save(_settings);
        StartupRegistration.Apply(_settings.StartWithWindows);
        _probe.TemperatureUnit = _settings.TemperatureUnit;
        if (_strip != null)
            _strip.CurrentAnchor = _settings.Anchor;
        Publish(_latest.Length == 0 ? BuildAll(CardWantsProcesses()) : _latest);
    }

    private void MaintainPlacement()
    {
        if (_strip == null)
            return;

        if (!TaskbarEmbedder.IsAttached(_strip.Handle))
            return;

        if (TaskbarEmbedder.ShouldConcealStrip())
        {
            TaskbarEmbedder.SetStripShown(_strip.Handle, false);
            TaskbarEmbedder.SetStripLayer(_strip.Handle, false);
            if (_card is { IsVisible: true })
                _card.Hide();
            return;
        }

        TaskbarEmbedder.SetStripLayer(_strip.Handle, true);
        TaskbarEmbedder.Move(_strip.Handle, _settings.Anchor, _strip.DesiredWidth, _settings.NudgePixels);
        TaskbarEmbedder.SetStripShown(_strip.Handle, true);
        if (_card is not { IsVisible: true } || _openModule is not ModuleId id)
            return;
        if (!_strip.TryGetItemScreenRect(id, out var left, out var top, out _, out _))
            return;
        if (left == _cardAnchorLeft && top == _cardAnchorTop)
            return;
        PositionCard(id);
    }

    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_latest.Length > 0)
                Publish(_latest);
        });
    }

    private void EnsureCard()
    {
        if (_card != null)
            return;
        _card = new ModuleCardWindow();
        _card.SettingsRequested += OpenSettings;
    }

    private void TrimWorkingSet()
    {
        if (_card is { IsVisible: true })
            return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
    }

    private static void SaveStripShots()
    {
        var folder = Path.GetTempPath();
        var notes = new StringBuilder();
        foreach (var density in new[] { StripDensity.Minimal, StripDensity.Balanced, StripDensity.Detailed })
        {
            foreach (var light in new[] { false, true })
            {
                var settings = new AppSettings();
                settings.ApplyDensity(density);
                if (density == StripDensity.Detailed)
                    settings.ThresholdColors = true;
                var widgets = StripComposer.Compose(settings, []);
                var image = StripRenderer.RenderPreview(widgets, 60, 120, light, settings.ThresholdColors, settings.Spacing, settings.WidthCapPixels);
                var name = $"statsbar-strip-{density.ToString().ToLowerInvariant()}-{(light ? "light" : "dark")}.png";
                var path = Path.Combine(folder, name);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(path);
                encoder.Save(stream);
                notes.AppendLine($"{name} {image.PixelWidth}x{image.PixelHeight}");
            }
        }

        var combined = new AppSettings();
        combined.ApplyDensity(StripDensity.Balanced);
        combined.CombinedItem = true;
        var combinedWidgets = StripComposer.Compose(combined, []);
        var combinedImage = StripRenderer.RenderPreview(combinedWidgets, 60, 120, false, true, combined.Spacing, 0);
        var combinedPath = Path.Combine(folder, "statsbar-strip-combined-dark.png");
        var combinedEncoder = new PngBitmapEncoder();
        combinedEncoder.Frames.Add(BitmapFrame.Create(combinedImage));
        using (var stream = File.Create(combinedPath))
            combinedEncoder.Save(stream);
        notes.AppendLine($"combined {combinedImage.PixelWidth}x{combinedImage.PixelHeight}");
        File.WriteAllText(Path.Combine(folder, "statsbar-strip-size.txt"), notes.ToString());
    }

    private static void SaveSettingsShot()
    {
        var path = Path.Combine(Path.GetTempPath(), "statsbar-settings-render.png");
        try
        {
        var window = new SettingsWindow(new AppSettings { Theme = ThemePreference.Dark });
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(window.Width, 4000));
        content.Arrange(new Rect(0, 0, window.Width, content.DesiredSize.Height));
        content.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.Width));
        var height = Math.Max(1, (int)Math.Ceiling(content.DesiredSize.Height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "statsbar-settings-size.txt"), $"{width}x{height}");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "statsbar-settings-size.txt"), ex.ToString());
        }
    }

    private static void SaveCardShot()
    {
        var path = Path.Combine(Path.GetTempPath(), "statsbar-card-render.png");
        using var probe = new MachineProbe();
        probe.Capture(includeProcesses: true, publicIp: false);
        Thread.Sleep(300);
        var frame = probe.Capture(includeProcesses: true, publicIp: false);
        IModuleProvider[] modules = [new CpuModule(), new RamModule(), new GpuModule(), new DiskModule(), new NetworkModule(), new SensorsModule()];
        var card = new ModuleCardWindow();
        card.Apply(modules[0].Build(frame), dark: false);
        var content = (FrameworkElement)card.Content;
        content.Measure(new Size(DesignTokens.CardWidth, double.PositiveInfinity));
        content.Arrange(new Rect(0, 0, content.DesiredSize.Width, content.DesiredSize.Height));
        content.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(content.DesiredSize.Width));
        var height = Math.Max(1, (int)Math.Ceiling(content.DesiredSize.Height));
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "statsbar-card-size.txt"), $"{width}x{height}");
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void WriteProbe()
    {
        var path = Path.Combine(Path.GetTempPath(), "statsbar-probe.txt");
        try
        {
            using var probe = new MachineProbe();
            var first = probe.Capture(includeProcesses: true, publicIp: false);
            Thread.Sleep(1100);
            var frame = probe.Capture(includeProcesses: true, publicIp: false);
            var builder = new StringBuilder();
            builder.AppendLine($"cpu={frame.HasCpu} {frame.CpuPercent:0.0} user={frame.CpuUserPercent:0.0} sys={frame.CpuSystemPercent:0.0} cores={frame.CorePercents.Count} mhz={frame.FrequencyMhz:0}");
            builder.AppendLine($"ram={frame.RamPercent:0.0} used={frame.RamUsedBytes} total={frame.RamTotalBytes} standby={frame.StandbyBytes} modified={frame.ModifiedBytes} free={frame.FreeBytes} compressed={frame.HasCompressed}:{frame.CompressedBytes} page={frame.PageFilePercent:0.0}");
            builder.AppendLine($"gpu={frame.HasGpu} {frame.GpuPercent:0.0} copy={frame.GpuCopyPercent:0.0} dec={frame.GpuDecodePercent:0.0} enc={frame.GpuEncodePercent:0.0} vram={frame.HasVram} {frame.VramUsedBytes}/{frame.VramTotalBytes} temp={frame.GpuTemperatureC} fan={frame.FanPercent} clock={frame.GraphicsClockMhz} power={frame.PowerWatts} name={frame.GpuName} driver={frame.DriverVersion}");
            builder.AppendLine($"disks={frame.Disks.Count} net={frame.Network.AdapterName} down={frame.Network.DownBytesPerSecond:0} up={frame.Network.UpBytesPerSecond:0} ip={frame.Network.LocalAddress}");
            builder.AppendLine($"procCpu={frame.TopCpu.Count} procRam={frame.TopMemory.Count} firstCpu={first.HasCpu}");
            foreach (var disk in frame.Disks)
                builder.AppendLine($"disk {disk.Letter} busy={disk.BusyPercent:0.0} read={disk.ReadBytesPerSecond:0} write={disk.WriteBytesPerSecond:0} free={disk.FreeBytes}/{disk.CapacityBytes}");
            foreach (var process in frame.TopCpu)
                builder.AppendLine($"cpu {process.Name} {process.CpuPercent:0.0}");
            File.WriteAllText(path, builder.ToString());
        }
        catch (Exception ex)
        {
            File.WriteAllText(path, ex.ToString());
        }
    }

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr process);
}
