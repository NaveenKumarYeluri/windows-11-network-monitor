# Architecture

## Why no true taskbar extension?

Windows 10 exposed Deskband COM objects. Windows 11 moved the taskbar to XAML (`StartMenuExperienceHost` / `ShellExperienceHost`) and removed that API. There is no supported way to host a custom control inside the taskbar.

This app uses the two pragmatic surrogates:

1. **Tray icon** (official) — `Shell_NotifyIcon` with a custom-drawn bitmap so speed text is visible.
2. **HUD window** — transparent, `WS_EX_TOPMOST`, borderless, docked over the taskbar via `SHAppBarMessage(ABM_GETSTATE / ABM_GETTASKBARPOS)` + `MonitorFromWindow`.

## Components

| Component | File | Responsibility |
|---|---|---|
| `NetworkAdapterService` | `Services/NetworkAdapterService.cs` | Poll `NetworkInterface.GetAllNetworkInterfaces()`, compute deltas, auto-select active adapter |
| `SpeedHistory` | `Services/SpeedHistory.cs` | 120-sample ring buffer + peak/avg |
| `WifiService` | `Services/WifiService.cs` | SSID/signal via `wlanapi.dll`, ping latency |
| `TrayIconService` | `Services/TrayIconService.cs` | Win32 tray, custom-drawn text icon |
| `TaskbarHudPosition` | `Services/TaskbarHudPosition.cs` | Dock HUD to taskbar (bottom/top/left/right) |
| `HudWindow` | `Views/HudWindow.cs` | Transparent floating HUD |
| `MainWindow` | `MainWindow.xaml(.cs)` | Dashboard, graph, adapter list, stats |
| `SettingsWindow` | `Views/SettingsWindow.xaml(.cs)` | Fluent settings |
| `SettingsService` | `Services/SettingsService.cs` | JSON persistence + `Run` key autostart |

## Data flow

```
NetworkInterface (iphlpapi) --poll 500ms-1s--> SpeedCalculator (delta / interval)
  --> SpeedHistory (ring buffer, 60s)
      --> TrayIconService.Update()   (custom icon bitmap)
      --> HudWindow.Update()         (text)
      --> MainWindow.DrawGraph()     (Polyline)
      --> Stats (session / daily / peak)
```

## Auto-detect logic

- Enumerate adapters where `OperationalStatus == Up`, exclude Loopback, optionally exclude Virtual.
- Speed = `(current.InOctets - prev.InOctets) / interval`.
- Active = adapter with IPv4 (fallback IPv6). In `Combined` mode sum all physical adapters.
- Handles WiFi / Ethernet / USB tethering / hotspot uniformly; switches automatically when the default route changes.

## Future

- LiveChartsCore graph (replace Canvas/Polyline)
- Full `GetIfTable2` native path for 64-bit counters
- Per-adapter HUD profiles, Mica blur, acrylic
- MSIX + Store signing
- Per-process traffic (WFP)
