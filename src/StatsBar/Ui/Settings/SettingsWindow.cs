using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using StatsBar.Core;
using StatsBar.Settings;
using StatsBar.Taskbar;
using StatsBar.Ui.Theme;
using StatsBar.Updates;

namespace StatsBar.Ui.Settings;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<ModuleSnapshot> _snapshots;
    private readonly List<ModuleSetting> _localModules;
    private readonly ThemePalette _palette;

    private ThemePreference _theme;
    private TemperatureUnit _temperatureUnit;
    private StripAnchor _anchor;
    private int _nudgePixels;
    private bool _startWithWindows;
    private bool _publicIp;
    private bool _cpuTemperatureEnabled;
    private SensorTemperatureSource _sensorTemperatureSource;
    private StripDensity _density;
    private bool _showLabels;
    private bool _thresholdColors;
    private StripSpacing _spacing;
    private StripArrangement _arrangement;

    private readonly StackPanel _moduleRows;
    private readonly TextBlock _nudgeValueText;
    private Image _previewImage = null!;
    private TextBlock _previewWidthText = null!;
    private ComboBox _spacingCombo = null!;
    private ComboBox _presetCombo = null!;
    private CheckBox _cpuTemperatureCheck = null!;
    private ComboBox _sensorSourceCombo = null!;
    private TextBlock _temperatureStatus = null!;
    private StackPanel _temperatureActions = null!;
    private Button _driverButton = null!;
    private Button _removeSensorButton = null!;
    private bool _saving;
    private bool _cpuSetupCompleted;
    private bool _ready;

    public event Action? Applied;

    public SettingsWindow(AppSettings settings, IReadOnlyList<ModuleSnapshot>? snapshots = null)
    {
        _settings = settings;
        _snapshots = snapshots ?? Array.Empty<ModuleSnapshot>();
        _localModules = settings.Modules.Select(module => new ModuleSetting
        {
            Id = module.Id,
            Visible = module.Visible,
            Style = module.Style
        }).ToList();
        _theme = settings.Theme;
        _temperatureUnit = settings.TemperatureUnit;
        _anchor = settings.Anchor;
        _nudgePixels = settings.NudgePixels;
        _startWithWindows = settings.StartWithWindows;
        _publicIp = settings.PublicIp;
        _cpuTemperatureEnabled = settings.CpuTemperatureEnabled;
        _sensorTemperatureSource = settings.CpuTemperatureEnabled
            ? settings.SensorTemperatureSource
            : SensorTemperatureSource.Gpu;
        _cpuSetupCompleted = settings.CpuTemperatureEnabled;
        _density = settings.Density;
        _showLabels = settings.ShowLabels;
        _thresholdColors = settings.ThresholdColors;
        _spacing = settings.Spacing;
        _arrangement = settings.Arrangement;
        _palette = ThemePalette.For(ThemeService.UseDarkCards(settings.Theme));

        Title = "Win Stats";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Min(860, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        FontFamily = new FontFamily(DesignTokens.FontFamilyName);
        FontSize = 14;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Background = new SolidColorBrush(_palette.Card);
        Foreground = new SolidColorBrush(_palette.Text);
        SourceInitialized += (_, _) => ApplyTitleBar();

        var dock = new DockPanel { Background = new SolidColorBrush(_palette.Card) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16)
        };
        var updateButton = new Button
        {
            Content = "Check for updates",
            MinWidth = 148,
            MinHeight = 34,
            Margin = new Thickness(0, 0, 8, 0)
        };
        StyleButton(updateButton, primary: false);
        updateButton.Click += async (_, _) => await CheckForUpdatesAsync(updateButton);
        buttons.Children.Add(updateButton);

        var saveButton = new Button { Content = "Save", IsDefault = true, MinWidth = 88, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, MinHeight = 34 };
        StyleButton(saveButton, primary: true);
        StyleButton(cancelButton, primary: false);
        saveButton.Click += async (_, _) => await SaveAsync(saveButton, cancelButton);
        cancelButton.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);

        var previewPanel = new StackPanel { Margin = new Thickness(16, 16, 16, 8) };
        previewPanel.Children.Add(Label("Preview"));
        _previewImage = new Image
        {
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            MaxWidth = 488,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _previewWidthText = new TextBlock
        {
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(_palette.Muted),
            FontSize = 13
        };
        Typography.SetNumeralAlignment(_previewWidthText, FontNumeralAlignment.Tabular);
        previewPanel.Children.Add(_previewImage);
        previewPanel.Children.Add(_previewWidthText);
        DockPanel.SetDock(previewPanel, Dock.Top);
        dock.Children.Add(previewPanel);

        var generalPanel = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };

        generalPanel.Children.Add(Label("Theme"));
        var themeCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        themeCombo.Items.Add(ThemePreference.System);
        themeCombo.Items.Add(ThemePreference.Light);
        themeCombo.Items.Add(ThemePreference.Dark);
        themeCombo.SelectedItem = _theme;
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is ThemePreference value)
                _theme = value;
        };
        StyleCombo(themeCombo);
        generalPanel.Children.Add(themeCombo);

        generalPanel.Children.Add(Spacer());
        generalPanel.Children.Add(Label("Taskbar temperature"));
        _sensorSourceCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        _sensorSourceCombo.Items.Add("GPU");
        _sensorSourceCombo.Items.Add("CPU");
        _sensorSourceCombo.SelectedIndex = _sensorTemperatureSource == SensorTemperatureSource.Cpu ? 1 : 0;
        _sensorSourceCombo.SelectionChanged += (_, _) =>
        {
            _sensorTemperatureSource = _sensorSourceCombo.SelectedIndex == 1
                ? SensorTemperatureSource.Cpu
                : SensorTemperatureSource.Gpu;
            if (_sensorTemperatureSource == SensorTemperatureSource.Cpu && _cpuTemperatureCheck.IsChecked != true)
                _cpuTemperatureCheck.IsChecked = true;
        };
        StyleCombo(_sensorSourceCombo);
        generalPanel.Children.Add(_sensorSourceCombo);

        _cpuTemperatureCheck = new CheckBox
        {
            Content = "CPU temperature",
            IsChecked = _cpuTemperatureEnabled,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _cpuTemperatureCheck.Checked += (_, _) =>
        {
            _cpuTemperatureEnabled = true;
            RefreshTemperatureStatus();
        };
        _cpuTemperatureCheck.Unchecked += (_, _) =>
        {
            _cpuTemperatureEnabled = false;
            if (_sensorTemperatureSource == SensorTemperatureSource.Cpu)
            {
                _sensorTemperatureSource = SensorTemperatureSource.Gpu;
                _sensorSourceCombo.SelectedIndex = 0;
            }
            RefreshTemperatureStatus();
        };
        StyleCheck(_cpuTemperatureCheck);
        generalPanel.Children.Add(_cpuTemperatureCheck);

        _temperatureStatus = new TextBlock
        {
            FontSize = 13,
            Foreground = new SolidColorBrush(_palette.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        generalPanel.Children.Add(_temperatureStatus);

        _temperatureActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        _driverButton = new Button { Content = "Install sensor driver" };
        StyleLinkButton(_driverButton);
        _driverButton.Click += async (_, _) => await InstallSensorDriverAsync();
        _removeSensorButton = new Button { Content = "Remove sensor service" };
        StyleLinkButton(_removeSensorButton);
        _removeSensorButton.Click += async (_, _) => await RemoveSensorServiceAsync();
        _temperatureActions.Children.Add(_driverButton);
        _temperatureActions.Children.Add(_removeSensorButton);
        generalPanel.Children.Add(_temperatureActions);

        generalPanel.Children.Add(Spacer());
        generalPanel.Children.Add(Label("Temperature unit"));
        var tempCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        tempCombo.Items.Add(TemperatureUnit.Celsius);
        tempCombo.Items.Add(TemperatureUnit.Fahrenheit);
        tempCombo.SelectedItem = _temperatureUnit;
        tempCombo.SelectionChanged += (_, _) =>
        {
            if (tempCombo.SelectedItem is TemperatureUnit value)
                _temperatureUnit = value;
        };
        StyleCombo(tempCombo);
        generalPanel.Children.Add(tempCombo);

        generalPanel.Children.Add(Spacer());
        generalPanel.Children.Add(Label("Position"));
        var positionPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var leftRadio = new RadioButton { Content = "Left", IsChecked = _anchor == StripAnchor.Left, Margin = new Thickness(0, 0, 16, 0) };
        var rightRadio = new RadioButton { Content = "Right", IsChecked = _anchor == StripAnchor.Right };
        leftRadio.Checked += (_, _) => _anchor = StripAnchor.Left;
        rightRadio.Checked += (_, _) => _anchor = StripAnchor.Right;
        StyleRadio(leftRadio);
        StyleRadio(rightRadio);
        positionPanel.Children.Add(leftRadio);
        positionPanel.Children.Add(rightRadio);
        generalPanel.Children.Add(positionPanel);

        generalPanel.Children.Add(Spacer());
        generalPanel.Children.Add(Label("Nudge"));
        var nudgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var nudgeSlider = new Slider
        {
            Minimum = -40,
            Maximum = 40,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Value = _nudgePixels,
            Width = 260,
            VerticalAlignment = VerticalAlignment.Center
        };
        _nudgeValueText = new TextBlock
        {
            Text = _nudgePixels.ToString(),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_palette.Text)
        };
        Typography.SetNumeralAlignment(_nudgeValueText, FontNumeralAlignment.Tabular);
        nudgeSlider.ValueChanged += (_, _) =>
        {
            _nudgePixels = (int)nudgeSlider.Value;
            _nudgeValueText.Text = _nudgePixels.ToString();
        };
        nudgeRow.Children.Add(nudgeSlider);
        nudgeRow.Children.Add(_nudgeValueText);
        generalPanel.Children.Add(nudgeRow);

        generalPanel.Children.Add(Spacer());
        var startCheck = new CheckBox
        {
            Content = "Start with Windows",
            IsChecked = _startWithWindows
        };
        startCheck.Checked += (_, _) => _startWithWindows = true;
        startCheck.Unchecked += (_, _) => _startWithWindows = false;
        StyleCheck(startCheck);
        generalPanel.Children.Add(startCheck);

        generalPanel.Children.Add(Spacer());
        var publicCheck = new CheckBox
        {
            Content = "Public IP",
            IsChecked = _publicIp
        };
        publicCheck.Checked += (_, _) => _publicIp = true;
        publicCheck.Unchecked += (_, _) => _publicIp = false;
        StyleCheck(publicCheck);
        generalPanel.Children.Add(publicCheck);
        generalPanel.Children.Add(new TextBlock
        {
            Text = "Off until you turn this on. The network card then looks up the public address.",
            FontSize = 13,
            Foreground = new SolidColorBrush(_palette.Muted),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });

        _moduleRows = new StackPanel();
        var modulesPanel = new StackPanel
        {
            Margin = new Thickness(16, 8, 16, 16),
            Visibility = Visibility.Collapsed,
            Children = { _moduleRows }
        };

        var widgetsPanel = new StackPanel { Margin = new Thickness(16, 8, 16, 16), Visibility = Visibility.Collapsed };

        widgetsPanel.Children.Add(Label("Preset"));
        _presetCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        _presetCombo.Items.Add(StripDensity.Minimal);
        _presetCombo.Items.Add(StripDensity.Balanced);
        _presetCombo.Items.Add(StripDensity.Detailed);
        _presetCombo.SelectedItem = _density;
        _presetCombo.SelectionChanged += (_, _) =>
        {
            if (!_ready || _presetCombo.SelectedItem is not StripDensity selected)
                return;
            var draft = new AppSettings { Modules = _localModules };
            draft.ApplyDensity(selected);
            _ready = false;
            _showLabels = true;
            _spacing = draft.Spacing;
            _density = draft.Density;
            _spacingCombo.SelectedItem = _spacing;
            RebuildModuleRows();
            _ready = true;
            RefreshPreview();
        };
        StyleCombo(_presetCombo);
        widgetsPanel.Children.Add(_presetCombo);

        widgetsPanel.Children.Add(Spacer());
        var thresholdCheck = new CheckBox
        {
            Content = "Threshold colors",
            IsChecked = _thresholdColors
        };
        thresholdCheck.Checked += (_, _) =>
        {
            _thresholdColors = true;
            RefreshPreview();
        };
        thresholdCheck.Unchecked += (_, _) =>
        {
            _thresholdColors = false;
            RefreshPreview();
        };
        StyleCheck(thresholdCheck);
        widgetsPanel.Children.Add(thresholdCheck);

        widgetsPanel.Children.Add(Spacer());
        widgetsPanel.Children.Add(Label("Spacing"));
        _spacingCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        _spacingCombo.Items.Add(StripSpacing.Compact);
        _spacingCombo.Items.Add(StripSpacing.Comfortable);
        _spacingCombo.SelectedItem = _spacing;
        _spacingCombo.SelectionChanged += (_, _) =>
        {
            if (!_ready || _spacingCombo.SelectedItem is not StripSpacing value)
                return;
            _spacing = value;
            RefreshPreview();
        };
        StyleCombo(_spacingCombo);
        widgetsPanel.Children.Add(_spacingCombo);

        widgetsPanel.Children.Add(Spacer());
        widgetsPanel.Children.Add(Label("Numbers"));
        var arrangementCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 0) };
        arrangementCombo.Items.Add(StripArrangement.Beside);
        arrangementCombo.Items.Add(StripArrangement.Above);
        arrangementCombo.SelectedItem = _arrangement;
        arrangementCombo.SelectionChanged += (_, _) =>
        {
            if (!_ready || arrangementCombo.SelectedItem is not StripArrangement value)
                return;
            _arrangement = value;
            RefreshPreview();
        };
        StyleCombo(arrangementCombo);
        widgetsPanel.Children.Add(arrangementCombo);

        var sectionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 8, 16, 0)
        };
        var generalButton = new Button { Content = "General", MinWidth = 96, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        var modulesButton = new Button { Content = "Modules", MinWidth = 96, MinHeight = 34, Margin = new Thickness(0, 0, 8, 0) };
        var widgetsButton = new Button { Content = "Widgets", MinWidth = 96, MinHeight = 34 };
        void ShowSection(string section)
        {
            generalPanel.Visibility = section == "General" ? Visibility.Visible : Visibility.Collapsed;
            modulesPanel.Visibility = section == "Modules" ? Visibility.Visible : Visibility.Collapsed;
            widgetsPanel.Visibility = section == "Widgets" ? Visibility.Visible : Visibility.Collapsed;
            StyleButton(generalButton, section == "General");
            StyleButton(modulesButton, section == "Modules");
            StyleButton(widgetsButton, section == "Widgets");
        }
        generalButton.Click += (_, _) => ShowSection("General");
        modulesButton.Click += (_, _) => ShowSection("Modules");
        widgetsButton.Click += (_, _) => ShowSection("Widgets");
        ShowSection("General");
        sectionButtons.Children.Add(generalButton);
        sectionButtons.Children.Add(modulesButton);
        sectionButtons.Children.Add(widgetsButton);
        var sections = new StackPanel();
        sections.Children.Add(sectionButtons);
        sections.Children.Add(generalPanel);
        sections.Children.Add(modulesPanel);
        sections.Children.Add(widgetsPanel);
        dock.Children.Add(new ScrollViewer
        {
            Content = sections,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(200, MaxHeight - 220)
        });

        Content = dock;

        RebuildModuleRows();
        _ready = true;
        RefreshPreview();
        RefreshTemperatureStatus();
    }

    private async Task SaveAsync(Button saveButton, Button cancelButton)
    {
        if (_saving)
            return;

        _saving = true;
        saveButton.IsEnabled = false;
        cancelButton.IsEnabled = false;
        _cpuTemperatureCheck.IsEnabled = false;
        _driverButton.IsEnabled = false;
        string? failureMessage = null;
        try
        {
            if (_cpuTemperatureEnabled
                && (!_cpuSetupCompleted || !SensorServiceSetup.IsInstalled() || !SensorServiceSetup.IsDriverInstalled()))
            {
                _temperatureStatus.Text = "Setting up CPU temperature…";
                _temperatureStatus.Visibility = Visibility.Visible;
                var progress = new Progress<string>(text =>
                {
                    _temperatureStatus.Text = text;
                    _temperatureStatus.Visibility = Visibility.Visible;
                });
                try
                {
                    var result = await SensorServiceSetup.InstallAsync(progress);
                    if (!result.Succeeded)
                    {
                        failureMessage = result.Message;
                        _cpuTemperatureEnabled = false;
                        _cpuTemperatureCheck.IsChecked = false;
                    }
                    else
                    {
                        _cpuSetupCompleted = true;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    failureMessage = ex.Message;
                    _cpuTemperatureEnabled = false;
                    _cpuTemperatureCheck.IsChecked = false;
                }
            }

            PersistSettings();
            Applied?.Invoke();
            RefreshTemperatureStatus();
            if (failureMessage != null)
            {
                _temperatureStatus.Text = failureMessage;
                _temperatureStatus.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _saving = false;
            saveButton.IsEnabled = true;
            cancelButton.IsEnabled = true;
            _cpuTemperatureCheck.IsEnabled = true;
            _driverButton.IsEnabled = true;
        }
    }

    private void PersistSettings()
    {
        _settings.Density = _density;
        _settings.ShowLabels = true;
        _settings.ThresholdColors = _thresholdColors;
        _settings.Spacing = _spacing;
        _settings.Arrangement = _arrangement;
        _settings.CombinedItem = false;
        _settings.WidthCapPixels = 0;
        _settings.Theme = _theme;
        _settings.TemperatureUnit = _temperatureUnit;
        _settings.Anchor = _anchor;
        _settings.NudgePixels = _nudgePixels;
        _settings.StartWithWindows = _startWithWindows;
        _settings.PublicIp = _publicIp;
        _settings.CpuTemperatureEnabled = _cpuTemperatureEnabled;
        _settings.SensorTemperatureSource = _cpuTemperatureEnabled
            ? _sensorTemperatureSource
            : SensorTemperatureSource.Gpu;
        _settings.Modules = _localModules.Select(module => new ModuleSetting
        {
            Id = module.Id,
            Visible = module.Visible,
            Style = module.Style
        }).ToList();
    }

    private async Task InstallSensorDriverAsync()
    {
        if (_saving)
            return;

        _saving = true;
        _driverButton.IsEnabled = false;
        _cpuTemperatureCheck.IsEnabled = false;
        string? failureMessage = null;
        var progress = new Progress<string>(text =>
        {
            _temperatureStatus.Text = text;
            _temperatureStatus.Visibility = Visibility.Visible;
        });
        try
        {
            _temperatureStatus.Text = "Setting up CPU temperature…";
            _temperatureStatus.Visibility = Visibility.Visible;
            var result = await SensorServiceSetup.InstallAsync(progress);
            if (result.Succeeded)
            {
                _cpuSetupCompleted = true;
            }
            else
            {
                failureMessage = result.Message;
            }

            RefreshTemperatureStatus();
            if (failureMessage != null)
            {
                _temperatureStatus.Text = failureMessage;
                _temperatureStatus.Visibility = Visibility.Visible;
            }
            else if (!SensorServiceSetup.IsDriverInstalled())
            {
                _temperatureStatus.Text = "Couldn't install the sensor driver automatically. Visit https://pawnio.eu/ in your browser.";
                _temperatureStatus.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            failureMessage = ex.Message;
            RefreshTemperatureStatus();
            _temperatureStatus.Text = failureMessage;
            _temperatureStatus.Visibility = Visibility.Visible;
        }
        finally
        {
            _saving = false;
            _driverButton.IsEnabled = true;
            _cpuTemperatureCheck.IsEnabled = true;
        }
    }

    private async Task RemoveSensorServiceAsync()
    {
        _removeSensorButton.IsEnabled = false;
        _cpuTemperatureCheck.IsEnabled = false;
        _temperatureStatus.Text = "Removing sensor service…";
        _temperatureStatus.Visibility = Visibility.Visible;
        try
        {
            var result = await SensorServiceSetup.RemoveAsync();
            if (result.Succeeded)
            {
                RefreshTemperatureStatus();
            }
            else
            {
                _temperatureStatus.Text = result.Message;
                _temperatureStatus.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _temperatureStatus.Text = ex.Message;
            _temperatureStatus.Visibility = Visibility.Visible;
        }
        finally
        {
            _removeSensorButton.IsEnabled = true;
            _cpuTemperatureCheck.IsEnabled = true;
        }
    }

    private void RefreshTemperatureStatus()
    {
        if (_temperatureStatus == null)
            return;

        var installed = SensorServiceSetup.IsInstalled();
        var driver = SensorServiceSetup.IsDriverInstalled();
        _driverButton.Visibility = Visibility.Collapsed;
        _removeSensorButton.Visibility = Visibility.Collapsed;

        if (!_cpuTemperatureEnabled)
        {
            _temperatureStatus.Text = installed ? "CPU temperature is off." : "";
            _removeSensorButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (!installed)
        {
            _temperatureStatus.Text = "Save to give Windows permission.";
        }
        else if (!driver)
        {
            _temperatureStatus.Text = "CPU temperature needs a one-time sensor driver.";
            _driverButton.Visibility = Visibility.Visible;
        }
        else
        {
            _temperatureStatus.Text = "CPU temperature is on.";
        }

        _temperatureStatus.Visibility = string.IsNullOrEmpty(_temperatureStatus.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _temperatureActions.Visibility = _driverButton.Visibility == Visibility.Visible
            || _removeSensorButton.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshPreview()
    {
        if (!_ready || _previewImage == null)
            return;

        var draft = new AppSettings
        {
            Anchor = _anchor,
            NudgePixels = _nudgePixels,
            Theme = _theme,
            TemperatureUnit = _temperatureUnit,
            PublicIp = _publicIp,
            StartWithWindows = _startWithWindows,
            Density = _density,
            ShowLabels = _showLabels,
            ThresholdColors = _thresholdColors,
            Spacing = _spacing,
            Arrangement = _arrangement,
            CombinedItem = false,
            Modules = _localModules,
            WidthCapPixels = 0
        };

        var widgets = StripComposer.Compose(draft, _snapshots);
        var dpi = (uint)Math.Round(VisualTreeHelper.GetDpi(this).PixelsPerInchX);
        if (dpi == 0)
            dpi = 96;
        var height = Math.Max(48, (int)Math.Round(48 * dpi / 96.0));
        var image = StripRenderer.RenderPreview(widgets, height, dpi, ThemeService.TaskbarIsLight(), draft.ThresholdColors, draft.Spacing, draft.WidthCapPixels);
        _previewImage.Source = image;
        _previewWidthText.Text = image.PixelWidth + " px";
    }

    private void RebuildModuleRows()
    {
        _moduleRows.Children.Clear();
        for (var i = 0; i < _localModules.Count; i++)
        {
            var mod = _localModules[i];
            var index = i;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var visible = new CheckBox
            {
                Content = ModuleDisplayName(mod.Id),
                IsChecked = mod.Visible,
                VerticalAlignment = VerticalAlignment.Center
            };
            visible.Checked += (_, _) =>
            {
                mod.Visible = true;
                RefreshPreview();
            };
            visible.Unchecked += (_, _) =>
            {
                mod.Visible = false;
                RefreshPreview();
            };
            StyleCheck(visible);
            Grid.SetColumn(visible, 0);
            row.Children.Add(visible);

            var styleCombo = new ComboBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            styleCombo.Items.Add("Mini");
            styleCombo.Items.Add("Line chart");
            styleCombo.Items.Add("Bar");
            styleCombo.Items.Add("Ring");
            styleCombo.Items.Add("Speed");
            styleCombo.SelectedIndex = mod.Style switch
            {
                StripItemStyle.LineChart => 1,
                StripItemStyle.Bar => 2,
                StripItemStyle.Ring => 3,
                StripItemStyle.Speed => 4,
                _ => 0
            };
            styleCombo.SelectionChanged += (_, _) =>
            {
                mod.Style = styleCombo.SelectedIndex switch
                {
                    1 => StripItemStyle.LineChart,
                    2 => StripItemStyle.Bar,
                    3 => StripItemStyle.Ring,
                    4 => StripItemStyle.Speed,
                    _ => StripItemStyle.Mini
                };
                RefreshPreview();
            };
            StyleCombo(styleCombo);
            Grid.SetColumn(styleCombo, 1);
            row.Children.Add(styleCombo);

            var upButton = new Button { Content = "Up", MinWidth = 58, MinHeight = 32, Margin = new Thickness(8, 0, 0, 0) };
            var downButton = new Button { Content = "Down", MinWidth = 64, MinHeight = 32, Margin = new Thickness(6, 0, 0, 0) };
            StyleButton(upButton, primary: false);
            StyleButton(downButton, primary: false);
            upButton.Click += (_, _) =>
            {
                if (index <= 0)
                    return;
                (_localModules[index - 1], _localModules[index]) = (_localModules[index], _localModules[index - 1]);
                RebuildModuleRows();
            };
            downButton.Click += (_, _) =>
            {
                if (index >= _localModules.Count - 1)
                    return;
                (_localModules[index + 1], _localModules[index]) = (_localModules[index], _localModules[index + 1]);
                RebuildModuleRows();
            };
            Grid.SetColumn(upButton, 2);
            Grid.SetColumn(downButton, 3);
            row.Children.Add(upButton);
            row.Children.Add(downButton);

            _moduleRows.Children.Add(row);
        }

        if (_ready)
            RefreshPreview();
    }

    private TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_palette.Text)
        };
    }

    private FrameworkElement Spacer()
    {
        return new Border { Height = 12 };
    }

    private async Task CheckForUpdatesAsync(Button button)
    {
        var originalContent = button.Content;
        button.IsEnabled = false;
        button.Content = "Checking…";
        try
        {
            var result = await UpdateChecker.CheckAsync();
            if (!result.IsUpdateAvailable)
            {
                MessageBox.Show(
                    this,
                    $"You're using the latest version of Win Stats ({result.CurrentVersion}).",
                    "Check for updates",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var choice = MessageBox.Show(
                this,
                $"Win Stats {result.LatestVersion} is available (you have {result.CurrentVersion}). Open the GitHub release page?",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(UpdateChecker.LatestReleasePage) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    MessageBox.Show(
                        this,
                        "Couldn't open GitHub Releases. Open it in your browser to download the update.",
                        "Check for updates",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                "Couldn't check for updates. Check your connection and try again.",
                "Check for updates",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private void StyleCombo(ComboBox combo)
    {
        var field = FieldBrush();
        var text = new SolidColorBrush(_palette.Text);
        var border = FieldBorder();
        combo.Foreground = text;
        combo.Background = field;
        combo.BorderBrush = border;
        combo.BorderThickness = new Thickness(1);
        combo.Padding = new Thickness(10, 6, 10, 6);
        combo.MinHeight = 34;
        combo.FontSize = 14;
        combo.Template = ComboTemplate();
        combo.ItemContainerStyle = ComboItemStyle();
    }

    private void StyleButton(Button button, bool primary)
    {
        button.Foreground = primary || _palette.Dark ? Brushes.White : new SolidColorBrush(_palette.Text);
        button.Background = primary ? new SolidColorBrush(DesignTokens.Cpu) : FieldBrush();
        button.BorderBrush = primary ? new SolidColorBrush(DesignTokens.Cpu) : FieldBorder();
        button.BorderThickness = new Thickness(1);
        button.Padding = new Thickness(12, 6, 12, 6);
        button.FontSize = 14;
        button.Template = ButtonTemplate();
    }

    private void StyleCheck(CheckBox check)
    {
        check.Foreground = new SolidColorBrush(_palette.Text);
        check.FontSize = 14;
    }

    private void StyleLinkButton(Button button)
    {
        button.Foreground = new SolidColorBrush(DesignTokens.Cpu);
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(0);
        button.FontSize = 13;
        button.Cursor = Cursors.Hand;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Template = LinkButtonTemplate();
    }

    private void StyleRadio(RadioButton radio)
    {
        radio.Foreground = new SolidColorBrush(_palette.Text);
        radio.FontSize = 14;
    }

    private SolidColorBrush FieldBrush()
    {
        var color = _palette.Dark ? Color.FromRgb(0x3A, 0x3A, 0x3C) : Color.FromRgb(0xF2, 0xF2, 0xF7);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private SolidColorBrush FieldBorder()
    {
        var color = _palette.Dark ? Color.FromRgb(0x8E, 0x8E, 0x93) : Color.FromRgb(0xAE, 0xAE, 0xB2);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Style ComboItemStyle()
    {
        var hover = _palette.Dark ? "#505057" : "#E5E5EA";
        var text = _palette.Dark ? "#F5F5F7" : "#1C1C1E";
        return (Style)XamlReader.Parse($"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ComboBoxItem">
              <Setter Property="Foreground" Value="{text}"/>
              <Setter Property="Background" Value="Transparent"/>
              <Setter Property="Padding" Value="10,8"/>
              <Setter Property="FontSize" Value="14"/>
              <Setter Property="HorizontalContentAlignment" Value="Left"/>
              <Style.Triggers>
                <Trigger Property="IsHighlighted" Value="True">
                  <Setter Property="Background" Value="{hover}"/>
                </Trigger>
              </Style.Triggers>
            </Style>
            """);
    }

    private static ControlTemplate ComboTemplate()
    {
        return (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ComboBox">
              <Grid MinHeight="34">
                <ToggleButton Focusable="False" ClickMode="Press"
                              IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                              Background="{TemplateBinding Background}"
                              BorderBrush="{TemplateBinding BorderBrush}"
                              BorderThickness="1">
                  <ToggleButton.Template>
                    <ControlTemplate TargetType="ToggleButton">
                      <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="10,6">
                        <Grid>
                          <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center" Margin="0,0,22,0" RecognizesAccessKey="False"
                                            Content="{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                                            ContentTemplate="{Binding SelectionBoxItemTemplate, RelativeSource={RelativeSource AncestorType=ComboBox}}"
                                            TextElement.Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ComboBox}}"/>
                          <TextBlock Text="&#xE70D;" FontFamily="Segoe MDL2 Assets" FontSize="12" HorizontalAlignment="Right" VerticalAlignment="Center"
                                     Foreground="{Binding Foreground, RelativeSource={RelativeSource AncestorType=ComboBox}}"/>
                        </Grid>
                      </Border>
                    </ControlTemplate>
                  </ToggleButton.Template>
                </ToggleButton>
                <Popup IsOpen="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Placement="Bottom" StaysOpen="False" AllowsTransparency="True" Focusable="False" PopupAnimation="Fade">
                  <Border Background="{Binding Background, RelativeSource={RelativeSource TemplatedParent}}" BorderBrush="{Binding BorderBrush, RelativeSource={RelativeSource TemplatedParent}}" BorderThickness="1" CornerRadius="6" Margin="0,4,0,0" Padding="0,4" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                    <ScrollViewer MaxHeight="240" CanContentScroll="True">
                      <ItemsPresenter/>
                    </ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
            </ControlTemplate>
            """);
    }

    private static ControlTemplate ButtonTemplate()
    {
        return (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" TextElement.Foreground="{TemplateBinding Foreground}"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter TargetName="Bd" Property="Opacity" Value="0.88"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter TargetName="Bd" Property="Opacity" Value="0.75"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter TargetName="Bd" Property="Opacity" Value="0.45"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private static ControlTemplate LinkButtonTemplate()
    {
        return (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <ContentPresenter/>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                  <Setter Property="Opacity" Value="0.72"/>
                </Trigger>
                <Trigger Property="IsPressed" Value="True">
                  <Setter Property="Opacity" Value="0.55"/>
                </Trigger>
                <Trigger Property="IsEnabled" Value="False">
                  <Setter Property="Opacity" Value="0.45"/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """);
    }

    private void ApplyTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = _palette.Dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    private static string ModuleDisplayName(ModuleId id)
    {
        return id switch
        {
            ModuleId.Ram => "Memory",
            ModuleId.Gpu => "GPU",
            ModuleId.Disk => "Disk",
            ModuleId.Network => "Network",
            ModuleId.Sensors => "Sensors",
            _ => "CPU"
        };
    }
}
