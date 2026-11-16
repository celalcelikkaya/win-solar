/**
 * Win Solar - system tray icon + context menu.
 * The menu mirrors the renderer state (mode / theme / next switch) that main.cjs receives over IPC.
 */
const { Tray, Menu, nativeImage } = require('electron');
const path = require('path');

let tray = null;

function createTray({ onToggle, onSetMode, onQuit }) {
  const icon = nativeImage.createFromPath(path.join(__dirname, 'assets', 'tray.png'));
  tray = new Tray(icon);
  tray.setToolTip('Win Solar');
  tray.on('click', onToggle);
  tray.on('double-click', onToggle);
  tray.callbacks = { onToggle, onSetMode, onQuit };
  updateTray({ mode: 'auto', theme: 'dark', tip: 'Win Solar' });
  return tray;
}

/** state: { mode: 'auto'|'light'|'dark', theme: 'light'|'dark', tip: string } */
function updateTray(state) {
  if (!tray || tray.isDestroyed()) return;
  const { onToggle, onSetMode, onQuit } = tray.callbacks;
  tray.setToolTip(state.tip || 'Win Solar');
  const l = Object.assign({ windows: 'Windows', light: 'Light', dark: 'Dark', auto: 'Auto (sunrise / sunset)', open: 'Open Win Solar', quit: 'Quit' }, ...[state.labels || {}].map((o) => Object.fromEntries(Object.entries(o).filter(([, v]) => v))));
  const radio = (label, mode) => ({ label, type: 'radio', checked: state.mode === mode, click: () => onSetMode(mode) });
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Win Solar', enabled: false },
    { label: `${l.windows}: ${state.theme === 'light' ? l.light : l.dark}`, enabled: false },
    { type: 'separator' },
    radio(l.auto, 'auto'),
    radio(l.light, 'light'),
    radio(l.dark, 'dark'),
    { type: 'separator' },
    { label: l.open, click: onToggle },
    { label: l.quit, click: onQuit },
  ]));
}

function getTrayBounds() {
  return tray && !tray.isDestroyed() ? tray.getBounds() : null;
}

module.exports = { createTray, updateTray, getTrayBounds };
