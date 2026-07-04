# CaptureIt

A Windows-only screenshot capture tool (.NET 10, WPF) that runs in the system tray.

## Features

- Runs as a system tray background app — no main window; access via the tray icon's
  right-click menu (**Capture Region**, **Capture Full Screen**, **Settings**, **Exit**).
- Region capture: dimmed overlay across all monitors, drag-to-select, Enter/mouse-release
  to confirm, Esc to cancel.
- Full-screen capture: with multiple monitors, a numbered monitor-picker overlay lets you
  choose which one to capture (skipped automatically with a single monitor).
- A single global hotkey (default **Ctrl+Alt+S**, remappable in Settings) repeats
  whichever mode (region or full-screen) was last used, and pre-shows the last
  region / last monitor so pressing **Enter** re-captures the same selection.
- Screenshots are saved as PNG to a configured folder, using a configurable filename
  pattern (default `Screenshot_{datetime}.png`), with automatic collision suffixing
  (`_001`, `_002`, ...) and automatic fallback to `%Pictures%\CaptureIt` if the
  configured folder becomes unavailable.
- An optional capture delay (`Off`, `3s`, `5s`, `10s`) waits before opening the
  region or monitor picker, then captures from the frozen screen shown in the overlay.
- Silent on successful captures; shows a Windows notification only on failures
  (e.g. hotkey conflicts, save-folder problems).
- Settings persisted as JSON at `%AppData%\CaptureIt\settings.json`.

See `docs` in the session history / `plan.md` used during design for the full set of
design decisions and the rubber-duck design review that shaped this implementation
(freeze-first capture model, per-monitor DPI handling, single-instance enforcement, etc.).

## Project layout

```
CaptureIt.slnx
src/
  CaptureIt.App/     # WPF tray app (net10.0-windows)
    Models/           # AppSettings, MonitorInfo, CaptureMode, HotkeyDefinition
    Settings/         # SettingsService (JSON persistence) + Settings window
    Hotkeys/          # RegisterHotKey-based global hotkey manager
    Capture/          # GDI-based virtual desktop capture, monitor enumeration, save pipeline
    Overlays/          # Region-select overlay, monitor-picker overlay
    TrayIcon/          # Tray icon + context menu, Explorer-restart resilience
    Core/              # CaptureController orchestration
  CaptureIt.Tests/    # xUnit tests for pure logic (filename rules, settings I/O, crop math)
```

## Building

Requires the .NET 10 SDK.

```
dotnet build
```

This project targets `net10.0-windows` (WPF + WinForms) and sets
`EnableWindowsTargeting=true` so it can be **restored and compiled** on non-Windows
machines for CI/editing purposes. However, since it uses WPF, WinForms, and Win32
interop (RegisterHotKey, BitBlt, monitor enumeration, etc.), **the app can only be run,
and its tests only executed, on a real Windows machine**. This is a fundamental platform
limitation, not a version issue: `Microsoft.WindowsDesktop.App` (the runtime component
that provides WPF/WinForms) is only published for Windows — there is no Linux/macOS
build of it at any .NET version (8, 9, 10, ...), so `dotnet run`/`dotnet test` will
always fail on non-Windows with a "No frameworks were found" error, even though
`dotnet build` succeeds there.

## Running tests

On Windows:

```
dotnet test
```

Tests cover pure logic only (no UI/interop invoked): filename pattern expansion and
sanitization, settings JSON round-tripping and corruption recovery, and the
coordinate math used to crop regions/monitors out of the frozen desktop bitmap
(including negative-coordinate multi-monitor layouts).

## Known limitations (by design, per requirements)

- No auto-start with Windows — launch manually.
- No clipboard copy or post-capture toast on success (silent by design).
- Cannot capture the UAC secure desktop or DRM-protected video content (inherent GDI
  capture limitation).
