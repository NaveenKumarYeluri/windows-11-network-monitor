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
| `SpeedHistory` | `Services/SpeedHistory.cs` | 120-sample ring buffer + peak/avg; `SpeedFormatter` (`B/s → GB/s`) |
| `WifiService` | `Services/WifiService.cs` | ICMP ping latency only (SSID/signal is future work) |
| `TrayIconService` | `Services/TrayIconService.cs` | Win32 tray, custom-drawn text icon |
| `TaskbarHudPosition` | `Services/TaskbarHudPosition.cs` | Dock HUD to taskbar (Win11: bottom, offset to clear `^` overflow) |
| `HudWindow` | `Views/HudWindow.cs` | Transparent `150×28` floating HUD, `DispatcherTimer` reposition |
| `MainWindow` | `MainWindow.xaml(.cs)` | Dashboard, graph (Y-axis labels), adapters, statistics (7-day history + top apps), apps-now; `StackPanel` rows inside one outer `ScrollViewer` (no inner list to snap) |
| `SettingsWindow` | `Views/SettingsWindow.xaml(.cs)` | Fluent settings — adapter, poll interval, Theme System/Light/Dark, retention 0–365 d, toggles (HUD/graph/per-app/virtual/autostart) |
| `SettingsService` | `Services/SettingsService.cs` | JSON persistence + `Run` key autostart (`%LOCALAPPDATA%\Windows11NetworkMonitor\settings.json`) |
| `HistoryStore` | `Services/HistoryStore.cs` | Per-day device totals + per-app totals (`usage-history.json`), retention pruning |
| `PerAppTracker` | `Services/PerAppTracker.cs` | `GetExtendedTcpTable` (TCP/UDP, IPv4+IPv6, owner-PID) → process list; bytes attributed by connection share (fractional remainder) |

## Data flow

```
NetworkInterface (iphlpapi) --poll 250–5000 ms--> NetworkAdapterService (delta / interval)
  --> SpeedHistory (ring buffer, 60s) --> MainWindow.DrawGraph() (Polyline + Max/Mid/Min labels)
      --> TrayIconService.Update()   (custom icon bitmap)
      --> HudWindow.Update()         (text)
      --> Stats (session / today / peak / avg)
  --> HistoryStore.AddDelta() (per-day device total, save every 30 writes, prune on load/save)
  --> PerAppTracker.Snapshot() (once per tick, reused for UI + attribution)
      --> HistoryStore.AddPerAppDelta() (fractional accumulator → per-app daily total)
```

## Auto-detect logic

- Enumerate adapters where `OperationalStatus == Up`, exclude Loopback, optionally include Virtual.
- Speed = `(current.InOctets - prev.InOctets) / interval`.
- Active = adapter with IPv4 (fallback IPv6) — first Up in that order. In `Combined` mode sum all Up non-loopback adapters.
- Handles WiFi / Ethernet / USB tethering / hotspot uniformly; switches automatically when the default route changes.

## Per-app attribution (estimate)

Windows exposes no per-socket byte counters to user mode. `PerAppTracker` enumerates owner PIDs via `GetExtendedTcpTable` and `MainWindow` splits each interface tick's byte delta proportionally to each process's open-connection share. Fractional remainders prevent truncation loss. Byte-accurate counts would need a WFP callout (admin/C++) — not bundled.

## Future

- Full WLAN SSID + signal (`wlanapi.dll` `WlanQueryInterface` CURRENT_CONNECTION parsing — the early stub was removed)
- LiveChartsCore graph (zoom/tooltips) — the current Canvas/Polyline is zero-dependency by design
- HUD dock/alignment UI (Top/Left/Right + offsets already supported internally via `HudDock`/`HudOffset*`)
- MSIX + Store signing
- Byte-accurate per-app attribution via WFP/ETW (admin)
