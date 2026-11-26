# Win Solar 3 — Plan 2: Real light and battery mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The theme follows the real light (the laptop's light sensor with hysteresis, 2-minute stability and self-calibration; Open-Meteo cloud cover when there is no sensor), and goes dark on battery by rule.

> **Scope change (2026-11-26, Celal):** travel detection is dropped — the light sensor already follows the real light, and the place is one click away in Settings. Tasks 5 and 7 and every travel item below are removed; task numbers are kept so the ledger stays valid.

**Architecture:** All rules live in `WinSolar.Core` as pure code behind three new ports (`IPower`, `IRoomLight`, `IWeather`) that `SolarEngine` consults on every evaluation, so every rule is unit-tested with fakes. `WinSolar.App` gets three small services that implement those ports with WinRT APIs (`LightSensor`, `PowerManager`) and `HttpClient` (Open-Meteo), each one triggering an engine `Tick` when its input changes. Learned calibration lives in a new `learned.json` owned by the App services (never by the engine, which owns `state.json`).

**Tech Stack:** .NET 8, WPF, WinRT projections from `net8.0-windows10.0.19041.0` (`Windows.Devices.Sensors`, `Windows.System.Power`), `System.Text.Json`, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md` — sections "What decides the theme", "Real light", "Heads-up before a switch", "Battery", "Timing", "Settings". UI wording: `docs/superpowers/specs/win-solar-demo.html`. Read `CLAUDE.md` first (rules, decisions, laptop facts).

## Global Constraints

- Core stays `net8.0` with no Windows APIs; App is `net8.0-windows10.0.19041.0`, x64; warnings are errors.
- No PowerShell, scripts or child processes; Windows access only through Win32/WinRT.
- No polling: one timer at the engine's next due moment; services trigger `EngineHost.Tick()` on events. The only periodic job added is the hourly weather refresh, and only while weather is in use.
- UI copy English, short, matching the demo. Notices use Win Solar's own card (`CardNotifier`), never Windows toasts.
- Spec values: sensor defaults dark below 60 lx / light above 150 lx; a change must last 2 minutes; `maxShift` 30 / 60 (default) / 90 min; weather factor clear 1, cloudy 0.6, overcast 0.3, storm 0.12; outdoor light `max(0, sin(elevation + 6°))`; threshold = clear-sky value at sunset `sin(5.167°)`; battery rules No change / Dark below 20% (default) / Always dark; weather refreshed about hourly.
- Saved files must stay readable: a Plan 1 `settings.json` / `state.json` loads with the new fields at their defaults.
- Write a failing test first for every Core behaviour (`superpowers:test-driven-development`).
- Work on branch `feat/plan-2-real-light`; merge to `main` (and fast-forward `v3-wpf`) only after Celal approves.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; the repo-local git identity is `Claude <noreply@anthropic.com>`.

## Review Focus

1. **A hand shadow, a person walking past, or a lamp switched on/off for a minute** must never flip the theme: readings that cross a threshold for less than 2 minutes are ignored. Test in Task 3 (`Short_dip_does_not_count`).
2. **A lamp at night or a dark cloud at noon** (sensor says the opposite of the schedule far from sunrise/sunset): the schedule wins outside the light window. Test in Task 4 (`Room_outside_window_is_ignored`).
3. **No internet / Open-Meteo down / forecast older than 6 h**: plain sun times, the flyout chip says "Weather unavailable", no exception reaches the user, and the next refresh retries. Tests in Task 2 (`Stale_forecast_is_unavailable`, `Parse_rejects_garbage`) and Task 4 (`No_weather_uses_plain_sun_times`).
4. **Polar day/night with real light on** (Tromsø): no sun events → no light window → the sensor never overrides the polar theme and nothing throws. Test in Task 4 (`Polar_night_ignores_room_light`).
5. **Battery at exactly 20%, or unplugged during a full-screen video**: 20% counts as low (as in the demo, `<= 20`); a battery switch is automatic, so it waits for full screen like any scheduled switch, and no heads-up appears while battery mode holds Dark. Tests in Task 4 (`Battery_at_20_percent_is_low`, `Battery_switch_waits_for_full_screen`, `No_heads_up_while_battery_dark`).

---

## File Structure

```
src/WinSolar.Core/
  Settings/AppSettings.cs        modify: FollowRealLight, MaxShiftMin, Battery
  Settings/LearnedState.cs       create: Calibration (learned.json)
  Power/Battery.cs               create: BatteryRule, PowerStatus, BatteryRules.IsDark
  Light/LightModel.cs            create: Daylight, WeatherFactor, WeatherLabel, Adjust, LightWindow
  Light/Weather.cs               create: HourWeather, WeatherForecast, OpenMeteo (url + parse)
  Light/RoomLightTracker.cs      create: Thresholds, hysteresis + 2-min stability
  Light/Calibration.cs           create: Calibration, CalibrationMath, CalibrationLearner
  Engine/Ports.cs                modify: IPower, IRoomLight, IWeather, LightInfo, snapshot fields
  Engine/NextDue.cs              modify: extra due moments
  Engine/SolarEngine.cs          modify: battery, room light, weather adjust, heads-up rules
src/WinSolar.App/
  Services/PowerService.cs       create: IPower via Windows.System.Power.PowerManager
  Services/LightSensorService.cs create: IRoomLight via Windows.Devices.Sensors.LightSensor + learning
  Services/WeatherService.cs     create: IWeather via Open-Meteo, hourly refresh
  Services/LearnedStore.cs       create: learned.json load/save
  Services/EngineHost.cs         modify: build and wire the services
  Notify/CardNotifier.cs         modify: battery/room/weather copy
  Flyout/FlyoutWindow.cs         modify: environment chip, captions, banners, moved tiles
  Settings/SettingsSheet.cs      modify: real light rows, learned thresholds + Reset, Battery section
tests/WinSolar.Tests/
  Fakes/Fakes.cs                 modify: FakePower, FakeRoom, FakeWeather
  Settings/CompatTests.cs        create
  Power/BatteryTests.cs          create
  Light/LightModelTests.cs, Light/WeatherTests.cs, Light/RoomLightTrackerTests.cs, Light/CalibrationTests.cs  create
  Engine/RealLightEngineTests.cs create (SolarEngineTests.cs: only the Rig constructor changes)
```

---

### Task 0: Fixes from the Plan 1 review (before anything moves switch times)

The whole-branch review of Plan 1 (2026-11-26) found three Important issues; the third becomes a bug as soon as Task 4 lets weather move transitions.

**Files:**
- Modify: `src/WinSolar.Core/Engine/SolarEngine.cs`, `src/WinSolar.Core/Decisions/Holds.cs`, `src/WinSolar.Core/Engine/Ports.cs`, `src/WinSolar.App/App.xaml.cs`, `src/WinSolar.App/Services/EngineHost.cs`, `src/WinSolar.Core/Zones.cs`, `installer/WinSolar.iss`
- Create: `src/WinSolar.App/Services/Log.cs`
- Test: `tests/WinSolar.Tests/Engine/SolarEngineTests.cs`, `tests/WinSolar.Tests/Decisions/HoldsTests.cs`

**Interfaces:**
- Produces:
  - `SolarEngine` re-reads `theme.Read()` at the start of every `Evaluate` and `Shortcut` (theme changed outside Win Solar is respected, never "switched back").
  - Heads-up identity = `(local date of the transition in the place's zone, Kind)` instead of `At`, so a transition that moves by a few minutes is not announced twice.
  - `HoldKind.SwitchNow` ends when the schedule's theme equals the hold's theme (checked in `ClearExpired` with the current schedule), with the old `Until` kept only as a safety cap (`Until = Next.At + 1 day`).
  - `SetMode(Mode.Auto)` and `Resume()` switch quietly (`Cause.Manual`), never with a "Switched" card.
  - `IStateStore.Save` failures (`IOException`, `UnauthorizedAccessException`) and `IThemeApplier.Apply` failures are caught in the engine: state stays in memory and is saved on the next tick; a failed apply keeps `applied` unchanged and sets `EngineSnapshot.Error` (`string?`, appended) to `"Could not change the Windows theme"`.
  - `Log.Write(string)` appends a timestamped line to `%AppData%\Win Solar\log.txt` (kept under 256 KB by trimming the oldest half). `App` handles `DispatcherUnhandledException` (log, mark handled), `TaskScheduler.UnobservedTaskException` and `AppDomain.UnhandledException` (log). The tray shows the snapshot `Error` in its tooltip.
  - `Zones.ClearCache()`; `EngineHost.OnTimeChanged` calls it and recomputes the fallback place (`SolarEngine.FallbackPlace` becomes settable).
  - Installer: stop a running `WinSolar.exe` in `PrepareToInstall` with `taskkill /IM WinSolar.exe /F` (as `[UninstallRun]` does; its windows are hidden, so Restart Manager may not close it); when the v2 uninstaller `Exec` fails (per-machine v2, needs admin), skip the 60 s wait and tell the user to remove Win Solar 2 from Settings → Apps.

- [ ] **Step 1: Write the failing tests**
  - `Theme_changed_outside_is_respected`: start Light at noon; `FakeTheme` set to Dark outside the engine; `Shortcut()` → returns Light (flips from what is on screen).
  - (The heads-up identity change is tested in Task 4, `Moved_transition_is_announced_once`, where weather can move a transition.)
  - `SwitchNow_ends_when_schedule_catches_up`: SwitchNow at Sunset-30 (Dark); tick at Sunset+1 → hold cleared, theme Dark, no extra apply.
  - `Auto_from_manual_is_quiet`: manual Dark at noon → `SetMode(Auto)` → Light applied, `Switches` empty.
  - `Save_failure_does_not_throw`: `MemoryStore` that throws `IOException` once → `Tick()` does not throw; next `Tick()` saves.
  - `Apply_failure_is_reported`: `FakeTheme` that throws → snapshot `Error` set, `applied` unchanged.
- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~SolarEngineTests` — Expected: the new tests fail.
- [ ] **Step 3: Implement** the engine, holds, log, handlers, `Zones.ClearCache`, fallback refresh and installer changes above.
- [ ] **Step 4: Run** `dotnet test` — all pass; `dotnet build` — 0 warnings.
- [ ] **Step 5: Commit** `fix: Plan 1 review findings (outside theme changes, moved heads-up, silent failures)`

---

### Task 1: Settings, learned state and battery rule

**Files:**
- Modify: `src/WinSolar.Core/Settings/AppSettings.cs`
- Create: `src/WinSolar.Core/Settings/LearnedState.cs`, `src/WinSolar.Core/Power/Battery.cs`
- Test: `tests/WinSolar.Tests/Settings/CompatTests.cs`, `tests/WinSolar.Tests/Power/BatteryTests.cs`

**Interfaces:**
- Produces:
  - `AppSettings` new init properties: `bool FollowRealLight = true`, `int MaxShiftMin = 60`, `BatteryRule Battery = BatteryRule.DarkBelow20`; `TimeSpan MaxShift => TimeSpan.FromMinutes(Math.Clamp(MaxShiftMin, 30, 90))`.
  - `namespace WinSolar.Core.Power`: `enum BatteryRule { NoChange, DarkBelow20, AlwaysDark }`, `sealed record PowerStatus(bool OnBattery, int? Percent)` with `static PowerStatus Mains = new(false, null)`, `static class BatteryRules { static bool IsDark(BatteryRule rule, PowerStatus power) }`.
  - `namespace WinSolar.Core.Settings`: `sealed record LearnedState(Calibration Calibration)` with `static LearnedState Empty => new(Calibration.Empty)` (`Calibration` comes from Task 3; until then declare `LearnedState` in Task 3's commit, or write Task 3's `Calibration` record first — keep the build green).

- [ ] **Step 1: Write the failing tests**

```csharp
// CompatTests
[Fact] public void Plan1_settings_file_loads_with_new_defaults()
{
    var dir = Directory.CreateTempSubdirectory().FullName;
    File.WriteAllText(Path.Combine(dir, "settings.json"), """{ "Source": "Sun", "OffsetSunsetMin": -10, "Hour12": true, "Schema": 3 }""");
    var s = new JsonFileStore(dir).Load("settings.json", () => new AppSettings());
    Assert.Equal(-10, s.OffsetSunsetMin);
    Assert.True(s.FollowRealLight);
    Assert.Equal(60, s.MaxShiftMin);
    Assert.Equal(BatteryRule.DarkBelow20, s.Battery);
}
[Fact] public void MaxShift_is_clamped_to_30_to_90_minutes()  // MaxShiftMin 5 → 30 min, 500 → 90 min
// BatteryTests
[Theory]
[InlineData(BatteryRule.DarkBelow20, true, 20, true)]
[InlineData(BatteryRule.DarkBelow20, true, 21, false)]
[InlineData(BatteryRule.DarkBelow20, false, 5, false)]
[InlineData(BatteryRule.AlwaysDark, true, 90, true)]
[InlineData(BatteryRule.AlwaysDark, false, 90, false)]
[InlineData(BatteryRule.NoChange, true, 3, false)]
public void IsDark(BatteryRule rule, bool onBattery, int percent, bool dark) =>
    Assert.Equal(dark, BatteryRules.IsDark(rule, new PowerStatus(onBattery, percent)));
[Fact] public void Unknown_percent_is_not_low() => Assert.False(BatteryRules.IsDark(BatteryRule.DarkBelow20, new PowerStatus(true, null)));
```

- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~CompatTests|FullyQualifiedName~BatteryTests"` — Expected: build errors (types missing).
- [ ] **Step 3: Implement** the properties and types above. `IsDark`: `AlwaysDark` → `OnBattery`; `DarkBelow20` → `OnBattery && Percent <= 20`; else false.
- [ ] **Step 4: Run** the same filter — Expected: PASS. Run `dotnet test` — all pass.
- [ ] **Step 5: Commit** `feat(core): real-light and battery settings with Plan 1 compatible defaults`

---

### Task 2: Light model and weather

**Files:**
- Create: `src/WinSolar.Core/Light/LightModel.cs`, `src/WinSolar.Core/Light/Weather.cs`
- Test: `tests/WinSolar.Tests/Light/LightModelTests.cs`, `tests/WinSolar.Tests/Light/WeatherTests.cs`

**Interfaces:**
- Produces (`namespace WinSolar.Core.Light`):
  - `static class LightModel`:
    - `double Daylight(DateTimeOffset at, Place place)` = `max(0, sin((SunCalculator.Elevation(at, lat, lng) + 6)°))`
    - `const double Threshold` = `Math.Sin(5.167 * Math.PI / 180)`
    - `double WeatherFactor(HourWeather w)`: `w.Storm` → 0.12; else `1 - 0.7 * Math.Pow(cloud/100.0, 1.5)` (0% → 1, ~70% → ≈0.6, 100% → 0.3).
    - `string WeatherLabel(HourWeather w)`: Storm → "Storm"; cloud < 25 → "Clear"; < 75 → "Cloudy"; else "Overcast".
    - `DateTimeOffset Adjust(DateTimeOffset sunEvent, TransitionKind kind, Place place, Func<DateTimeOffset, double> factorAt, TimeSpan maxShift)` — the `ScheduleCalculator` `adjust` hook. Port of the demo's `weatherTime`, with a per-minute factor:
      ```
      if factorAt(sunEvent) >= 1: return sunEvent
      Sunset: for t = sunEvent - maxShift; t <= sunEvent; t += 1 min: if Daylight(t)*factorAt(t) < Threshold return t; return sunEvent
      Sunrise: for t = sunEvent; t <= sunEvent + maxShift; t += 1 min: if Daylight(t)*factorAt(t) >= Threshold return t; return sunEvent + maxShift
      ```
    - `bool InWindow(DateTimeOffset now, IReadOnlyList<DateTimeOffset> sunEvents, TimeSpan maxShift)` → any `|now - e| <= maxShift`.
    - `IEnumerable<DateTimeOffset> WindowEdges(IReadOnlyList<DateTimeOffset> sunEvents, TimeSpan maxShift)` → `e - maxShift`, `e + maxShift` for each.
  - `sealed record HourWeather(DateTimeOffset Hour, int CloudCover, bool Storm)` (Storm = WMO weather code 95–99).
  - `sealed class WeatherForecast(IReadOnlyList<HourWeather> hours, DateTimeOffset fetchedAt)`: `static readonly TimeSpan MaxAge = 6 h`; `HourWeather? At(DateTimeOffset t, DateTimeOffset now)` → null when `now - fetchedAt > MaxAge` or no hour with `Hour <= t < Hour + 1 h`.
  - `static class OpenMeteo`: `string ForecastUrl(Place p)` = `https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lng}&hourly=cloud_cover,weather_code&timezone=UTC&past_days=1&forecast_days=2` (invariant culture, 4 decimals); `IReadOnlyList<HourWeather> Parse(string json)` (times are `"2026-11-26T15:00"` UTC); throws `FormatException` on anything unexpected.

- [ ] **Step 1: Write the failing tests** (Blantyre 2026-11-26; `sunset` = `SunCalculator.ForDate(new(2026,11,26), lat, lng).Sunset!.Value`)

```csharp
[Fact] public void Clear_sky_keeps_the_sun_time() =>
    Assert.Equal(sunset, LightModel.Adjust(sunset, TransitionKind.Sunset, Blantyre, _ => 1, TimeSpan.FromMinutes(60)));
[Fact] public void Storm_moves_sunset_by_the_whole_window() =>
    Assert.Equal(sunset.AddMinutes(-30), LightModel.Adjust(sunset, TransitionKind.Sunset, Blantyre, _ => 0.12, TimeSpan.FromMinutes(30)));
[Fact] public void Storm_moves_sunrise_by_the_whole_window()   // sunrise + 30 min
[Fact] public void Overcast_moves_sunset_earlier_inside_the_window()
{
    var t = LightModel.Adjust(sunset, TransitionKind.Sunset, Blantyre, _ => 0.3, TimeSpan.FromMinutes(60));
    Assert.InRange(t, sunset.AddMinutes(-60), sunset.AddMinutes(-1));
}
[Fact] public void Threshold_is_clear_sky_at_sunset() => Assert.InRange(LightModel.Daylight(sunset, Blantyre), LightModel.Threshold - 0.01, LightModel.Threshold + 0.01);
[Theory] [InlineData(0, 1.0)] [InlineData(100, 0.3)] [InlineData(70, 0.59)]
public void Factor_from_cloud_cover(int cloud, double f) => Assert.Equal(f, LightModel.WeatherFactor(new(default, cloud, false)), 2);
[Fact] public void Storm_factor_is_0_12()
[Fact] public void InWindow_is_true_within_maxShift_of_any_sun_event()   // sunset-60 true, sunset+61 false, noon false
// WeatherTests
[Fact] public void Parse_reads_hours_in_utc()   // fixture: 3 hours, cloud 10/80/100, codes 3/61/95 → Storm only on the third
[Fact] public void Parse_rejects_garbage() => Assert.Throws<FormatException>(() => OpenMeteo.Parse("<html>"));
[Fact] public void Stale_forecast_is_unavailable()   // fetchedAt = now - 7 h → At(now, now) is null; now - 5 h → not null
[Fact] public void Forecast_url_uses_invariant_numbers()   // under CultureInfo "tr-TR": contains "latitude=-15.7861&longitude=35.0058"
```

- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~Light"` — Expected: build errors.
- [ ] **Step 3: Implement** `LightModel` and `Weather.cs` as specified. `Parse`: `JsonDocument`; read `hourly.time`, `hourly.cloud_cover`, `hourly.weather_code` arrays of equal length; parse times with `DateTime.ParseExact(s, "yyyy-MM-dd'T'HH:mm", InvariantCulture)` as UTC; wrap `JsonException`/`KeyNotFoundException`/`InvalidOperationException` into `FormatException`.
- [ ] **Step 4: Run** the filter — Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): outdoor light model, weather factor and Open-Meteo parsing`

---

### Task 3: Room light tracker and self-calibration

**Files:**
- Create: `src/WinSolar.Core/Light/RoomLightTracker.cs`, `src/WinSolar.Core/Light/Calibration.cs`, `src/WinSolar.Core/Settings/LearnedState.cs` (if not done in Task 1)
- Test: `tests/WinSolar.Tests/Light/RoomLightTrackerTests.cs`, `tests/WinSolar.Tests/Light/CalibrationTests.cs`

**Interfaces:**
- Produces (`namespace WinSolar.Core.Light`):
  - `sealed record Thresholds(double DarkBelow, double LightAbove)`
  - `sealed class RoomLightTracker(Thresholds thresholds)`:
    - `static readonly TimeSpan Stability = TimeSpan.FromMinutes(2)`
    - `Thresholds Thresholds { get; set; }`, `double? Lux { get; }`
    - `void Feed(DateTimeOffset at, double lux)` — classify: `lux <= DarkBelow` → Dark, `lux >= LightAbove` → Light, in between → the current candidate (or the stable theme) stays. A candidate that differs from the stable theme starts a pending change at `at`; a reading that returns to the stable theme cancels it.
    - `Theme? Stable(DateTimeOffset now)` — promotes the pending theme when `now >= since + Stability`, then returns the stable theme (null until the first promotion).
    - `(Theme Theme, DateTimeOffset At)? Pending { get; }` — `At` is the moment it will count (`since + Stability`).
  - `sealed record Calibration(IReadOnlyList<double> Day, IReadOnlyList<double> Evening)`, `static Calibration Empty`.
  - `static class CalibrationMath`: `static readonly Thresholds Default = new(60, 150)`; `const int MinHours = 6`; `const int Keep = 48`; `bool IsLearned(Calibration c)` (both lists ≥ `MinHours` and median(Day) ≥ 1.5 × median(Evening)); `Thresholds For(Calibration c)` — Default unless learned; learned: in log space between `e = median(Evening)` and `d = median(Day)`: `DarkBelow = exp(ln e + 0.35 (ln d − ln e))`, `LightAbove = exp(ln e + 0.65 (ln d − ln e))`.
  - `sealed class CalibrationLearner(Calibration start)`: `Calibration Current`; `bool Add(DateTimeOffset at, double lux, double sunElevation)` — collects the readings of the current UTC hour into a day bucket (`sunElevation >= 10`) or an evening bucket (`sunElevation <= -6`), ignoring twilight and readings below 5 lx (lid closed, lights off); when a reading arrives in a new hour, each bucket from the finished hour with ≥ 3 readings adds its median to `Day`/`Evening` (keeping the last `Keep`); returns true when `Current` changed (the caller saves). `void Reset()` → `Empty`.
- `namespace WinSolar.Core.Settings`: `sealed record LearnedState(Calibration Calibration)`, `static LearnedState Empty`.

- [ ] **Step 1: Write the failing tests** (`t0` = any instant; `th` = `new Thresholds(60, 150)`)

```csharp
[Fact] public void Dark_counts_after_two_minutes()
{
    var r = new RoomLightTracker(th);
    r.Feed(t0, 40);
    Assert.Null(r.Stable(t0.AddSeconds(119)));
    Assert.Equal((Theme.Dark, t0.AddMinutes(2)), r.Pending);
    Assert.Equal(Theme.Dark, r.Stable(t0.AddMinutes(2)));
}
[Fact] public void Short_dip_does_not_count()     // stable Light; Feed 30 lx at t, 300 lx at t+90 s → Stable(t+5 min) is Light, Pending null
[Fact] public void Between_thresholds_keeps_the_current_theme()   // stable Dark; Feed 100 lx for 10 min → still Dark, no Pending
[Fact] public void Learned_thresholds_sit_between_evening_and_day()
{
    var c = new Calibration(Enumerable.Repeat(400.0, 6).ToList(), Enumerable.Repeat(120.0, 6).ToList());
    var t = CalibrationMath.For(c);
    Assert.Equal(182.9, t.DarkBelow, 0);   // tolerance 1
    Assert.Equal(262.4, t.LightAbove, 0);
}
[Fact] public void Too_little_data_uses_defaults()            // 5 hours each → (60, 150)
[Fact] public void Day_not_brighter_than_evening_uses_defaults() // day 150, evening 120 → (60, 150)
[Fact] public void Learner_adds_hour_median_when_hour_ends()   // 3 readings 100/120/500 at elevation -10 in hour H, one reading in H+1 → Evening == [120], Add returns true
[Fact] public void Learner_ignores_twilight_and_dark_room()    // elevation 0 or lux 2 → nothing added
[Fact] public void Learner_keeps_the_last_48_hours()
```

- [ ] **Step 2: Run** `dotnet test --filter "FullyQualifiedName~RoomLight|FullyQualifiedName~Calibration"` — Expected: build errors.
- [ ] **Step 3: Implement** as specified (median = middle of the sorted list, mean of the two middles when even).
- [ ] **Step 4: Run** the filter — Expected: PASS.
- [ ] **Step 5: Commit** `feat(core): room light hysteresis with 2-minute stability and self-calibration`

---

### Task 4: Engine — battery, room light and weather

**Files:**
- Modify: `src/WinSolar.Core/Engine/Ports.cs`, `src/WinSolar.Core/Engine/NextDue.cs`, `src/WinSolar.Core/Engine/SolarEngine.cs`, `src/WinSolar.Core/Decisions/Types.cs` (no change to `ThemeDecider`: it already takes `BatteryDark`, `RoomTheme`, `InLightWindow`)
- Modify: `tests/WinSolar.Tests/Fakes/Fakes.cs`, `tests/WinSolar.Tests/Engine/SolarEngineTests.cs` (Rig constructor only), `tests/WinSolar.Tests/Engine/NextDueTests.cs` (new parameter)
- Create: `tests/WinSolar.Tests/Engine/RealLightEngineTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces (`namespace WinSolar.Core.Engine`):
  - `interface IPower { PowerStatus Read(); }`
  - `interface IRoomLight { bool Available { get; } double? Lux { get; } Theme? Current(DateTimeOffset now); (Theme Theme, DateTimeOffset At)? Pending { get; } }`
  - `interface IWeather { HourWeather? At(DateTimeOffset t); }` (null = unavailable)
  - `enum LightSource { Off, Sensor, Weather, WeatherUnavailable }`
  - `sealed record LightInfo(LightSource Source, double? Lux, HourWeather? Weather, (Theme Theme, DateTimeOffset At)? Pending, bool InWindow)`
  - `EngineSnapshot` gains `LightInfo Light, PowerStatus Power, bool BatteryDark` (appended).
  - `SolarEngine(IClock, IThemeApplier, INotifier, IFullScreenProbe, IStateStore, Place fallbackPlace, IPower power, IRoomLight room, IWeather weather)`.
  - `NextDue.Compute(now, s, hold, manualUntil, headsUpLead, IEnumerable<DateTimeOffset> extra)`.
- Rules (spec "What decides the theme" and "Real light"):
  - `Source`: `Off` unless `Settings.FollowRealLight && Settings.Source == Sun`; then `Sensor` if `room.Available`, else `Weather` if `weather.At(now)` is not null, else `WeatherUnavailable`.
  - Schedule: weather adjusts sunrise/sunset only when `Source == Weather` (factor per minute from `weather.At(t)`, 1 when null), via `LightModel.Adjust` and `Settings.MaxShift`.
  - Decision input: `BatteryDark = BatteryRules.IsDark(Settings.Battery, power.Read())`; `RoomTheme = Source == Sensor ? room.Current(now) : null`; `InLightWindow = LightModel.InWindow(now, s.SunEvents, Settings.MaxShift)`.
  - Heads-up: also not shown when `Source == Sensor` or `BatteryDark`.
  - Next due adds `room.Pending?.At` and, when `Source != Off`, `LightModel.WindowEdges(s.SunEvents, MaxShift)`.
  - Fakes: `FakePower { PowerStatus Status = PowerStatus.Mains }`, `FakeRoom { bool Available; double? Lux; Theme? Theme; (Theme, DateTimeOffset)? Pending }`, `FakeWeather { HourWeather? Now }` (returns `Now` for any `t`).

- [ ] **Step 1: Write the failing tests** in `RealLightEngineTests` (Rig as in `SolarEngineTests`, plus the three fakes; Blantyre; `Sunset` as there)

```csharp
[Fact] public void Room_dark_inside_window_switches_early()        // room Dark (sensor), now = Sunset-40 → Dark, switch cause Room
[Fact] public void Room_outside_window_is_ignored()                 // room Dark at Noon → stays Light; room Light at 23:00 → stays Dark
[Fact] public void Room_light_holds_light_until_the_window_ends()   // room Light: Sunset+30 → Light; Sunset+61 → Dark (cause Sunset)
[Fact] public void No_heads_up_in_sensor_mode()                     // Sunset-4 with room null → no HeadsUps
[Fact] public void Sensor_wins_over_weather()                       // room available + storm weather → Schedule.Next.At == Sunset
[Fact] public void Weather_moves_sunset_when_there_is_no_sensor()   // room unavailable, cloud 100 → Next.At < Sunset, Next.ShiftMinutes < 0, Light.Source == Weather
[Fact] public void No_weather_uses_plain_sun_times()                // weather null → Next.At == Sunset, Light.Source == WeatherUnavailable
[Fact] public void Real_light_off_ignores_sensor_and_weather()      // FollowRealLight false → Source Off, room Dark at Sunset-40 → Light
[Fact] public void Polar_night_ignores_room_light()                 // Tromsø 2026-12-15 12:00 Europe/Oslo, room Light → Dark, no throw
[Fact] public void Battery_at_20_percent_is_low()                   // OnBattery 20% at Noon → Dark, cause Battery, one Switched; Status → Mains, Tick → Light
[Fact] public void Battery_switch_waits_for_full_screen()           // full screen on, battery 15% → applied stays Light, WaitingForFullScreen true
[Fact] public void No_heads_up_while_battery_dark()                 // battery 10% at Sunset-4 → no HeadsUps
[Fact] public void Moved_transition_is_announced_once()            // no sensor, cloud 60 at moved-sunset-4 → one heads-up; weather changes to cloud 70 (transition moves ~2 min) → still one HeadsUps entry
[Fact] public void Next_due_includes_window_edge_and_room_pending() // at Noon, room Pending (Dark, Noon+2 min) → NextDue == Noon+2 min; no pending → NextDue == Sunset-60 min
```

- [ ] **Step 2: Run** `dotnet test --filter FullyQualifiedName~RealLightEngineTests` — Expected: build errors.
- [ ] **Step 3: Implement** the ports, snapshot fields, constructor parameters and rules above in `SolarEngine` (`Recompute` builds the `adjust` delegate; `Evaluate` builds `DecisionInput` and `LightInfo`). Update the Rig in `SolarEngineTests` and `NextDueTests` callers to the new signatures.
- [ ] **Step 4: Run** `dotnet test` — Expected: all tests pass (Plan 1's 59 plus the new ones).
- [ ] **Step 5: Commit** `feat(core): engine follows battery, room light and weather`

---

### Task 5: (removed — travel dropped)

---

### Task 6: Windows services for power, light sensor and weather

**Files:**
- Create: `src/WinSolar.App/Services/PowerService.cs`, `LightSensorService.cs`, `WeatherService.cs`, `LearnedStore.cs`
- Modify: `src/WinSolar.App/Services/EngineHost.cs`

**Interfaces:**
- Consumes: Task 4 ports; Task 3 tracker and learner; Task 2 `OpenMeteo`, `WeatherForecast`.
- Produces:
  - `LearnedStore` (file `learned.json` in the same `%AppData%\Win Solar` folder via `JsonFileStore`): `LearnedState State { get; }`, `void Update(Func<LearnedState, LearnedState> change)` (saves).
  - `PowerService : IPower, IDisposable` — `Read()` from `Windows.System.Power.PowerManager`: `OnBattery = PowerSupplyStatus == NotPresent && BatteryStatus != NotPresent`, `Percent = RemainingChargePercent` (null when `BatteryStatus == NotPresent`). Subscribes to `BatteryStatusChanged`, `PowerSupplyStatusChanged`, `RemainingChargePercentChanged`; each raises `Changed` (marshalled to the UI dispatcher).
  - `LightSensorService : IRoomLight, IDisposable` — `LightSensor.GetDefault()`; null or any exception → `Available = false`. Sets `ReportInterval = Math.Max(sensor.MinimumReportInterval, 1000)` and subscribes to `ReadingChanged` (the sensor only reports while subscribed — CLAUDE.md). Each reading (on the UI dispatcher): `tracker.Feed`, `learner.Add(at, lux, SunCalculator.Elevation(at, place))`; save `learned.json` when `Add` returns true; refresh `tracker.Thresholds = CalibrationMath.For(learner.Current)`. Raises `Changed` only when `Current(now)` or `Pending` changed (not on every reading). `Thresholds Thresholds`, `Calibration Calibration`, `void ResetLearning()` for Settings. Place comes from `Func<Place>` (the engine's current place).
  - `WeatherService : IWeather, IDisposable` — static `HttpClient` (10 s timeout, User-Agent `WinSolar/3.0`); `Refresh(Place)` fetches `OpenMeteo.ForecastUrl`, parses, keeps the `WeatherForecast`; failures keep the previous forecast (it expires after 6 h by itself) and are written to `Debug`. Refreshes hourly with a `DispatcherTimer` and on place change, only while `Enabled` (set by `EngineHost` when the engine's `LightSource` is `Weather` or `WeatherUnavailable`); raises `Changed` after a successful refresh.
  - `EngineHost` builds the three services, passes them to `SolarEngine`, ticks on each `Changed`, sets `weather.Enabled` from every snapshot, disposes them.

- [ ] **Step 1:** Implement `LearnedStore`, `PowerService`, `LightSensorService`, `WeatherService` and the `EngineHost` wiring. `dotnet build` — Expected: 0 warnings, 0 errors. `dotnet test` — all pass.
- [ ] **Step 2: Verify on the laptop** (it has a working sensor, see CLAUDE.md): `dotnet run --project src\WinSolar.App` (quit the installed copy first). In the flyout chip (Task 8 not done yet: check with a temporary `Debug.WriteLine` or the debugger instead) the lux value changes when the sensor is covered; covering it for 2+ minutes inside the light window switches to Dark; for 30 s does not. Unplug the charger: `Read()` reports `OnBattery = true` and the percent. With the sensor disabled in the code (temporary), weather is fetched once and the next transition moves (`ShiftMinutes`). Remove temporary code.
- [ ] **Step 3: Commit** `feat(app): light sensor, power and weather services`

---

### Task 7: (removed — travel dropped)

---

### Task 8: UI — settings, flyout and notices

**Files:**
- Modify: `src/WinSolar.App/Settings/SettingsSheet.cs`, `src/WinSolar.App/Flyout/FlyoutWindow.cs`, `src/WinSolar.App/Notify/CardNotifier.cs`, `src/WinSolar.App/Theme/Ui.cs` (icons: `Bulb`, `Cloud`, `Storm`, `Bolt` from Lucide, 24×24 stroke like the others)

**Interfaces:**
- Consumes: snapshot `Light`, `Power`, `BatteryDark` (Task 4), `LightSensorService.Thresholds/Calibration/ResetLearning` (Task 6) through `SheetContext` (add `Func<(bool HasSensor, Thresholds Thresholds, bool Learned)> RoomLight` and `Action ResetLearning`).
- Copy (from the demo, verbatim):
  - Schedule section, when Source is Sun: `Follow the real light` — sub `Uses the light sensor in this PC` or `No light sensor found. Uses the weather in {place}`; when on: `Move a switch by at most` — sub `So a lamp or a passing cloud never flips the theme at noon`, segmented `30 min` / `1 hour` / `1.5 hours`. With a sensor, one more row: `Room light` — sub `Dark below {d:0} lx, light above {l:0} lx` + ` · learned` or ` · learning this room`, button `Reset learning`.
  - New section `Battery` before General: `On battery` — sub `Dark pixels use less power on OLED screens`, segmented `No change` / `Dark below 20%` / `Always dark`.
  - Flyout hero environment chip (next to the phase chip): Sensor → bulb + `{lux:0} lx`; Weather → cloud/storm/sun icon + `LightModel.WeatherLabel`; WeatherUnavailable → cloud + `Weather unavailable`; Off → hidden.
  - Caption in Auto: Sensor → `Until {Theme} around {time} · follows the room light`; Weather with `Next.ShiftMinutes != 0` → append ` · {label lower} sky, {n} min {before|after} {sunset|sunrise}`.
  - Banners, in this priority after "waiting for full screen": battery → bolt + `Dark while on battery ({p}%). Your schedule comes back when you plug in.`; sensor pending inside the window → bulb + `The room is getting {darker|brighter}. Checking that it lasts…`; then the existing heads-up / hold banners.
  - Tiles: sunrise/sunset sub shows `Dark at {time}` / `Light at {time}` also when the today transition's `ShiftMinutes != 0`.
  - Notices: switch with `Cause.Battery` → title `Switched to Dark theme`, line `Battery at {p}%. Your schedule comes back when you plug in.`; `Cause.Room` → line `The room got {darker|brighter}`; heads-up line for a weather-moved transition → `{Label} sky · {time}`.

- [ ] **Step 1:** Implement the settings rows, flyout chip/caption/banners/tiles and notice copy. `dotnet build` — 0 warnings.
- [ ] **Step 2: Verify on the laptop** against the demo (open `docs/superpowers/specs/win-solar-demo.html` next to the app): every row, chip and banner above appears with the demo's wording, in light and dark theme, at 200% scaling; the settings changes take effect immediately (e.g. switching Battery to `Always dark` while unplugged turns Dark); `Reset learning` puts the row back to `Dark below 60 lx, light above 150 lx · learning this room`.
- [ ] **Step 3: Commit** `feat(app): real light and battery in settings, flyout and notices`

---

### Task 9: Release check and handover

**Files:**
- Modify: `CLAUDE.md` (State, Next, Decisions: thresholds algorithm, weather mapping, `learned.json`, `<= 20%`, travel dropped), `README.md` (feature list: real light, battery)

- [ ] **Step 1:** `dotnet test` — all pass. `pwsh build\release.ps1` — `Done: ...WinSolarSetup-3.0.0.exe`. Install it over the current copy (`/VERYSILENT /SUPPRESSMSGBOXES`), start it, check: flyout chip shows lux, Settings show the new rows, RAM in Task Manager stays under ~80 MB after 10 minutes with the sensor subscribed.
- [ ] **Step 2:** Update `CLAUDE.md` and `README.md`; commit `docs: Plan 2 handover notes`.
- [ ] **Step 3:** Request the whole-branch review (`superpowers:requesting-code-review`, base = `main` at branch start), fix Critical/Important findings, then show Celal the changes and ask for approval to merge to `main`.
