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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszTitle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
    /// <summary>
    /// Docks a window relative to the screen edges and the taskbar:
    /// - Bottom: over the taskbar strip, just left of the tray (clock + ^ chevron),
    ///   reserving the tray's actual measured width (DPI-independent).
    /// - Top: top screen edge, centered; Left/Right: screen sides, vertically centered
    ///   within the work area. (Previously every side was computed against the
    ///   taskbar rect, which made Top and Left collapse to the same spot on a
    ///   bottom taskbar.)
    /// Settings offsets are DIPs, scaled to physical pixels by the window's DPI.
    /// </summary>
    public static void Dock(AppWindow window, IntPtr hwnd, IntPtr topmostHwnd, string dock, int offsetX, int offsetY)
    {
        var mon = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mon);
        var screen = mon.rcMonitor;
        var work = mon.rcWork;

        var size = window.Size;
        var dpi = topmostHwnd != IntPtr.Zero ? GetDpiForWindow(topmostHwnd) : 96;
        var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
        var ox = (int)Math.Round(offsetX * scale);
        var oy = (int)Math.Round(offsetY * scale);
        var (tx, ty, tw, th, _) = GetTaskbarBounds(hwnd, dock);
        var pos = new Windows.Graphics.PointInt32();

        switch (dock)
        {
            case "Top":
                if (ty <= screen.Top + 2 && th > 0)
                {
                    // The taskbar itself is at the top edge here — use the
                    // tray-adjacent strip placement instead of covering it.
                    pos = new Windows.Graphics.PointInt32(
                        tx + tw - size.Width - GetTrayReservePx(scale) + ox,
                        ty + Math.Max(0, (th - size.Height) / 2) + oy);
                }
                else
                {
                    // Top edge of the screen, centered horizontally.
                    pos = new Windows.Graphics.PointInt32(
                        screen.Left + (screen.Right - screen.Left - size.Width) / 2 + ox,
                        screen.Top + 8 + oy);
                }
                break;
            case "Left":
                // Left screen edge, centered vertically in the work area.
                pos = new Windows.Graphics.PointInt32(
                    screen.Left + 8 + ox,
                    work.Top + (work.Bottom - work.Top - size.Height) / 2 + oy);
                break;
            case "Right":
                // Right screen edge, centered vertically in the work area.
                pos = new Windows.Graphics.PointInt32(
                    screen.Right - size.Width - 8 + ox,
                    work.Top + (work.Bottom - work.Top - size.Height) / 2 + oy);
                break;
            default: // Bottom (Windows 11 default) - over the taskbar, clear of the tray
                pos = new Windows.Graphics.PointInt32(
                    tx + tw - size.Width - GetTrayReservePx(scale) + ox,
                    ty + Math.Max(0, (th - size.Height) / 2) + oy);
                break;
        }
        window.Move(pos);
    }

    /// <summary>
    /// Pixels to reserve between the bottom-docked HUD and the tray's right
    /// cluster (clock + ^ overflow chevron). Measured from the actual tray
    /// window so it automatically follows DPI / tray contents; falls back to
    /// a DPI-scaled 190 px when the shell windows can't be found.
    /// </summary>
    private static int GetTrayReservePx(double scale)
    {
        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero)
        {
            var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero && GetWindowRect(notify, out var r) && r.Right > r.Left)
                return (r.Right - r.Left) + 8;
        }
        return (int)Math.Round(190 * scale);
    }
}
