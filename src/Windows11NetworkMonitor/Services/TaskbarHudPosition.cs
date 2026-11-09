using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Positions the HUD over the taskbar. Windows 11 has no taskbar control API,
/// so a transparent topmost window is docked using SHAppBarMessage + Monitor info.
/// Handles bottom/top/left/right, DPI, resolution change, auto-hide.
/// </summary>
public static class TaskbarHudPosition
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uState;
        public RECT rc;
        public IntPtr lParam;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern int GetDpiForWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private const uint ABM_GETSTATE = 4;
    private const uint ABM_GETTASKBARPOS = 5;
    private const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    private const uint ABS_AUTOHIDE = 0x1;
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    public static (int x, int y, int width, int height, bool autoHide) GetTaskbarBounds(IntPtr hwnd, string dock)
    {
        var data = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        var state = (uint)SHAppBarMessage(ABM_GETSTATE, ref data);
        bool autoHide = (state & ABS_AUTOHIDE) != 0;

        var pos = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        SHAppBarMessage(ABM_GETTASKBARPOS, ref pos);

        var mon = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        // NEAREST (not PRIMARY): on multi-monitor setups the HUD should dock to
        // the taskbar of the monitor it lives on, not always the primary one.
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        GetMonitorInfo(hMonitor, ref mon);

        RECT rc = pos.rc;
        int w = rc.Right - rc.Left;
        int h = rc.Bottom - rc.Top;

        // Taskbar height is typically 40-48 dip; if ABM fails, assume bottom 48px
        if (w == 0 && h == 0)
        {
            h = 48;
            rc = new RECT { Left = mon.rcWork.Left, Top = mon.rcWork.Bottom, Right = mon.rcWork.Right, Bottom = mon.rcWork.Bottom + h };
        }

        return (rc.Left, rc.Top, w, h, autoHide);
    }

    /// <summary>
    /// Docks a window over the taskbar. Settings offsets are treated as DIPs
    /// (as documented) and scaled to physical pixels by the window's DPI,
    /// so placement is consistent at 125%/150% scaling.
    /// </summary>
    public static void Dock(AppWindow window, IntPtr hwnd, IntPtr topmostHwnd, string dock, int offsetX, int offsetY)
    {
        var (x, y, tw, th, _) = GetTaskbarBounds(hwnd, dock);
        var size = window.Size;
        var dpi = topmostHwnd != IntPtr.Zero ? GetDpiForWindow(topmostHwnd) : 96;
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        var ox = (int)Math.Round(offsetX * scale);
        var oy = (int)Math.Round(offsetY * scale);
        var pos = new Windows.Graphics.PointInt32();

        switch (dock)
        {
            case "Top":
                pos = new Windows.Graphics.PointInt32(x + 8 + ox, y + Math.Max(0, (th - size.Height) / 2) + oy);
                break;
            case "Left":
            case "Right":
                // Vertical-centered like Top/Bottom so the HUD floats over the
                // taskbar strip (vertical taskbars are tall; bottom taskbars
                // clamp the same as Top/Bottom via Max(0, ...)).
                pos = new Windows.Graphics.PointInt32(
                    dock == "Left" ? x + 8 + ox : x + tw - size.Width - 8 + ox,
                    y + Math.Max(0, (th - size.Height) / 2) + oy);
                break;
            default: // Bottom (Windows 11) - dock at right, just left of system tray (clear of ^ overflow)
                pos = new Windows.Graphics.PointInt32(x + tw - size.Width - 190 + ox, y + Math.Max(0, (th - size.Height) / 2) + oy);
                break;
        }
        window.Move(pos);
    }
}
