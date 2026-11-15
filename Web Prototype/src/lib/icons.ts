import {
  Bell, Bomb, Car, Cat, Dog, Drum, Flame, Ghost, Heart, Laugh, Megaphone, Mic, Monitor, Music,
  PartyPopper, Radio, Rocket, Siren, Skull, Star, Swords, Trophy, Volume2, Zap, type LucideIcon,
} from 'lucide-react';

export const SOUND_ICONS = {
  volume: Volume2, skull: Skull, megaphone: Megaphone, laugh: Laugh, monitor: Monitor, bomb: Bomb,
  flame: Flame, bell: Bell, music: Music, zap: Zap, drum: Drum, siren: Siren, ghost: Ghost,
  heart: Heart, star: Star, mic: Mic, radio: Radio, trophy: Trophy, swords: Swords, rocket: Rocket,
  party: PartyPopper, cat: Cat, dog: Dog, car: Car,
} satisfies Record<string, LucideIcon>;

export type IconKey = keyof typeof SOUND_ICONS;

/** Muted tints for sound icons — matte palette, no neon. */
export const TINTS = {
  beige: '#e3c8a4',
  ivory: '#efe8dd',
  amber: '#d9a35b',
  clay: '#c98a72',
  sage: '#9fb59a',
  slate: '#9aa7b8',
  mauve: '#b59ab0',
} as const;

export type TintKey = keyof typeof TINTS;
