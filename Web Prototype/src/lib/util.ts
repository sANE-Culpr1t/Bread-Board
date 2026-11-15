import type { AppData, Channel, ChannelId, MiniPlayerState, Processing, Sound } from '../types';

export const uid = () => Math.random().toString(36).slice(2, 9) + Date.now().toString(36).slice(-4);
export const clamp = (v: number, a: number, b: number) => Math.min(b, Math.max(a, v));
export const cx = (...c: (string | false | null | undefined)[]) => c.filter(Boolean).join(' ');

export const patchSound = (d: AppData, id: string, patch: Partial<Sound>): AppData => ({
  ...d,
  sounds: d.sounds.map((s) => (s.id === id ? { ...s, ...patch } : s)),
});

export const patchChannel = (d: AppData, ch: ChannelId, patch: Partial<Channel>): AppData => ({
  ...d,
  channels: { ...d.channels, [ch]: { ...d.channels[ch], ...patch } },
});

export const patchMini = (d: AppData, patch: Partial<MiniPlayerState>): AppData => ({
  ...d,
  mini: { ...d.mini, ...patch },
});

export function patchProcessing<K extends keyof Processing>(d: AppData, k: K, patch: Partial<Processing[K]>): AppData {
  return { ...d, processing: { ...d.processing, [k]: { ...d.processing[k], ...patch } } };
}

export const pushRecent = (d: AppData, id: string): AppData =>
  d.recent[0] === id ? d : { ...d, recent: [id, ...d.recent.filter((x) => x !== id)].slice(0, 12) };

export const removeSound = (d: AppData, id: string): AppData => ({
  ...d,
  sounds: d.sounds.filter((s) => s.id !== id),
  recent: d.recent.filter((x) => x !== id),
  mini: d.mini.selectedId === id ? { ...d.mini, selectedId: null } : d.mini,
});
