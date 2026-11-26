# Win Solar 3 — Plan 1: Foundation and v2 parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A native WPF Win Solar that does everything v2 did (sun schedule, modes, heads-up with Snooze/Skip, tray, flyout, settings, start with Windows) with a testable core engine, installable with Inno Setup next to and replacing v2.

**Architecture:** `WinSolar.Core` (net8.0, no Windows APIs) holds the sun math, schedule, decision priority and a `SolarEngine` that talks to the outside world only through small interfaces, so all behaviour is unit-tested with fakes. `WinSolar.App` (WPF) implements those interfaces with Windows APIs and draws the tray, flyout and settings. One timer fires at the next due moment; there is no polling.

**Tech Stack:** .NET 8, WPF, C# latest, xUnit, CommunityToolkit.Mvvm 8.4.2 (as Harbor), H.NotifyIcon.Wpf 2.1.x (tray), CommunityToolkit.WinUI.Notifications 7.1.2 (toasts), Inno Setup 6.

**Spec:** `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md` (approved). Visual reference: `docs/superpowers/specs/win-solar-demo.html`.

**Roadmap (later plans, written when this one is done):**
- Plan 2: Real light (sensor + self-calibration, weather fallback), battery mode, travel detection.
- Plan 3: Roller/Fade switch overlay, Day/Night profiles (pointer, sounds, accent), taskbar theme option.
- Plan 4: Dynamic wallpaper import (.heic, original quality), welcome screen with every feature on.
- Plan 5: In-app updates, Defender/SmartScreen release checklist, removing the Electron code, merge.

## Global Constraints

- Target `net8.0-windows10.0.19041.0` for the app, `net8.0` for Core and tests; x64; `Nullable` enable; `TreatWarningsAsErrors` true (as Harbor).
- No PowerShell, no script hosts, no child processes for Windows changes.
- UI text English only. Product name "Win Solar", company "Celal Çelikkaya", version 3.0.0.
- Settings and state in `%AppData%\Win Solar\` (`settings.json`, `state.json`).
- Per-user install, app manifest `requestedExecutionLevel level="asInvoker"`.
- Builds and tests run on Celal's laptop (Windows 11, .NET 8 SDK). C: has ~11 GB free: clone to `D:\Projects\win-solar`, keep NuGet cache on D:.
- Branch `v3-wpf`. Electron code stays in the repo untouched until Plan 5.
- Commits end with the `Co-Authored-By` / `Claude-Session` trailers the session gives.

## Review Focus

1. **Place time zone differs from the PC time zone** (place Istanbul, PC on Blantyre): every time shown and every switch must use the place's zone. Test in Task 3.
2. **Polar day/night** (Tromsø in December/June): no transitions for days; no crash, no timer set in the past, theme = dark/light all day. Tests in Tasks 2, 3, 6.
3. **Laptop asleep across a switch**: on wake after the switch time the theme switches once, immediately, with no heads-up for a switch already past. Test in Task 6.
4. **Damaged or half-written settings file** (power loss during save): app starts with defaults (or the last good file), never crashes; writes are atomic. Test in Task 5.
5. **Saved hold or manual-until already in the past at startup** (PC was off overnight): cleared on load, normal Auto applies, no stale "Postponed until" banner. Test in Task 6.

---

### Task 1: Solution scaffold and sun calculator

**Files:**
- Create: `WinSolar.sln`, `Directory.Build.props`, `src/WinSolar.Core/WinSolar.Core.csproj`, `tests/WinSolar.Tests/WinSolar.Tests.csproj`
- Create: `src/WinSolar.Core/Sun/SunCalculator.cs`, `src/WinSolar.Core/Sun/SunDay.cs`, `src/WinSolar.Core/Place.cs`
- Create: `nuget.config` (no sources change; only `globalPackagesFolder` = `D:\.nuget\packages` is **not** committed: set it as the `NUGET_PACKAGES` user environment variable instead, see Step 1)
- Test: `tests/WinSolar.Tests/Sun/SunCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record Place(string Name, string? Country, double Lat, double Lng, string TimeZoneId)`
  - `public enum PolarState { None, Day, Night }`
  - `public sealed record SunDay(DateTimeOffset? Sunrise, DateTimeOffset? Sunset, DateTimeOffset Noon, PolarState Polar)` (all UTC)
  - `public static class SunCalculator { SunDay ForDate(DateOnly date, double lat, double lng); double Elevation(DateTimeOffset at, double lat, double lng); }`

- [ ] **Step 1: Prepare the laptop**

In Windows Terminal: `setx NUGET_PACKAGES D:\.nuget\packages`, open a new terminal, then
`git clone https://github.com/celalcelikkaya/win-solar D:\Projects\win-solar`, `cd D:\Projects\win-solar`, `git checkout v3-wpf`.
Expected: `git status` shows `On branch v3-wpf`, clean.

- [ ] **Step 2: Create the solution**

`Directory.Build.props` as Harbor's (Nullable, LangVersion latest, TreatWarningsAsErrors, x64) with `<Version>3.0.0</Version>`, `<Company>Celal Çelikkaya</Company>`, `<Product>Win Solar</Product>`. Core: `net8.0` class library. Tests: `net8.0` xUnit referencing Core.
Run: `dotnet build` → Expected: `Build succeeded. 0 Warning(s)`.

- [ ] **Step 3: Write the failing tests**

Port of `src/lib/sun.ts` (same NOAA equation). The expected values below were produced by that TypeScript code, so a faithful port matches them within a minute.

```csharp
static TimeOnly Local(DateTimeOffset? utc, string tz) =>
    TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc!.Value, TimeZoneInfo.FindSystemTimeZoneById(tz)).DateTime);

[Fact] public void Blantyre_2026_11_26()
{
    var d = SunCalculator.ForDate(new DateOnly(2026, 11, 26), -15.7861, 35.0058);
    Assert.Equal(PolarState.None, d.Polar);
    AssertNear(new TimeOnly(4, 58), Local(d.Sunrise, "Africa/Blantyre"), 1);
    AssertNear(new TimeOnly(17, 55), Local(d.Sunset, "Africa/Blantyre"), 1);
    AssertNear(new TimeOnly(11, 27), Local(d.Noon, "Africa/Blantyre"), 1);
}
[Fact] public void Tromso_midwinter_is_polar_night()
    => Assert.Equal(PolarState.Night, SunCalculator.ForDate(new DateOnly(2026, 12, 21), 69.6496, 18.957).Polar);
[Fact] public void Tromso_midsummer_is_polar_day()
    => Assert.Equal(PolarState.Day, SunCalculator.ForDate(new DateOnly(2026, 6, 21), 69.6496, 18.957).Polar);
[Fact] public void Elevation_is_about_minus_0_8_at_sunset()
{
    var d = SunCalculator.ForDate(new DateOnly(2026, 11, 26), -15.7861, 35.0058);
    Assert.InRange(SunCalculator.Elevation(d.Sunset!.Value, -15.7861, 35.0058), -1.5, 0.0);
}
```
(`AssertNear(expected, actual, minutes)` is a small helper in the test class.)

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test` → Expected: build errors, `SunCalculator` not found.

- [ ] **Step 5: Implement `SunCalculator.ForDate` and `SunCalculator.Elevation`**

`ForDate`: line-for-line port of `sunForDate` in `src/lib/sun.ts`. `Elevation`: the SunCalc formula used in the demo (`sunElev` in `docs/superpowers/specs/win-solar-demo.html`).

- [ ] **Step 6: Run to verify they pass**

Run: `dotnet test` → Expected: `Passed: 4`.

- [ ] **Step 7: Commit** — `git add -A` then `git commit -m "feat(core): solution scaffold and sun calculator"`

---

### Task 2: Schedule calculator

**Files:**
- Create: `src/WinSolar.Core/Schedule/Theme.cs`, `Transition.cs`, `ScheduleOptions.cs`, `Schedule.cs`, `ScheduleCalculator.cs`
- Test: `tests/WinSolar.Tests/Schedule/ScheduleCalculatorTests.cs`

**Interfaces:**
- Consumes: `SunCalculator.ForDate`, `Place`
- Produces:
  - `public enum Theme { Light, Dark }`, `public enum TransitionKind { Sunrise, Sunset, Fixed }`, `public enum Phase { Night, Dawn, Day, Dusk }`, `public enum ScheduleSource { Sun, Fixed }`
  - `public sealed record Transition(DateTimeOffset At, Theme Theme, TransitionKind Kind, int ShiftMinutes = 0)`
  - `public sealed record ScheduleOptions(ScheduleSource Source, int OffsetSunriseMin, int OffsetSunsetMin, TimeOnly FixedLight, TimeOnly FixedDark)`
  - `public sealed record Schedule(Transition? Prev, Transition? Next, Transition? After, Theme Theme, Phase Phase, double Progress, SunDay Today, Transition? TodayLight, Transition? TodayDark, IReadOnlyList<DateTimeOffset> SunEvents)`
  - `public static class ScheduleCalculator { Schedule Compute(DateTimeOffset now, Place place, ScheduleOptions opt, Func<DateTimeOffset, TransitionKind, DateTimeOffset>? adjust = null); }`
  - `adjust` is the seam Plan 2 uses for weather; it receives the astronomical event and returns the moved time; `ShiftMinutes` records the move.

- [ ] **Step 1: Write the failing tests**

Behaviour mirrors `computeSchedule` in `src/lib/engine.ts`: transitions for days −1..+2 in the **place's** zone, prev/next/after, theme from prev (polar day → Light, polar night → Dark), phase Dawn/Dusk within 40 min of a switch, progress 0..1, offsets in minutes.

```csharp
static readonly Place Blantyre = new("Blantyre", "Malawi", -15.7861, 35.0058, "Africa/Blantyre");
static readonly ScheduleOptions Sun = new(ScheduleSource.Sun, 0, 0, new(7, 0), new(19, 0));
static DateTimeOffset At(string localIso, string tz) => /* local wall time in tz → DateTimeOffset */;

[Fact] public void Before_sunset_is_light_and_next_is_sunset()
{
    var s = ScheduleCalculator.Compute(At("2026-11-26T17:36", "Africa/Blantyre"), Blantyre, Sun);
    Assert.Equal(Theme.Light, s.Theme);
    Assert.Equal(TransitionKind.Sunset, s.Next!.Kind);
    Assert.Equal(Theme.Dark, s.Next.Theme);
    Assert.Equal(Phase.Dusk, s.Phase);
}
[Fact] public void Sunset_offset_moves_the_switch()
{
    var a = ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun);
    var b = ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun with { OffsetSunsetMin = 10 });
    Assert.Equal(TimeSpan.FromMinutes(10), b.Next!.At - a.Next!.At);
}
[Fact] public void Fixed_times_before_7_are_dark_with_next_light_at_7()
{
    var s = ScheduleCalculator.Compute(At("2026-11-26T06:00", "Africa/Blantyre"), Blantyre, Sun with { Source = ScheduleSource.Fixed });
    Assert.Equal(Theme.Dark, s.Theme);
    Assert.Equal(At("2026-11-26T07:00", "Africa/Blantyre"), s.Next!.At);
}
[Fact] public void Polar_night_has_dark_theme_and_no_crash()
{
    var tromso = new Place("Tromsø", "Norway", 69.6496, 18.957, "Europe/Oslo");
    var s = ScheduleCalculator.Compute(At("2026-12-21T12:00", "Europe/Oslo"), tromso, Sun);
    Assert.Equal(Theme.Dark, s.Theme);
    Assert.Equal(PolarState.Night, s.Today.Polar);
}
[Fact] public void Adjust_hook_moves_sunset_and_records_shift()
{
    var s = ScheduleCalculator.Compute(At("2026-11-26T12:00", "Africa/Blantyre"), Blantyre, Sun,
        (t, k) => k == TransitionKind.Sunset ? t.AddMinutes(-15) : t);
    Assert.Equal(-15, s.Next!.ShiftMinutes);
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter ScheduleCalculatorTests` → Expected: FAIL (types missing).
- [ ] **Step 3: Implement `ScheduleCalculator.Compute`** — port of `computeSchedule`; date parts come from `TimeZoneInfo.ConvertTime(now, place zone)`; fixed times built with the place zone (`TimeZoneInfo.ConvertTimeToUtc` on an unspecified `DateTime`, so DST gaps resolve forward).
- [ ] **Step 4: Run to verify they pass** — `dotnet test` → Expected: all pass.
- [ ] **Step 5: Commit** — `feat(core): schedule calculator`

---

### Task 3: Time zones and formatting

**Files:**
- Create: `src/WinSolar.Core/Presentation/Format.cs`
- Test: `tests/WinSolar.Tests/Presentation/FormatTests.cs`, add one test to `ScheduleCalculatorTests.cs`

**Interfaces:**
- Produces: `public static class Format { string Duration(int minutes); string Time(DateTimeOffset at, string timeZoneId, bool hour12); string When(DateTimeOffset at, DateTimeOffset now, string timeZoneId, bool hour12); }`
  - `Duration(100)` → `"1 h 40 m"`, `Duration(5)` → `"5 min"` (demo wording).
  - `When` adds `" tomorrow"` when the date in the place zone differs from today's.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Duration_formats() { Assert.Equal("1 h 40 m", Format.Duration(100)); Assert.Equal("5 min", Format.Duration(5)); }
[Fact] public void Time_24h_and_12h()
{
    var t = new DateTimeOffset(2026, 11, 26, 15, 55, 0, TimeSpan.Zero);        // 17:55 in Blantyre
    Assert.Equal("17:55", Format.Time(t, "Africa/Blantyre", false));
    Assert.Equal("5:55 PM", Format.Time(t, "Africa/Blantyre", true));
}
[Fact] public void When_says_tomorrow()
{
    var now = new DateTimeOffset(2026, 11, 26, 18, 0, 0, TimeSpan.Zero);
    Assert.EndsWith(" tomorrow", Format.When(now.AddHours(10), now, "Africa/Blantyre", false));
}
// in ScheduleCalculatorTests — Review Focus 1
[Fact] public void Uses_place_zone_not_pc_zone()
{
    var istanbul = new Place("Istanbul", "Türkiye", 41.0138, 28.9497, "Europe/Istanbul");
    var s = ScheduleCalculator.Compute(new DateTimeOffset(2026, 11, 26, 10, 0, 0, TimeSpan.Zero), istanbul, Sun);
    var localSunset = TimeZoneInfo.ConvertTime(s.TodayDark!.At, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"));
    Assert.InRange(localSunset.Hour, 16, 17);   // Istanbul late-November sunset ≈ 16:40
}
```

- [ ] **Step 2: Run, expect FAIL** — `dotnet test --filter "FormatTests|Uses_place_zone"`.
- [ ] **Step 3: Implement `Format`** with `CultureInfo.InvariantCulture` patterns `HH:mm` / `h:mm tt`.
- [ ] **Step 4: Run, expect PASS** — `dotnet test`.
- [ ] **Step 5: Commit** — `feat(core): formatting in the place's time zone`

---

### Task 4: Decision rules

**Files:**
- Create: `src/WinSolar.Core/Decision/Mode.cs`, `Hold.cs`, `Cause.cs`, `ThemeDecider.cs`, `Holds.cs`
- Test: `tests/WinSolar.Tests/Decision/ThemeDeciderTests.cs`, `HoldsTests.cs`

**Interfaces:**
- Consumes: `Schedule`, `Transition`, `Theme`
- Produces:
  - `public enum Mode { Auto, Light, Dark }`
  - `public enum HoldKind { Snooze, Skip, Pause, SwitchNow }`, `public sealed record Hold(Theme Theme, DateTimeOffset Until, HoldKind Kind)`
  - `public enum Cause { Manual, Battery, Hold, SwitchNow, Room, Sunrise, Sunset, Fixed, Polar, Start, FullScreenEnded, Shortcut }`
  - `public sealed record DecisionInput(Mode Mode, Hold? Hold, bool BatteryDark, Theme? RoomTheme, bool InLightWindow, Schedule Schedule)`
  - `public sealed record Decision(Theme Theme, Cause Cause)`
  - `public static class ThemeDecider { Decision Decide(DecisionInput i); bool ShouldDefer(Cause cause, bool fullScreen, bool waitForFullScreen); }`
  - `public static class Holds { Hold? Snooze(Schedule s, Theme current); Hold? Skip(Schedule s, Theme current); Hold Pause(DateTimeOffset now, Theme current, TimeSpan length); Hold? SwitchNow(Schedule s); DateTimeOffset? ManualUntil(Schedule s, bool endsByItself); }`
  - Spec values: Snooze = until `Next.At + 15 min`; Skip = until `After.At`, or `Next.At + 12 h` if there is no After; Pause default 1 h; SwitchNow = `Next.Theme` until `Next.At`.

- [ ] **Step 1: Write the failing tests** (spec "What decides the theme": Manual > Battery > Hold > Room (only inside light window) > Schedule; defer only automatic causes)

```csharp
[Fact] public void Manual_beats_everything() => Assert.Equal(new Decision(Theme.Dark, Cause.Manual),
    ThemeDecider.Decide(new(Mode.Dark, AnyHold(Theme.Light), true, Theme.Light, true, LightSchedule)));
[Fact] public void Battery_beats_hold_and_schedule() => Assert.Equal(Cause.Battery,
    ThemeDecider.Decide(new(Mode.Auto, AnyHold(Theme.Light), true, null, false, LightSchedule)).Cause);
[Fact] public void Hold_beats_room_and_schedule() => Assert.Equal(Theme.Light,
    ThemeDecider.Decide(new(Mode.Auto, AnyHold(Theme.Light), false, Theme.Dark, true, DarkSchedule)).Theme);
[Fact] public void Room_only_counts_inside_light_window()
{
    Assert.Equal(Cause.Room, ThemeDecider.Decide(new(Mode.Auto, null, false, Theme.Dark, true, LightSchedule)).Cause);
    Assert.Equal(Theme.Light, ThemeDecider.Decide(new(Mode.Auto, null, false, Theme.Dark, false, LightSchedule)).Theme);
}
[Fact] public void Schedule_cause_is_previous_transition_kind() => Assert.Equal(Cause.Sunset,
    ThemeDecider.Decide(new(Mode.Auto, null, false, null, false, DarkSchedule)).Cause);
[Theory, InlineData(Cause.Sunset, true), InlineData(Cause.Battery, true), InlineData(Cause.Manual, false),
 InlineData(Cause.Shortcut, false), InlineData(Cause.SwitchNow, false)]
public void Defers_only_automatic_switches(Cause c, bool expected) => Assert.Equal(expected, ThemeDecider.ShouldDefer(c, true, true));
// HoldsTests: Snooze_is_next_plus_15_min, Skip_is_until_the_switch_after_next, Skip_without_after_is_12_hours,
//             SwitchNow_takes_next_theme_until_next, ManualUntil_is_next_switch_or_null_when_off
```
(`LightSchedule` / `DarkSchedule` / `AnyHold` are test builders.)

- [ ] **Step 2: Run, expect FAIL.** - [ ] **Step 3: Implement `ThemeDecider` and `Holds`.** - [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(core): decision priority and holds`

---

### Task 5: Settings, state and atomic JSON store

**Files:**
- Create: `src/WinSolar.Core/Settings/AppSettings.cs`, `AppState.cs`, `JsonFileStore.cs`
- Test: `tests/WinSolar.Tests/Settings/JsonFileStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AppSettings` with defaults (Plan 1 fields only): `Place? Place = null`, `ScheduleSource Source = Sun`, `int OffsetSunriseMin = 0`, `int OffsetSunsetMin = 0`, `TimeOnly FixedLight = 07:00`, `TimeOnly FixedDark = 19:00`, `bool HeadsUp = true`, `bool WaitForFullScreen = true`, `bool ManualEndsByItself = true`, `bool Shortcut = true`, `bool NotifyAfterSwitch = true`, `bool StartWithWindows = true`, `bool HideOnClickOutside = true`, `bool Hour12 = false`, `int Schema = 3`.
  - `public sealed record AppState(Mode Mode = Auto, DateTimeOffset? ManualUntil = null, Hold? Hold = null, Theme? LastApplied = null)`
  - `public sealed class JsonFileStore { JsonFileStore(string folder); T Load<T>(string name, Func<T> defaults); void Save<T>(string name, T value); }`
  - Save writes `name.tmp` then `File.Move(tmp, name, overwrite: true)`; Load returns `defaults()` on missing file or any `JsonException`/`IOException`, and keeps the damaged file as `name.bad` for diagnosis.

- [ ] **Step 1: Write the failing tests** (Review Focus 4)

```csharp
[Fact] public void Round_trips_settings() { /* save AppSettings with Hour12 = true, load, Assert.True(loaded.Hour12) */ }
[Fact] public void Missing_file_gives_defaults() { /* Assert.Equal(new AppSettings(), store.Load("settings.json", () => new AppSettings())) */ }
[Fact] public void Damaged_file_gives_defaults_and_is_kept_as_bad()
{
    File.WriteAllText(Path.Combine(dir, "settings.json"), "{ \"Hour12\": tr");   // cut off mid-write
    Assert.Equal(new AppSettings(), store.Load("settings.json", () => new AppSettings()));
    Assert.True(File.Exists(Path.Combine(dir, "settings.json.bad")));
}
[Fact] public void Leftover_tmp_file_does_not_break_save() { /* create settings.json.tmp, Save, Load equals saved */ }
```

- [ ] **Step 2: Run, expect FAIL.** - [ ] **Step 3: Implement** with `System.Text.Json` (`JsonStringEnumConverter`, `WriteIndented`). - [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** — `feat(core): settings and state with atomic saves`

---

### Task 6: SolarEngine

The heart: one `Tick()` that decides, defers, applies, raises the heads-up and persists, plus the user actions.

**Files:**
- Create: `src/WinSolar.Core/Engine/Ports.cs`, `EngineSnapshot.cs`, `SolarEngine.cs`, `NextDue.cs`
- Test: `tests/WinSolar.Tests/Engine/SolarEngineTests.cs`, `NextDueTests.cs`, `tests/WinSolar.Tests/Fakes/*.cs`

**Interfaces:**
- Consumes: Tasks 2, 4, 5.
- Produces (`Ports.cs`):
  - `public interface IClock { DateTimeOffset Now { get; } }`
  - `public interface IThemeApplier { Theme Read(); void Apply(Theme theme); }`
  - `public interface INotifier { void HeadsUp(Transition next, int minutesLeft); void DismissHeadsUp(); void Switched(Theme theme, Cause cause, Transition? next); }`
  - `public interface IFullScreenProbe { bool IsFullScreen(); }`
  - `public interface IStateStore { AppSettings Settings { get; } AppState State { get; } void Save(AppState state); }`
- Produces (engine):
  - `public sealed record EngineSnapshot(Schedule Schedule, Mode Mode, DateTimeOffset? ManualUntil, Hold? Hold, Theme Applied, bool WaitingForFullScreen, Transition? HeadsUp, Place Place)`
  - `public sealed class SolarEngine(IClock clock, IThemeApplier theme, INotifier notifier, IFullScreenProbe fullScreen, IStateStore store, Place fallbackPlace)` with
    `EngineSnapshot Start()`, `EngineSnapshot Tick()`, `void SetMode(Mode mode)`, `void Snooze()`, `void Skip()`, `void SwitchNow()`, `void Pause(TimeSpan length)`, `void Resume()`, `(Theme Theme, string Detail) Shortcut()`, `DateTimeOffset NextDue { get; }`, `event Action<EngineSnapshot>? Changed`.
  - `public static class NextDue { DateTimeOffset Compute(DateTimeOffset now, Schedule s, Hold? hold, DateTimeOffset? manualUntil, TimeSpan headsUpLead); }` → the earliest future moment among `Next.At − 5 min`, `Next.At`, `Hold.Until`, `ManualUntil`, the start of the next minute when a heads-up is showing (countdown), else `now + 6 h` (polar). Never ≤ now.
- Rules (spec): heads-up 5 min before an automatic switch, once per transition, not when Mode ≠ Auto, a hold exists, full screen, or the switch already passed; `Start()` clears expired hold/manual, applies only if `theme.Read()` differs from the decision (Cause.Start) and never notifies for it; `Shortcut()` flips the applied theme as manual (Cause.Shortcut), or returns to Auto when the flip equals the schedule; returned `Detail` is "Auto again at 05:14" / "Back to Auto" / "Until you change it".

- [ ] **Step 1: Write the failing tests** (fakes: `FakeClock` settable, `FakeTheme` counting applies, `FakeNotifier` recording calls, `FakeFullScreen` settable, `MemoryStore`)

```csharp
[Fact] public void Applies_due_switch_once()          // tick at sunset+1 min twice → theme.Applies == 1, notifier.Switched cause Sunset
[Fact] public void Quiet_start_when_theme_already_right()   // theme.Read() == Light at noon → Start() → Applies == 0, no Switched
[Fact] public void Start_applies_with_Start_cause_and_no_notification()
[Fact] public void HeadsUp_once_five_minutes_before()    // ticks at −6, −5, −4 min → HeadsUp called once, minutesLeft 5
[Fact] public void No_headsUp_in_manual_or_hold_or_fullscreen()
[Fact] public void Snooze_holds_current_theme_and_is_saved() // Snooze at −4 → store.State.Hold.Until == next + 15 min; tick at next+1 → still Light
[Fact] public void FullScreen_defers_then_applies_on_exit()  // fullscreen at sunset → no apply, snapshot.WaitingForFullScreen; exit → Switched(…, FullScreenEnded)
[Fact] public void Manual_applies_immediately_even_in_fullscreen()
[Fact] public void Manual_ends_at_next_switch()          // SetMode(Dark) at noon, tick after sunrise next day → Mode Auto
[Fact] public void Wake_after_missed_switch_applies_once_without_headsUp()     // Review Focus 3: clock jumps from −30 min to +2 h
[Fact] public void Expired_hold_and_manual_are_cleared_on_start()              // Review Focus 5
[Fact] public void Polar_night_never_sets_a_due_time_in_the_past()             // Review Focus 2: NextDue > now, == now + 6 h
[Fact] public void Shortcut_flips_then_returns_to_auto()
```

- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement `NextDue.Compute` and `SolarEngine`.** `Tick()`: clear expired → compute schedule (place = settings.Place ?? fallbackPlace) → decide → `ShouldDefer` → apply/notify → heads-up → save state when it changed → raise `Changed`. Battery and room inputs are `false`/`null` in Plan 1.
- [ ] **Step 4: Run, expect PASS** — `dotnet test` → all tests pass.
- [ ] **Step 5: Commit** — `feat(core): solar engine with heads-up, holds, full-screen wait`

---

### Task 7: WPF app shell, Windows theme service and engine host

**Files:**
- Create: `src/WinSolar.App/WinSolar.App.csproj`, `app.manifest`, `App.xaml(.cs)`, `GlobalUsings.cs`
- Create: `src/WinSolar.App/Services/WindowsTheme.cs`, `SystemClock.cs`, `FullScreenProbe.cs`, `AppStateStore.cs`, `EngineHost.cs`, `SingleInstance.cs`
- Modify: `WinSolar.sln`

**Interfaces:**
- Consumes: all Core ports.
- Produces:
  - `WindowsTheme : IThemeApplier` — reads/writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize` `AppsUseLightTheme` and `SystemUsesLightTheme` (1 = light), writes only values that differ, then `SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", SMTO_ABORTIFHUNG, 200)`.
  - `FullScreenProbe : IFullScreenProbe` — `SHQueryUserNotificationState` is `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN` or `QUNS_PRESENTATION_MODE`, or the foreground window (not desktop/shell) covers its whole monitor.
  - `AppStateStore : IStateStore` over `JsonFileStore("%AppData%\Win Solar")`.
  - `EngineHost` — owns `SolarEngine`, one `DispatcherTimer` set to `engine.NextDue`, re-ticks on `SystemEvents.PowerModeChanged` (Resume), `SessionSwitch` (SessionUnlock), `TimeChanged` (after `TimeZoneInfo.ClearCachedData()`), and every 10 s **only while** a full-screen wait is pending.
  - Csproj: `WinExe`, `net8.0-windows10.0.19041.0`, `UseWPF`, `ApplicationManifest app.manifest` (`asInvoker`, PerMonitorV2 DPI), `AssemblyName WinSolar`, package refs from Tech Stack.

- [ ] **Step 1: Create the project and build** — `dotnet build` → `Build succeeded. 0 Warning(s)`.
- [ ] **Step 2: Manual check on the laptop** — run `src\WinSolar.App\bin\Debug\...\WinSolar.exe` with no UI yet (tray comes next): set the place to Blantyre in `%AppData%\Win Solar\settings.json`, set Windows to the wrong theme, start the app → Windows switches within a second, no console window, Task Manager shows < 60 MB. Start it again → second instance exits (single instance via named mutex `WinSolar.SingleInstance`).
- [ ] **Step 3: Commit** — `feat(app): WPF host, Windows theme service, engine timer`

---

### Task 8: Tray icon, tooltip and menu

**Files:**
- Create: `src/WinSolar.App/Tray/TrayIcon.cs`, `Tray/TrayGlyphs.cs`, `Assets/sun.ico`, `Assets/moon.ico` (generated from vector at 16/20/24/32/48 px)

**Interfaces:**
- Consumes: `EngineHost.Engine`, `EngineSnapshot`, `Format`.
- Produces: `TrayIcon(EngineHost host, Action toggleFlyout, Action openSettings)`; `void Update(EngineSnapshot s)`.

- [ ] **Step 1: Implement** with H.NotifyIcon: icon = sun (Applied Light) or moon (Dark); badge overlay amber (hold/pause), orange (waiting for full screen), accent (manual) drawn onto the icon at runtime; tooltip lines exactly as spec ("Win Solar · Dark", then "Light in 22 min (05:14)" / "Postponed until 18:10" / "Waiting for full screen to close" / "Dark until 05:14"); left click → `toggleFlyout`; context menu: Auto (sunrise and sunset) / Light / Dark (checked by Mode), separator, "Pause switching for 1 hour" or "Resume switching (paused until 19:42)", separator, "Open Win Solar", "Settings", "Quit".
- [ ] **Step 2: Manual check** — at 200% scaling the icon is crisp; right-click menu items change the mode and Windows follows; tooltip countdown updates within a minute.
- [ ] **Step 3: Commit** — `feat(app): tray icon with live glyph, tooltip and menu`

---

### Task 9: Flyout window (shell and motion)

**Files:**
- Create: `src/WinSolar.App/Flyout/FlyoutWindow.xaml(.cs)`, `Flyout/TaskbarEdge.cs`, `Theme/Tokens.xaml`, `Theme/Controls.xaml`, `Interop/Dwm.cs`

**Interfaces:**
- Produces: `FlyoutWindow.Toggle()`, `Show()`, `Hide()`; `TaskbarEdge.Get() → (Edge edge, Rect workArea)` via `SHAppBarMessage(ABM_GETTASKBARPOS)`; `enum Edge { Bottom, Top, Left, Right }`.
- Values from the demo: width 360 DIP, 12 DIP from the screen edge, opens next to the tray; slide 28 DIP from the taskbar edge with opacity, open 300 ms `CubicEase EaseOut`, close 150 ms `CubicEase EaseIn`; corner radius 12; Mica via `DwmSetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_TRANSIENTWINDOW = 3)` with a transparent window background, falling back to a solid `#F3F3F3` / `#202124` on Windows 10; light/dark tokens follow the applied theme.

- [ ] **Step 1: Implement**: borderless, `ShowInTaskbar=false`, `Topmost` only while pinned; hides on `Deactivated` unless pinned (pin button in the header); Esc hides.
- [ ] **Step 2: Manual check** — move the taskbar to the top (Windows setting or registry tool): the flyout opens below it, sliding down; click outside hides it; pinned stays.
- [ ] **Step 3: Commit** — `feat(app): flyout window with taskbar-edge placement and Mica`

---

### Task 10: Flyout content

**Files:**
- Create: `src/WinSolar.App/Flyout/FlyoutViewModel.cs`, `Flyout/HeroArc.xaml(.cs)`, `Flyout/ModeSwitch.xaml`, `Flyout/StatTiles.xaml`, `Flyout/DayBar.xaml`, `Flyout/NextDays.xaml`, `Flyout/Banner.xaml`
- Modify: `Flyout/FlyoutWindow.xaml`

**Interfaces:**
- Consumes: `EngineSnapshot`, `Format`, `SunCalculator.ForDate` (7-day table), engine actions.
- Produces: `FlyoutViewModel(EngineHost host)` with observable properties bound by the views; commands `SetModeCommand(Mode)`, `SnoozeCommand`, `SkipCommand`, `SwitchNowCommand`, `ResumeCommand`, `OpenLocationCommand`, `OpenSettingsCommand`.

- [ ] **Step 1: Implement** the demo's layout and copy: header (pin icon + place name ▾, pin, settings); banner (heads-up: "Switching to Dark in 4 min" + Snooze 15 min / Skip / Switch now; hold: "Switch postponed to 18:10" + Switch now; pause: "Switching paused until 19:42" + Resume now; wait: "Dark theme is due. It switches when you leave full screen."); hero with sky gradient per phase (day `#3F8EF2→#86C1FF`, dawn `#4B5BC9→#C9709B→#F6A95E`, dusk `#26306F→#8B4F8F→#EF8655`, night `#0A1330→#1A2A5C`, 1.2 s crossfade), arc radius 92 in a 320×118 box, sun/moon eased along the arc over ~1.3 s on open, twinkling stars at night; countdown ("1 h 40 m", caption "Until Dark · 17:55"); Auto/Light/Dark switch with sliding accent thumb (280 ms); hint ("Follows sunrise and sunset" / "Dark until 05:14, then back to Auto"); four tiles (Sunrise, Sunset with "Dark at 18:05" when offset, Solar noon, Day length); today bar with now marker; "Next 7 days" expander.
- [ ] **Step 2: Manual check** — compare side by side with `docs/superpowers/specs/win-solar-demo.html`; nothing overlaps at 360 DIP width; light and dark both readable; countdown ticks each minute.
- [ ] **Step 3: Commit** — `feat(app): flyout content`

---

### Task 11: Settings and location sheets

**Files:**
- Create: `src/WinSolar.App/Settings/SettingsSheet.xaml(.cs)`, `Settings/SettingsViewModel.cs`, `Settings/LocationSheet.xaml(.cs)`, `Services/PlaceSearch.cs`, `Services/WindowsLocation.cs`, `Services/Autostart.cs`

**Interfaces:**
- Consumes: `IStateStore`, `AppSettings`, `EngineHost`.
- Produces:
  - `PlaceSearch.SearchAsync(string query, CancellationToken ct) → Task<IReadOnlyList<Place>>` — `https://geocoding-api.open-meteo.com/v1/search?name={q}&count=8&language=en&format=json`, IANA zone converted with `TimeZoneInfo.FindSystemTimeZoneById` (.NET 8 accepts IANA ids on Windows via ICU).
  - `WindowsLocation.GetAsync(CancellationToken ct) → Task<Place?>` — `Windows.Devices.Geolocation.Geolocator` (city accuracy), name from a reverse lookup of the nearest search result; returns null when location is off, and the sheet says "Turn on location in Windows Settings > Privacy & security > Location."
  - `Autostart.Set(bool on)` — `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `Win Solar` = `"<exe path>" --minimized`; `Autostart.IsOn()`.
- Sheet sections for Plan 1 (later plans add theirs): Schedule (Sunrise and sunset / Fixed times; Light and Dark offsets ±120 in 5-minute steps, or two time pickers; Location row), Around a switch (Warn before switching, Wait for full-screen apps, Manual mode ends by itself, Keyboard shortcut, Notify after switching), General (Start with Windows, Hide when clicking outside, 24-hour clock), About (Win Solar 3.0.0, Celal Çelikkaya). Popular places list = the 12 places in the demo's `PLACES`.

- [ ] **Step 1: Implement**; every change saves immediately and calls `host.Tick()`.
- [ ] **Step 2: Manual check** — search "Istanbul" → pick → flyout shows Istanbul times; offline search shows "Search is unavailable right now. Pick from the list below." and the popular list; Start with Windows on → value appears in the Run key; reboot → app starts hidden in the tray.
- [ ] **Step 3: Commit** — `feat(app): settings and location`

---

### Task 12: Notifications and shortcut

**Files:**
- Create: `src/WinSolar.App/Services/ToastNotifier.cs`, `Services/Hotkey.cs`, `Flyout/Osd.xaml(.cs)`

**Interfaces:**
- Consumes: `INotifier`, `SolarEngine.Shortcut()`, engine actions.
- Produces: `ToastNotifier : INotifier` (CommunityToolkit.WinUI.Notifications; activation arguments `action=snooze|skip|now` routed to the engine; heads-up toast uses tag `headsup` so `DismissHeadsUp` removes it and the countdown updates by replacing it); `Hotkey.Register(ModifierKeys.Windows | ModifierKeys.Shift, Key.D, Action onPress) → bool` via `RegisterHotKey` on a message-only window.
- Copy (spec): heads-up title "Switching to Dark in 4 min", body "Sunset at 17:55", buttons "Snooze 15 min", "Skip", "Switch now"; after switch "Switched to Dark theme" / "Next: Light at 05:14 tomorrow"; after full screen "Waited until you left full screen."; OSD "Dark theme" + detail from `Shortcut()`, shown 1.8 s bottom-center above the taskbar.

- [ ] **Step 1: Implement.** If `Register` returns false, Settings shows "Win + Shift + D is used by another app."
- [ ] **Step 2: Manual check** — set the sunset offset so the switch is 6 minutes away: toast appears at 5 min with buttons, Snooze works from the toast with the flyout closed; play a full-screen video across the switch: no switch until Esc, then it switches and the toast says it waited; Win + Shift + D flips with the OSD.
- [ ] **Step 3: Commit** — `feat(app): heads-up toasts and Win+Shift+D shortcut`

---

### Task 13: Installer, v2 removal and release script

**Files:**
- Create: `installer/WinSolar.iss`, `build/release.ps1` (developer tool run by hand, not by the app), `build/README-release.md`
- Modify: `README.md` (v3 install section, SmartScreen "More info → Run anyway" note)

**Interfaces:**
- Consumes: published app.
- Produces: `dist/WinSolarSetup-3.0.0.exe` and `dist/SHA256SUMS.txt`.
- Installer: per-user (`PrivilegesRequired=lowest`, `{localappdata}\Programs\Win Solar`), version info from 3.0.0 / Celal Çelikkaya, start menu shortcut, "Start Win Solar" checkbox on finish; `[Code]` detects v2 (uninstall key of electron-builder product "Win Solar" with appId `com.winsolar.app` under HKCU, or `%LOCALAPPDATA%\Programs\win-solar`), asks "Win Solar 2 is installed. Remove it now?", closes `Win Solar.exe`, deletes its Run entry, runs its uninstaller silently; uninstall of v3 removes its Run value and asks whether to delete `%AppData%\Win Solar`.
- `release.ps1`: `dotnet test`, `dotnet publish src/WinSolar.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true`, ISCC, SHA-256 file.

- [ ] **Step 1: Implement and run** `pwsh build/release.ps1` → Expected: tests pass, `dist\WinSolarSetup-3.0.0.exe` exists, size under 15 MB.
- [ ] **Step 2: Manual check** — on the laptop with v2 installed and running: install v3 → asked to remove v2 → v2 gone (no process, no Run entry), v3 in tray, no UAC prompt at any point; uninstall v3 → Run entry gone.
- [ ] **Step 3: Commit** — `feat: installer with v2 removal and release script`
