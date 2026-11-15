import { useEffect, useRef, useState } from 'react';
import { captureState, comboFromEvent } from '../lib/hotkeys';
import { cx } from '../lib/util';

export function KeyCombo({ combo }: { combo: string }) {
  return (
    <span className="inline-flex items-center gap-1">
      {combo.split('+').map((k, i) => (
        <kbd key={i} className="rounded-md bg-raised px-1.5 py-0.5 font-sans text-xs font-medium text-ink shadow-[inset_0_-1px_0_rgb(0_0_0/0.45)]">
          {k}
        </kbd>
      ))}
    </span>
  );
}

/** Click, then press a key combination. Esc cancels, Backspace/Delete clears. */
export function HotkeyInput({
  value, onChange, placeholder = 'Set shortcut', className, label,
}: { value: string | null; onChange: (v: string | null) => void; placeholder?: string; className?: string; label: string }) {
  const [listening, setListening] = useState(false);
  const cb = useRef(onChange);
  cb.current = onChange;

  useEffect(() => {
    if (!listening) return;
    captureState.active = true;
    const down = (e: KeyboardEvent) => {
      e.preventDefault();
      e.stopPropagation();
      const bare = !e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey;
      if (bare && e.key === 'Escape') return setListening(false);
      if (bare && (e.key === 'Backspace' || e.key === 'Delete')) {
        cb.current(null);
        return setListening(false);
      }
      const combo = comboFromEvent(e);
      if (!combo) return; // only modifiers so far
      cb.current(combo);
      setListening(false);
    };
    const cancel = () => setListening(false);
    window.addEventListener('keydown', down, true);
    window.addEventListener('blur', cancel);
    return () => {
      captureState.active = false;
      window.removeEventListener('keydown', down, true);
      window.removeEventListener('blur', cancel);
    };
  }, [listening]);

  return (
    <button
      type="button"
      aria-label={`${label}: ${listening ? 'listening' : value ?? 'not set'}`}
      onClick={() => setListening((l) => !l)}
      onBlur={() => setListening(false)}
      className={cx(
        'inline-flex h-9 min-w-[8rem] items-center justify-center gap-1 rounded-full border px-3 text-sm transition-colors',
        listening ? 'animate-pulse border-accent bg-accent/10 text-accent' : 'border-line-strong bg-side text-ink hover:border-accent/50',
        className,
      )}
    >
      {listening ? 'Press keys…' : value ? <KeyCombo combo={value} /> : <span className="text-mute">{placeholder}</span>}
    </button>
  );
}
