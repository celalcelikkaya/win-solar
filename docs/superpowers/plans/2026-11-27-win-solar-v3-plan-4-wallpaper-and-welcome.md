# Win Solar 3 — Plan 4: Dynamic wallpaper and welcome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The user imports macOS dynamic wallpapers (.heic); the wallpaper follows the sun (solar sets), the time of
day (h24 sets) or the theme (apr sets); each profile can have its own picture instead; the user's own wallpaper is
saved and can be restored; a first-run welcome sheet lists every feature with one switch each.

**Architecture:** Core gets the pure parts: a small binary-plist reader, `DynamicSchedule` (parse Apple's XMP
`apple_desktop:solar|h24|apr` values) and `FrameChooser` (which frame now, and when the next change is). The App gets
`WallpaperImporter` (Magick.NET reads every frame at original size, converts an embedded profile to sRGB, writes
lossless PNGs + `set.json`), `WallpaperService` (applies a frame or a profile picture through `IDesktopWallpaper`,
only when it changes, with its own timer to the next frame change; saves/restores the user's wallpaper), the
settings Wallpaper section and the Welcome sheet.

**Tech Stack:** .NET 8, WPF, Magick.NET-Q8-x64 14.x (HEIC read via libheif; checked on the laptop 2026-11-27:
4 frames + XMP read in 1 s), COM `IDesktopWallpaper`, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md` — "Wallpaper", "Desktop profiles" (Wallpaper per
profile), "First run". UI wording: `win-solar-demo.html` (`settingsHtml` Wallpaper group, `welcomeHtml`).

## Global Constraints

- Core stays `net8.0`, no Windows APIs; warnings are errors; failing test first for Core behaviour.
- No PowerShell/child processes; per-user only; files under `%AppData%\Win Solar\Wallpapers\<set>\`.
- Original resolution, no cropping, lossless PNG; Display P3 (or any embedded ICC) converted to sRGB with the profile.
- Wallpaper modes: Moves with the sun (default), Per profile, Off. Until a set is imported (sun mode) or a picture is
  chosen (per profile), Win Solar does not touch the wallpaper.
- The user's own wallpaper is saved before Win Solar first changes it (and again when Win Solar sees the user change
  it); "Restore" puts it back and sets mode Off.
- Frame changes only when the frame actually differs; `IDesktopWallpaper` with Fill on every monitor.
- Clear import errors: not a dynamic HEIC, damaged file, no decoder, not enough free space.
- Welcome sheet once on first run: every feature on, one switch each, "Start using Win Solar".

## Review Focus

1. **h24 before the first item / after the last** (wrap around midnight): the last item of the previous day shows.
   Test `H24_before_first_item_wraps_to_last`.
2. **Solar set at polar night or with a place far from the set's photos**: the closest frame by sun position still
   shows; nothing throws. Test `Solar_picks_closest_sun_position` + `Solar_works_in_polar_night`.
3. **apr-only set**: follows the theme, not the sun. Test `Apr_follows_theme`.
4. **Import of a plain (single image) HEIC or a damaged file**: a clear message, nothing half-written left behind.
   Import writes into a temp folder and renames at the end.
5. **User changed the wallpaper in Windows while Win Solar runs**: saved as the user's own (Restore gives it back);
   Win Solar keeps applying its frames only in sun/per-profile mode.

---

### Task 1: Binary plist + dynamic schedule (Core)

**Produces:** `Wallpaper/BinaryPlist.Parse(byte[]) -> object?` (dict → `Dictionary<string, object?>`, array →
`List<object?>`, int → long, real → double, string, bool); `enum DynamicKind { Solar, Time, Appearance }`;
`record SolarFrame(double Altitude, double Azimuth, int Index)`; `record TimeFrame(double DayFraction, int Index)`;
`record DynamicSchedule(DynamicKind Kind, IReadOnlyList<SolarFrame> Solar, IReadOnlyList<TimeFrame> Time, int? Light, int? Dark)`;
`DynamicSchedule.FromXmp(string xmp)` (throws `FormatException` when no apple_desktop value);
`FrameChooser.Frame(DynamicSchedule, DateTimeOffset now, Place, Theme) -> int`;
`FrameChooser.NextChange(DynamicSchedule, DateTimeOffset now, Place, Theme) -> DateTimeOffset?`.

- [ ] Tests: parse the Riverside h24 XMP (4 items at 0, 0.25?, …, light/dark indices) — values read from the real file;
  `H24_picks_latest_item_before_now`, `H24_before_first_item_wraps_to_last`, `Solar_picks_closest_sun_position`
  (synthetic plist built in the test), `Solar_works_in_polar_night`, `Apr_follows_theme`,
  `Next_change_is_the_next_frame_boundary`, `FromXmp_rejects_plain_xmp`.

### Task 2: Import (App)

- [ ] `WallpaperImporter.Inspect(path)` → frames, size, kind, estimated PNG size, free space on the AppData drive;
  `Import(path, name)` → writes `NN.png` + `set.json` (`WallpaperSet(Name, Kind, Schedule, Frames)`) in a temp folder,
  then moves it into `Wallpapers\<name>`. ICC present → `TransformColorSpace(profile, ColorProfile.SRGB)`.
- [ ] Check on the laptop with `D:\WinSolarTest\Riverside by ArseniXC.heic`.

### Task 3: Apply + save/restore (App)

- [ ] `WallpaperService`: `IDesktopWallpaper` (COM) set on every monitor with Fill; `saved-wallpaper.json` with the
  user's own path per monitor saved before the first change; `Restore()`; timer to `FrameChooser.NextChange`;
  re-applies on engine snapshot (theme/place change) and settings change; per-profile mode uses `Profile.Wallpaper`.
- [ ] Settings: `WallpaperMode Wallpaper = Sun`, `string? ActiveSet`; `Profile` gets `string? Wallpaper` (Core, with a
  compat test that Plan 3 settings load).

### Task 4: Settings and welcome (App)

- [ ] Wallpaper section as in the demo: mode (Moves with the sun / Per profile / Off), active set with a frame strip
  and "Now showing …", "Import…" (file picker, shows size and free space first, then a preview strip and "Use this
  wallpaper"), delete a set, "Your own wallpaper · Restore". Per profile: a "Wallpaper" row with "Choose…" in each
  profile group.
- [ ] Welcome sheet on first run (no settings.json before start): text and switches from the demo (minus travel and
  taskbar), "Choose a dynamic wallpaper" button, "Start using Win Solar".

### Task 5: Install, check, docs, review
