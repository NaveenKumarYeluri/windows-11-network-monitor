# Roadmap

## Shipped
### v1.1.0 (via GitHub Releases — folder zip)
- [x] Export history to CSV (Settings → writes device + per-app daily totals to
      `%LOCALAPPDATA%\...\exports\*.csv`, UTF-8 BOM, opens the folder in Explorer)
- [x] HUD dock UI (Settings: Bottom/Top/Left/Right side + ±500 DIP offsets, applied
      live on Save). Bottom auto-reserves the measured TrayNotifyWnd width so the
      clock and ^ overflow chevron are never covered; Top/Left/Right dock to
      screen edges (Top = top edge centered, Left/Right = mid-height screen sides)
- [x] Tray right-click menu (native TrackPopupMenu): Open dashboard / ⏸ Pause
      tracking / Check for updates / Settings / Exit, with a dynamic Pause ⇄ Resume
      label
- [x] Pause tracking — stops session/daily/history/per-app accumulation only;
      live speed, graph, tray and HUD keep updating (HUD shows ⏸); state persisted
      in settings.json
- [x] Check for updates — single unauthenticated GitHub Releases API call
      (startup + tray menu), tray balloon on newer tag, balloon click opens the
      release page; UpdateChecker compares tag vs assembly version
- [x] Daily usage alerts — Settings cap in GB (0 = off), tray balloon at 90% and
      100% of the limit (download + upload combined); alert stage persisted per
      day so restarts never re-warn the same crossing; balloon click → dashboard
- [x] 30-day history chart — stacked daily bars (blue download, orange upload)
      with bytes Y-axis + hover tooltips, repaint on totals change / reflow
- [x] WiFi SSID + signal % — wlanapi.dll WlanQueryInterface CURRENT_CONNECTION
      (DOT11_SSID + wlanSignalQuality), refreshed on the latency cadence,
      shown in the dashboard; hidden when not associated / wired-only
- [x] Bug fix: Settings no longer scrolls to top after Save (programmatic focus
      on the top ComboBox scrolled the page; focus move removed + offset restored)

## Shipped
### v1.2.2 (via GitHub Releases — folder zip)
- [x] Tray icon root-cause fix for "icon gone after close" - the tray icon was
      gated on a stale `ShowTrayIcon: false` in settings.json (left over from the
      pre-v1.0.1 Settings checkbox that was later removed, leaving no way to
      re-enable it). The icon is now ALWAYS on: it is the app's exit path and
      restore anchor; the dead setting is removed
- [x] Self-healing tray icon - `Shell_NotifyIcon(NIM_MODIFY)` failures now
      re-register the icon instead of fading away silently; `NIM_ADD` success
      is tracked honestly; the icon can rebind to a new HWND if WinUI re-homes
      the main window (rebind deletes the stale registration first)
- [x] Second-launch restore fixed - `FindWindow` cannot see WinUI 3 windows
      (returns 0 even for an exact title match), so `ActivateExistingInstance`
      silently did nothing. It now enumerates windows and posts `WM_RESTORE`,
      which the running instance handles via `ShowFromTray`. Verified
      programmatically: close → second launch → window visible again

### v1.2.1 (via GitHub Releases — folder zip)
- [x] Close-to-tray made unconditional — the v1.2.0 Settings toggle made X exit
      when deselected (confusing: app + HUD closed). The toggle is removed;
      closing the window ALWAYS hides to tray and keeps monitoring; exit is the
      tray menu's Exit. Verified programmatically (WM_CLOSE → process alive,
      window hidden)

### v1.2.0 (via GitHub Releases — folder zip)
- [x] Run in tray / background — close (X) hides the window and keeps monitoring;
      one-time "Still running" balloon; reliable restore from tray click / HUD tap /
      second launch (AppWindow.Show + ShowWindow restore + foreground)
- [x] Periodic counter persistence — session/daily counters flushed ~once/minute
      (cadence from poll interval) with thread-safe settings writes, so hard
      kills lose < 1 min of counts instead of everything since launch
- [x] HUD focus-steal fix — the half-second topmost re-assertion in
      `HudWindow.Reposition()` now passes `SWP_NOACTIVATE`; without it the call
      could activate the HUD and steal focus from the active app (observed as
      focus jumping while clicking "New" in Snipping Tool mid-snip)
- [x] Screenshots shipped in README (`docs/screenshots/`, reviewed for PII - none)

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

## Next
- [ ] Screenshots under `docs/screenshots/`
- [ ] LiveChartsCore graph (zoom, tooltips, fill)
- [ ] Native `GetIfTable2` path (64-bit counters, link speed)
- [ ] MSIX package + signing for Store
- [ ] Byte-accurate per-process traffic (WFP callout, admin)
- [ ] Installer (WiX / msix installer) + auto-update via Releases
- [ ] Localization (en-US, hi-IN first)
- [ ] Installer (WiX / msix installer) + auto-update via Releases
- [ ] Localization (en-US, hi-IN first)
