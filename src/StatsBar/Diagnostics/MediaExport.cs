using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StatsBar.Core;
using StatsBar.Modules;
using StatsBar.Settings;
using StatsBar.Taskbar;
using StatsBar.Ui.Cards;
using StatsBar.Ui.Settings;

namespace StatsBar.Diagnostics;

internal static class MediaExport
{
    public static void Save(string directory)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        IModuleProvider[] modules = [new CpuModule(), new RamModule(), new GpuModule(), new DiskModule(), new NetworkModule(), new SensorsModule()];
        foreach (var module in modules)
        {
            module.WantsDetails = true;
            module.WantsSparkline = true;
        }
        ModuleSnapshot[] snapshots = [];
        for (var i = 0; i < 120; i++)
            snapshots = modules.Select(module => module.Build(ExampleFrame(i))).ToArray();

        foreach (var dark in new[] { false, true })
        {
            var theme = dark ? "dark" : "light";
            foreach (var snapshot in snapshots)
            {
                var card = new ModuleCardWindow();
                card.LimitHeight(2400);
                card.Apply(snapshot, dark);
                Render((FrameworkElement)card.Content, 320, Path.Combine(directory, $"{snapshot.Id.ToString().ToLowerInvariant()}-{theme}.png"));
                card.Close();
            }
            var settings = new AppSettings { Theme = dark ? ThemePreference.Dark : ThemePreference.Light };
            settings.ApplyDensity(StripDensity.Balanced);
            var window = new SettingsWindow(settings, snapshots);
            Render((FrameworkElement)window.Content, window.Width, Path.Combine(directory, $"settings-{theme}.png"));
            window.Close();
            foreach (var density in Enum.GetValues<StripDensity>())
            {
                settings.ApplyDensity(density);
                var widgets = StripComposer.Compose(settings, snapshots);
                var bitmap = StripRenderer.RenderPreview(widgets, 60, 120, !dark, true, settings.Spacing, 0);
                SaveBitmap(bitmap, Path.Combine(directory, $"strip-{density.ToString().ToLowerInvariant()}-{theme}.png"));
            }
        }
        File.WriteAllText(Path.Combine(directory, "README.md"), "# Product media\n\nThese PNGs are rendered by Win Stats' actual WPF and taskbar UI, using deterministic illustrative values. They are not desktop screenshots or performance measurements. No personal process names or network addresses are captured.\n\nRegenerate on Windows: `WinStats.exe --media-export <output-directory>`.\n");
    }

    private static void Render(FrameworkElement content, double width, string path)
    {
        content.Measure(new Size(width, 2400));
        content.Arrange(new Rect(0, 0, width, content.DesiredSize.Height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(content.DesiredSize.Height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(content);
        SaveBitmap(bitmap, path);
    }

    private static void SaveBitmap(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static SampleFrame ExampleFrame(int index)
    {
        const ulong gib = 1024UL * 1024 * 1024;
        double Wave(double mid, double amp, double slow, double fast)
            => mid + amp * (0.72 * Math.Sin(index / slow) + 0.28 * Math.Sin(index / fast));

        var cpu = Math.Clamp(Wave(32, 18, 7.5, 2.8), 6, 90);
        var ram = Math.Clamp(Wave(46, 24, 11, 3.6), 18, 88);
        var gpu = Math.Clamp(Wave(18, 14, 5.5, 2.2), 2, 70);
        var used = (ulong)(32 * gib * ram / 100d);
        return new SampleFrame
        {
            HasCpu = true, CpuPercent = cpu, CpuUserPercent = cpu * .72, CpuSystemPercent = cpu * .28,
            CpuIdlePercent = 100 - cpu,
            CorePercents = [Math.Clamp(cpu + 8, 0, 100), Math.Clamp(cpu - 6, 0, 100), Math.Clamp(cpu + 14, 0, 100), Math.Clamp(cpu - 3, 0, 100)],
            FrequencyMhz = 3600, Uptime = TimeSpan.FromHours(8.5),
            RamTotalBytes = 32 * gib, RamUsedBytes = used, RamPercent = ram,
            StandbyBytes = 5 * gib, ModifiedBytes = gib / 4, FreeBytes = 32 * gib > used ? 32 * gib - used : 0,
            HasCompressed = true, CompressedBytes = gib / 2, PageFilePercent = 12.5,
            PageFileTotalBytes = 8 * gib, PageFileUsedBytes = gib,
            HasGpu = true, GpuPercent = gpu, GpuCopyPercent = gpu * .2, GpuDecodePercent = 1, GpuEncodePercent = 0,
            HasVram = true, VramTotalBytes = 8 * gib, VramUsedBytes = (ulong)(8 * gib * Math.Clamp(gpu / 100d, 0.08, 0.7)),
            GpuName = "NVIDIA GPU (example)", DriverVersion = "Example", GpuTemperatureC = 38 + gpu * .4,
            FanPercent = 20 + gpu * .3, GraphicsClockMhz = 900 + gpu * 12, PowerWatts = 18 + gpu * .6,
            Disks = [new DiskReading { Letter = "C", VolumeLabel = "Windows", CapacityBytes = 512 * gib,
                FreeBytes = 280 * gib, BusyPercent = Math.Clamp(Wave(8, 6, 4.2, 1.7), 1, 40),
                ReadBytesPerSecond = Math.Max(40_000, Wave(900_000, 700_000, 6, 2.1)),
                WriteBytesPerSecond = Math.Max(20_000, Wave(180_000, 120_000, 5, 1.8)) }],
            Network = new NetworkReading { AdapterName = "Ethernet (example)", LocalAddress = "192.0.2.10",
                DownBytesPerSecond = Math.Max(8_000, Wave(1_400_000, 900_000, 6.4, 2.4)),
                UpBytesPerSecond = Math.Max(4_000, Wave(220_000, 140_000, 5.2, 1.9)),
                SessionDownBytes = 2 * gib, SessionUpBytes = gib / 4 },
            IncludesProcesses = true,
            TopCpu = [new ProcessReading { Name = "Example app", CpuPercent = 5.1 }, new ProcessReading { Name = "Browser (example)", CpuPercent = 2.3 }],
            TopMemory = [new ProcessReading { Name = "Browser (example)", WorkingSetBytes = gib / 2 }, new ProcessReading { Name = "Example app", WorkingSetBytes = gib / 8 }]
        };
    }
}
