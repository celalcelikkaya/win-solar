/**
 * Win Solar - Windows integration (theme registry, wallpaper).
 * No arbitrary script execution: only fixed scripts with validated parameters.
 */
const { execFile } = require('child_process');
const fs = require('fs');
const path = require('path');

const IS_WIN = process.platform === 'win32';
const PERSONALIZE_KEY = 'HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize';
const IMAGE_EXT = new Set(['.jpg', '.jpeg', '.png', '.bmp']);

function run(file, args, timeout = 15000) {
  return new Promise((resolve) => {
    execFile(file, args, { windowsHide: true, timeout }, (err, stdout, stderr) => {
      resolve({ ok: !err, stdout: String(stdout || '').trim(), stderr: String(stderr || '').trim(), error: err ? err.message : null });
    });
  });
}

function runPowerShell(script, timeout) {
  const encoded = Buffer.from(script, 'utf16le').toString('base64');
  return run('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', encoded], timeout);
}

/** PowerShell that tells every open window the colors changed (taskbar/Explorer refresh instantly). */
const BROADCAST_SCRIPT = [
  "$ErrorActionPreference = 'SilentlyContinue'",
  "$src = @'",
  'using System;',
  'using System.Runtime.InteropServices;',
  'public static class WinSolarNative {',
  '  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]',
  '  public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);',
  '}',
  "'@",
  'Add-Type -TypeDefinition $src',
  '$r = [UIntPtr]::Zero',
  "foreach ($k in 'ImmersiveColorSet', 'WindowsThemeElement') {",
  '  [WinSolarNative]::SendMessageTimeout([IntPtr]0xffff, 0x001A, [UIntPtr]::Zero, $k, 2, 200, [ref]$r) | Out-Null',
  '}',
].join('\n');

/** Set Windows app + system theme. mode: 'light' | 'dark' */
async function setWindowsTheme(mode) {
  if (mode !== 'light' && mode !== 'dark') return { success: false, reason: 'invalid-mode' };
  if (!IS_WIN) return { success: false, reason: 'unsupported-platform' };
  const value = mode === 'light' ? '1' : '0';
  // Registry via reg.exe: fast and independent of PowerShell.
  for (const name of ['AppsUseLightTheme', 'SystemUsesLightTheme']) {
    const r = await run('reg.exe', ['add', PERSONALIZE_KEY, '/v', name, '/t', 'REG_DWORD', '/d', value, '/f'], 8000);
    if (!r.ok) return { success: false, reason: 'registry-failed', detail: r.error };
  }
  // Best effort: broadcast so open windows refresh right away.
  const b = await runPowerShell(BROADCAST_SCRIPT, 20000);
  return { success: true, mode, broadcast: b.ok };
}

/** Read current Windows apps theme. */
async function getWindowsTheme() {
  if (!IS_WIN) return 'dark';
  const r = await run('reg.exe', ['query', PERSONALIZE_KEY, '/v', 'AppsUseLightTheme'], 8000);
  const m = /AppsUseLightTheme\s+REG_DWORD\s+0x([0-9a-f]+)/i.exec(r.stdout);
  return m && parseInt(m[1], 16) === 1 ? 'light' : 'dark';
}

function isSafeImagePath(p) {
  return typeof p === 'string' && path.isAbsolute(p) && IMAGE_EXT.has(path.extname(p).toLowerCase()) && fs.existsSync(p);
}

/** Set the desktop wallpaper to an existing local image. */
async function setWallpaper(imagePath) {
  if (!IS_WIN) return { success: false, reason: 'unsupported-platform' };
  if (!isSafeImagePath(imagePath)) return { success: false, reason: 'invalid-path' };
  const b64 = Buffer.from(imagePath, 'utf8').toString('base64'); // path never becomes script text
  const script = [
    "$ErrorActionPreference = 'Stop'",
    "$src = @'",
    'using System.Runtime.InteropServices;',
    'public static class WinSolarWall {',
    '  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]',
    '  public static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);',
    '}',
    "'@",
    'Add-Type -TypeDefinition $src',
    `$p = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('${b64}'))`,
    '[WinSolarWall]::SystemParametersInfo(20, 0, $p, 3) | Out-Null',
  ].join('\n');
  const r = await runPowerShell(script, 20000);
  return r.ok ? { success: true } : { success: false, reason: 'failed', detail: r.error };
}

module.exports = { setWindowsTheme, getWindowsTheme, setWallpaper, isSafeImagePath, IS_WIN, BROADCAST_SCRIPT };
