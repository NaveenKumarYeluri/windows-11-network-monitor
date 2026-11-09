using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Windows11NetworkMonitor.Services;

/// <summary>Snapshot of open connections grouped by owning process.</summary>
public sealed class AppUsage
{
    public required string ProcessName { get; init; }
    public required int Pid { get; init; }
    public int TcpConnections { get; init; }
    public int UdpEndpoints { get; init; }
    public int TotalConnections => TcpConnections + UdpEndpoints;
}

/// <summary>
/// Enumerates TCP/UDP owner PIDs via GetExtendedTcpTable. Matches process
/// names via Process.GetProcessById, falling back to "PID N" for protected
/// processes (System / elevated) without dropping the row — otherwise most
/// machines look empty. Per-app bytes remain an estimate (share by conn count).
/// </summary>
public sealed class PerAppTracker
{
    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, uint reserved);

    private const int TCP_TABLE_OWNER_PID = 5;
    private const int UDP_TABLE_OWNER_PID = 2;
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    public bool TrackPerApp { get; set; } = true;

    public IReadOnlyList<AppUsage> Snapshot()
    {
        if (!TrackPerApp) return Array.Empty<AppUsage>();
        var counts = new Dictionary<int, (int tcp, int udp)>();
        CountTcp(counts, AF_INET);
        CountTcp(counts, AF_INET6);
        CountUdp(counts, AF_INET);
        CountUdp(counts, AF_INET6);

        var list = new List<AppUsage>();
        foreach (var kv in counts)
        {
            string name;
            try { name = Process.GetProcessById(kv.Key).ProcessName; }
            catch { name = kv.Key == 0 || kv.Key == 4 ? "System" : $"PID {kv.Key}"; }
            list.Add(new AppUsage
            {
                Pid = kv.Key,
                ProcessName = name,
                TcpConnections = kv.Value.tcp,
                UdpEndpoints = kv.Value.udp
            });
        }
        return list.OrderByDescending(a => a.TotalConnections).ToList();
    }

                private static void CountTcp(Dictionary<int, (int tcp, int udp)> counts, int af)
                {
                    try
                    {
                        int size = 0;
                        var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID, 0);
                        // First call returns ERROR_INSUFFICIENT_BUFFER (122) with required size — that is success for sizing.
                        // Do not bail on 122, only on other errors.
                        if (rc != ERROR_INSUFFICIENT_BUFFER && rc != 0) return;
                        if (size <= 0) return;
                        var buf = Marshal.AllocHGlobal(size);
                        try
                        {
                            rc = GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID, 0);
                            if (rc != 0) return;
                            int n = Marshal.ReadInt32(buf);
                            // MIB_TCPROW_OWNER_PID = 24 bytes (PID at +20)
                            // MIB_TCP6ROW_OWNER_PID = 52 bytes (PID at +48)
                            int stride = af == AF_INET6 ? 52 : 24;
                            int pidDelta = af == AF_INET6 ? 48 : 20;
                            for (int i = 0; i < n; i++)
                            {
                                long rowOff = 4L + i * stride;
                                int pidOff = (int)(rowOff + pidDelta);
                                if (pidOff + 4 > size) break;
                                var pid = Marshal.ReadInt32(buf, pidOff);
                                if (pid <= 0) continue;
                                counts.TryGetValue(pid, out var v);
                                v.tcp++;
                                counts[pid] = v;
                            }
                        }
                        finally { Marshal.FreeHGlobal(buf); }
                    }
                    catch { }
                }

    private static void CountUdp(Dictionary<int, (int tcp, int udp)> counts, int af)
    {
        try
        {
            int size = 0;
            var rc = GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
            if (rc != ERROR_INSUFFICIENT_BUFFER && rc != 0) return;
            if (size <= 0) return;
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                rc = GetExtendedTcpTable(buf, ref size, false, af, UDP_TABLE_OWNER_PID, 0);
                if (rc != 0) return;
                int n = Marshal.ReadInt32(buf);
                int stride = af == AF_INET6 ? 28 : 12;
                for (int i = 0; i < n; i++)
                {
                    long rowOff = 4L + i * stride;
                    int pidOff = (int)(rowOff + (af == AF_INET6 ? 24 : 8));
                    if (pidOff + 4 > size) break;
                    var pid = Marshal.ReadInt32(buf, pidOff);
                    if (pid <= 0) continue;
                    counts.TryGetValue(pid, out var v);
                    v.udp++;
                    counts[pid] = v;
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { }
    }
}
