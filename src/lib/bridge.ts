import { ElectronAPI, Theme } from '../types';

/** Thin wrapper over the Electron preload API. Every call is a safe no-op in the browser. */
export const api = (): ElectronAPI | undefined => (typeof window !== 'undefined' ? window.electronAPI : undefined);
export const isElectron = () => !!api();

export const applyWindowsTheme = (theme: Theme) => api()?.toggleTheme(theme);
export const setLoginItem = (enabled: boolean, minimized: boolean) => api()?.setLoginItem({ enabled, minimized });
