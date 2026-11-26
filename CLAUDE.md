# Win Solar

Windows tray app that switches Windows between light and dark with the sun. Version 3 is a native rewrite in
C# .NET 8 + WPF (replacing the Electron/React v2 that still lives in `src/`, `electron/`, `package.json` until Plan 5).
Owner: Celal. **Talk to him in Turkish; everything in the repo (code, UI text, commits, docs) is English.**
He prefers few questions: make sensible calls, write them down (see "Decisions"), and keep going.

## Where things are

- Design (approved): `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md`
- Clickable demo of the target look and every planned feature: `docs/superpowers/specs/win-solar-demo.html`
  (open in a browser; this is the visual reference for all UI work)
- Plan 1 (done): `docs/superpowers/plans/2026-11-26-win-solar-v3-plan-1-foundation.md`
- Core logic, no Windows APIs, fully unit-tested: `src/WinSolar.Core`
  - `Sun/SunCalculator.cs` offline NOAA sun times (port of v2 `src/lib/sun.ts`) + sun elevation
  - `Scheduling/ScheduleCalculator.cs` prev/next/after switch, phase, progress, in the **place's** time zone;
    `adjust` hook is where Plan 2 moves sunrise/sunset for weather
  - `Decisions/ThemeDecider.cs` priority Manual > Battery > Hold > Room light > Schedule; `ShouldDefer` (full screen)
  - `Engine/SolarEngine.cs` the heart: Start/Tick, heads-up, holds (snooze/skip/pause/switch now), shortcut, NextDue
  - `Settings/` `AppSettings`, `AppState`, atomic `JsonFileStore` (`%AppData%\Win Solar\settings.json`, `state.json`)
- WPF app: `src/WinSolar.App`
  - `Services/EngineHost.cs` one timer at the engine's next due moment + wake/unlock/time-change re-checks
  - `Services/WindowsServices.cs` theme via registry + `WM_SETTINGCHANGE`, full-screen probe
  - `Flyout/FlyoutWindow.cs` the tray flyout (built in code), `Settings/SettingsSheet.cs` settings + location sheets
  - `Notify/` own notification card (no Windows toasts), OSD for the shortcut; `Tray/` NotifyIcon + menu + glyphs
  - `Theme/` `Light.xaml` / `Dark.xaml` brush tokens (swapped by `ThemeManager`), `Controls.xaml` styles, `Ui.cs` helpers
- Tests: `tests/WinSolar.Tests` (fakes in `Fakes/Fakes.cs`)
- Installer: `installer/WinSolar.iss` (Inno Setup 6, per-user, removes Win Solar 2), release: `build/release.ps1`

## Commands (on Celal's laptop, repo at `D:\Projects\win-solar`)

- Test: `dotnet test`  (59 tests, all passing at handover)
- Run: `dotnet run --project src\WinSolar.App` (quit the installed copy first; single instance)
- Installer: `pwsh build\release.ps1` → `dist\WinSolarSetup-3.0.0.exe` (+ `SHA256SUMS.txt`)
- C: has little free space: `NUGET_PACKAGES` points to `D:\.nuget\packages`; keep build output on D:.
  NuGet downloads of the ReadyToRun crossgen package sometimes drop; a re-run fixes it.

## Rules that matter

- No PowerShell, scripts or child processes inside the app; Windows changes go through Win32/WinRT directly.
- Per-user only: `asInvoker` manifest, HKCU Run key, no admin prompts ever.
- UI copy is English, short and plain; match the demo's wording.
- Write a failing test first for every Core behaviour change (`superpowers:test-driven-development`).
- Warnings are errors (`Directory.Build.props`). WPF projects don't get `System.IO` implicitly; WinForms/Drawing are
  removed from implicit usings (name clashes with WPF). Namespaces `Theme`/`Schedule`/`Decision`/`Settings` are
  avoided on purpose (they would shadow types): use `Styling`, `Scheduling`, `Decisions`, `SettingsUi`.
- Work on a branch; merge to `main` after Celal approves.

## State (2026-11-26)

- Plan 1 (foundation + v2 parity) is built, installed on Celal's laptop and merged to `main`: tray icon with live
  sun/moon, flyout, settings, location search + Windows location, heads-up card with Snooze/Skip/Switch now that
  tucks into the tray, wait for full screen, Win + Shift + D with OSD, start with Windows, installer.
- Celal's laptop: Huawei WRT-WX9, i5-8265U, 8 GB RAM, 2160x1440 (200%), Windows 11 Home 26H2, **has a working
  ambient light sensor** (`Windows.Devices.Sensors.LightSensor`; evening room ≈ 70–180 lx; it only reports while a
  `ReadingChanged` subscriber exists / `ReportInterval` is set).

### Known issues reported by Celal at handover (fix these first)

1. **The flyout stutters while sliding up.** Likely causes: `AllowsTransparency=True` layered window plus a
   `DropShadowEffect` on a full-size border (software rendering of the whole surface each frame), and content
   being rebuilt/laid out on show. Ideas: render the shadow as a pre-blurred image or use a smaller/cached effect
   (`CacheMode = new BitmapCache()`), build content once and only update values, call `UpdateLayout` before starting
   the animation, animate a cached visual, or try a borderless non-transparent window with DWM rounded corners + Mica
   and animate content only. Measure before/after.
2. **No logo:** the Start menu shows "WinSolar" with a generic icon. `build/icon.ico` is fine (16–256 px). Check
   that the published `WinSolar.exe` really carries the icon (`<ApplicationIcon>`), that the Start shortcut is the
   installer's "Win Solar" one, and that Windows' icon cache isn't stale. The flyout/settings also have no logo yet:
   add the Win Solar mark (sun/moon) to the About row and the tray.
3. **Dynamic wallpaper is not there yet**: expected, it is Plan 4.

## Next (one plan each, written with `superpowers:writing-plans`, executed with `superpowers:executing-plans`)

- Plan 2: real light (light sensor with hysteresis + 2-min stability + self-calibration; Open-Meteo cloud cover when
  there is no sensor, moving a switch by at most 30/60/90 min), battery mode (No change / Dark below 20% / Always
  dark), travel detection (time-zone change → new place, asks when "Follow me" is off).
- Plan 3: Roller / Fade / Instant switch overlay (screen capture shown topmost, theme applied underneath, soft-edged
  wipe top→bottom ~1.6 s), Day/Night profiles (accent if reliable, pointer White/Black, sounds Normal/Off),
  taskbar theme Follow / Always dark / Always light.
- Plan 4: dynamic wallpaper import from macOS `.heic` (original resolution, Display P3 → sRGB, lossless PNG frames,
  solar/h24/apr metadata), save + restore the user's own wallpaper, welcome screen with every feature on.
- Plan 5: in-app updates from GitHub Releases, Defender false-positive submission + checksums in the release
  checklist, remove the Electron code, final review.
- Plan 1's final whole-branch review was not run (Celal asked to hand over); do one before Plan 2 builds on it.

## Decisions made during Plan 1 (check before changing)

- Heads-up and notices are Win Solar's own WPF card, not Windows toasts (toasts can't be styled); not kept in the
  notification center; "do not disturb" is honoured best effort (`SHQueryUserNotificationState` + `ToastEnabled`).
- Heads-up is raised once per transition; the live countdown lives in the card ring and the flyout.
- Tray uses the built-in WinForms `NotifyIcon` + a styled WPF `ContextMenu` (H.NotifyIcon.Wpf's current release
  doesn't target net8). DPI stays in `app.manifest` (PerMonitorV2); `WFAC010` is suppressed for that reason.
- Fallback place (until the user picks one): popular city in the same IANA zone, else same UTC offset, else an
  approximate place (`Core/Places.cs`). The PC zone maps Blantyre to Africa/Johannesburg, hence the offset step.
- Flyout is our own rounded transparent window (not Mica) so it can slide and fade; UI is built in C# with styles
  from `Theme/Controls.xaml` rather than per-view XAML.
- "Start with Windows" writes the Run key automatically only for the installed copy (not from `bin/`).
- Sounds per profile will be Normal/Off only (no clean "Quiet" in Windows). No paid code signing for now: free
  measures only (version info, per-user, no PowerShell, Defender submission, README SmartScreen note).
