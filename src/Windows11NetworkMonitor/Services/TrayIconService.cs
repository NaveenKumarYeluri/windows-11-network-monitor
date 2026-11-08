using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Win32 system tray icon (Shell_NotifyIcon) with custom-drawn speed text.
/// Windows 11 hides new icons under ^ (overflow) until you promote them - the
/// icon is still working even when it looks "not working" at first glance.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 2;
    private const uint NIM_DELETE = 1;
    private const uint NIF_ICON = 0x1;
    private const uint NIF_MESSAGE = 0x4;
    private const uint NIF_TIP = 0x8;
    private const uint NIM_SETVERSION = 4;

    private const uint WM_APP = 0x8000;
    public const uint WM_TRAYICON = WM_APP + 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    private readonly IntPtr _hwnd;
    private readonly uint _id = 1;
    private Icon? _icon;
    private bool _added;
    public bool IsVisible => _added;

    public event Action? Clicked;
    public event Action? RightClick;

    public TrayIconService(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public void Hide()
    {
        if (!_added) return;
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
    }

    public void Show()
    {
        if (_added) return;
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = SystemIcons.Application.Handle,
            szTip = "Windows 11 Network Monitor"
        };
        Shell_NotifyIcon(NIM_ADD, ref data);
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
        _added = true;
    }

    public void Update(double downBps, double upBps)
    {
        if (!_added) return;
        var (down, _) = SplitShort(downBps);
        var text = $"↓{down} ↑{SplitShort(upBps).down}";
        var icon = DrawIcon(text);
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_ICON | NIF_TIP,
            hIcon = icon.Handle,
            szTip = $"↓ {SpeedFormatter.Format(downBps)}  ↑ {SpeedFormatter.Format(upBps)}"
        };
        Shell_NotifyIcon(NIM_MODIFY, ref data);
        _icon?.Dispose();
        _icon = icon;
    }

    private static (string down, string unit) SplitShort(double bps)
    {
        if (bps < 1024) return ($"{bps:F0}", "B");
        if (bps < 1024 * 1024) return ($"{bps / 1024:F1}", "K");
        return ($"{bps / (1024 * 1024):F1}", "M");
    }

    internal Icon DrawIcon(string text)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(30, 30, 30));
            using var font = new Font("Segoe UI", 14f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString(text, font, brush, 2, 20);
            g.DrawString("MB/s", font, brush, 2, 38);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void HandleCallback(uint msg, IntPtr wParam)
    {
        if (msg == 0x0202 /* WM_LBUTTONUP */) Clicked?.Invoke();
        if (wParam.ToInt64() == 0x0205 /* WM_RBUTTONUP */) RightClick?.Invoke();
    }

    public void Dispose()
    {
        Hide();
        _icon?.Dispose();
    }
}
