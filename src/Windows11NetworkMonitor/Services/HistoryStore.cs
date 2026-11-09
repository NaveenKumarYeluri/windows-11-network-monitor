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
    // Single lock guarding _data/_perApp: writes come from the poll timer thread,
    // reads (Recent/TopApps) and Save come from the UI thread.
    private readonly object _sync = new();

    public sealed class DayUsage
    {
        public long DownloadBytes { get; set; }
        public long UploadBytes { get; set; }
    }

    /// <summary>Flat row for CSV export: one per device-day and per app-day.</summary>
    public sealed record HistoryCsvRow(string Type, string Date, string App, long DownloadBytes, long UploadBytes);

    private sealed class HistoryFile
    {
        public Dictionary<string, DayUsage> Days { get; set; } = new();
        public Dictionary<string, Dictionary<string, DayUsage>> Apps { get; set; } = new();
    }

    public void Load()
    {
        lock (_sync)
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
    }

    public void AddDelta(long downDelta, long upDelta)
    {
        if (downDelta == 0 && upDelta == 0) return;
        lock (_sync)
        {
            AddDay(_data, DateOnly.FromDateTime(DateTime.Now), downDelta, upDelta);
            if (++_writesSinceSave >= 30) Save();
        }
    }

    public void AddPerAppDelta(string app, long downDelta, long upDelta)
    {
        if (string.IsNullOrWhiteSpace(app) || (downDelta == 0 && upDelta == 0)) return;
        lock (_sync)
        {
            var key = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
            if (!_perApp.TryGetValue(app, out var days)) { days = new(); _perApp[app] = days; }
            AddDay(days, DateOnly.FromDateTime(DateTime.Now), downDelta, upDelta);
            if (++_writesSinceSave >= 30) Save();
        }
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
        lock (_sync)
        {
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
    }

    private static void PruneMap(Dictionary<string, DayUsage> map, DateOnly cutoff)
    {
        var toRemove = map.Keys.Where(k => DateOnly.TryParse(k, out var d) && d < cutoff).ToList();
        foreach (var k in toRemove) map.Remove(k);
    }

    public IReadOnlyList<(DateOnly Date, long Down, long Up)> Recent(int days)
    {
        lock (_sync)
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
    }

    public IReadOnlyList<(string App, long Down, long Up)> TopApps(DateOnly date)
    {
        var key = date.ToString("yyyy-MM-dd");
        lock (_sync)
        {
            var list = new List<(string, long, long)>();
            foreach (var kv in _perApp)
            {
                if (kv.Value.TryGetValue(key, out var u))
                    list.Add((kv.Key, u.DownloadBytes, u.UploadBytes));
            }
            return list.OrderByDescending(x => x.Item2 + x.Item3).ToList();
        }
    }

    /// <summary>All stored data as flat rows (device days + per-app days) for CSV export.</summary>
    public IReadOnlyList<HistoryCsvRow> ExportRows()
    {
        lock (_sync)
        {
            var rows = new List<HistoryCsvRow>();
            foreach (var kv in _data)
                rows.Add(new HistoryCsvRow("Device", kv.Key, string.Empty, kv.Value.DownloadBytes, kv.Value.UploadBytes));
            foreach (var app in _perApp)
                foreach (var day in app.Value)
                    rows.Add(new HistoryCsvRow("App", day.Key, app.Key, day.Value.DownloadBytes, day.Value.UploadBytes));
            // Date strings are yyyy-MM-dd → stable ascending order for CSV consumers.
            return rows.OrderBy(r => r.Date, StringComparer.Ordinal)
                       .ThenBy(r => r.Type, StringComparer.Ordinal)
                       .ThenBy(r => r.App, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }
    }

    public void Save()
    {
        // Callers may or may not hold the lock; Monitor is reentrant so this is safe either way.
        lock (_sync)
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
}
