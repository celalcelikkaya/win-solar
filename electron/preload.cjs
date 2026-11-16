/**
 * Win Solar - preload (sandboxed). Exposes a small, explicit API; no generic IPC and no script execution.
 */
const { contextBridge, ipcRenderer } = require('electron');

const on = (channel, cb) => {
  const handler = (_event, ...args) => cb(...args);
  ipcRenderer.on(channel, handler);
  return () => ipcRenderer.removeListener(channel, handler);
};

contextBridge.exposeInMainWorld('electronAPI', {
  isElectron: true,
  platform: process.platform,
  version: process.versions.electron,

  toggleTheme: (mode) => ipcRenderer.invoke('theme:set', mode),
  getSystemTheme: () => ipcRenderer.invoke('theme:get'),

  setLoginItem: (opts) => ipcRenderer.invoke('login:set', opts),
  getLoginItem: () => ipcRenderer.invoke('login:get'),

  publishState: (state) => ipcRenderer.send('state:publish', state),
  onSetMode: (cb) => on('mode:set', cb),
  onResume: (cb) => on('system:resume', cb),

  hideWindow: () => ipcRenderer.send('window:hide'),
  quit: () => ipcRenderer.send('app:quit'),
  setHideOnBlur: (value) => ipcRenderer.send('window:hide-on-blur', value),

  getLocation: () => ipcRenderer.invoke('location:get'),
  pickWallpaper: () => ipcRenderer.invoke('wallpaper:pick'),
  setWallpaper: (p) => ipcRenderer.invoke('wallpaper:set', p),
});
