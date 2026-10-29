using System.Runtime.InteropServices;
using System.Text;
using StatsBar.Core;
using StatsBar.Interop;
using StatsBar.Settings;

namespace StatsBar.Taskbar;

public sealed class StripHost : IDisposable
{
    private const uint WmMouseMove = 0x0200;
    private const uint WmMouseLeave = 0x02A3;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmWindowPosChanged = 0x0047;
    private const int DtCenter = 0x0001;
    private const int DtVCenter = 0x0004;
    private const int DtSingleLine = 0x0020;
    private const int DtCalcRect = 0x0400;
    private const int Transparent = 1;
    private const uint UlwAlpha = 0x00000002;
    private const uint MenuString = 0;
    private const uint MenuSeparator = 0x00000800;
    private const uint MenuChecked = 0x00000008;
    private const uint TrackReturn = 0x0100;
    private const uint TrackRightButton = 0x0002;

    private static readonly WndProcDelegate Proc = StaticProc;
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static StripHost? _current;
    private static bool _classRegistered;

    private readonly IntPtr _hwnd;
    private IReadOnlyList<StripWidget> _widgets = [];
    private PlacedWidget[] _placed = [];
    private string _signature = "";
    private string _presentedSignature = "";
    private int _presentedWidth;
    private int _presentedHeight;
    private int _layoutHeight = 60;
    private int _hover = -1;
    private bool _lightTaskbar;
    private bool _thresholdColors = true;
    private StripSpacing _spacing = StripSpacing.Compact;
    private int _widthCap;
    private bool _trackingLeave;
    private bool _presenting;
    private bool _disposed;
    private IntPtr _frameDib;
    private IntPtr _frameDc;
    private IntPtr _framePrevious;
    private IntPtr _frameBits;
    private int _frameWidth;
    private int _frameHeight;
    private bool _trayAdded;
    private IntPtr _trayIcon;
    private string _trayTip = "Win Stats";
    private const uint TrayMessage = 0x8001;
    private const uint NimAdd = 0;
    private const uint NimModify = 1;
    private const uint NimDelete = 2;
    private const uint NifMessage = 0x1;
    private const uint NifIcon = 0x2;
    private const uint NifTip = 0x4;

    public event Action<ModuleId>? ItemActivated;
    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action<StripAnchor>? AnchorChosen;
    public event Action? TaskbarRecreated;

    public IntPtr Handle => _hwnd;
    public int DesiredWidth { get; private set; } = 240;
    public StripAnchor CurrentAnchor { get; set; }

    private StripHost(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _current = this;
    }

    public static bool TryCreate(out StripHost? host, out int win32Error)
    {
        host = null;
        win32Error = 0;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            if (win32Error == 0)
                win32Error = 1400;
            return false;
        }

        if (!EnsureClass())
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        var hwnd = CreateWindowEx(
            0x00080000 | 0x00000080 | 0x08000000 | 0x00000008,
            "WinStatsStrip",
            "",
            unchecked((int)0x80000000),
            0,
            0,
            240,
            40,
            IntPtr.Zero,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        host = new StripHost(hwnd);
        host.AddTrayIcon();
        return true;
    }

    public void Update(IReadOnlyList<StripWidget> widgets, bool lightTaskbar, bool thresholdColors, StripSpacing spacing, int widthCapPixels)
    {
        _widgets = widgets;
        _lightTaskbar = lightTaskbar;
        _thresholdColors = thresholdColors;
        _spacing = spacing;
        _widthCap = widthCapPixels;
        UpdateTrayTip();
        Relayout();
        var signature = Signature();
        if (signature == _signature && !NeedsPresent())
            return;
        _signature = signature;
        if (WindowWidth() == DesiredWidth)
            Present(force: true);
    }

    public void Refresh()
    {
        Present(force: false);
    }

    public bool TryGetItemScreenRect(ModuleId id, out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        if (!NativeMethods.GetWindowRect(_hwnd, out var rect))
            return false;
        foreach (var placed in _placed)
        {
            if (placed.Parts.Length > 0)
            {
                foreach (var part in placed.Parts)
                {
                    if (part.Id != id)
                        continue;
                    left = rect.Left + part.X;
                    right = left + part.Width;
                    top = rect.Top;
                    bottom = rect.Bottom;
                    return true;
                }
            }
            else if (placed.Widget.Id == id)
            {
                left = rect.Left + placed.X;
                right = left + placed.Width;
                top = rect.Top;
                bottom = rect.Bottom;
                return true;
            }
        }

        left = rect.Left;
        top = rect.Top;
        right = rect.Right;
        bottom = rect.Bottom;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ReleaseFrameBuffer();
        RemoveTrayIcon();
        if (_current == this)
            _current = null;
        if (NativeMethods.IsWindow(_hwnd))
            DestroyWindow(_hwnd);
    }

    private void Relayout()
    {
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;
        var layout = StripRenderer.Layout(_widgets, _layoutHeight, dpi, _spacing, _widthCap);
        _placed = layout.Widgets;
        DesiredWidth = Math.Max(80, layout.Width);
    }

    private bool NeedsPresent()
    {
        return !NativeMethods.GetWindowRect(_hwnd, out var rect) || rect.Right - rect.Left != DesiredWidth;
    }

    private int WindowWidth()
    {
        return NativeMethods.GetWindowRect(_hwnd, out var rect) ? rect.Right - rect.Left : 0;
    }

    private string Signature()
    {
        var builder = new StringBuilder();
        builder.Append(_hover).Append(_lightTaskbar ? 'L' : 'D').Append((int)_spacing).Append(_widthCap).Append(_thresholdColors ? 'T' : 'N');
        foreach (var placed in _placed)
        {
            var item = placed.Widget;
            builder.Append('|').Append(item.Label).Append(item.Value).Append(item.SecondaryValue).Append((int)item.Style).Append(item.Level.ToString("0.00")).Append(item.ShowLabel).Append(item.Stacked);
            foreach (var part in item.Parts)
                builder.Append(part.Label).Append(part.Value).Append(part.Level.ToString("0.00"));
        }

        return builder.ToString();
    }

    private static bool EnsureClass()
    {
        if (_classRegistered)
            return true;

        var windowClass = new WndClass
        {
            Style = 0x0003,
            WndProc = Marshal.GetFunctionPointerForDelegate(Proc),
            Instance = GetModuleHandle(null),
            Cursor = LoadCursor(IntPtr.Zero, 32512),
            ClassName = "WinStatsStrip"
        };
        _classRegistered = RegisterClass(ref windowClass) != 0;
        return _classRegistered;
    }

    private static IntPtr StaticProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        return _current?.HandleMessage(hwnd, msg, wParam, lParam)
            ?? DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr HandleMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TaskbarCreatedMessage)
        {
            AddTrayIcon();
            TaskbarRecreated?.Invoke();
            return IntPtr.Zero;
        }

        if (msg == TrayMessage)
        {
            var mouse = unchecked((uint)lParam.ToInt64());
            if (mouse is WmLButtonUp or 0x0203)
                SettingsRequested?.Invoke();
            else if (mouse is WmRButtonUp or 0x007B)
                ShowMenu();
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case WmWindowPosChanged:
                Present(force: false);
                return IntPtr.Zero;
            case WmMouseMove:
                TrackLeave();
                SetHover(HitTest(LowWord(lParam)));
                return IntPtr.Zero;
            case WmMouseLeave:
                _trackingLeave = false;
                SetHover(-1);
                return IntPtr.Zero;
            case WmLButtonUp:
                var id = ModuleAt(LowWord(lParam));
                if (id != null)
                    ItemActivated?.Invoke(id.Value);
                return IntPtr.Zero;
            case WmRButtonUp:
                ShowMenu();
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void SetHover(int index)
    {
        if (index == _hover)
            return;
        _hover = index;
        _signature = Signature();
        Present(force: true);
    }

    private int HitTest(int x)
    {
        for (var i = 0; i < _placed.Length; i++)
        {
            if (x >= _placed[i].X && x < _placed[i].X + _placed[i].Width)
                return i;
        }

        return -1;
    }

    private ModuleId? ModuleAt(int x)
    {
        foreach (var placed in _placed)
        {
            if (x < placed.X || x >= placed.X + placed.Width)
                continue;
            if (placed.Parts.Length == 0)
                return placed.Widget.Id;

            PlacedPart nearest = placed.Parts[0];
            var nearestDistance = int.MaxValue;
            foreach (var part in placed.Parts)
            {
                if (x >= part.X && x < part.X + part.Width)
                    return part.Id;
                var distance = Math.Abs(x - (part.X + part.Width / 2));
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = part;
                }
            }

            return nearest.Id;
        }

        return null;
    }

    private void TrackLeave()
    {
        if (_trackingLeave)
            return;
        var track = new TrackMouse { Size = Marshal.SizeOf<TrackMouse>(), Flags = 2, Hwnd = _hwnd };
        _trackingLeave = TrackMouseEvent(ref track);
    }

    private void AddTrayIcon()
    {
        if (_trayIcon == IntPtr.Zero)
            _trayIcon = CreateTrayIcon();
        if (_trayIcon == IntPtr.Zero)
            return;

        var data = TrayData();
        Shell_NotifyIcon(NimDelete, ref data);
        _trayAdded = Shell_NotifyIcon(NimAdd, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (_trayAdded)
        {
            var data = TrayData();
            Shell_NotifyIcon(NimDelete, ref data);
            _trayAdded = false;
        }

        if (_trayIcon == IntPtr.Zero)
            return;
        DestroyIcon(_trayIcon);
        _trayIcon = IntPtr.Zero;
    }

    private void UpdateTrayTip()
    {
        var tip = "Win Stats";
        if (_widgets.Count > 0)
            tip = "Win Stats  " + string.Join("   ", _widgets.Select(widget => $"{widget.Label} {widget.Value}"));
        if (tip.Length > 127)
            tip = tip[..127];
        if (tip == _trayTip)
            return;
        _trayTip = tip;
        if (!_trayAdded)
            return;
        var data = TrayData();
        Shell_NotifyIcon(NimModify, ref data);
    }

    private NotifyIconData TrayData()
    {
        return new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayMessage,
            hIcon = _trayIcon,
            szTip = _trayTip
        };
    }

    private static IntPtr CreateTrayIcon()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            return IntPtr.Zero;
        ExtractIconEx(executable, 0, out var large, out var small, 1);
        if (small == IntPtr.Zero)
            return large;
        if (large != IntPtr.Zero && large != small)
            DestroyIcon(large);
        return small;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, MenuString | (CurrentAnchor == StripAnchor.Left ? MenuChecked : 0), 1, "Move to the left");
        AppendMenu(menu, MenuString | (CurrentAnchor == StripAnchor.Right ? MenuChecked : 0), 2, "Move to the right");
        AppendMenu(menu, MenuSeparator, 0, null);
        AppendMenu(menu, MenuString, 3, "Settings");
        AppendMenu(menu, MenuString, 4, "Quit");
        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenu(menu, TrackReturn | TrackRightButton, point.X, point.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(menu);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        switch (command)
        {
            case 1:
                AnchorChosen?.Invoke(StripAnchor.Left);
                break;
            case 2:
                AnchorChosen?.Invoke(StripAnchor.Right);
                break;
            case 3:
                SettingsRequested?.Invoke();
                break;
            case 4:
                QuitRequested?.Invoke();
                break;
        }
    }

    private void Present(bool force)
    {
        if (_presenting || _disposed || !NativeMethods.GetWindowRect(_hwnd, out var bounds))
            return;

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 2 || height < 2)
            return;
        if (height != _layoutHeight)
        {
            _layoutHeight = height;
            Relayout();
            if (width != DesiredWidth)
                return;
        }

        if (!force && width == _presentedWidth && height == _presentedHeight && _signature == _presentedSignature)
            return;

        _presenting = true;
        try
        {
            EnsureFrameBuffer(width, height);
            if (_frameDib == IntPtr.Zero || _frameDc == IntPtr.Zero)
                return;

            var dpi = NativeMethods.GetDpiForWindow(_hwnd);
            if (dpi == 0)
                dpi = 96;
            StripRenderer.Draw(_frameDc, _frameBits, width, height, _placed, _hover, _lightTaskbar, _thresholdColors, dpi, _spacing);
            StripRenderer.ApplyHitMask(_frameBits, width, height);

            var origin = new NativePoint();
            var size = new NativeSize { Width = width, Height = height };
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, ref size, _frameDc, ref origin, 0, ref blend, UlwAlpha))
                return;

            _presentedWidth = width;
            _presentedHeight = height;
            _presentedSignature = _signature;
        }
        finally
        {
            _presenting = false;
        }
    }

    private void EnsureFrameBuffer(int width, int height)
    {
        if (_frameDib != IntPtr.Zero && _frameWidth == width && _frameHeight == height)
            return;

        ReleaseFrameBuffer();

        var info = new BitmapInfo();
        info.Header.Size = Marshal.SizeOf<BitmapInfoHeader>();
        info.Header.Width = width;
        info.Header.Height = -height;
        info.Header.Planes = 1;
        info.Header.BitCount = 32;

        var screen = GetDC(IntPtr.Zero);
        try
        {
            _frameDib = CreateDIBSection(screen, ref info, 0, out _frameBits, IntPtr.Zero, 0);
            _frameDc = CreateCompatibleDC(screen);
            if (_frameDib == IntPtr.Zero || _frameDc == IntPtr.Zero)
            {
                ReleaseFrameBuffer();
                return;
            }

            _framePrevious = SelectObject(_frameDc, _frameDib);
            if (_framePrevious == IntPtr.Zero || _framePrevious == new IntPtr(-1))
            {
                ReleaseFrameBuffer();
                return;
            }

            _frameWidth = width;
            _frameHeight = height;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private void ReleaseFrameBuffer()
    {
        if (_frameDc != IntPtr.Zero)
        {
            if (_framePrevious != IntPtr.Zero && _framePrevious != new IntPtr(-1))
                SelectObject(_frameDc, _framePrevious);
            DeleteDC(_frameDc);
            _frameDc = IntPtr.Zero;
            _framePrevious = IntPtr.Zero;
        }
        if (_frameDib != IntPtr.Zero)
        {
            DeleteObject(_frameDib);
            _frameDib = IntPtr.Zero;
        }
        _frameBits = IntPtr.Zero;
        _frameWidth = 0;
        _frameHeight = 0;
    }

    private static int LowWord(IntPtr value) => (short)(value.ToInt32() & 0xFFFF);

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public IntPtr WndProc;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouse
    {
        public int Size;
        public uint Flags;
        public IntPtr Hwnd;
        public uint HoverTime;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadCursor(IntPtr instance, int cursor);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern uint SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(IntPtr hdc, string text, int count, ref NativeRect rect, int format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr destination, ref NativeSize size, IntPtr hdcSrc, ref NativePoint source, int colorKey, ref BlendFunction blend, uint flags);

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouse track);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
