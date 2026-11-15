import type { AppData, ProfileSnapshot } from '../types';

export function makeSnapshot(d: AppData): ProfileSnapshot {
  return structuredClone({
    sounds: d.sounds,
    categories: d.categories,
    channels: d.channels,
    processing: d.processing,
    devices: d.devices,
    globalHotkeys: d.globalHotkeys,
    settings: d.settings,
    appearance: d.appearance,
  });
}

export function applySnapshot(d: AppData, snap: ProfileSnapshot, profileId: string): AppData {
  const s = structuredClone(snap);
  const ids = new Set(s.sounds.map((x) => x.id));
  return { ...d, ...s, recent: d.recent.filter((id) => ids.has(id)), activeProfileId: profileId };
}
