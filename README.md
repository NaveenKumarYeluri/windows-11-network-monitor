<p align="center">
  <img src="docs/assets/icon-256.png" width="128" height="128" alt="Windows 11 Network Monitor icon" />
</p>

<h1 align="center">Windows 11 Network Monitor</h1>

<p align="center"><b>Windows 11 Network Monitor</b> — a lightweight, modern network speed monitor for Windows 11 and Windows 10 (1809+). Shows live download/upload speed + graph on the taskbar (HUD) and in the system tray. Auto-detects WiFi, Ethernet, USB tethering, mobile hotspot and any other connection, with per-app usage and per-day history.</p>

<p align="center">
  <a href="https://github.com/NaveenKumarYeluri/windows-11-network-monitor/actions/workflows/build.yml"><img src="https://github.com/NaveenKumarYeluri/windows-11-network-monitor/actions/workflows/build.yml/badge.svg" alt="Build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT" /></a>
  <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-blue" alt="Platform" /></a>
</p>

## Why

Windows 11 removed Deskband taskbar extensions, so there's no official way to inject a real control into the taskbar. This project mimics a native extension using the two supported tricks:

1. **System tray icon with live-drawn speed text** — the official, stable way.
2. **Transparent floating HUD docked over the taskbar** — looks like a native applet, follows taskbar position / DPI / resolution.

## Features

- [x] Live download / upload speed (auto B/s → KB/s → MB/s → GB/s)
- [x] Auto-detects **WiFi, Ethernet, USB tethering, mobile hotspot** — no manual selection
- [x] Auto-switches to the adapter that owns the default route (internet)
- [x] Live mini-graph (last 60 seconds, download = blue, upload = orange)
- [x] Statistics: session total, daily total, peak, average, latency (ping)
- [x] WiFi details: SSID + signal % (via `wlanapi.dll`)
- [x] System tray icon with custom-drawn text (updates every 1s)
- [x] Taskbar HUD (top/bottom/left/right, center/left alignment, DPI-aware, auto-hide aware)
- [x] Modern Fluent / WinUI 3 interface — Mica, dark/light theme, accent color
- [x] Settings window (JSON persisted to `%LocalAppData%`)
- [x] Start with Windows (registry `Run` key)
- [x] Lightweight: < 0.3% CPU, < 50 MB RAM, polling every 500 ms–1 s

## Screenshots

> Coming soon — run `src/Windows11NetworkMonitor` once built and check `docs/screenshots/`.

## Requirements

- Windows 10 1809+ / Windows 11 (build 19041+)
- [NET 8 SDK](https://dotnet.microsoft.com/download) (for building from source)
- No admin rights required

## Install

This app ships as an **unpackaged folder build** (single-file WinUI does not work reliably). Keep the whole folder together.

### Option A — Download a build (recommended)
1. Go to [Actions → Build](../../actions) and pick the latest green run
2. Download the artifact `Windows11NetworkMonitor-win-x64-<sha>.zip`
3. Unzip to e.g. `%LOCALAPPDATA%\Programs\Windows11NetworkMonitor\` (no admin needed) or `C:\Program Files\Windows11NetworkMonitor\`
4. Run `Windows11NetworkMonitor.exe` from that folder (SmartScreen: **More info → Run anyway**)
5. Right-click the `.exe` → **Send to → Desktop (create shortcut)** and enable **Start with Windows** in Settings. Pin the tray icon via **Settings → Personalization → Taskbar → Other system tray icons** (Win11 hides it under `^` by default).

### Option B — Build from source
```powershell
git clone https://github.com/NaveenKumarYeluri/windows-11-network-monitor.git
cd windows-11-network-monitor
dotnet build Windows11NetworkMonitor.sln -c Release
```
CI builds are available as artifacts on every push (see Actions tab). Releases are being migrated to the same folder packaging.

## Usage

1. Launch the app — tray icon + taskbar HUD appear.
2. Click the tray icon or HUD to open the detail window (graph + stats).
3. Right-click tray icon → Settings to change adapter, colors, polling rate, HUD position.
4. In Settings → **General** → enable **Start with Windows**.

### Auto-detect logic

```
Enumerate adapters where:
  OperStatus == Up AND NOT Loopback AND NOT Virtual (optional, toggleable)
Poll InOctets / OutOctets every interval
speed = (current - previous) / interval
Active adapter = one that owns default IPv4/IPv6 route, or highest traffic if tie
Switch automatically when WiFi disconnects and Ethernet comes up
Show "Combined" mode = sum of all physical adapters
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

```
AdapterPoller (NetworkInterface) → SpeedCalculator → RingBuffer (120 samples)
      → TrayIconService (custom drawn text)
      → TaskbarHudWindow (transparent, docked)
      → DetailWindow (graph + stats)
SettingsService (JSON) ← SettingsWindow (WinUI Fluent)
WifiService (wlanapi) → SSID + signal
```

## Tech Stack

| Layer        | Choice                                      |
| ------------ | ------------------------------------------- |
| Language     | C# .NET 8                                   |
| UI           | WinUI 3 (Windows App SDK 1.7.250310001)      |
| Network      | `System.Net.NetworkInformation` + Win32 `iphlpapi` / `wlanapi` |
| Graph        | Custom Canvas/Polyline (v1, zero deps) — LiveChartsCore planned |
| Build        | GitHub Actions (`windows-latest`)           |
| Packaging    | Unpackaged self-contained **folder** WinExe (single-file WinUI crashes with `0x80040111`), MSIX planned |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). PRs welcome — focus on:

1. HUD taskbar positioning edge cases (multi-monitor, vertical taskbar, auto-hide)
2. LiveChartsCore graph upgrade
3. MSIX / Store packaging + signing
4. Localization

## License

MIT — see [LICENSE](LICENSE). Copyright © 2026 Naveen Kumar Yeluri.

## Icon

`src/Windows11NetworkMonitor/Assets/NetworkMonitor.ico` (multi-size) + `NetworkMonitor-*.png` + editable `NetworkMonitor.svg`. The icon shows live up/down arrows with a pulse line on a blue→violet rounded square — download (cyan) and upload (orange) to match the graph colors. Generated with Pillow (supersampled antialiasing) from the SVG source.

## Built with AI assistance

This project was built with **AI (Claude Spark / OpenCode via Crouton Alpha, Meta AI)** — the assistant generated the initial scaffold, WinUI XAML/C# implementation, CI workflows, and icon assets, then iterated with the human maintainer through testing (tray/HUD behaviour, scroll bugs, per-app attribution). Every AI suggestion was reviewed, tested and committed by the maintainer. No third-party code was copied verbatim; all dependencies are NuGet packages under their own licenses (below). Human-authored code, product decisions and release choices remain the maintainer's.

Suggested citation: *Naveen Kumar Yeluri (2026), Windows 11 Network Monitor, MIT.*

## Acknowledgments

**Original work:** All application code in `src/` (MainWindow, Settings, HUD, tray, adapter polling, history, per-app estimator) was written for this project — see git history. No code was copied from existing monitors.

**Inspiration / concepts (no code reused):**
- TrafficMonitor, GlassWire, XMeters — for feature inspiration only.
- Windows 11 taskbar HUD approach follows the documented `SHAppBarMessage` + transparent topmost window pattern and Microsoft's WinUI 3 / Windows App SDK guidance (custom taskbar applets are not supported on Win11).

**Dependencies (NuGet, used as binaries — not copied into source):**
- `.NET 8` (runtime/SDK) — MIT
- `Microsoft.WindowsAppSDK 1.7.250310001` (WinUI 3) — MIT
- `CommunityToolkit.Mvvm 8.2.2` (`ObservableObject`) — MIT
- `System.Drawing.Common 8.0.0` (tray icon drawing) — MIT
- `System.Management 8.0.0` — MIT

**System APIs used (no code copied):** `System.Net.NetworkInformation`, Win32 `iphlpapi.dll` (`GetExtendedTcpTable` for per-process connection enumeration), `wlanapi.dll` (Wi-Fi SSID/signal), `shell32.dll` (`Shell_NotifyIcon`).

Per-app byte counts are **estimates proportional to open-connection count** — the shipped build does not bundle a WFP driver. Windows byte-accurate attribution would require an admin ETW/WFP callout (future work).
