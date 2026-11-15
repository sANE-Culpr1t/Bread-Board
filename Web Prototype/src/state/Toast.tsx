import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { CircleAlert, CircleCheck, Info } from 'lucide-react';

type Tone = 'info' | 'success' | 'error';
type Push = (msg: string, tone?: Tone) => void;

const Ctx = createContext<Push>(() => {});

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<{ id: number; msg: string; tone: Tone }[]>([]);
  const idRef = useRef(0);

  const push = useCallback<Push>((msg, tone = 'info') => {
    const id = ++idRef.current;
    setItems((l) => [...l.slice(-2), { id, msg, tone }]);
    window.setTimeout(() => setItems((l) => l.filter((i) => i.id !== id)), 3200);
  }, []);

  return (
    <Ctx.Provider value={push}>
      {children}
      <div className="pointer-events-none fixed left-1/2 top-5 z-[100] flex -translate-x-1/2 flex-col items-center gap-2" aria-live="polite">
        <AnimatePresence>
          {items.map((t) => {
            const Icon = t.tone === 'error' ? CircleAlert : t.tone === 'success' ? CircleCheck : Info;
            const color = t.tone === 'error' ? 'text-bad' : t.tone === 'success' ? 'text-ok' : 'text-accent';
            return (
              <motion.div
                key={t.id}
                layout
                initial={{ opacity: 0, y: -12, scale: 0.96 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                exit={{ opacity: 0, y: -8, scale: 0.96 }}
                className="pointer-events-auto flex max-w-[90vw] items-center gap-2.5 rounded-full border border-line bg-raised px-4 py-2.5 text-sm shadow-pop"
              >
                <Icon size={16} className={color} />
                {t.msg}
              </motion.div>
            );
          })}
        </AnimatePresence>
      </div>
    </Ctx.Provider>
  );
}

export const useToast = () => useContext(Ctx);
