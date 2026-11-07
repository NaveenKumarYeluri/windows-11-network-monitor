# Windows 11 Network Monitor

> A lightweight, modern network speed monitor for Windows 11. Shows live download/upload speed + graph on the taskbar (HUD) and in the system tray. Auto-detects WiFi, Ethernet and any other connection.

[![Build](https://github.com/NaveenKumarYeluri/windows-11-network-monitor/actions/workflows/build.yml/badge.svg)](https://github.com/NaveenKumarYeluri/windows-11-network-monitor/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2011%20%7C%2010-blue)](https://www.microsoft.com/windows)

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

## Try It

### Option A — Download a release (recommended)

1. Go to [Releases](../../releases)
2. Download `Windows11NetworkMonitor-win-x64.zip`
3. Unzip, run `Windows11NetworkMonitor.exe`

### Option B — Build from source

```powershell
git clone https://github.com/NaveenKumarYeluri/windows-11-network-monitor.git
cd windows-11-network-monitor
dotnet build Windows11NetworkMonitor.sln -c Release
# or publish a single-file build:
dotnet publish src/Windows11NetworkMonitor/Windows11NetworkMonitor.csproj -c Release -r win-x64 --self-contained
```

CI builds are available as artifacts on every push (see Actions tab).

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
| UI           | WinUI 3 (Windows App SDK 1.5)               |
| Network      | `System.Net.NetworkInformation` + Win32 `iphlpapi` / `wlanapi` |
| Graph        | Custom Canvas/Polyline (v1, zero deps) — LiveChartsCore planned |
| Build        | GitHub Actions (`windows-latest`)           |
| Packaging    | Unpackaged single-file WinExe (MSIX planned) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). PRs welcome — focus on:

1. HUD taskbar positioning edge cases (multi-monitor, vertical taskbar, auto-hide)
2. LiveChartsCore graph upgrade
3. MSIX / Store packaging + signing
4. Localization

## License

MIT — see [LICENSE](LICENSE).

## Acknowledgments

Inspired by TrafficMonitor, XMeters and GlassWire. Windows 11 taskbar HUD approach follows the documented `SHAppBarMessage` + transparent topmost window pattern.
