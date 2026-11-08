using System.Text.Json;

namespace Windows11NetworkMonitor.Services;

/// <summary>Daily usage history (device + per-app) with user-controlled retention (default 90 days).</summary>
public sealed class HistoryStore
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Windows11NetworkMonitor");
    private static readonly string FilePath = Path.Combine(Dir, "usage-history.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private Dictionary<string, DayUsage> _data = new();
    private Dictionary<string, Dictionary<string, DayUsage>> _perApp = new();
    private int _writesSinceSave;

    public sealed class DayUsage
    {
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }
    }

    private sealed class HistoryFile
    {
        public Dictionary<string, DayUsage> Days { get; set; } = new();
        public Dictionary<string, Dictionary<string, DayUsage>> Apps { get; set; } = new();
    }

    public void Load()
    {
        try
        {
            if (!System.IO.File.Exists(FilePath)) return;
            var json = System.IO.File.ReadAllText(FilePath);
            // New nested shape first
            var nested = JsonSerializer.Deserialize<HistoryFile>(json, JsonOpts);
            if (nested is not null && (nested.Days.Count > 0 || nested.Apps.Count > 0))
            {
                _data = nested.Days ?? new();
                _perApp = nested.Apps ?? new();
                return;
            }
            // Legacy flat shape { "2026-01-01": { DownloadBytes, UploadBytes } }
            _data = JsonSerializer.Deserialize<Dictionary<string, DayUsage>>(json, JsonOpts) ?? new();
        }
        catch { _data = new(); }
    }

    public void AddDelta(long downDelta, long upDelta)
    {
        if (downDelta == 0 && upDelta == 0) return;
        AddDay(_data, DateOnly.FromDateTime(DateTime.Now), downDelta, upDelta);
        if (++_writesSinceSave >= 30) Save();
    }

    public void AddPerAppDelta(string app, long downDelta, long upDelta)
    {
        if (string.IsNullOrWhiteSpace(app) || (downDelta == 0 && upDelta == 0)) return;
        var key = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (!_perApp.TryGetValue(app, out var days)) { days = new(); _perApp[app] = days; }
        AddDay(days, DateOnly.FromDateTime(DateTime.Now), downDelta, upDelta);
        if (++_writesSinceSave >= 30) Save();
    }

    private static void AddDay(Dictionary<string, DayUsage> map, DateOnly date, long down, long up)
    {
        var k = date.ToString("yyyy-MM-dd");
        if (!map.TryGetValue(k, out var day)) { day = new DayUsage(); map[k] = day; }
        day.DownloadBytes += Math.Max(0, down);
        day.UploadBytes += Math.Max(0, up);
    }

    public void Prune(int retentionDays)
    {
        // 0 = keep forever
        if (retentionDays <= 0) return;
        var cutoff = DateOnly.FromDateTime(DateTime.Now.AddDays(-retentionDays));
        PruneMap(_data, cutoff);
        foreach (var app in _perApp.Keys.ToList())
        {
            var days = _perApp[app];
            PruneMap(days, cutoff);
            if (days.Count == 0) _perApp.Remove(app);
        }
        Save();
    }

    private static void PruneMap(Dictionary<string, DayUsage> map, DateOnly cutoff)
    {
        var toRemove = map.Keys.Where(k => DateOnly.TryParse(k, out var d) && d < cutoff).ToList();
        foreach (var k in toRemove) map.Remove(k);
    }

    public DayUsage For(DateOnly date) => _data.TryGetValue(date.ToString("yyyy-MM-dd"), out var d) ? d : new DayUsage();

    public IReadOnlyList<(DateOnly Date, long Down, long Up)> Recent(int days)
    {
        var list = new List<(DateOnly, long, long)>();
        for (int i = days - 1; i >= 0; i--)
        {
            var d = DateOnly.FromDateTime(DateTime.Now.AddDays(-i));
            var u = _data.TryGetValue(d.ToString("yyyy-MM-dd"), out var v) ? v : new DayUsage();
            list.Add((d, u.DownloadBytes, u.UploadBytes));
        }
        return list;
    }

    public IReadOnlyList<(string App, long Down, long Up)> TopApps(DateOnly date)
    {
        var key = date.ToString("yyyy-MM-dd");
        var list = new List<(string, long, long)>();
        foreach (var kv in _perApp)
        {
            if (kv.Value.TryGetValue(key, out var u))
                list.Add((kv.Key, u.DownloadBytes, u.UploadBytes));
        }
        return list.OrderByDescending(x => x.Item2 + x.Item3).ToList();
    }

    public void Save()
    {
        _writesSinceSave = 0;
        try
        {
            Directory.CreateDirectory(Dir);
            var payload = new HistoryFile { Days = _data, Apps = _perApp };
            System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, JsonOpts));
        }
        catch { }
    }
}
