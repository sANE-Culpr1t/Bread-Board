import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import type { AppData } from '../types';
import { loadData, saveData } from '../lib/storage';

type Updater = (d: AppData) => AppData;
interface Store {
  data: AppData;
  setData: (fn: Updater) => void;
  replaceData: (d: AppData) => void;
}

const Ctx = createContext<Store | null>(null);

/** Single persisted app state (localStorage, debounced). */
export function AppStoreProvider({ children }: { children: ReactNode }) {
  const [data, setState] = useState<AppData>(loadData);
  const latest = useRef(data);
  latest.current = data;

  const setData = useCallback((fn: Updater) => setState(fn), []);
  const replaceData = useCallback((d: AppData) => setState(d), []);

  useEffect(() => {
    const t = window.setTimeout(() => saveData(data), 250);
    return () => window.clearTimeout(t);
  }, [data]);

  useEffect(() => {
    const flush = () => saveData(latest.current);
    window.addEventListener('beforeunload', flush);
    return () => window.removeEventListener('beforeunload', flush);
  }, []);

  return <Ctx.Provider value={{ data, setData, replaceData }}>{children}</Ctx.Provider>;
}

export function useApp() {
  const c = useContext(Ctx);
  if (!c) throw new Error('useApp must be used inside AppStoreProvider');
  return c;
}
