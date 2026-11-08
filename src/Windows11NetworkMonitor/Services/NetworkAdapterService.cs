using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows11NetworkMonitor.Models;

namespace Windows11NetworkMonitor.Services;

/// <summary>
/// Polls all network adapters via System.Net.NetworkInformation.
/// Works for WiFi, Ethernet, USB tethering, hotspot, VPN — no hardcoded types.
/// Auto-selects the adapter that owns the default route (internet).
/// </summary>
public sealed class NetworkAdapterService : IDisposable
{
    private readonly PeriodicTimer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly AppSettings _settings;
    private readonly Dictionary<string, (long Rx, long Tx, DateTimeOffset At)> _prev = new(StringComparer.OrdinalIgnoreCase);

    public event Action<IReadOnlyList<NetworkAdapterInfo>, NetworkAdapterInfo?, double, double>? Changed;

    public NetworkAdapterService(AppSettings settings, TimeSpan? interval = null)
    {
        _settings = settings;
        _timer = new PeriodicTimer(interval ?? TimeSpan.FromMilliseconds(settings.PollIntervalMs));
    }

    public async Task RunAsync()
    {
        await PollOnceAsync();
        while (await _timer.WaitForNextTickAsync(_cts.Token))
        {
            await PollOnceAsync();
        }
    }

    public Task PollOnceAsync()
    {
        var now = DateTimeOffset.Now;
        var adapters = GetAdapters();
        var active = SelectActive(adapters);

        double downBps = 0, upBps = 0;
        var targets = string.Equals(_settings.AdapterMode, "Combined", StringComparison.OrdinalIgnoreCase)
            ? adapters.Where(a => a.IsUp && !a.IsLoopback).ToList()
            : active is null ? new List<NetworkAdapterInfo>() : new List<NetworkAdapterInfo> { active };

        foreach (var a in targets)
        {
            if (!_prev.TryGetValue(a.Id, out var p)) continue;
            var secs = (now - p.At).TotalSeconds;
            if (secs <= 0) continue;
            var dRx = Math.Max(0, a.BytesReceived - p.Rx);
            var dTx = Math.Max(0, a.BytesSent - p.Tx);
            downBps += dRx / secs;
            upBps += dTx / secs;
        }

        foreach (var a in adapters)
            _prev[a.Id] = (a.BytesReceived, a.BytesSent, now);

        Changed?.Invoke(adapters, active, downBps, upBps);
        return Task.CompletedTask;
    }

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            if (!_settings.IncludeVirtualAdapters && IsVirtual(ni)) continue;

            var ipProps = ni.GetIPProperties();
            var unicast = ipProps.UnicastAddresses.ToList();
            var ipv4 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString();
            var ipv6 = unicast.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)?.Address.ToString();
            var stats = ni.GetIPStatistics();

            list.Add(new NetworkAdapterInfo
            {
                Id = ni.Id,
                Name = ni.Name,
                Description = ni.Description,
                IsWiFi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211,
                IsEthernet = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet,
                IsLoopback = ni.NetworkInterfaceType == NetworkInterfaceType.Loopback,
                IsVirtual = IsVirtual(ni),
                IsUp = ni.OperationalStatus == OperationalStatus.Up,
                IPv4 = ipv4,
                IPv6 = ipv6,
                MacAddress = ni.GetPhysicalAddress()?.ToString(),
                BytesReceived = stats.BytesReceived,
                BytesSent = stats.BytesSent,
                SpeedBps = ni.Speed,
            });
        }
        return list;
    }

    private NetworkAdapterInfo? SelectActive(IReadOnlyList<NetworkAdapterInfo> adapters)
    {
        if (!string.IsNullOrEmpty(_settings.AdapterMode)
            && !string.Equals(_settings.AdapterMode, "Auto", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_settings.AdapterMode, "Combined", StringComparison.OrdinalIgnoreCase))
        {
            var want = _settings.AdapterMode.Trim();
            var m = adapters.FirstOrDefault(a =>
                string.Equals(a.Id, want, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Name, want, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Description, want, StringComparison.OrdinalIgnoreCase));
            if (m is not null) return m;
        }

        // Prefer adapter that has an IPv4 and is Up; prefer Ethernet > WiFi if both have traffic
        var up = adapters.Where(a => a.IsUp && !a.IsLoopback).ToList();
        return up.FirstOrDefault(a => a.IPv4 is not null) ?? up.FirstOrDefault(a => a.IPv6 is not null) ?? up.FirstOrDefault();
    }

    private static bool IsVirtual(NetworkInterface ni)
    {
        var n = ni.Name.ToLowerInvariant();
        var d = ni.Description.ToLowerInvariant();
        return n.Contains("vmware") || n.Contains("virtualbox") || n.Contains("hyper-v") || n.Contains("wsl")
            || d.Contains("vmware") || d.Contains("virtual") || d.Contains("tap") || d.Contains("vpn") || d.Contains("tun");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _timer.Dispose();
        _cts.Dispose();
    }
}
