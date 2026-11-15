import type { AppData } from '../types';
import { defaultData } from './defaults';

const KEY = 'breadboard.prototype.v1';

/** Merge stored data over defaults so new fields added later always exist. */
export function migrate(raw: unknown): AppData {
  const def = defaultData();
  if (!raw || typeof raw !== 'object' || (raw as AppData).version !== 1) return def;
  const p = raw as Partial<AppData>;
  return {
    ...def,
    ...p,
    channels: { ...def.channels, ...p.channels },
    processing: { ...def.processing, ...p.processing },
    devices: { ...def.devices, ...p.devices },
    appearance: { ...def.appearance, ...p.appearance },
    mini: { ...def.mini, ...p.mini },
    globalHotkeys: { ...def.globalHotkeys, ...p.globalHotkeys },
    settings: { ...def.settings, ...p.settings },
    sounds: Array.isArray(p.sounds) ? p.sounds : def.sounds,
    categories: Array.isArray(p.categories) ? p.categories : def.categories,
    recent: Array.isArray(p.recent) ? p.recent : [],
    profiles: Array.isArray(p.profiles) ? p.profiles : def.profiles,
    webShortcuts: Array.isArray(p.webShortcuts) ? p.webShortcuts : def.webShortcuts,
    version: 1,
  };
}

export function loadData(): AppData {
  try {
    const raw = localStorage.getItem(KEY);
    return raw ? migrate(JSON.parse(raw)) : defaultData();
  } catch {
    return defaultData();
  }
}

export function saveData(d: AppData) {
  try {
    localStorage.setItem(KEY, JSON.stringify(d));
  } catch {
    /* storage full or blocked — prototype keeps working in memory */
  }
}

export function clearData() {
  try {
    localStorage.removeItem(KEY);
  } catch {
    /* ignore */
  }
}
