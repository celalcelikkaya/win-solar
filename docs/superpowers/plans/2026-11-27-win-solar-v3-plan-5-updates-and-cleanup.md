# Win Solar 3 Plan 5: Updates, Release Checklist, Electron Removal

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Win Solar finds new versions on GitHub Releases and installs them in one click. Releases follow a written
checklist with checksums and a Defender check. The Electron v2 code leaves the repo.

**Architecture:** Core parses the GitHub "latest release" JSON and `SHA256SUMS.txt` and compares versions (pure,
tested). The App's `UpdateService` checks at start and once a day. The About row and the tray menu offer
"Restart to update". That downloads the installer, checks its SHA-256 against the release's `SHA256SUMS.txt`, starts
it silently with `/UPDATE=1` and quits. The installer restarts Win Solar when it sees `/UPDATE=1`.

**Tech Stack:** C# .NET 8, WPF, `HttpClient`, `System.Text.Json`, `System.Security.Cryptography.SHA256`, Inno Setup 6, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-26-win-solar-wpf-design.md` ("Updates", "Keeping Windows from getting in
the way", "Structure").

## Global Constraints

- **Repo:** GitHub Releases of `celalcelikkaya/win-solar`. Endpoint
  `https://api.github.com/repos/celalcelikkaya/win-solar/releases/latest`, which skips drafts and pre-releases.
- **Release assets:** `WinSolarSetup-<version>.exe` and `SHA256SUMS.txt` (lines `<hex>  <file>`), as
  `build/release.ps1` writes them. Tags look like `v3.0.1`. The old `v1.0.0` (Electron) release must not count as newer.
- **No personal data in requests.** User-Agent is `WinSolar/<version>`. Use `HttpClient` only, no extra packages.
- **No child processes** except the one the spec names: running the downloaded installer
  (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1`). Never run anything whose SHA-256 does not match.
- **Download hosts:** HTTPS only, from `github.com` or `objects.githubusercontent.com` (where GitHub redirects
  assets). Any other URL is refused.
- **Settings:** none. Update checks are always on (Celal: every feature on, no settings dump).
- **UI copy:** "Check for updates", "Checking…", "Win Solar is up to date", "Win Solar <v> is available",
  "Restart to update", "Downloading…", "Could not check for updates", "The download did not match its checksum".
- **Repo rules:** warnings are errors, test first for Core, English everywhere in the repo.

## Review Focus

- **Offline or rate-limited** (HTTP 403/5xx, DNS failure, timeout): log it. The About row says "Could not check
  for updates". No crash, no notice storm.
- **A release without the installer asset or without `SHA256SUMS.txt`:** not offered as an update.
- **Tag oddities:** `v3.0.1`, `3.0.1`, `v3.1`, `v3.0.1-beta` (pre-release: skipped), garbage. Only a strictly newer
  three-part version counts.
- **Hash mismatch, or a half-written download:** the file is deleted, nothing runs, and the message is shown.
- **Clicking "Restart to update" twice, or while a check is running:** one download only.

---

### Task 1: Core: release parsing and version compare

**Files:**
- Create: `src/WinSolar.Core/Updates/ReleaseInfo.cs`
- Test: `tests/WinSolar.Tests/Updates/ReleaseInfoTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AvailableUpdate(Version Version, string InstallerName, Uri InstallerUrl, Uri ChecksumsUrl, Uri PageUrl)`
  - `public static class ReleaseInfo` with:
    - `static AvailableUpdate? Newer(string latestReleaseJson, Version current)`: null when not newer, a draft,
      a pre-release, or missing either asset. Throws `FormatException` on JSON that is not a release.
    - `static Version? ParseTag(string tag)`: `v3.0.1` / `3.0.1` / `v3.1` → three-part `Version`;
      anything with `-` or extra text → null.
    - `static string? ChecksumFor(string sums, string fileName)`: lower-case hex for that file name
      (case-insensitive, tolerates `*name` and CRLF). Null when the file is absent or the hash is not 64 hex chars.
    - `static bool IsAllowedDownload(Uri url)`: https and host `github.com` or `objects.githubusercontent.com`.

- [ ] Tests first. Cover:
  - Newer release → update with the exe and sums URLs.
  - Same version → null. Older (`v1.0.0`) → null.
  - `prerelease: true` → null. `draft: true` → null.
  - No exe asset → null. No sums asset → null.
  - `ParseTag` theory: `v3.0.1`→3.0.1, `3.0.1`→3.0.1, `v3.1`→3.1.0, `v3.0.1-beta`→null, `latest`→null.
  - `ChecksumFor`: two lines with CRLF, `*` prefix, wrong-length hash → null.
  - `IsAllowedDownload`: http → false, other host → false.
- [ ] Run them and watch them fail. Implement with `System.Text.Json` (`JsonDocument`). Run them and watch them pass.
- [ ] Commit `feat(core): parse GitHub releases for updates`.

### Task 2: App: UpdateService, About row, tray item, installer restart

**Files:**
- Create: `src/WinSolar.App/Services/UpdateService.cs`
- Modify:
  - `Services/EngineHost.cs` (own and start it, dispose)
  - `Settings/SettingsSheet.cs` (About row)
  - `Tray/TrayIcon.cs` (menu item when an update is ready)
  - `installer/WinSolar.iss` (`[Run]` restart on `/UPDATE=1`)

**Interfaces:**
- Consumes: `ReleaseInfo.*`, `AvailableUpdate` (Task 1).
- Produces:
  - `UpdateService` with `UpdateState State`, where
    `enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Failed }`
  - `AvailableUpdate? Available`, `string? Message`, `event Action? Changed`
  - `Task CheckAsync()`, `Task InstallAsync(Action quit)`
  - `void Start()`: first check 1 min after start, then every 24 h.

- [ ] `CheckAsync`: GET the endpoint (timeout 30 s). Pass the result to `ReleaseInfo.Newer` with the running assembly
  version. Failures (`HttpRequestException`, `TaskCanceledException`, `FormatException`) → `Failed` plus a log line.
  A second call while busy returns at once.
- [ ] `InstallAsync`:
  - Refuse disallowed URLs.
  - Download `SHA256SUMS.txt` and the exe to `%LOCALAPPDATA%\Win Solar\Updates\<name>.part`, compute SHA-256, compare.
  - Mismatch → delete the file and set `Failed` with the checksum message.
  - Match → rename to `.exe` and start it with the four flags (`UseShellExecute = true`), then `quit()`.
  - Old files in `Updates\` are deleted at the start.
- [ ] About row:
  - A second line under the version shows the state.
  - A pill button reads "Check for updates", or "Restart to update" when an update is `Available`.
  - Refresh it on `Changed`.
- [ ] Tray menu: "Restart to update (<v>)" above "Quit", only while `Available`.
- [ ] Installer: add
  `Filename: "{app}\WinSolar.exe"; Parameters: "--minimized"; Flags: nowait; Check: IsUpdate`, with
  `function IsUpdate: Boolean; begin Result := ExpandConstant('{param:UPDATE|0}') = '1'; end;`.
- [ ] Build with 0 warnings and run `dotnet test`. Commit `feat(app): check GitHub Releases and update in one click`.

### Task 3: Release checklist and script

**Files:**
- Modify: `build/release.ps1` (version read from `Directory.Build.props`, prints the next steps)
- Create: `docs/release-checklist.md`

- [ ] `release.ps1`: `$version` comes from `<Version>` in `Directory.Build.props` (single source). At the end it
  prints the Defender submission URL and the `gh release create v$version <exe> <sums>` command. It does not run it.
- [ ] Checklist steps:
  1. Bump `<Version>`.
  2. Run `pwsh build\release.ps1`.
  3. Install over the current version and check it.
  4. Submit the exe at https://www.microsoft.com/en-us/wdsi/filesubmission (Software developer, "Incorrectly
     detected as malware/malicious" check). Wait for the result.
  5. Run `gh release create` with both assets, notes and the checksums in the notes.
  6. Check that an older install offers the update.
- [ ] Commit `docs: release checklist; version from Directory.Build.props`.

### Task 4: Remove the Electron v2 code

**Files:**
- Delete:
  - `electron/`, `public/`
  - `src/App.tsx`, `src/main.tsx`, `src/index.css`, `src/types.ts`, `src/components/`, `src/lib/`
  - `index.html`, `package.json`, `package-lock.json`, `tsconfig.json`, `vite.config.ts`
  - `start.bat`, `start-desktop.bat`, `build-exe.bat`
- Modify:
  - `README.md` (v3 only: features, download, SmartScreen note, build)
  - `.gitignore` (drop `node_modules`, `release`)
  - `CLAUDE.md` (no "Electron until Plan 5")

- [ ] Make sure nothing in `src/WinSolar.*`, `tests`, `installer` or `build` references the deleted paths (grep).
  The installer's v2 removal stays: it works on installed copies, not on repo files.
- [ ] `dotnet build` and `dotnet test` still pass. Commit `chore: remove the Electron v2 app`.

### Task 5: Final review and install

- [ ] Whole-branch review (fresh reviewer). Fix the Critical and Important findings test-first.
- [ ] Run `pwsh build\release.ps1`, install, and check that the About row says "Win Solar is up to date" (the latest
  release is v1.0.0).
- [ ] Check the update path end to end without publishing: run the service against a local copy of a newer release
  JSON (test hook in a debug-only build is not allowed). Instead the Core tests cover parsing, and one manual run
  checks that the hash check refuses a tampered file.
- [ ] Update CLAUDE.md (state, next, decisions). Ask Celal to merge. Publishing the first v3 GitHub release is
  his call: it is public.
