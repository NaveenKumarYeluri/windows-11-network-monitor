# Contributing

1. Fork + branch (`feat/xxx`, `fix/xxx`).
2. Build locally: `dotnet build Windows11NetworkMonitor.sln -c Release`
3. CI must be green (GitHub Actions `build.yml` on `windows-2022`).
4. Keep polling cheap: never block UI thread, throttle icon redraws to 1 Hz.
5. Open PR with description + screenshot if UI changed.

## Priorities

- HUD edge cases: vertical taskbar, auto-hide, multi-monitor, 125%+ DPI
- LiveChartsCore upgrade for graph
- MSIX packaging
- Localization (i18n)

## Code style

- C# .NET 8, nullable + implicit usings enabled
- Async where I/O bound, no `async void`
- Services under `Services/`, models under `Models/`, windows under `Views/`
- File-scoped namespaces
