# Roadmap

## Shipped (v1.0 folder build)
- [x] WinUI 3 scaffold, solution, GitHub Actions build (folder publish) + release
- [x] NetworkAdapterService auto-detect (WiFi/Ethernet/any, Auto + Combined)
- [x] SpeedHistory + Canvas graph with `B/s → GB/s` Y-axis and gridlines
- [x] Tray icon with drawn text + taskbar HUD (`150×28`, bottom-docked, TOPMOST)
- [x] Settings (JSON) + start-with-Windows + Theme System/Light/Dark
- [x] History: 7-day daily totals + per-app totals, retention `0–365 days` (pruned)
- [x] Per-app live list (`Apps using network now`) — estimated by connection share
- [x] App icon (ICO + PNG + SVG) and README acknowledgements / AI-build note

## Next
- [ ] Screenshots under `docs/screenshots/`
- [ ] Full WLAN SSID + signal % via `WlanQueryInterface` CURRENT_CONNECTION (surface in Dashboard)
- [ ] HUD dock/alignment UI (Top/Left/Right + offsets already in `AppSettings`)
- [ ] LiveChartsCore graph (zoom, tooltips, fill)
- [ ] Native `GetIfTable2` path (64-bit counters, link speed)
- [ ] MSIX package + signing for Store
- [ ] Byte-accurate per-process traffic (WFP callout, admin)
- [ ] 30-day history chart (currently 7-day list)
- [ ] Installer (WiX / msix installer) + auto-update via Releases
- [ ] Localization (en-US, hi-IN first)
