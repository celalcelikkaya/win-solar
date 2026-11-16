export type ThemeMode = 'auto' | 'light' | 'dark';
export type Theme = 'light' | 'dark';
export type Lang = 'tr' | 'en';

export interface Place {
  name: string;
  region?: string;
  country?: string;
  lat: number;
  lng: number;
  timezone: string;
  /** true when the location is only a rough guess (offline / not chosen by the user) */
  approx?: boolean;
}

export interface Settings {
  mode: ThemeMode;
  place: Place | null;
  offsetSunrise: number; // minutes, + = later
  offsetSunset: number;
  autoLaunch: boolean;
  startMinimized: boolean;
  notify: boolean;
  wallpaperSync: boolean;
  lang: Lang;
  hour12: boolean;
  /** show a heads-up (snooze / skip) shortly before an automatic switch */
  headsUp: boolean;
  /** manual Light/Dark ends automatically at the next sunrise/sunset */
  manualUntilNext: boolean;
  /** epoch ms when the current manual mode ends (null = until changed) */
  manualUntil: number | null;
  /** desktop: hide the widget when clicking outside (off = stays on top like a widget) */
  hideOnBlur: boolean;
  wallpaperLight: string | null;
  wallpaperDark: string | null;
  schema: number;
}

export const DEFAULT_SETTINGS: Settings = {
  mode: 'auto',
  place: null,
  offsetSunrise: 0,
  offsetSunset: 0,
  autoLaunch: false,
  startMinimized: true,
  notify: false,
  wallpaperSync: false,
  lang: 'en',
  hour12: false,
  headsUp: true,
  manualUntilNext: true,
  manualUntil: null,
  hideOnBlur: true,
  wallpaperLight: null,
  wallpaperDark: null,
  schema: 2,
};

export interface DesktopResult { success: boolean; reason?: string; [k: string]: unknown }

/** Exposed by electron/preload.cjs (absent in the browser). */
export interface TrayLabels { windows: string; light: string; dark: string; auto: string; open: string; quit: string }

export interface ElectronAPI {
  isElectron: true;
  platform: string;
  version: string;
  toggleTheme: (mode: Theme) => Promise<DesktopResult>;
  getSystemTheme: () => Promise<Theme>;
  setLoginItem: (opts: { enabled: boolean; minimized: boolean }) => Promise<DesktopResult>;
  getLoginItem: () => Promise<{ enabled: boolean; packaged: boolean }>;
  publishState: (state: { mode: ThemeMode; theme: Theme; tip: string; labels: TrayLabels }) => void;
  onSetMode: (cb: (mode: ThemeMode) => void) => () => void;
  onResume: (cb: () => void) => () => void;
  hideWindow: () => void;
  quit: () => void;
  setHideOnBlur: (value: boolean) => void;
  getLocation: () => Promise<DesktopResult & { name?: string; country?: string; lat?: number; lng?: number; timezone?: string }>;
  pickWallpaper: () => Promise<DesktopResult & { path?: string }>;
  setWallpaper: (path: string) => Promise<DesktopResult>;
}

declare global {
  interface Window {
    electronAPI?: ElectronAPI;
  }
}
