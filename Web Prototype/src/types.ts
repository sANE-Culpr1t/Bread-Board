import type { IconKey, TintKey } from './lib/icons';

/** oneshot: every press stacks a new voice · loop: press to start/stop looping
 *  hold: plays while the key/button is held · toggle: press to play once, press again to stop */
export type PlayMode = 'oneshot' | 'loop' | 'hold' | 'toggle';
/** app = audio captured from one running application/window chosen by the user. */
export type ChannelId = 'mic' | 'app' | 'soundboard' | 'master';
export type PageId = 'soundboard' | 'mixer' | 'shortcuts' | 'settings' | 'appearance';

/** A running application whose audio is captured (process loopback in the native app). */
export interface AppSource { processName: string; title: string }
export type Density = 'compact' | 'comfortable' | 'spacious';
export type MotionLevel = 'full' | 'reduced' | 'off';
export type CardStyle = 'soft' | 'flat' | 'outlined';
export type MiniTab = 'favorites' | 'recent' | 'playing';

export interface Sound {
  id: string;
  name: string;
  icon: IconKey;
  tint: TintKey;
  category: string;
  hotkey: string | null;
  volume: number; // 0–100
  mode: PlayMode;
  favorite: boolean;
  durationMs: number; // simulated length (read from file metadata when imported)
  fileName: string | null; // prototype keeps only the name, never the audio
  sourceUrl?: string | null; // set when imported from a website
}

export interface Channel {
  gain: number; // 0–100
  muted: boolean;
  toVirtualMic: boolean;
  toMonitor: boolean;
}

export interface Processing {
  noiseGate: { enabled: boolean; threshold: number };
  compressor: { enabled: boolean; amount: number };
  limiter: { enabled: boolean; ceiling: number };
  ducking: { enabled: boolean; amount: number };
  pushToTalk: { enabled: boolean; key: string | null };
}

export interface Devices {
  input: string;
  output: string;
  monitor: string;
  virtualMicEnabled: boolean;
  appSource: AppSource | null;
}

export interface Appearance {
  accent: string;
  accent2: string;
  radius: number;
  density: Density;
  columns: number; // 0 = auto
  motion: MotionLevel;
  cardStyle: CardStyle;
  contrast: number; // surface separation multiplier
  fontScale: number;
}

export interface MiniPlayerState {
  open: boolean;
  x: number; // -1 = place automatically
  y: number;
  w: number;
  h: number;
  opacity: number; // 0.3–1
  pinned: boolean;
  collapsed: boolean;
  tab: MiniTab;
  selectedId: string | null;
  showMixer: boolean;
}

export interface WebShortcut { id: string; name: string; url: string }

export interface GlobalHotkeys {
  stopAll: string | null;
  muteMic: string | null;
  toggleMini: string | null;
}

export interface PlaybackSettings {
  allowStacking: boolean;
  maxVoices: number;
  confirmDelete: boolean;
}

export interface ProfileSnapshot {
  sounds: Sound[];
  categories: string[];
  channels: Record<ChannelId, Channel>;
  processing: Processing;
  devices: Devices;
  globalHotkeys: GlobalHotkeys;
  settings: PlaybackSettings;
  appearance: Appearance;
}

export interface Profile { id: string; name: string; updatedAt: number; snapshot: ProfileSnapshot }

export interface AppData {
  version: 1;
  page: PageId;
  sounds: Sound[];
  categories: string[];
  recent: string[];
  channels: Record<ChannelId, Channel>;
  processing: Processing;
  devices: Devices;
  appearance: Appearance;
  mini: MiniPlayerState;
  /** Sound sites offered in Add Sound → From website. */
  webShortcuts: WebShortcut[];
  globalHotkeys: GlobalHotkeys;
  settings: PlaybackSettings;
  profiles: Profile[];
  activeProfileId: string | null;
}
