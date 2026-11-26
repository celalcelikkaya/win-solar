# Win Solar 3 — WPF rewrite and new features

Date: 2026-11-26
Status: approved by Celal (2026-11-26)
Reference demo: `docs/superpowers/specs/win-solar-demo.html` (open in a browser; same page as the private artifact Celal reviewed)

## Intent

Win Solar switches Windows between light and dark with the sun. Version 2 (Electron + React) works but is heavy for a
tray app that runs all day (100+ MB RAM on an 8 GB laptop) and does every Windows change by spawning PowerShell,
which makes switches slow and sometimes late. Celal wants it rebuilt as a native Windows app (C# .NET 8 + WPF, the
same stack as his Harbor app) and turned into something special: an app that follows the *real* light, paints the
switch smoothly across the screen, and makes the whole desktop (wallpaper, accent, pointer, sounds) change with the
time of day.

Success: Celal runs it on his laptop for a week and the theme changes when the room actually gets dark, without
flashes, without interrupting a video, and he never has to think about it.

## Goals

1. Native WPF rewrite with feature parity with v2 (see "Kept from v2").
2. New features agreed during the demo review (see "New in v3").
3. First install turns every feature on, with a welcome screen to switch off what the user doesn't want.
4. Small and quiet: low RAM, no console windows, no polling Windows through PowerShell.

## Non-goals (for now)

- Screen brightness control (discussed, not chosen; possible later).
- Windows Night light control.
- Per-app theme rules (Windows has no general API for it).
- Languages other than English (UI text is English only; v2's Turkish option is dropped).
- macOS / Linux, Microsoft Store packaging, code signing.
- Migrating v2 settings (v3 starts fresh with the welcome screen).

## Anti-goals (failure even if it "works")

- A bright flash or a hard cut when switching.
- Switching while a full-screen video, game or slideshow is running.
- Losing the user's own wallpaper.
- The theme flipping because of a hand shadow, a passing person or a lamp switched on at noon.
- Silent failure: if a switch, sensor or weather lookup fails, the user can see it (tray badge, banner or log).
- Two Win Solars fighting: the v2 Electron app must not keep running next to v3.

## Constraints

- Target machine: Huawei WRT-WX9 laptop, Windows 11 Home 26H2, i5-8265U, 8 GB RAM, one 2160x1440 display
  (probably 200% scaling), ambient light sensor present and working (reads 0–620 lux; room in the evening about
  70–180 lux), .NET 8 SDK installed. C: has only ~11 GB free, D: has ~50 GB: keep build output and caches on D:.
- Windows 11 is the target. Windows 10 should not crash, but Mica and some visuals may fall back.
- The cloud workspace can compile and unit-test the Core project (`net8.0`, no Windows APIs) and can compile the WPF
  project with `EnableWindowsTargeting=true`, but cannot run it. Every UI and Windows-integration step is verified on
  Celal's laptop.

## Approaches considered

1. **C# .NET 8 + WPF (chosen).** Same stack as Harbor. ~20–40 MB RAM, installer of a few MB (framework-dependent;
   .NET 8 Desktop Runtime is present), direct Windows APIs. Cost: UI redrawn in XAML; acrylic look needs Windows 11
   Mica via DWM.
2. **WinUI 3.** Fluent visuals built in, but heavier, slower start, no built-in tray icon, more complex packaging.
   Too much for a tray app.
3. **Tauri.** Would reuse the demo's HTML almost as is and is lighter than Electron, but the back end would be Rust
   (not installed, different from Harbor) and WebView2 still costs 60–100 MB.

Staying on Electron was ruled out for RAM and PowerShell latency.

## Behaviour

### What decides the theme (highest priority first)

1. **Manual** Light or Dark (from the flyout, tray menu or shortcut). If "Manual mode ends by itself" is on (default),
   manual ends at the next scheduled switch and Auto resumes.
2. **Battery mode**: on battery and the rule matches (`Dark below 20%` default, or `Always dark`, or `No change`) → Dark.
   When the rule stops matching (plugged in, or above 20%), the schedule takes over again.
3. **Hold**: Snooze (keep current theme until the next switch + 15 min), Skip (keep it until the switch after next),
   Pause (1 hour, from the tray menu) and Switch now. Holds are saved to disk and survive restarts and reboots.
4. **Room light** (only when a light sensor exists and "Follow the real light" is on): inside the light window (see
   below) the sensor decides.
5. **Schedule**: sunrise/sunset (optionally moved by weather when there is no sensor) plus the user's offsets, or
   fixed times.

Full-screen rule: if an automatic switch (2–5) becomes due while a full-screen app is in the foreground, wait and
switch right after the user leaves full screen. Manual changes and the shortcut apply immediately.

### Real light

- **Light window:** real light may move a switch by at most `maxShift` around the astronomical sunrise/sunset
  (30 min / 1 h default / 1.5 h). Outside the window the plain schedule applies, so a lamp at night or a cloud at noon
  never flips the theme.
- **With a sensor:** use `Windows.Devices.Sensors.LightSensor` with a `ReadingChanged` subscription (the sensor only
  reports while someone is listening; setting `ReportInterval` is what woke it in the PowerShell test).
  - Hysteresis: separate dark and light thresholds.
  - Stability: a change must last 2 minutes before it counts.
  - **Self-calibration:** start with 60 lx (dark) / 150 lx (light), then learn this room's typical daytime and
    evening levels over the first days and set the thresholds between them. Celal's evening room light (70–180 lx)
    sits right inside the default band, so calibration matters here. Show the learned thresholds in Settings with a
    "Reset learning" action.
  - No heads-up notification in sensor mode (the exact time is not known in advance); the flyout shows
    "Dark around 17:55 · follows the room light".
- **Without a sensor (or if the sensor fails):** weather. Hourly cloud cover from Open-Meteo for the chosen place,
  refreshed about every hour. Cloudier sky = less light: the evening switch moves earlier and the morning switch later,
  inside the same window. Offline → plain sun times, and the flyout says the weather is unavailable.
- Model used by the demo (builder may refine): outdoor light ≈ `max(0, sin(elevation + 6°))` × weather factor
  (clear 1, cloudy 0.6, overcast 0.3, storm 0.12); the switch happens where that crosses the clear-sky value at
  sunset. Map Open-Meteo cloud cover (0–100%) onto the factor continuously.

### Heads-up before a switch

5 minutes before an automatic switch, a Windows toast notification with **Snooze 15 min**, **Skip** and **Switch now**.
Shown even when the flyout is closed. Not shown in full screen, sensor mode or battery mode. The flyout shows the same
as a banner while the toast is up.

### Applying a theme

- Apps and system theme via `HKCU\...\Themes\Personalize` (`AppsUseLightTheme`, `SystemUsesLightTheme`) and a
  `WM_SETTINGCHANGE` "ImmersiveColorSet" broadcast, all in-process (no PowerShell).
- "Taskbar and Start": Follow (default) / Always dark / Always light → controls `SystemUsesLightTheme` separately.
- Only write what changed. Starting the app when the theme is already right changes nothing.

### Switch animation (Roller, default)

Windows changes the theme instantly, so Win Solar hides the cut:
1. Capture each monitor (per-monitor DPI aware; 2160x1440 at 200% here).
2. Show the captures in a topmost, click-through, borderless window per monitor.
3. Apply the theme underneath.
4. Wipe the capture away top to bottom with a soft-edged opacity mask over ~1.6 s (ease in-out), so the new look
   appears to be painted on with a roller. Remove the overlay.
Options: Roller / Fade (crossfade ~1.3 s) / Instant. Never runs in full screen (switches are deferred there anyway).
The screen looks frozen during the 1.6 s; acceptable.

### Desktop profiles

Two profiles, Day (Light theme) and Night (Dark theme). Each holds:
- **Accent color** (8 Windows-style swatches). Setting the accent has no documented API; builder verifies the
  registry approach (`Explorer\Accent`, `DWM`) on Celal's machine. If it is unreliable, drop accent and say so.
- **Mouse pointer**: White or Black (Windows' own "Windows Default" / "Windows Black" schemes, applied via the Cursors
  registry key and `SPI_SETCURSORS`).
- **Sounds**: Normal (the user's scheme) or Off ("No Sounds" scheme). The demo also showed "Quiet"; there is no clean
  Windows equivalent, so it is dropped (builder's call recorded here; Celal can object).
- **Wallpaper** (only when wallpaper mode is "Per profile").
Toggle "Pointer and sounds follow the profile" (default on); off keeps the daytime pointer and sounds.

### Wallpaper

- Modes: **Moves with the sun** (default), Per profile, Off.
- The user's own wallpaper path(s) are saved at first run (and whenever Win Solar sees the user change it
  themselves); "Restore" puts it back and sets mode to Off.
- Sun mode uses **macOS dynamic wallpapers (.heic) that the user imports** (Celal's choice). Apple's own sets are
  copyrighted and are not shipped; users bring their own (e.g. free sets from dynamicwallpaper.club).
  - **Original images only** (Celal): no drawn or generated fallback set, and no quality loss on import.
  - Import: pick a `.heic` file (drag and drop also works). Win Solar:
    1. Reads every image in the file at its **original resolution** (Apple sets are typically 5K or 6K); no
       downscaling, no cropping. Windows' "Fill" handles fitting to the 2160x1440 screen.
    2. Converts color correctly: Apple frames are usually Display P3 with an embedded profile; convert to sRGB with
       the profile (not by dropping it), so colors don't look washed out on Windows.
    3. Saves each frame as lossless PNG in `%AppData%\Win Solar\Wallpapers\<set name>\` (D: is not needed; a set is
       ~50–150 MB, show the size before importing and the free space on C:).
    4. Reads the frame schedule from the file's XMP metadata: solar sets (`apple_desktop:solar`, sun altitude and
       azimuth per frame) are matched to the current sun position at the chosen place; time-based sets
       (`apple_desktop:h24`) by time of day; light/dark-only sets (`apple_desktop:apr`) by theme.
    5. Shows a preview strip of all frames with when each one appears, then "Use this wallpaper".
    6. Clear errors for a plain (non-dynamic) HEIC, a damaged file or missing decoder support, naming what is wrong.
  - Several sets can be imported; one is active; sets can be deleted.
  - Windows cannot decode HEIF on its own: add a decoding library (e.g. Magick.NET with libheif; a few MB) instead of
    relying on the Store "HEIF Image Extensions".
  - Frame changes set the Windows wallpaper via `IDesktopWallpaper` (Windows' own short fade), only when the frame
    actually changes.
  - **First run:** the welcome screen shows "Wallpaper moves with the sun" with a "Choose a dynamic wallpaper"
    button. Until a set is imported, Win Solar does not touch the wallpaper.

### Battery

Rules: No change / Dark below 20% (default) / Always dark on battery. Uses `PowerManager` battery and power-supply
events. Toast when it switches ("Battery at 18%. Your schedule comes back when you plug in.").

### Travel

When the PC time zone changes (`SystemEvents.TimeChanged` + `TimeZoneInfo.ClearCachedData`):
- "Follow me when I travel" on (default): find a city for the new zone (Windows location if allowed, otherwise
  geocode the zone's city name), switch to it, toast "Welcome to Istanbul".
- Off: toast "New time zone found" with "Use Istanbul" / "Keep Blantyre".

### Location

- City search: Open-Meteo geocoding (as in v2).
- "Use Windows location": `Windows.Devices.Geolocation` instead of v2's IP lookup.
- First run: guess from the time zone as v2 does, then the welcome screen shows it.

### Tray and flyout

- Tray icon: sun by day, moon by night (vector, crisp at 200%). Small badge: amber = postponed/paused,
  orange = waiting for full screen, accent = manual.
- Tooltip: theme + what happens next ("Dark in 22 min (17:55)").
- Right-click menu: Auto / Light / Dark (checked), Pause switching for 1 hour / Resume, Open, Settings, Quit.
- Left click toggles the flyout. The flyout opens next to the tray on whichever edge the taskbar is, sliding in from
  that edge (~300 ms, decelerate) and out (~150 ms). Mica background on Windows 11. Hides when clicking outside unless
  pinned (pin button).
- Flyout content as in the demo: place, environment chip (weather or lux), phase chip and clock, sun/moon arc with
  countdown, Auto/Light/Dark switch, four tiles (sunrise, sunset, solar noon, day length, with "Dark at 17:40" when
  moved), today's light/dark bar, next 7 days, banners (heads-up, postponed, battery, waiting for full screen,
  room getting darker).

### Shortcut

`Win + Shift + D` (global, `RegisterHotKey`) flips the theme as a manual change; pressing it again when that matches
the schedule returns to Auto. A small OSD shows "Dark theme · Auto again at 05:14". If the key is taken, Settings says
so.

### Settings (sections as in the demo)

Schedule (Sunrise and sunset / Fixed times, Follow the real light, Move a switch by at most, offsets or fixed times,
Location, Follow me when I travel) · Around a switch (Warn before switching, Wait for full-screen apps, Manual mode
ends by itself, Keyboard shortcut, Notify after switching) · Wallpaper · Day profile · Night profile · Windows (Switch
animation, Pointer and sounds follow the profile, Taskbar and Start) · Battery · General (Start with Windows, Hide
when clicking outside, 24-hour clock) · About (version, Check for updates).

### First run

Every feature on: real light, sun wallpaper (active once a set is chosen in the welcome screen), profiles, Roller, heads-up, wait for full screen, battery `Dark below
20%`, travel, shortcut, start with Windows. Save the user's wallpaper first. Open the flyout with the Welcome sheet
listing these with one switch each and "Start using Win Solar".

### Kept from v2

Offline NOAA sun calculation (port `src/lib/sun.ts`, ~1–2 min accuracy, polar day/night), schedule engine
(`src/lib/engine.ts`: prev/next/after transitions, phases, progress), offsets ±120 min, manual-until-next, heads-up,
snooze/skip, notifications after a switch, wallpaper per theme, start with Windows (minimized), hide on blur, 24-hour
clock, 7-day table, wake-from-sleep re-check (`SystemEvents.PowerModeChanged`, session unlock).

### Timing

No 15-second polling. Compute the next due moment (next switch, heads-up time, wallpaper frame change, hold expiry)
and set one timer for it; recompute on wake, unlock, time/zone change, settings change, sensor and power events.

### Updates

Check GitHub Releases of `celalcelikkaya/win-solar`; "Restart to update" downloads the installer and runs it silently.

### Install and v2 coexistence

Inno Setup 6 (as Harbor), per-user install, start-with-Windows via the `HKCU\...\Run` key with `--minimized`.
The installer detects Win Solar 2 (Electron, appId `com.winsolar.app`): closes it, removes its start-with-Windows
entry and runs its uninstaller (asks first).

### Keeping Windows from getting in the way (no paid signing)

Celal asked that Windows not cause trouble when installing or using the app. Paid code signing is out for now:
Azure Artifact Signing is open to individuals only in the USA and Canada, and a CA certificate costs money yearly and
still does not remove SmartScreen right away. So v3 does everything that is free:
- Per-user install, `requestedExecutionLevel asInvoker` in the app manifest: no UAC prompt, ever.
- Full version resources on the exe and installer (product "Win Solar", company "Celal Çelikkaya", file version,
  description) so Windows and antivirus show a real name, not "Unknown".
- No PowerShell, no script hosts, no packers or obfuscation; ReadyToRun is fine.
- Release checklist step: submit the installer and exe to Microsoft Defender's file submission as a false positive
  check before publishing; publish SHA-256 checksums with each GitHub release.
- The README explains the one-time SmartScreen screen on the installer ("More info → Run anyway"). The installed app
  itself never shows it.
- Signing can be added later without code changes (a step in the release script).

## Structure (builder may adjust)

```
src/WinSolar.Core   net8.0, no Windows APIs: sun math, schedule, decision priority, light model,
                    sensor calibration, settings model + JSON store. Unit-tested.
src/WinSolar.App    net8.0-windows10.0.19041.0, WPF: tray, flyout, settings, overlay, Windows services
                    (theme, wallpaper, cursor, sounds, sensor, power, hotkey, full-screen detection, notifications).
tests/WinSolar.Tests  xUnit, fake clock / sensor / power / weather.
installer/          Inno Setup script.
```
Settings and state (holds, calibration, saved wallpaper) in `%AppData%\Win Solar\`. Electron code is removed from the
repo once v3 reaches parity, on a branch, merged after Celal approves.

## Left to the builder

Exact XAML styling within the demo's look, threshold-learning algorithm details, weather-factor mapping curve,
library choices (e.g. tray icon, notifications), test design, task order.

## Decided with Celal

- Source of truth is the GitHub repo; the PC copy is not needed (Celal deletes local copies after pushing).
- Sun wallpaper images: imported macOS dynamic wallpapers (.heic), original quality, no built-in drawn set.
- First run: "Choose a dynamic wallpaper" step in the welcome screen.
- Screen brightness: not now.
- English UI only.

## Calls made by Claude (check these)

- Sounds per profile: Normal / Off only ("Quiet" from the demo has no clean Windows equivalent).
- Accent color: kept only if the undocumented registry approach proves reliable on Celal's laptop.
- Light-sensor defaults 60 / 150 lx with self-calibration; changes must last 2 minutes; real light may move a switch
  by at most 1 hour by default.
- v2 settings are not migrated; the v3 installer removes v2 (asks first).
- Version 3.0.0, same GitHub repo, work on branch `v3-wpf`, Electron code removed only after parity.
- No paid code signing for now (see "Keeping Windows from getting in the way"); the free measures there are in scope.
