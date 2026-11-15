import type { AppData, Appearance, PlayMode, Sound } from '../types';
import type { IconKey, TintKey } from './icons';
import { makeSnapshot } from './profiles';
import { INPUT_DEVICES, MONITOR_DEVICES, OUTPUT_DEVICES } from './devices';

export const DEFAULT_APPEARANCE: Appearance = {
  accent: '#e3c8a4',
  accent2: '#d9a35b',
  radius: 18,
  density: 'comfortable',
  columns: 0,
  motion: 'full',
  cardStyle: 'soft',
  contrast: 1,
  fontScale: 1,
};

const s = (
  id: string, name: string, icon: IconKey, tint: TintKey, category: string,
  hotkey: string | null, mode: PlayMode, durationMs: number, favorite = false,
): Sound => ({ id, name, icon, tint, category, hotkey, mode, durationMs, favorite, volume: 85, fileName: null });

export const DEFAULT_SOUNDS: Sound[] = [
  s('bruh', 'Bruh', 'volume', 'beige', 'Memes', '1', 'oneshot', 900, true),
  s('vine-boom', 'Vine Boom', 'skull', 'ivory', 'Memes', '2', 'oneshot', 1400, true),
  s('airhorn', 'Airhorn', 'megaphone', 'amber', 'Effects', '3', 'oneshot', 2200),
  s('laugh', 'Laugh', 'laugh', 'amber', 'Reactions', '4', 'oneshot', 2600, true),
  s('startup', 'Startup Chime', 'monitor', 'slate', 'Effects', '5', 'oneshot', 3200),
  s('explosion', 'Explosion', 'bomb', 'clay', 'Effects', '6', 'oneshot', 2400),
  s('drumroll', 'Drumroll', 'drum', 'beige', 'Music', '7', 'hold', 4000),
  s('siren', 'Siren', 'siren', 'clay', 'Effects', '8', 'loop', 2000),
  s('applause', 'Applause', 'party', 'sage', 'Reactions', '9', 'toggle', 5200),
  s('sad-trombone', 'Sad Trombone', 'music', 'mauve', 'Reactions', '0', 'oneshot', 3000),
];

export function defaultData(): AppData {
  const base: AppData = {
    version: 1,
    page: 'soundboard',
    sounds: DEFAULT_SOUNDS.map((x) => ({ ...x })),
    categories: ['Memes', 'Reactions', 'Effects', 'Music'],
    recent: [],
    channels: {
      mic: { gain: 80, muted: false, toVirtualMic: true, toMonitor: false },
      app: { gain: 65, muted: false, toVirtualMic: true, toMonitor: false },
      soundboard: { gain: 70, muted: false, toVirtualMic: true, toMonitor: true },
      master: { gain: 90, muted: false, toVirtualMic: true, toMonitor: false },
    },
    processing: {
      noiseGate: { enabled: true, threshold: 4 },
      compressor: { enabled: false, amount: 40 },
      limiter: { enabled: true, ceiling: 92 },
      ducking: { enabled: false, amount: 50 },
      pushToTalk: { enabled: false, key: 'V' },
    },
    devices: {
      input: INPUT_DEVICES[0],
      output: OUTPUT_DEVICES[0],
      monitor: MONITOR_DEVICES[0],
      virtualMicEnabled: true,
      appSource: null,
    },
    appearance: { ...DEFAULT_APPEARANCE },
    mini: {
      open: true, x: -1, y: 110, w: 320, h: 500, opacity: 1, pinned: true,
      collapsed: false, tab: 'favorites', selectedId: null, showMixer: false,
    },
    webShortcuts: [
      { id: 'myinstants', name: 'Myinstants', url: 'https://www.myinstants.com/' },
    ],
    globalHotkeys: { stopAll: 'Ctrl+Shift+S', muteMic: 'Ctrl+Shift+M', toggleMini: 'Ctrl+Shift+G' },
    settings: { allowStacking: true, maxVoices: 8, confirmDelete: true },
    profiles: [],
    activeProfileId: 'general',
  };

  const general = makeSnapshot(base);
  const gaming = makeSnapshot(base);
  gaming.channels.app.gain = 45;
  gaming.processing.pushToTalk.enabled = false;
  gaming.processing.noiseGate.threshold = 18;
  const streaming = makeSnapshot(base);
  streaming.channels.app.gain = 55;
  streaming.processing.compressor.enabled = true;
  streaming.processing.ducking.enabled = true;

  const now = Date.now();
  base.profiles = [
    { id: 'general', name: 'General Use', updatedAt: now, snapshot: general },
    { id: 'gaming', name: 'Gaming', updatedAt: now, snapshot: gaming },
    { id: 'streaming', name: 'Streaming', updatedAt: now, snapshot: streaming },
  ];
  return base;
}
