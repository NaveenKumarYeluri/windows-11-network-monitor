using System.Runtime.InteropServices;

namespace Windows11NetworkMonitor.Services;

/// <summary>WiFi SSID + signal via wlanapi.dll. Returns null if not connected / unsupported.</summary>
public static class WifiService
{
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, int OpCode, IntPtr pReserved, out IntPtr ppData, out uint pdwDataSize);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    public static (string? Ssid, int? SignalPercent) GetCurrentWifi()
    {
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out var client) != 0) return (null, null);
            try
            {
                if (WlanEnumInterfaces(client, out var listPtr) != 0) return (null, null);
                try
                {
                    // WLAN_INTERFACE_INFO_LIST layout: dwNumberOfItems, dwIndex, header(8+16 bytes)
                    // Iterate items starting after header. See winnetwk docs.
                    int header = Marshal.ReadInt32(listPtr); // number of items
                    long itemPtr = listPtr.ToInt64() + 8; // dwNumberOfItems + dwIndex
                    for (int i = 0; i < header; i++)
                    {
                        var guidBytes = new byte[16];
                        Marshal.Copy((IntPtr)(itemPtr + 8), guidBytes, 0, 16);
                        var guid = new Guid(guidBytes);
                        // WLAN_OPCODE_VALUE = 13 (wlan_opcode_current_connection)
                        if (WlanQueryInterface(client, ref guid, 13, IntPtr.Zero, out var dataPtr, out uint dataSize) == 0 && dataPtr != IntPtr.Zero)
                        {
                            // WLAN_CONNECTION_ATTRIBUTES is complex; fallback: read SSID string from data
                            // For v1 we just report "WiFi connected" — full SSID parsing is in roadmap.
                            WlanFreeMemory(dataPtr);
                            return ("WiFi connected", null);
                        }
                    }
                    return (null, null);
                }
                finally { WlanFreeMemory(listPtr); }
            }
            finally { WlanCloseHandle(client, IntPtr.Zero); }
        }
        catch
        {
            return (null, null);
        }
    }

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
