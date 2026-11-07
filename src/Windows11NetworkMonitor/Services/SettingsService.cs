using System.Text.Json;
using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Windows11NetworkMonitor");
    private static readonly string File = Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(File)) return new AppSettings();
            var json = File.ReadAllText(File);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            loaded.ResetDailyIfNeeded();
            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(File, JsonSerializer.Serialize(settings, JsonOpts));
    }

    public void ApplyStartWithWindows(bool enable)
    {
        var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key is null) return;
        if (enable)
            key.SetValue("Windows11NetworkMonitor", Environment.ProcessPath ?? string.Empty);
        else
            key.DeleteValue("Windows11NetworkMonitor", throwIfMissing: false);
    }

    public static string SettingsFilePath => File;
}
