# Roadmap

## v0.1 — MVP (this commit)
- [x] WinUI 3 scaffold, solution, GitHub Actions build/release
- [x] NetworkAdapterService auto-detect (WiFi/Ethernet/any)
- [x] SpeedHistory + Canvas graph
- [x] Tray icon with drawn text
- [x] Taskbar HUD (docked, reposition timer)
- [x] Settings (JSON) + autostart
- [x] Ping latency + WiFi stub

## v0.2
- [ ] LiveChartsCore graph (zoom, tooltips, fill)
- [ ] Full SSID + signal % via WlanQueryInterface
- [ ] Native GetIfTable2 path (64-bit counters, link speed)
- [ ] Per-adapter theme + compact/expanded HUD sizes

## v0.3
- [ ] MSIX package + signing for Store
- [ ] Per-process traffic (WFP events)
- [ ] Daily history chart (30 days)
- [ ] Dark/light following Windows theme automatically

## v1.0
- [ ] Installer (WiX / msix installer)
- [ ] Auto-update (via GitHub Releases)
- [ ] Localization (en-US, hi-IN first)
