/**
 * Win Solar - Electron main process.
 * Small tray widget: the UI (React) decides the theme, this process applies it to Windows.
 */
const { app, BrowserWindow, Notification, dialog, ipcMain, net, powerMonitor, screen, shell } = require('electron');
const path = require('path');
const { setWindowsTheme, getWindowsTheme, setWallpaper, isSafeImagePath } = require('./windows.cjs');
const { createTray, updateTray, getTrayBounds } = require('./tray.cjs');

const DEV_URL = process.env.WINSOLAR_DEV_URL || '';
const WIN_W = 404; // 380 widget + 12px margin each side (room for the shadow)
const WIN_H = 664; // 640 widget + 12px margin

let mainWindow = null;
let hideOnBlur = true;
let lastHiddenAt = 0;
app.isQuitting = false;

if (!app.requestSingleInstanceLock()) {
  app.quit();
  process.exit(0);
}

app.on('second-instance', () => showWindow());

function positionWindow() {
  if (!mainWindow) return;
  const tb = getTrayBounds();
  const point = tb && tb.width ? { x: tb.x, y: tb.y } : screen.getCursorScreenPoint();
  const area = screen.getDisplayNearestPoint(point).workArea;
  const b = mainWindow.getBounds();
  mainWindow.setPosition(Math.round(area.x + area.width - b.width + 8), Math.round(area.y + area.height - b.height + 8), false);
}

function showWindow() {
  if (!mainWindow) return;
  positionWindow();
  mainWindow.show();
  mainWindow.focus();
}

function toggleWindow() {
  if (!mainWindow) return;
  if (mainWindow.isVisible()) {
    mainWindow.hide();
  } else if (Date.now() - lastHiddenAt > 250) {
    // (a tray click that just caused a blur-hide must not re-open the window)
    showWindow();
  }
}

function applyPinning() {
  if (!mainWindow) return;
  // Not hiding on blur => behave like a widget that stays on top.
  mainWindow.setAlwaysOnTop(!hideOnBlur, 'floating');
}

function createWindow() {
  mainWindow = new BrowserWindow({
    width: WIN_W,
    height: WIN_H,
    useContentSize: true,
    resizable: false,
    maximizable: false,
    minimizable: false,
    fullscreenable: false,
    frame: false,
    transparent: true,
    hasShadow: false,
    skipTaskbar: true,
    show: false,
    title: 'Win Solar',
    icon: path.join(__dirname, 'assets', 'icon.png'),
    backgroundColor: '#00000000',
    webPreferences: {
      preload: path.join(__dirname, 'preload.cjs'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      webSecurity: true,
      backgroundThrottling: false, // keep the sunrise/sunset timer exact while hidden in the tray
      spellcheck: false,
    },
  });

  // Lock the renderer down: no popups, no navigation away from the app.
  mainWindow.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  mainWindow.webContents.on('will-navigate', (e, url) => {
    const allowed = DEV_URL ? url.startsWith(DEV_URL) : url.startsWith('file://');
    if (!allowed) { e.preventDefault(); if (/^https:\/\//.test(url)) shell.openExternal(url); }
  });

  if (DEV_URL) mainWindow.loadURL(DEV_URL);
  else mainWindow.loadFile(path.join(__dirname, '..', 'dist', 'index.html'));

  mainWindow.once('ready-to-show', () => {
    if (!process.argv.includes('--minimized')) showWindow();
  });

  mainWindow.on('blur', () => {
    if (hideOnBlur && mainWindow.isVisible() && !mainWindow.webContents.isDevToolsOpened()) {
      mainWindow.hide();
      lastHiddenAt = Date.now();
    }
  });

  // Closing only hides to the tray; quit from the tray menu or Settings.
  mainWindow.on('close', (e) => {
    if (!app.isQuitting) { e.preventDefault(); mainWindow.hide(); lastHiddenAt = Date.now(); }
  });
}

/* ---------- IPC (every handler only trusts our own window) ---------- */
const trusted = (event) => mainWindow && !mainWindow.isDestroyed() && event.sender === mainWindow.webContents;
const handle = (channel, fn) => ipcMain.handle(channel, (event, ...args) => (trusted(event) ? fn(...args) : { success: false, reason: 'untrusted' }));
const listen = (channel, fn) => ipcMain.on(channel, (event, ...args) => { if (trusted(event)) fn(...args); });

function registerIpc() {
  handle('theme:set', (mode) => setWindowsTheme(mode));
  ipcMain.handle('theme:get', (event) => (trusted(event) ? getWindowsTheme() : 'dark'));

  handle('login:set', (opts) => {
    if (!opts || typeof opts.enabled !== 'boolean') return { success: false, reason: 'invalid' };
    if (!app.isPackaged) return { success: false, reason: 'dev' }; // would register electron.exe, not Win Solar
    app.setLoginItemSettings({ openAtLogin: opts.enabled, args: opts.minimized ? ['--minimized'] : [] });
    return { success: true };
  });
  ipcMain.handle('login:get', (event) => {
    if (!trusted(event) || !app.isPackaged) return { enabled: false, packaged: app.isPackaged };
    const a = app.getLoginItemSettings({ args: ['--minimized'] }).openAtLogin;
    const b = app.getLoginItemSettings({ args: [] }).openAtLogin;
    return { enabled: a || b, packaged: true };
  });

  listen('state:publish', (s) => {
    if (!s || !['auto', 'light', 'dark'].includes(s.mode) || !['light', 'dark'].includes(s.theme)) return;
    const L = {};
    for (const k of ['windows', 'light', 'dark', 'auto', 'open', 'quit']) {
      L[k] = s.labels && typeof s.labels[k] === 'string' ? s.labels[k].slice(0, 60) : undefined;
    }
    updateTray({ mode: s.mode, theme: s.theme, tip: String(s.tip || 'Win Solar').slice(0, 120), labels: L });
  });

  listen('window:hide', () => { mainWindow.hide(); lastHiddenAt = Date.now(); });
  listen('app:quit', () => { app.isQuitting = true; app.quit(); });
  listen('window:hide-on-blur', (v) => { hideOnBlur = v !== false; applyPinning(); });

  // Chromium's geolocation needs a Google key inside Electron, so use IP geolocation (city level is plenty for sun times).
  handle('location:get', async () => {
    try {
      const res = await net.fetch('https://ipwho.is/', { signal: AbortSignal.timeout(8000) });
      const j = await res.json();
      if (!j.success || typeof j.latitude !== 'number' || typeof j.longitude !== 'number') return { success: false, reason: 'lookup-failed' };
      return {
        success: true, name: j.city || 'My location', country: j.country || undefined,
        lat: Math.round(j.latitude * 10000) / 10000, lng: Math.round(j.longitude * 10000) / 10000,
        timezone: (j.timezone && j.timezone.id) || Intl.DateTimeFormat().resolvedOptions().timeZone,
      };
    } catch (e) {
      return { success: false, reason: 'network' };
    }
  });

  handle('wallpaper:pick', async () => {
    const r = await dialog.showOpenDialog(mainWindow, {
      title: 'Choose wallpaper',
      properties: ['openFile'],
      filters: [{ name: 'Images', extensions: ['jpg', 'jpeg', 'png', 'bmp'] }],
    });
    if (r.canceled || !r.filePaths[0] || !isSafeImagePath(r.filePaths[0])) return { success: false, reason: 'canceled' };
    return { success: true, path: r.filePaths[0] };
  });
  handle('wallpaper:set', (p) => setWallpaper(p));
}

app.whenReady().then(() => {
  if (process.platform === 'win32') app.setAppUserModelId('com.winsolar.app');

  registerIpc();
  createWindow();
  try {
    createTray({
      onToggle: toggleWindow,
      onSetMode: (mode) => { if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('mode:set', mode); },
      onQuit: () => { app.isQuitting = true; app.quit(); },
    });
  } catch (e) {
    console.warn('Tray unavailable:', e.message);
  }

  // After sleep / unlock the renderer must re-evaluate immediately (a switch may have been due meanwhile).
  const wake = () => { if (mainWindow && !mainWindow.isDestroyed()) mainWindow.webContents.send('system:resume'); };
  powerMonitor.on('resume', wake);
  powerMonitor.on('unlock-screen', wake);
});

app.on('before-quit', () => { app.isQuitting = true; });
// Stay alive in the tray when the window closes.
app.on('window-all-closed', () => {});
