using System.Runtime.InteropServices;
using Microsoft.Win32;
using StatsBar.Interop;
using StatsBar.Settings;

namespace StatsBar.Taskbar;

public static class TaskbarEmbedder
{
    private static long _lastRaiseTick;

    public static bool TryAttach(IntPtr windowHandle, out int win32Error)
    {
        win32Error = 0;
        var taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            if (win32Error == 0)
                win32Error = 1400;
            return false;
        }

        var style = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlStyle);
        style &= ~(NativeMethods.WsPopup | NativeMethods.WsCaption | NativeMethods.WsThickFrame | NativeMethods.WsMinimizeBox | NativeMethods.WsMaximizeBox | NativeMethods.WsSysMenu);
        style |= NativeMethods.WsChild;
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlStyle, (nint)style);

        var exStyle = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle);
        exStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        exStyle &= ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle, (nint)exStyle);

        var parent = NativeMethods.SetParent(windowHandle, taskbar);
        if (parent == IntPtr.Zero)
        {
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        NativeMethods.SetWindowPos(windowHandle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);
        NativeMethods.ShowWindow(windowHandle, NativeMethods.SwShowNa);
        return true;
    }

    public static bool IsAttached(IntPtr windowHandle)
    {
        if (!NativeMethods.IsWindow(windowHandle))
            return false;

        var parent = NativeMethods.GetParent(windowHandle);
        var taskbar = FindTaskbar();
        return parent == IntPtr.Zero || parent == taskbar;
    }

    public static bool IsTaskbarVisible()
    {
        var taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar))
            return false;
        if (!NativeMethods.GetWindowRect(taskbar, out var rect))
            return false;

        var monitor = NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        var visibleWidth = Overlap(rect.Left, rect.Right, info.Monitor.Left, info.Monitor.Right);
        var visibleHeight = Overlap(rect.Top, rect.Bottom, info.Monitor.Top, info.Monitor.Bottom);
        return visibleWidth > 40 && visibleHeight > 20;
    }

    public static void SetStripShown(IntPtr windowHandle, bool shown)
    {
        var visible = NativeMethods.IsWindowVisible(windowHandle);
        if (shown && !visible)
            NativeMethods.ShowWindow(windowHandle, NativeMethods.SwShowNa);
        else if (!shown && visible)
            NativeMethods.ShowWindow(windowHandle, NativeMethods.SwHide);
    }

    public static void SetStripLayer(IntPtr windowHandle, bool aboveTaskbar)
    {
        var style = (long)NativeMethods.GetWindowLongPtr(windowHandle, NativeMethods.GwlExStyle);
        var isTopmost = (style & NativeMethods.WsExTopmost) != 0;
        if (isTopmost == aboveTaskbar)
            return;

        NativeMethods.SetWindowPos(
            windowHandle,
            aboveTaskbar ? new IntPtr(-1) : new IntPtr(-2),
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public static bool ShouldConcealStrip()
    {
        if (!IsTaskbarVisible())
            return true;

        var taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero || !TryGetMonitorRect(taskbar, out var monitor))
            return false;

        if (ExclusiveFullscreenOnMonitor(taskbar))
            return true;

        return WindowCoversMonitor(NativeMethods.GetForegroundWindow(), monitor)
            || MonitorHasCoveringWindow(monitor);
    }

    public static bool Move(IntPtr windowHandle, StripAnchor anchor, int stripWidth, int nudgePixels)
    {
        var taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var screen))
            return false;

        var dpi = NativeMethods.GetDpiForWindow(taskbar);
        if (dpi == 0)
            dpi = 96;
        var scale = dpi / 96d;
        if (stripWidth < 80)
            stripWidth = (int)Math.Round(210 * scale);
        var gap = (int)Math.Round(8 * scale);
        var child = NativeMethods.GetParent(windowHandle) == taskbar;
        int x;
        int y;
        int height;

        if (child)
        {
            if (!NativeMethods.GetClientRect(taskbar, out var client))
                return false;
            var barWidth = client.Right - client.Left;
            height = client.Bottom - client.Top;
            y = 0;
            x = anchor == StripAnchor.Left
                ? LeftEdge(scale) + nudgePixels
                : RightEdge(taskbar, barWidth, stripWidth, gap, scale) + nudgePixels;
            if (x < 0)
                x = 0;
            if (x + stripWidth > barWidth)
                x = Math.Max(0, barWidth - stripWidth);
        }
        else
        {
            height = screen.Bottom - screen.Top;
            y = screen.Top;
            if (anchor == StripAnchor.Left)
            {
                x = screen.Left + LeftEdge(scale) + nudgePixels;
            }
            else
            {
                var tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
                if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect) && trayRect.Right - trayRect.Left > 8)
                    x = trayRect.Left - stripWidth - gap + nudgePixels;
                else
                    x = screen.Right - stripWidth - (int)Math.Round(140 * scale) + nudgePixels;
            }

            if (x < screen.Left)
                x = screen.Left;
            if (x + stripWidth > screen.Right)
                x = Math.Max(screen.Left, screen.Right - stripWidth);
        }

        if (height < 20)
            return false;

        var samePlace = NativeMethods.GetWindowRect(windowHandle, out var current)
            && current.Left == x
            && current.Top == y
            && current.Right - current.Left == stripWidth
            && current.Bottom - current.Top == height;
        var covered = IsTaskbarAbove(windowHandle, taskbar);
        if (samePlace && !covered)
            return true;

        if (samePlace && Environment.TickCount64 - _lastRaiseTick < 2000)
            return true;

        if (covered)
            _lastRaiseTick = Environment.TickCount64;

        return NativeMethods.SetWindowPos(
            windowHandle,
            covered ? new IntPtr(-1) : IntPtr.Zero,
            x,
            y,
            stripWidth,
            height,
            NativeMethods.SwpNoActivate | (covered ? 0 : NativeMethods.SwpNoZOrder));
    }

    public static bool TryGetScreenRect(IntPtr windowHandle, out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        if (!NativeMethods.GetWindowRect(windowHandle, out var rect))
            return false;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return false;

        left = rect.Left;
        top = rect.Top;
        right = rect.Right;
        bottom = rect.Bottom;
        return true;
    }

    public static bool IsTaskbarAtTop()
    {
        var taskbar = FindTaskbar();
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var rect))
            return false;

        return rect.Top <= 2 && rect.Bottom < rect.Top + 200;
    }

    private static int LeftEdge(double scale)
    {
        var inset = ReadTaskbarAlignment() == 0 ? 48 : 8;
        return (int)Math.Round(inset * scale);
    }

    private static int RightEdge(IntPtr taskbar, int barWidth, int stripWidth, int gap, double scale)
    {
        var tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect) && trayRect.Right - trayRect.Left > 8)
        {
            var point = new NativeMethods.Point { X = trayRect.Left, Y = trayRect.Top };
            if (NativeMethods.ScreenToClient(taskbar, ref point))
                return point.X - stripWidth - gap;
        }

        return barWidth - stripWidth - (int)Math.Round(140 * scale);
    }

    private static int ReadTaskbarAlignment()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
        return key?.GetValue("TaskbarAl") is int alignment ? alignment : 1;
    }

    private static IntPtr FindTaskbar()
    {
        return NativeMethods.FindWindow("Shell_TrayWnd", null);
    }

    private static int Overlap(int start, int end, int limitStart, int limitEnd)
    {
        var left = Math.Max(start, limitStart);
        var right = Math.Min(end, limitEnd);
        return Math.Max(0, right - left);
    }

    private static bool ExclusiveFullscreenOnMonitor(IntPtr taskbar)
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) != 0 || state != 3)
            return false;

        return ForegroundIsOnMonitor(taskbar);
    }

    private static bool ForegroundIsOnMonitor(IntPtr taskbar)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || IsOwnProcess(foreground))
            return false;

        var taskbarMonitor = NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToNearest);
        var foregroundMonitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
        return taskbarMonitor != IntPtr.Zero && taskbarMonitor == foregroundMonitor;
    }

    private static bool MonitorHasCoveringWindow(NativeMethods.Rect monitor)
    {
        var covered = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (WindowCoversMonitor(hwnd, monitor))
            {
                covered = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return covered;
    }

    private static bool WindowCoversMonitor(IntPtr hwnd, NativeMethods.Rect monitor)
    {
        if (hwnd == IntPtr.Zero || IsOwnProcess(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || IsCloaked(hwnd) || IsOverlay(hwnd))
            return false;

        var className = new System.Text.StringBuilder(256);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        var name = className.ToString();
        if (name is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return false;

        const int slop = 4;
        return rect.Left <= monitor.Left + slop
            && rect.Top <= monitor.Top + slop
            && rect.Right >= monitor.Right - slop
            && rect.Bottom >= monitor.Bottom - slop;
    }

    private static bool IsOverlay(IntPtr hwnd)
    {
        var style = (long)NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle);
        if ((style & 0x20L) != 0)
            return true;

        var nonActivating = (style & NativeMethods.WsExNoActivate) != 0;
        var layeredOrTool = (style & (0x00080000L | NativeMethods.WsExToolWindow)) != 0;
        return nonActivating && layeredOrTool;
    }

    private static bool IsOwnProcess(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        return NativeMethods.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    private static bool TryGetMonitorRect(IntPtr window, out NativeMethods.Rect monitor)
    {
        monitor = default;
        var handle = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
            return false;

        monitor = info.Monitor;
        return true;
    }

    private static bool IsTaskbarAbove(IntPtr windowHandle, IntPtr taskbar)
    {
        var above = windowHandle;
        for (var i = 0; i < 32; i++)
        {
            above = NativeMethods.GetWindow(above, NativeMethods.GwHwndPrev);
            if (above == IntPtr.Zero)
                return false;
            if (above == taskbar)
                return true;
        }

        return false;
    }
}
