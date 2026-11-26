# Win Solar 3 — Plan 3: Switch animation, desktop profiles and taskbar theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A switch is painted over the screen (Roller, or Fade, or Instant), the Day and Night profiles change the
accent color, mouse pointer and sounds with the theme, and the taskbar can follow the switch or keep one theme.

> **Scope change (2026-11-27, Celal):** no separate taskbar theme; apps, taskbar and Start always follow the switch.
> `TaskbarTheme` and the "Taskbar and Start" row were removed after Task 4; `Look` became `Look(Theme, Accent, Pointer, Sounds)`.

**Architecture:** Core gets the settings model and pure rules (`Desktop/DesktopLook.cs`: which accent, pointer, sounds
and taskbar theme a given theme means; accent swatches). The engine is unchanged: it still calls
`IThemeApplier.Apply(theme)`. In the App, `WindowsTheme` becomes the place where a switch happens: it asks
`SwitchOverlay` to freeze the screen, writes the theme (taskbar by its rule), applies the profile through
`DesktopProfile` (accent via uxtheme, pointer via the Cursors key + `SPI_SETCURSORS`, sounds via the AppEvents
scheme), then lets the overlay animate the old picture away. Settings changes re-apply the look at once, without
animation.

**Tech Stack:** .NET 8, WPF, Win32 (GDI `BitBlt`, `SetWindowDisplayAffinity`, `SystemParametersInfo`), uxtheme
ordinals 120/122 (`GetUserColorPreference` / `SetUserColorPreference`), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md` — sections "Applying a theme", "Switch animation",
"Desktop profiles", "Settings", "First run". UI wording: `docs/superpowers/specs/win-solar-demo.html`
(`profileGroup`, the "Windows" settings group). Read `CLAUDE.md` first.

## Global Constraints

- Core stays `net8.0` with no Windows APIs; App is `net8.0-windows10.0.19041.0`, x64; warnings are errors.
- No PowerShell, scripts or child processes; Windows access only through Win32/WinRT/registry in-process.
- Per-user only (HKCU); never write HKLM.
- Animation: Roller ~1.6 s ease in-out, soft edge, top to bottom; Fade ~1.3 s; Instant. Default Roller. Never runs
  in full screen (switches wait there anyway). Reduced motion (Windows "Animation effects" off) means Instant.
- Accent swatches (demo): `#0067c0 #6b69d6 #8764b8 #c239b3 #e3008c #ca5010 #0f7b0f #038387`.
  Day default `#0067c0`, White pointer, Normal sounds. Night default `#6b69d6`, Black pointer, Normal sounds
  (the demo's "Quiet" does not exist; Off is not forced on anyone by default).
- "Pointer and sounds follow the profile" default on; off keeps the Day profile's pointer and sounds.
- "Taskbar and Start": Follow (default) / Always dark / Always light → `SystemUsesLightTheme` only.
  Outside-change detection keeps reading `AppsUseLightTheme` only.
- Accent: the registry alone does not work on Celal's laptop (checked 2026-11-27: nothing changed); uxtheme
  `SetUserColorPreference` (ordinal 122) did (Settings toggles and selection turned purple). If the ordinals are
  missing or the call fails, accent is skipped, logged, and the accent rows are hidden.
- Sounds Off saves the user's `.Current` sounds first (`%AppData%\Win Solar\saved.json`) and Normal restores them.
- Only write what changed; starting the app when everything is right changes nothing.
- Saved files stay readable: a Plan 2 `settings.json` loads with the new fields at their defaults.
- Failing test first for every Core behaviour. Branch `feat/plan-3-switch-and-profiles`; merge after Celal approves.

## Review Focus

1. **Two monitors or a different DPI per monitor**: each monitor gets its own overlay at its own physical size; no
   gap, no blurry or shifted picture. Checked by hand (Celal has one 200% screen; the code path is per monitor).
2. **A switch while another switch animates** (shortcut pressed twice fast): the running overlay ends at once and
   the new one starts from the real screen; no overlay is left on screen. Covered by `SwitchOverlay` keeping one
   instance and closing it before starting.
3. **Overlay never left behind**: an exception while capturing or applying closes the overlay and the theme still
   applies (or the error reaches the engine as before). A watchdog closes the overlay after 5 s whatever happens.
4. **Sounds restored correctly**: Off then Normal gives back exactly the user's own sounds, also after a restart
   (the snapshot is on disk), and a user with sound scheme "No Sounds" is never forced to sounds.
   Test in Task 1 (`Pointer_and_sounds_stay_daytime_when_not_following`) for the rule; the snapshot by hand.
5. **Accent call unavailable** on another Windows build: no crash, accent skipped and its rows hidden.

---

## File Structure

```
src/WinSolar.Core/
  Desktop/DesktopLook.cs        enums SwitchAnimation, TaskbarTheme, Pointer, Sounds; record Profile; record Look;
                                DesktopLook.For(Theme, AppSettings); Accents.Swatches; Accents.Parse
  Settings/AppSettings.cs       + Animation, Taskbar, Day, Night, ProfilesFollow
src/WinSolar.App/
  Services/DesktopProfile.cs    applies a Look: accent (uxtheme), pointer (Cursors), sounds (AppEvents + snapshot)
  Services/SavedStore.cs        saved.json: the user's own sounds before Win Solar turned them off
  Services/WindowsServices.cs   WindowsTheme: taskbar rule, overlay + profile around the switch
  Overlay/SwitchOverlay.cs      capture, topmost click-through window per monitor, Roller/Fade
  Interop/Native.cs             + BitBlt/GetDC/monitor enumeration, SetWindowDisplayAffinity, SystemParametersInfo
  Settings/SettingsSheet.cs     + Day profile, Night profile, Windows sections
tests/WinSolar.Tests/Desktop/DesktopLookTests.cs, Settings/CompatTests.cs
```

### Task 1: Look model and settings (Core)

**Interfaces:**
- Produces: `enum SwitchAnimation { Roller, Fade, Instant }`, `enum TaskbarTheme { Follow, AlwaysDark, AlwaysLight }`,
  `enum Pointer { White, Black }`, `enum Sounds { Normal, Off }`,
  `record Profile(string Accent, Pointer Pointer, Sounds Sounds)`,
  `record Look(Theme Apps, Theme Taskbar, string Accent, Pointer Pointer, Sounds Sounds)`,
  `static Look DesktopLook.For(Theme theme, AppSettings s)`,
  `static IReadOnlyList<string> Accents.Swatches`, `static (byte R, byte G, byte B) Accents.Parse(string hex)`.
  AppSettings: `SwitchAnimation Animation = Roller`, `TaskbarTheme Taskbar = Follow`,
  `Profile Day = new("#0067c0", White, Normal)`, `Profile Night = new("#6b69d6", Black, Normal)`,
  `bool ProfilesFollow = true`.

- [ ] Tests (`DesktopLookTests`): `Light_theme_uses_the_day_profile`, `Dark_theme_uses_the_night_profile`,
  `Pointer_and_sounds_stay_daytime_when_not_following` (night accent kept, day pointer/sounds),
  `Taskbar_follows_or_keeps_one_theme` (Follow → theme; AlwaysDark → Dark; AlwaysLight → Light; Apps always theme),
  `Parse_reads_hex` (`#6b69d6` → (0x6b,0x69,0xd6); bad input → FormatException),
  `Swatches_are_the_demo_colors`. Compat: `Plan_2_settings_get_profile_defaults` (Roller, Follow, day/night defaults,
  ProfilesFollow true).
- [ ] Run, watch them fail; implement; run the suite; commit `feat(core): desktop look, profiles and switch settings`.

### Task 2: Taskbar rule and desktop profile service (App)

- [ ] `WindowsTheme.Apply(Theme)` writes `AppsUseLightTheme` = theme and `SystemUsesLightTheme` =
  `DesktopLook.For(theme, settings).Taskbar`, then broadcasts once if anything changed. `Read()` unchanged.
  `WindowsTheme.Refresh()` re-applies the current theme's look without animation (settings changed).
- [ ] `DesktopProfile.Apply(Look look)`:
  accent — `GetUserColorPreference` (ordinal 120) then `SetUserColorPreference` (122) with Start = Accent = ABGR of the
  swatch when it differs; any failure sets `AccentAvailable = false` and logs once.
  pointer — White = Windows Default (aero cursors, the built-in default list), Black = the `Windows Black` scheme
  read from `HKLM\...\Control Panel\Cursors\Schemes`; write the `HKCU\Control Panel\Cursors` values + default value
  (scheme name), `Scheme Source` = 2, then `SystemParametersInfo(SPI_SETCURSORS, 0, null, SPIF_UPDATEINIFILE |
  SPIF_SENDCHANGE)`; skip when the Arrow path already matches.
  sounds — Off: if the scheme is not `.None`, save every `Apps\*\*\.Current` default value to `saved.json`, set them to
  "" and the scheme to `.None`. Normal: if `saved.json` has a snapshot, write it back and the saved scheme name,
  then clear the snapshot. Never touch sounds when the user's own scheme is `.None` and no snapshot exists.
- [ ] Build; check by hand on the laptop (pointer turns black at a Dark switch; Settings toggles show the accent).
  Commit `feat(app): taskbar rule and desktop profiles`.

### Task 3: Switch overlay (App)

- [ ] `SwitchOverlay.Run(SwitchAnimation anim, Action apply)`: Instant or reduced motion → `apply()` only. Otherwise:
  for each monitor (`EnumDisplayMonitors`, physical pixels) `BitBlt` the screen into a frozen `BitmapSource`; show an
  opaque, topmost, click-through (`WS_EX_LAYERED|WS_EX_TRANSPARENT|WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`, alpha 255),
  `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` window covering that monitor with the old picture; call
  `apply()`; wait 450 ms for Windows to repaint; capture again (the overlay is excluded) as the new picture below the
  old one; animate the old picture away — Roller: `OpacityMask` linear gradient (opaque below the edge, transparent
  above, soft band 12% of the height) moving top → bottom over 1.6 s, ease in-out; Fade: opacity 1 → 0 over 1.3 s;
  then close. One overlay at a time; a new Run closes the old one first; watchdog closes after 5 s; any exception
  closes the overlay and still calls `apply()` once.
- [ ] Measure frame times on the laptop with `CompositionTarget.Rendering` (log mean ms/frame); goal under 25 ms.
- [ ] Commit `feat(app): roller and fade switch overlay`.

### Task 4: Settings rows (App)

- [ ] Sections after Battery, as in the demo: "Day profile · Light theme" and "Night profile · Dark theme"
  (Accent color: 8 swatches, hidden when accent is unavailable; Mouse pointer: White/Black; Sounds: Normal/Off),
  "Windows" (Switch animation: Roller/Fade/Instant, "How the screen changes over"; "Pointer and sounds follow the
  profile", "Off keeps your daytime pointer and sounds"; "Taskbar and Start", "Follow the switch, or keep one theme":
  Follow/Always dark/Always light). A change re-applies the current look at once.
- [ ] Commit `feat(app): profile and Windows settings`.

### Task 5: Install, check, docs

- [ ] `pwsh build\release.ps1`, install silently, Celal checks a Roller switch (shortcut), pointer, accent, taskbar.
- [ ] CLAUDE.md (state, decisions, test count), spec "Calls made by Claude" (accent kept via uxtheme; night sounds
  default Normal). Commit `docs: Plan 3 handover notes`.
