# Roadmap

## Shipped
### v1.0.1 (first public release — folder zip via GitHub Releases)
- [x] WinUI 3 scaffold, solution, GitHub Actions build (folder publish) + release packaging
- [x] NetworkAdapterService auto-detect (WiFi/Ethernet/any, Auto + Combined, live interval changes)
- [x] SpeedHistory + Canvas graph (~60 s window derived from poll interval, `B/s → GB/s` Y-axis, gridlines)
- [x] Tray icon with drawn speed text, click → dashboard / right-click → Settings (WndProc subclass),
      resilient to Explorer restarts, GDI-icon handles correctly freed
- [x] Taskbar HUD (bottom-docked over taskbar, nearest-monitor, DPI-scaled offsets, TOPMOST)
- [x] Settings (JSON) + start-with-Windows + Theme System/Light/Dark
- [x] Single-instance guard (second launch focuses the running window)
- [x] Session/daily counters + history persisted on every exit (no more lost "Today" data)
- [x] History: 7-day daily totals + per-app totals, retention `0–365 days` (pruned)
- [x] Per-app live list (TCP + UDP owner PIDs, IPv4 + IPv6, cached PID names) — estimated by connection share
- [x] App icon (ICO + PNG + SVG) and README acknowledgements / AI-build note

### v1.0.0 (internal build)
- [x] Initial feature freeze (see git history)

### Unreleased
- [x] Export history to CSV (Settings → writes device + per-app daily totals to
      `%LOCALAPPDATA%\...\exports\*.csv`, UTF-8 BOM, opens the folder in Explorer)
- [x] HUD dock UI (Settings: Bottom/Top/Left/Right side + ±500 DIP offsets, applied
      live on Save; Left/Right docks center the HUD vertically over the taskbar)

## Next
- [ ] Screenshots under `docs/screenshots/`
- [ ] Full WLAN SSID + signal % via `WlanQueryInterface` CURRENT_CONNECTION (surface in Dashboard)
- [ ] LiveChartsCore graph (zoom, tooltips, fill)
- [ ] Native `GetIfTable2` path (64-bit counters, link speed)
- [ ] MSIX package + signing for Store
- [ ] Byte-accurate per-process traffic (WFP callout, admin)
- [ ] 30-day history chart (currently 7-day list)
- [ ] Installer (WiX / msix installer) + auto-update via Releases
- [ ] Localization (en-US, hi-IN first)
