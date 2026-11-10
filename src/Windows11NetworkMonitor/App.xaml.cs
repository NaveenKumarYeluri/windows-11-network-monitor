using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows11NetworkMonitor.Models;
using Windows11NetworkMonitor.Services;

namespace Windows11NetworkMonitor;

public partial class App : Application
{
    private static DispatcherQueue? _dispatcher;
    private static AppSettings? _settings;
    private static NetworkAdapterService? _adapterService;
    private static MainWindow? _mainWindow;
    private static Mutex? _singleInstanceMutex;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance: a second copy would double-count usage into the
        // settings/history files and race each other's writes.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\Windows11NetworkMonitor.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            ActivateExistingInstance();
            Environment.Exit(0);
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        var store = new SettingsService();
        _settings = store.Load();
        _adapterService = new NetworkAdapterService(_settings);

        _mainWindow = new MainWindow(_settings, _adapterService);
        _mainWindow.Activate();
        _mainWindow.InitTray();
        _mainWindow.RunUpdateChecks();

        _ = _adapterService.RunAsync();
    }

    private static void ActivateExistingInstance()
    {
        // FindWindow CANNOT see WinUI 3 windows: it returns 0 even for an exact
        // title match (verified empirically), so the old code silently did
        // nothing. Enumerate instead and post WM_RESTORE; the running
        // instance's tray subclass handles it via ShowFromTray, which copes
        // with the window being hidden to tray.
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (sb.ToString() == "Windows 11 Network Monitor") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (found != IntPtr.Zero)
            PostMessage(found, TrayIconService.WM_RESTORE, IntPtr.Zero, IntPtr.Zero);
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static DispatcherQueue? Dispatcher => _dispatcher;
    public static AppSettings? Settings => _settings;
}
