namespace Windows11NetworkMonitor.Services;

/// <summary>Latency probe (ICMP ping). Full WLAN SSID/signal parsing is roadmap work — see docs/ROADMAP.md.</summary>
public static class WifiService
{
    /// <summary>Simple ICMP ping latency in ms, or -1 on failure.</summary>
    public static Task<long> PingAsync(string host = "1.1.1.1", int timeoutMs = 1000)
        => Task.Run(() =>
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(host, timeoutMs);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch { return -1L; }
        });
}
