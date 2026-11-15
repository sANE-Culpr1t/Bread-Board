// SIMULATED playback engine: tracks "voices" with timers. No audio is decoded or played.
import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useApp } from './AppStore';
import { pushRecent } from '../lib/util';

export interface Instance {
  iid: number;
  soundId: string;
  start: number; // performance.now()
  duration: number;
  loop: boolean;
}

export interface BrowserTrack { title: string; site: string }

interface PlaybackApi {
  instances: Instance[];
  trigger: (soundId: string, phase?: 'down' | 'up') => void;
  stop: (soundId: string) => void;
  stopAll: () => void;
  browserTrack: BrowserTrack | null;
  setBrowserTrack: (t: BrowserTrack | null) => void;
  pttDown: boolean;
  setPtt: (v: boolean) => void;
}

const Ctx = createContext<PlaybackApi | null>(null);

export function PlaybackProvider({ children }: { children: ReactNode }) {
  const { data, setData } = useApp();
  const dataRef = useRef(data);
  dataRef.current = data;

  const [instances, setInstances] = useState<Instance[]>([]);
  const instRef = useRef<Instance[]>([]);
  const timers = useRef(new Map<number, number>());
  const nextId = useRef(1);
  const [browserTrack, setBrowserTrack] = useState<BrowserTrack | null>(null);
  const [pttDown, setPtt] = useState(false);

  const commit = useCallback((next: Instance[]) => {
    const keep = new Set(next.map((i) => i.iid));
    for (const [iid, t] of timers.current) {
      if (!keep.has(iid)) {
        window.clearTimeout(t);
        timers.current.delete(iid);
      }
    }
    instRef.current = next;
    setInstances(next);
  }, []);

  const stop = useCallback((soundId: string) => commit(instRef.current.filter((i) => i.soundId !== soundId)), [commit]);
  const stopAll = useCallback(() => commit([]), [commit]);

  const trigger = useCallback(
    (soundId: string, phase: 'down' | 'up' = 'down') => {
      const d = dataRef.current;
      const s = d.sounds.find((x) => x.id === soundId);
      if (!s) return;
      const playing = instRef.current.some((i) => i.soundId === soundId);

      if (phase === 'up') {
        if (s.mode === 'hold') stop(soundId);
        return;
      }
      if ((s.mode === 'toggle' || s.mode === 'loop') && playing) return stop(soundId);
      if (s.mode === 'hold' && playing) return;

      let list = instRef.current;
      if (s.mode === 'oneshot' && !d.settings.allowStacking) list = list.filter((i) => i.soundId !== soundId);
      while (list.length >= d.settings.maxVoices) list = list.slice(1); // drop oldest voice

      const loop = s.mode === 'loop' || s.mode === 'hold';
      const inst: Instance = { iid: nextId.current++, soundId, start: performance.now(), duration: s.durationMs, loop };
      commit([...list, inst]);
      if (!loop) {
        const t = window.setTimeout(() => {
          timers.current.delete(inst.iid);
          commit(instRef.current.filter((i) => i.iid !== inst.iid));
        }, s.durationMs);
        timers.current.set(inst.iid, t);
      }
      setData((x) => pushRecent(x, soundId));
    },
    [commit, setData, stop],
  );

  // Drop voices whose sound was deleted.
  useEffect(() => {
    const ids = new Set(data.sounds.map((s) => s.id));
    if (instRef.current.some((i) => !ids.has(i.soundId))) commit(instRef.current.filter((i) => ids.has(i.soundId)));
  }, [data.sounds, commit]);

  useEffect(() => () => timers.current.forEach((t) => window.clearTimeout(t)), []);

  return (
    <Ctx.Provider value={{ instances, trigger, stop, stopAll, browserTrack, setBrowserTrack, pttDown, setPtt }}>
      {children}
    </Ctx.Provider>
  );
}

export function usePlayback() {
  const c = useContext(Ctx);
  if (!c) throw new Error('usePlayback must be used inside PlaybackProvider');
  return c;
}
