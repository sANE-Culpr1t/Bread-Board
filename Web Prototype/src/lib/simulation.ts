// SIMULATED audio levels. Nothing here touches real audio; it only produces believable
// meter values from the mixer state so the UI can be evaluated.
import type { AppData } from '../types';
import type { Instance } from '../state/Playback';

export interface Levels { mic: number; browser: number; soundboard: number; master: number }

export interface SimState {
  talking: boolean;
  switchAt: number;
  mic: number;
  browser: number;
  soundboard: number;
  master: number;
}

export const newSimState = (): SimState => ({ talking: false, switchAt: 0, mic: 0, browser: 0, soundboard: 0, master: 0 });

const smooth = (prev: number, target: number) =>
  target > prev ? prev + (target - prev) * 0.55 : prev + (target - prev) * 0.16;
const jitter = () => 0.8 + Math.random() * 0.4;
const clamp01 = (v: number) => Math.min(1, Math.max(0, v));
const compress = (x: number, amount: number) => (x <= 0.45 ? x : 0.45 + (x - 0.45) * (1 - (amount / 100) * 0.8));

export function computeLevels(
  now: number,
  d: AppData,
  pb: { instances: Instance[]; browserPlaying: boolean; pttDown: boolean },
  s: SimState,
): Levels {
  // Fake "voice": alternates between talking and silence.
  if (now > s.switchAt) {
    s.talking = !s.talking;
    s.switchAt = now + (s.talking ? 900 + Math.random() * 2200 : 1400 + Math.random() * 3200);
  }
  const ch = d.channels;
  const pr = d.processing;

  let mic = s.talking
    ? (0.35 + 0.4 * Math.random()) * (0.6 + 0.4 * Math.abs(Math.sin(now / 90)))
    : 0.03 + Math.random() * 0.05;
  if (pr.noiseGate.enabled && mic < pr.noiseGate.threshold / 100) mic = 0;
  if (pr.pushToTalk.enabled && !pb.pttDown) mic = 0;
  if (pr.compressor.enabled) mic = compress(mic, pr.compressor.amount);
  mic = ch.mic.muted ? 0 : mic * (ch.mic.gain / 100);

  let sb = 0;
  for (const i of pb.instances) {
    const snd = d.sounds.find((x) => x.id === i.soundId);
    if (!snd) continue;
    const el = now - i.start;
    const env = i.loop
      ? 0.55 + 0.15 * Math.sin(el / 140)
      : Math.min(1, el / 40) * Math.pow(1 - Math.min(1, el / i.duration), 0.7);
    sb += env * (snd.volume / 100) * jitter() * 0.9;
  }
  sb = ch.soundboard.muted ? 0 : Math.min(1.2, sb) * (ch.soundboard.gain / 100);

  let br = pb.browserPlaying ? (0.4 + 0.25 * Math.random()) * (0.8 + 0.2 * Math.sin(now / 300)) : 0;
  if (pr.ducking.enabled && mic > 0.15) br *= 1 - pr.ducking.amount / 100;
  br = ch.browser.muted ? 0 : br * (ch.browser.gain / 100);

  let m = (ch.mic.toVirtualMic ? mic : 0) + (ch.browser.toVirtualMic ? br : 0) + (ch.soundboard.toVirtualMic ? sb : 0);
  m *= 0.85;
  if (pr.compressor.enabled) m = compress(m, pr.compressor.amount);
  m = ch.master.muted ? 0 : m * (ch.master.gain / 100);
  if (pr.limiter.enabled) m = Math.min(m, pr.limiter.ceiling / 100);

  s.mic = smooth(s.mic, clamp01(mic));
  s.browser = smooth(s.browser, clamp01(br));
  s.soundboard = smooth(s.soundboard, clamp01(sb));
  s.master = smooth(s.master, clamp01(m));
  return { mic: s.mic, browser: s.browser, soundboard: s.soundboard, master: s.master };
}
