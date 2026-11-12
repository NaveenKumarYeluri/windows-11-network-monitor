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
| `SpeedHistory` | `Services/SpeedHistory.cs` | Ring buffer sized for up to 15 min of samples (clamped 60-1200 by poll interval) + peak/avg; `SpeedFormatter` (`B/s → GB/s`) |
| `WifiService` | `Services/WifiService.cs` | ICMP ping latency + WiFi association (SSID, signal 0-100%) via `WlanQueryInterface` `CURRENT_CONNECTION` on the same 3 s cadence |
| `TrayIconService` | `Services/TrayIconService.cs` | Win32 tray, custom-drawn text icon, WndProc subclass for clicks + native right-click popup menu (`CreatePopupMenu`/`TrackPopupMenu` at cursor with `SetForegroundWindow` re-activate) + native balloon toasts (`NIF_INFO`, click → `BalloonClicked` event), re-adds icon on Explorer restart (`TaskbarCreated`), self-heals dropped icons (failed `NIM_MODIFY` → re-add; `NIM_ADD` success tracked), rebinds to a new HWND if the WinUI window is re-homed, `WM_RESTORE` handler for second-launch restore, reclaims GDI icons via `DestroyIcon` |
| `UpdateChecker` | `Services/UpdateChecker.cs` | One GET to the GitHub Releases API (`releases/latest`), tag→`Version` compare vs assembly version; no auth, no telemetry (User-Agent only) |
| `TaskbarHudPosition` | `Services/TaskbarHudPosition.cs` | Bottom = dock HUD over the taskbar, left of the measured tray width (`TrayNotifyWnd`: clock + `^` chevron, never covered); Top/Left/Right = screen edges; DIP offsets scaled by `GetDpiForWindow` |
| `HudWindow` | `Views/HudWindow.cs` | Transparent `150×28` floating HUD, `DispatcherTimer` reposition |
| `MainWindow` | `MainWindow.xaml(.cs)` | Dashboard with Mica backdrop + status chip row (connection state / WiFi / paused) and a one-shot launch fade-in, live graph (1/5/15-min range selector, Catmull-Rom spline areas with gradient fill, nice-tick Y scale, hover crosshair tooltip), adapters, statistics (7/30/90-day stacked bar chart + 7-day MA trend line + tap-for-per-app-day breakdown + trend cards WoW/MoM/projection/best-day + weekly/monthly aggregates + top apps), apps-now; `StackPanel` rows inside one outer `ScrollViewer` (no inner list to snap); update checks + daily usage alerts (90%/100% balloons, stage persisted per day); close-to-tray background mode with reliable restore (`AppWindow.Show` + `ShowWindow(SW_RESTORE)` + `SetForegroundWindow`) - WinUI's `Activate()`-only re-show is unreliable |
| `SettingsWindow` | `Views/SettingsWindow.xaml(.cs)` | Fluent settings with Mica backdrop - adapter, poll interval, Theme System/Light/Dark, retention 0-365 d, toggles (HUD/graph/per-app/virtual/autostart) |
| `SettingsService` | `Services/SettingsService.cs` | JSON persistence + `Run` key autostart (`%LOCALAPPDATA%\Windows11NetworkMonitor\settings.json`); thread-safe writes (UI + poll threads both save). MainWindow also flushes counters ~once/minute so hard kills lose < 1 min of counts |
| `HistoryStore` | `Services/HistoryStore.cs` | Per-day device totals + per-app totals (`usage-history.json`), retention pruning |
| `PerAppTracker` | `Services/PerAppTracker.cs` | `GetExtendedTcpTable` + `GetExtendedUdpTable` (IPv4+IPv6, owner-PID) → process list (10 s PID-name cache); bytes attributed by connection share (fractional remainder) |

## Data flow

```
NetworkInterface (iphlpapi) --poll 250–5000 ms--> NetworkAdapterService (delta / interval)
  --> SpeedHistory (ring buffer, up to 15 min) --> MainWindow.DrawGraph() (spline areas + hover crosshair + Max/Mid/Min labels)
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

- LiveChartsCore graph (zoom/tooltips) — the current Canvas/Polyline is zero-dependency by design
- MSIX + Store signing
- Byte-accurate per-app attribution via WFP/ETW (admin)
