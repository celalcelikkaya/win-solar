# <img src="https://github.com/celalcelikkaya/win-solar/blob/main/docs/screenshots/win-solar-icon.png" width="32" align="absmiddle" /> Win Solar

**Win Solar** switches Windows between light and dark with the sun. It is a small native tray app (C# .NET 8 + WPF)
that stays out of the way.

## Features

- **Sunrise and sunset** for your place, calculated offline. With **Follow the weather**, cloudy days move the
  switches by up to a limit you set.
- **Room light:** on a PC with a light sensor, a dark room means dark mode and a bright room means light mode.
- **Fixed times** if you prefer a clock.
- **A smooth switch:** the new theme rolls in from the top. Full-screen apps are never interrupted; the switch waits
  for them.
- **Heads-up** before a switch, with Snooze, Skip and Switch now. **Win + Shift + D** switches by hand.
- **Day and Night profiles:** accent color, mouse pointer, sounds and a wallpaper for each.
- **Dynamic wallpaper:** "Lake" comes built in and follows the sun from night to dusk. You can also import macOS
  dynamic wallpapers (.heic). Your own wallpaper, or slideshow, is saved and can be restored.
- **Battery:** dark below 20% when unplugged.
- **Updates** from GitHub Releases in one click, checked against published SHA-256 checksums.

## Install

Download `WinSolarSetup-<version>.exe` from [Releases](https://github.com/celalcelikkaya/win-solar/releases) and run it.
It installs for your user only and needs no administrator rights. Win Solar 2 is removed for you if you have it.
It needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0); the setup tells you if
it is missing.

**First install:** the installer is not code-signed yet, so Windows SmartScreen may say "Windows protected your PC".
Click **More info → Run anyway**. This appears only for the installer; the installed app and its updates run normally.
Each release lists the installer's SHA-256 in `SHA256SUMS.txt`.

## Build from source

Requirements: Windows 10 or 11, .NET 8 SDK, [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer.

| Command | What it does |
|---|---|
| `dotnet test` | Runs the tests |
| `dotnet run --project src\WinSolar.App` | Starts Win Solar (quit an installed copy first) |
| `pwsh build\release.ps1` | Builds `dist\WinSolarSetup-<version>.exe` and `SHA256SUMS.txt` |

```
src/WinSolar.Core    sun math, schedule, decisions, light model, wallpaper schedules, settings (no Windows APIs, tested)
src/WinSolar.App     WPF tray app: flyout, settings, switch overlay, Windows services, updates
tests/               xUnit tests
installer/           Inno Setup script
docs/                design, plans, release checklist
```

Releases follow [docs/release-checklist.md](docs/release-checklist.md). Win Solar 2 (Electron) lives in the git
history and the `v1.0.0` release.
