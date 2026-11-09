using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Win32 system tray icon (Shell_NotifyIcon) with custom-drawn speed text.
/// Windows 11 hides new icons under ^ (overflow) until you promote them - the
/// icon is still working even when it looks "not working" at first glance.
/// Clicks are received via a comctl32 window subclass of the main window:
/// the callback message is posted to the owning window's WndProc, which WinUI
/// 3 exposes no other way. The subclass is also where we re-add the icon when
/// the shell broadcasts TaskbarCreated (Explorer restart) - otherwise the
/// icon would stay missing until the app is relaunched.
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
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const uint WM_APP = 0x8000;
    public const uint WM_TRAYICON = WM_APP + 1;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

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
        public uint uVersion; // union with uTimeout; named uVersion so NIM_SETVERSION is valid
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    // --- WndProc subclassing (comctl32) so tray messages actually reach us ---
    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    // Must be kept referenced in a field: the marshaled function pointer inside
    // the delegate would otherwise be collected while shell messages still come in.
    private readonly SUBCLASSPROC _subclassProc;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly IntPtr _hwnd;
    private readonly uint _id = 1;
    private IntPtr _lastHicon; // GDI icon we own (created via GetHicon) - must be DestroyIcon'd
    private bool _added;
    private uint _taskbarCreatedMsg;
    public bool IsVisible => _added;

    public event Action? Clicked;
    public event Action? RightClick;

    public TrayIconService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _taskbarCreatedMsg = RegisterWindowMessage("TaskbarCreated");
        _subclassProc = WndProcImpl;
        SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero);
    }

    private IntPtr WndProcImpl(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        try
        {
            if (uMsg == WM_TRAYICON)
            {
                // Mouse message arrives in the low word of lParam (both legacy
                // and NOTIFYICON_VERSION_4 layouts share that position).
                var mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse == WM_LBUTTONUP) Clicked?.Invoke();
                else if (mouse == WM_RBUTTONUP) RightClick?.Invoke();
                return IntPtr.Zero;
            }
            if (_taskbarCreatedMsg != 0 && uMsg == _taskbarCreatedMsg && _added)
            {
                // Explorer restarted: the shell dropped every tray icon.
                // Re-register ours instead of silently disappearing.
                _added = false;
                Show();
                return IntPtr.Zero;
            }
        }
        catch { }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
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
        // Remove any stale registration first (e.g. after Explorer restart races)
        RemoveSilently();

        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
            uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = SystemIcons.Application.Handle, // shared icon; not owned by us
            szTip = "Windows 11 Network Monitor"
        };
        Shell_NotifyIcon(NIM_ADD, ref data);

        // Enable NOTIFYICON_VERSION_4 so callbacks are well-defined; harmless if unsupported.
        var sv = data;
        sv.uVersion = NOTIFYICON_VERSION_4;
        sv.uFlags = 0;
        Shell_NotifyIcon(NIM_SETVERSION, ref sv);

        _added = true;
    }

    private void RemoveSilently()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = _id,
        };
        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    public void Update(double downBps, double upBps)
    {
        if (!_added) return;
        var (downVal, downUnit) = SplitShort(downBps);
        var (upVal, upUnit) = SplitShort(upBps);
        var icon = DrawIcon($"{downVal}{downUnit}", $"{upVal}{upUnit}");
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
        DestroyIcon(_lastHicon); // Icon.FromHandle does NOT own the handle: only we can free it
        _lastHicon = icon.Handle;
    }

    private static (string value, string unit) SplitShort(double bps)
    {
        if (bps < 1024) return ($"{bps:F0}", "B");
        if (bps < 1024 * 1024) return ($"{bps / 1024:F1}", "K");
        if (bps < 1024L * 1024 * 1024) return ($"{bps / (1024 * 1024):F1}", "M");
        return ($"{bps / (1024L * 1024 * 1024):F2}", "G");
    }

    /// <summary>Draws the two metric lines. Caller MUST DestroyIcon the resulting handle.</summary>
    internal Icon DrawIcon(string line1, string line2)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(30, 30, 30));
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            g.DrawString(line1, font, brush, 2, 12);
            g.DrawString(line2, font, brush, 2, 34);
        }
        return Icon.FromHandle(bmp.GetHicon()); // not owned → destroyed explicitly by caller
    }

    public void Dispose()
    {
        Hide();
        DestroyIcon(_lastHicon);
        _lastHicon = IntPtr.Zero;
    }
}
