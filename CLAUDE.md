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
- Plan 2 (done): `docs/superpowers/plans/2026-11-26-win-solar-v3-plan-2-real-light-battery-travel.md` (travel dropped)
- Plan 3 (done): `docs/superpowers/plans/2026-11-27-win-solar-v3-plan-3-switch-and-profiles.md`
- Core logic, no Windows APIs, fully unit-tested: `src/WinSolar.Core`
  - `Sun/SunCalculator.cs` offline NOAA sun times (port of v2 `src/lib/sun.ts`) + sun elevation
  - `Scheduling/ScheduleCalculator.cs` prev/next/after switch, phase, progress, in the **place's** time zone;
    `adjust` hook moves sunrise/sunset for weather (`Light/LightModel.Adjust`)
  - `Decisions/ThemeDecider.cs` priority Manual > Battery > Hold > Room light (room mode) > Schedule; `ShouldDefer` (full screen)
  - `Engine/SolarEngine.cs` the heart: Start/Tick, heads-up, holds (snooze/skip/pause/switch now), shortcut, NextDue
  - `Settings/` `AppSettings`, `AppState`, `LearnedState`, atomic `JsonFileStore` (`%AppData%\Win Solar\settings.json`,
    `state.json` owned by the engine, `learned.json` owned by the App services, `log.txt`)
  - `Light/` outdoor light model + weather factor, Open-Meteo parsing, `RoomLightTracker` (hysteresis +
    5-s stability), `Calibration` (self-learned thresholds); `Power/Battery.cs` battery rules
  - Engine ports for Plan 2: `IPower`, `IRoomLight`, `IWeather` (`Engine/Ports.cs`); snapshot has `Light`, `Power`,
    `BatteryDark`, `Error`
- WPF app: `src/WinSolar.App`
  - `Services/EngineHost.cs` one timer at the engine's next due moment + wake/unlock/time-change re-checks
  - `Services/WindowsServices.cs` theme via registry + `WM_SETTINGCHANGE`, full-screen probe
  - `Services/DesktopProfile.cs` accent (uxtheme ordinals 120/122), pointer (Cursors + `SPI_SETCURSORS`), sounds
    (AppEvents scheme; the user's own sounds saved in `saved-sounds.json` while Off); `WindowsTheme` applies apps +
    taskbar theme (always the same as the apps) + profile behind `Overlay/SwitchOverlay.cs` (Roller/Fade/Instant)
  - `Services/LightSensorService.cs` (WinRT LightSensor), `PowerService.cs` (PowerManager), `WeatherService.cs`
    (Open-Meteo, hourly, only without a sensor), `LearnedStore.cs`, `Log.cs`
  - `Flyout/FlyoutWindow.cs` the tray flyout (built in code), `Settings/SettingsSheet.cs` settings + location sheets
  - `Notify/` own notification card (no Windows toasts), OSD for the shortcut; `Tray/` NotifyIcon + menu + glyphs
  - `Theme/` `Light.xaml` / `Dark.xaml` brush tokens (swapped by `ThemeManager`), `Controls.xaml` styles, `Ui.cs` helpers
- Tests: `tests/WinSolar.Tests` (fakes in `Fakes/Fakes.cs`)
- Installer: `installer/WinSolar.iss` (Inno Setup 6, per-user, removes Win Solar 2), release: `build/release.ps1`

## Commands (on Celal's laptop, repo at `D:\Projects\win-solar`)

- Test: `dotnet test`  (135 tests, all passing)
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

## State (2026-11-27)

- Plan 1 (foundation + v2 parity) is built, installed on Celal's laptop and merged to `main`: tray icon with live
  sun/moon, flyout, settings, location search + Windows location, heads-up card with Snooze/Skip/Switch now that
  tucks into the tray, wait for full screen, Win + Shift + D with OSD, start with Windows, installer.
- Plan 2 (branch `feat/plan-2-real-light`) adds: Plan 1 review fixes (outside theme changes respected, errors logged
  and shown on the tray, quiet Auto/Resume, Switch now follows the schedule), real light (sensor first, weather
  without a sensor), battery mode, settings rows, flyout light reading and banners. Installed on the laptop.
  Not yet seen live: the battery banner/card (needs unplugging below 20%) and the weather chip (the laptop has a
  sensor; the weather path was checked once with the sensor forced off: sunrise moved 04:58 → 05:00).
- Celal's laptop: Huawei WRT-WX9, i5-8265U, 8 GB RAM, 2160x1440 (200%), Windows 11 Home 26H2, **has a working
  ambient light sensor** (`Windows.Devices.Sensors.LightSensor`; evening room ≈ 70–180 lx; it only reports while a
  `ReadingChanged` subscriber exists / `ReportInterval` is set).

### Known issues from the handover (fixed 2026-11-26 on `fix/flyout-stutter-and-logo`)

1. **Flyout stutter: fixed.** Measured on the laptop: ~36.7 ms/frame (~28 fps) while sliding. Three causes, each
   measured: the per-pixel transparent (layered) window, the `DropShadowEffect` on the whole surface, and the sun
   sweep starting on the same frames. Now the window is opaque, DWM draws corners/border/shadow, the window itself
   moves (`SetWindowPos`) and fades (constant layered alpha), and the sweep starts after the slide: 18.4 ms mean.
   The notification card (`Notify/NoticeCard.cs`) still uses the old transparent + shadow approach; it is small and
   nobody reported it, but use the same technique if it ever stutters.
2. **"WinSolar" with a generic icon in Start: fixed.** Root cause was the installer, not the icon: the v2 NSIS
   uninstaller returned at once and its %TEMP% copy deleted `Programs\Win Solar` and the `Win Solar` shortcut after
   v3 was installed (same names). The 3.0.0 install on the laptop was left with only the 3 locked files and could not
   start again. The Start entry seen was a stale `WinSolar.lnk` from the v2 portable build. The installer now runs
   the v2 uninstaller with `_?=` (synchronous) and deletes that stale shortcut; the laptop was reinstalled.
   The Win Solar mark is in About and the tray menu title row; the exe's FileDescription is "Win Solar".
3. **Dynamic wallpaper is not there yet**: expected, it is Plan 4.

- Plan 3 (branch `feat/plan-3-switch-and-profiles`): Roller/Fade/Instant switch overlay, Day/Night profiles (accent,
  pointer, sounds), final review fixes, room light stability 5 s with a flyout countdown. Room light as its own
  "Switch by" choice was merged before Plan 3. Checked live by Celal: Roller, accent and pointer.

## Next (one plan each, written with `superpowers:writing-plans`, executed with `superpowers:executing-plans`)

- Plan 4: dynamic wallpaper import from macOS `.heic` (original resolution, Display P3 → sRGB, lossless PNG frames,
  solar/h24/apr metadata), save + restore the user's own wallpaper, welcome screen with every feature on.
- Plan 5: in-app updates from GitHub Releases, Defender false-positive submission + checksums in the release
  checklist, remove the Electron code, final review.

## Decisions (check before changing)

- Heads-up and notices are Win Solar's own WPF card, not Windows toasts (toasts can't be styled); not kept in the
  notification center; "do not disturb" is honoured best effort (`SHQueryUserNotificationState` + `ToastEnabled`).
- Heads-up is raised once per transition; the live countdown lives in the card ring and the flyout.
- Tray uses the built-in WinForms `NotifyIcon` + a styled WPF `ContextMenu` (H.NotifyIcon.Wpf's current release
  doesn't target net8). DPI stays in `app.manifest` (PerMonitorV2); `WFAC010` is suppressed for that reason.
- Fallback place (until the user picks one): popular city in the same IANA zone, else same UTC offset, else an
  approximate place (`Core/Places.cs`). The PC zone maps Blantyre to Africa/Johannesburg, hence the offset step.
- Flyout is an opaque window framed by DWM (Windows 11 rounded corners, border colour, shadow; square on Windows 10)
  that slides and fades as a whole window: changed from a transparent window because that stuttered (see Known
  issues). No Mica. UI is built in C# with styles from `Theme/Controls.xaml` rather than per-view XAML.
- "Start with Windows" writes the Run key automatically only for the installed copy (not from `bin/`).
- Sounds per profile will be Normal/Off only (no clean "Quiet" in Windows). No paid code signing for now: free
  measures only (version info, per-user, no PowerShell, Defender submission, README SmartScreen note).
- "Switch by" has three choices: Sunrise and sunset, Room light, Fixed times (Celal, 2026-11-27: day/night and the
  room are separate features). Room light: the sensor decides at any hour (dark room → Dark, bright room → Light,
  5-s stability with a countdown in the flyout, hysteresis), sun times until the room is known or without a sensor; no heads-up. Sunrise and
  sunset: the weather (Open-Meteo cloud cover → factor `1 − 0.7·(cloud/100)^1.5`, storm codes 95–99 → 0.12) moves
  the switches by at most the max shift when "Follow the weather" is on; the sensor is not used. Learned thresholds sit at 35% / 65% between this room's evening
  and daytime hourly medians in log space (needs 6 hours of each, day ≥ 1.5× evening; else 60 / 150 lx).
  "Dark below 20%" includes 20% (as the demo). Weather requests allow 30 s (Open-Meteo took 8–17 s on the laptop's
  network) and retry after 5 min.
- A theme changed in Windows Settings is kept until the next switch passes (not reverted on the next tick); during a
  hold or manual mode it is kept until the user changes the mode or the hold.
- No travel detection (Celal, 2026-11-26): the place is changed by hand in Settings.
- Flyout hero has no phase chip ("Night", "Daytime") and the light reading has no pill (Celal, 2026-11-27).
- Accent color works only through uxtheme `SetUserColorPreference` (ordinal 122); writing the Accent/DWM registry
  values alone changed nothing on Celal's laptop (2026-11-27). If the call is missing, accent rows hide.
- Switch overlay: frozen picture per monitor (topmost, click-through, excluded from capture), theme applied below,
  a short wait for most of the screen to repaint (flicker-free 64x40 samples, 3 equal looks, 0.4–1.5 s), then the
  frozen picture is cut away from the top with a window region (cheap) behind a thin soft band (a small per-pixel
  transparent window), so the live screen shows above the edge; Fade lowers the window's constant alpha. Measured
  17–22 ms/frame on the laptop. Earlier tries that replaced the old picture with a second capture failed: apps
  (Electron, this chat) repaint for 2–5 s and never "settle", so the roll showed stale pictures and then popped.
  No nested switch while the overlay pumps the dispatcher (engine re-entrancy guard; overlay timers owned by a run
  number; one-shot watchdog); no overlay over full-screen apps.
- Night profile sounds default Normal (the demo's "Quiet" does not exist; Off is opt-in). The profile is re-applied at
  start and when a profile setting changes (only what differs is written), never on unrelated settings.
- No separate taskbar theme (Celal, 2026-11-27): apps, taskbar and Start always follow the switch together.
- Profiles apply by default, including the user's own accent and pointer on first start (Celal, 2026-11-27: every
  feature on; no "Keep mine" choice).
