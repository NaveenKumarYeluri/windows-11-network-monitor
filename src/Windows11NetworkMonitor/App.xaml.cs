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

        _ = _adapterService.RunAsync();
    }

    private static void ActivateExistingInstance()
    {
        var hwnd = FindWindow(null, "Windows 11 Network Monitor");
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public static DispatcherQueue? Dispatcher => _dispatcher;
    public static AppSettings? Settings => _settings;
}
