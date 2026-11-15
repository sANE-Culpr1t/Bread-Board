// ~30 fps animation clock + simulated meter levels. Only components that call useTick()
// re-render per frame; requestAnimationFrame pauses automatically when the tab is hidden.
import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { useApp } from './AppStore';
import { usePlayback } from './Playback';
import { computeLevels, newSimState, type Levels } from '../lib/simulation';

interface Tick { now: number; levels: Levels }

const ZERO: Levels = { mic: 0, browser: 0, soundboard: 0, master: 0 };
const Ctx = createContext<Tick>({ now: 0, levels: ZERO });

export function TickProvider({ children }: { children: ReactNode }) {
  const { data } = useApp();
  const pb = usePlayback();
  const src = useRef({ data, pb });
  src.current = { data, pb };
  const sim = useRef(newSimState());
  const [tick, setTick] = useState<Tick>({ now: performance.now(), levels: ZERO });

  useEffect(() => {
    let raf = 0;
    let last = 0;
    const loop = (t: number) => {
      raf = requestAnimationFrame(loop);
      if (t - last < 33) return;
      last = t;
      const { data: d, pb: p } = src.current;
      const levels = computeLevels(t, d, { instances: p.instances, browserPlaying: !!p.browserTrack, pttDown: p.pttDown }, sim.current);
      setTick({ now: t, levels });
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, []);

  return <Ctx.Provider value={tick}>{children}</Ctx.Provider>;
}

export const useTick = () => useContext(Ctx);
