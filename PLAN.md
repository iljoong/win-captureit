# CaptureIt — Windows Screenshot Capture App

**Status: Implemented.** All items below have been built (see `src/CaptureIt.App/` and
`src/CaptureIt.Tests/`). This document is kept as the design record and rationale.

## Problem
Build a Windows desktop screenshot tool (C#/.NET 10, WPF) that runs quietly in the system
tray, supports multi-monitor full-screen and manual region capture, remembers the last
capture mode, is triggered by a configurable global hotkey, and auto-saves PNG files to a
user-configured folder with a full settings window.

## Confirmed Requirements (from user interview + rubber-duck review)

### App shape
- .NET 10, WPF, Windows-only.
- Runs as a **system tray background app** — no main window, no taskbar entry.
- **Single-instance enforced** via named mutex. Second launch signals the running
  instance to open Settings, then exits.
- Tray icon right-click menu: **Capture Region**, **Capture Full Screen**, **Settings**,
  **Exit**.

### Capture modes
- **Region capture**: dimmed overlay spanning the virtual desktop (all monitors),
  drag-to-select rectangle, confirm on mouse-release (or Enter), Esc cancels.
- **Full-screen capture**: since multiple monitors may exist, show a **quick monitor
  picker overlay** — each monitor dimmed with a large number; click a monitor or press
  its number key to select; Esc cancels. Skip the picker entirely if only one monitor.
- **Freeze-first capture model** (rubber-duck recommendation): grab the full virtual
  desktop bitmap *before* showing any overlay UI, then draw overlays on top of that
  frozen image and crop from the frozen bitmap. This avoids the overlay/picker
  contaminating the final screenshot and avoids re-capture race conditions.

### Hotkey
- Single global hotkey, default **Ctrl+Shift+S**, registered via **`RegisterHotKey`
  P/Invoke** on a hidden message-only window (not a low-level keyboard hook).
- Pressing it repeats **whichever mode (region or full-screen) was last used** — either
  via hotkey or via the tray menu action.
- **Remapping**: fully user-remappable in Settings. On remap, test-register the new
  combination before saving; reject with a clear error if it's already reserved by
  Windows or another app, and keep the previous hotkey active.

### "Remember last capture config"
- Only remembers **which mode** (region vs full-screen) was last used, so the hotkey
  knows what to repeat. Does **not** remember last region size/position or last chosen
  monitor — user picks fresh each time.

### Saving
- Auto-saves to a **configured folder** — no clipboard copy, no toast on success.
- Format: **PNG only** (fixed, not user-configurable).
- Filename pattern: **configurable in Settings**, default
  `Screenshot_yyyyMMdd_HHmmss.png`.
  - Sanitize invalid filename characters/reserved Windows names.
  - Collision handling: if a file with the same name already exists (e.g., two captures
    within the same second), append `_001`, `_002`, etc.
  - Live preview of the pattern in the Settings UI.
- **Fallback folder policy**: if the configured save folder becomes invalid (deleted,
  unplugged drive, permission denied), automatically fall back to
  `%Pictures%\CaptureIt`, save the capture there, and show a failure/fallback
  notification.

### Feedback / notifications
- **Silent on success** (no toast, no sound) per user preference.
- **Mandatory Windows notification on failure** (e.g., save folder invalid before
  fallback succeeds, hotkey conflict, disk full, path too long), including an
  "Open Settings" action where relevant.

### Settings window
Covers:
- Save folder picker (with validation).
- Filename pattern editor with live preview.
- Hotkey remapping control (with conflict validation).
- (No auto-start-with-Windows option — user launches manually.)
- Settings persisted as JSON at `%AppData%\CaptureIt\settings.json`, with safe recovery
  to defaults if the file is missing/corrupted.

### Explicitly out of scope / unsupported (documented to user)
- No auto-start with Windows.
- No clipboard copy, no post-capture toast/preview.
- Cannot capture secure desktop (UAC prompts) or DRM-protected video content (known
  Windows limitation of GDI capture — appears black).
- No "repeat last region" or "remember last monitor" behavior.

## Technical Approach

- **Capture technique**: GDI (`BitBlt`) via P/Invoke for v1 — simpler and mature vs.
  `Windows.Graphics.Capture`; documented DRM/secure-desktop limitation.
- **DPI handling**: app manifest set to **Per-Monitor DPI Aware V2**. All capture and
  overlay math done in **physical pixels**, never WPF DIPs, to avoid mixed-DPI
  (100%/150%/200%) coordinate bugs. Monitor bounds may be negative/non-rectangular —
  never assume (0,0) is the primary monitor's origin.
- **Global hotkey**: `RegisterHotKey`/`UnregisterHotKey` on a hidden `HwndSource`
  message window; re-register on remap with pre-validation.
- **Tray icon**: recreated on `TaskbarCreated` broadcast message (Explorer restart
  resilience).
- **Reentrancy**: while a capture/overlay is active, additional hotkey presses or tray
  actions are ignored until it completes or is cancelled.
- **Resource cleanup**: GDI handles (HBITMAP/HDC) and overlay windows are disposed
  immediately after each capture to avoid leaks in a long-running tray process.

## Actual Project Structure
```
CaptureIt.slnx
src/
  CaptureIt.App/                     # WPF tray app (net10.0-windows)
    App.xaml(.cs)                    # Startup, single-instance mutex, wiring
    app.manifest                     # Per-Monitor DPI Aware V2, asInvoker
    Models/                          # AppSettings, MonitorInfo, CaptureMode, HotkeyDefinition
    Settings/                        # SettingsService (JSON persistence) + SettingsWindow
    Hotkeys/                         # HotkeyManager (RegisterHotKey wrapper)
    Capture/                         # NativeMethods, MonitorService, ScreenCaptureService, ImageSaveService
    Overlays/                        # RegionSelectOverlayWindow, MonitorPickerOverlayWindow, native window positioning
    TrayIcon/                        # TrayIconManager + TaskbarCreatedListener
    Core/                            # CaptureController (orchestration)
  CaptureIt.Tests/                   # xUnit tests (net10.0-windows, EnableWindowsTargeting)
    ImageSaveServiceFileNameTests.cs
    SettingsServiceTests.cs
    ScreenCaptureServiceCropTests.cs
```

## Todos
All 12 implementation todos tracked for this build are complete (tracked in the session's
SQL todos table during development: project scaffold, settings model/persistence, tray
icon, global hotkey, capture engine, region overlay, monitor picker overlay, capture
orchestration, save pipeline, failure notifications, settings window, unit tests).

## Build & Test Notes
- Upgraded from net8.0-windows to **net10.0-windows** (the .NET 10 SDK is installed in
  this dev environment). `dotnet build` succeeds on non-Windows dev machines via
  `EnableWindowsTargeting=true` (for restore/compile/CI purposes only).
- Because the app uses WPF, WinForms, and Win32 interop, it can only be **run**, and its
  tests only **executed**, on an actual Windows machine. This is independent of .NET
  version: `Microsoft.WindowsDesktop.App` (the WPF/WinForms runtime) has no Linux/macOS
  build at any version, so `dotnet run`/`dotnet test` fail here with a
  "No frameworks were found" error even on .NET 10. See `README.md` for details.

## Open Risks Carried Into Implementation (recommend validating on real Windows hardware)
- Mixed-DPI multi-monitor math — needs testing on real multi-monitor setups (100%/150%/200% mixes).
- Portrait monitors / unusual monitor layouts.
- Explorer restart / tray icon recreation.
- Monitor hot-plug/unplug while an overlay is open (should cancel + reopen).
