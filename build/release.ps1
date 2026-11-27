# Builds the Win Solar 3 installer: tests, publish, Inno Setup, checksums.
# Run from anywhere:  pwsh D:\Projects\win-solar\build\release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Single source of the version: <Version> in Directory.Build.props (the exe, the installer and the update check use it).
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version '$version' in Directory.Build.props is not x.y.z." }

Write-Host "Testing..." -ForegroundColor Cyan
dotnet test "$root\tests\WinSolar.Tests\WinSolar.Tests.csproj" -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

Write-Host "Publishing..." -ForegroundColor Cyan
$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish "$root\src\WinSolar.App\WinSolar.App.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishReadyToRun=true -p:DebugType=none -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with:  winget install JRSoftware.InnoSetup' }

Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc "/DVersion=$version" "$root\installer\WinSolar.iss" | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$dist = Join-Path $root 'dist'
$setup = Join-Path $dist "WinSolarSetup-$version.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  WinSolarSetup-$version.exe" | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding ascii
$size = [math]::Round((Get-Item $setup).Length / 1MB, 1)
Write-Host "Done: $setup ($size MB)" -ForegroundColor Green

Write-Host ""
Write-Host "Next (docs/release-checklist.md):" -ForegroundColor Cyan
Write-Host "  1. Install it over the current version and check it."
Write-Host "  2. Submit $setup at https://www.microsoft.com/en-us/wdsi/filesubmission (Software developer, false positive)."
Write-Host "  3. gh release create v$version `"$setup`" `"$(Join-Path $dist 'SHA256SUMS.txt')`" --title `"Win Solar $version`" --notes-file <notes>"
