import { useEffect, useId, type CSSProperties, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { X, type LucideIcon } from 'lucide-react';
import { clamp, cx } from '../lib/util';
import { useTick } from '../state/Tick';
import type { ChannelId } from '../types';

export function Slider({
  value, onChange, min = 0, max = 100, step = 1, label, disabled, small, className,
}: {
  value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number;
  label: string; disabled?: boolean; small?: boolean; className?: string;
}) {
  const pct = ((value - min) / (max - min)) * 100;
  return (
    <input
      type="range" aria-label={label} title={label} disabled={disabled}
      min={min} max={max} step={step} value={value}
      onChange={(e) => onChange(Number(e.target.value))}
      className={cx('bb-range', small && 'sm', className)}
      style={{ '--fill': `${pct}%` } as CSSProperties}
    />
  );
}

/** Vertical fader with pointer + keyboard support. */
export function Fader({ value, onChange, label, height = 200 }: { value: number; onChange: (v: number) => void; label: string; height?: number }) {
  const setFromY = (el: HTMLElement, y: number) => {
    const r = el.getBoundingClientRect();
    onChange(Math.round(clamp((r.bottom - y) / r.height, 0, 1) * 100));
  };
  const onKey = (e: KeyboardEvent) => {
    const step = e.key === 'PageUp' || e.key === 'PageDown' ? 10 : 2;
    if (e.key === 'ArrowUp' || e.key === 'ArrowRight' || e.key === 'PageUp') onChange(clamp(value + step, 0, 100));
    else if (e.key === 'ArrowDown' || e.key === 'ArrowLeft' || e.key === 'PageDown') onChange(clamp(value - step, 0, 100));
    else return;
    e.preventDefault();
  };
  return (
    <div
      role="slider" tabIndex={0} aria-label={label} aria-orientation="vertical"
      aria-valuemin={0} aria-valuemax={100} aria-valuenow={value}
      onKeyDown={onKey}
      onPointerDown={(e: PointerEvent<HTMLDivElement>) => { e.currentTarget.setPointerCapture(e.pointerId); setFromY(e.currentTarget, e.clientY); }}
      onPointerMove={(e: PointerEvent<HTMLDivElement>) => { if (e.currentTarget.hasPointerCapture(e.pointerId)) setFromY(e.currentTarget, e.clientY); }}
      className="relative w-9 cursor-pointer touch-none rounded-full"
      style={{ height }}
    >
      <div className="absolute inset-y-0 left-1/2 w-1.5 -translate-x-1/2 rounded-full bg-track" />
      <div className="absolute bottom-0 left-1/2 w-1.5 -translate-x-1/2 rounded-full bg-accent" style={{ height: `${value}%` }} />
      <div
        className="absolute left-1/2 h-4 w-8 -translate-x-1/2 rounded-md bg-accent shadow-[0_3px_10px_rgb(0_0_0/0.55),inset_0_-2px_0_rgb(0_0_0/0.15)]"
        style={{ bottom: `calc(${value}% - 8px)` }}
      />
    </div>
  );
}

export function Toggle({ checked, onChange, label, small }: { checked: boolean; onChange: (v: boolean) => void; label: string; small?: boolean }) {
  return (
    <button
      type="button" role="switch" aria-checked={checked} aria-label={label} title={label}
      onClick={() => onChange(!checked)}
      className={cx(
        'inline-flex shrink-0 items-center rounded-full px-[3px] transition-colors duration-200',
        small ? 'h-5 w-9' : 'h-[26px] w-[46px]',
        checked ? 'justify-end bg-amber' : 'justify-start bg-track',
      )}
    >
      <motion.span
        layout transition={{ type: 'spring', stiffness: 600, damping: 34 }}
        className={cx('block rounded-full bg-ink shadow-[0_1px_3px_rgb(0_0_0/0.4)]', small ? 'h-3.5 w-3.5' : 'h-5 w-5')}
      />
    </button>
  );
}

export function IconButton({
  icon: Icon, label, onClick, active, danger, size = 'md', className, disabled,
}: {
  icon: LucideIcon; label: string; onClick?: () => void; active?: boolean; danger?: boolean;
  size?: 'xs' | 'sm' | 'md' | 'lg'; className?: string; disabled?: boolean;
}) {
  const dims = { xs: 'h-7 w-7', sm: 'h-8 w-8', md: 'h-10 w-10', lg: 'h-12 w-12' }[size];
  const isz = { xs: 14, sm: 16, md: 18, lg: 22 }[size];
  return (
    <motion.button
      type="button" whileTap={disabled ? undefined : { scale: 0.9 }}
      onClick={onClick} disabled={disabled} aria-label={label} title={label}
      aria-pressed={active === undefined ? undefined : active}
      className={cx(
        'grid shrink-0 place-items-center rounded-full transition-colors disabled:opacity-35',
        dims,
        active
          ? danger ? 'bg-bad/20 text-bad' : 'bg-accent/15 text-accent'
          : 'bg-raised text-ink/80 hover:bg-hover hover:text-ink',
        className,
      )}
    >
      <Icon size={isz} strokeWidth={1.9} />
    </motion.button>
  );
}

export function Button({
  children, icon: Icon, onClick, variant = 'soft', type = 'button', disabled, className, title,
}: {
  children: ReactNode; icon?: LucideIcon; onClick?: () => void; variant?: 'primary' | 'soft' | 'ghost' | 'danger';
  type?: 'button' | 'submit'; disabled?: boolean; className?: string; title?: string;
}) {
  const v = {
    primary: 'bg-accent text-on-accent hover:brightness-105',
    soft: 'bg-raised text-ink hover:bg-hover',
    ghost: 'text-mute hover:bg-raised hover:text-ink',
    danger: 'bg-bad/15 text-bad hover:bg-bad/25',
  }[variant];
  return (
    <motion.button
      type={type} onClick={onClick} disabled={disabled} title={title}
      whileTap={disabled ? undefined : { scale: 0.96 }}
      className={cx('inline-flex h-10 shrink-0 items-center justify-center gap-2 rounded-full px-4 text-sm font-medium transition-colors disabled:opacity-40', v, className)}
    >
      {Icon && <Icon size={17} strokeWidth={2} className={variant === 'soft' ? 'text-amber' : undefined} />}
      {children}
    </motion.button>
  );
}

export function Segmented<T extends string | number>({
  value, options, onChange, label,
}: { value: T; options: { value: T; label: ReactNode }[]; onChange: (v: T) => void; label: string }) {
  const id = useId();
  return (
    <div className="inline-flex flex-wrap gap-0.5 rounded-full bg-side p-1" role="radiogroup" aria-label={label}>
      {options.map((o) => {
        const on = o.value === value;
        return (
          <button
            key={String(o.value)} type="button" role="radio" aria-checked={on}
            onClick={() => onChange(o.value)}
            className={cx('relative rounded-full px-3 py-1.5 text-sm transition-colors', on ? 'text-on-accent' : 'text-mute hover:text-ink')}
          >
            {on && (
              <motion.span layoutId={`seg-${id}`} className="absolute inset-0 rounded-full bg-accent"
                transition={{ type: 'spring', stiffness: 500, damping: 38 }} />
            )}
            <span className="relative">{o.label}</span>
          </button>
        );
      })}
    </div>
  );
}

export function Meter({ level, vertical, className, label }: { level: number; vertical?: boolean; className?: string; label?: string }) {
  const hidden = (1 - clamp(level, 0, 1)) * 100;
  return (
    <div
      className={cx('relative overflow-hidden rounded-full bg-track', vertical ? 'w-1.5' : 'h-1.5', className)}
      role="meter" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round((1 - hidden / 100) * 100)}
    >
      <div
        className="meter-fill absolute inset-0" data-vertical={vertical || undefined}
        style={{ clipPath: vertical ? `inset(${hidden}% 0 0 0)` : `inset(0 ${hidden}% 0 0)` }}
      />
    </div>
  );
}

/** Meter bound to the simulated level of a mixer channel. */
export function LiveMeter({ channel, vertical, className }: { channel: ChannelId; vertical?: boolean; className?: string }) {
  const { levels } = useTick();
  return <Meter level={levels[channel]} vertical={vertical} className={className} label={`${channel} level (simulated)`} />;
}

export function Modal({ open, onClose, title, children, width = 540 }: { open: boolean; onClose: () => void; title: string; children: ReactNode; width?: number }) {
  useEffect(() => {
    if (!open) return;
    const h = (e: globalThis.KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [open, onClose]);

  return (
    <AnimatePresence>
      {open && (
        <motion.div className="fixed inset-0 z-[80] grid place-items-center p-4" initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}>
          <div className="absolute inset-0 bg-black/60" onClick={onClose} />
          <motion.div
            role="dialog" aria-modal="true" aria-label={title}
            initial={{ y: 16, scale: 0.98, opacity: 0 }} animate={{ y: 0, scale: 1, opacity: 1 }} exit={{ y: 10, scale: 0.98, opacity: 0 }}
            transition={{ type: 'spring', stiffness: 420, damping: 34 }}
            className="relative max-h-[90vh] w-full overflow-auto rounded-[22px] border border-line bg-raised p-6 shadow-pop"
            style={{ maxWidth: width }}
          >
            <div className="mb-5 flex items-center justify-between gap-4">
              <h2 className="text-lg font-semibold">{title}</h2>
              <IconButton icon={X} label="Close" size="sm" onClick={onClose} className="bg-hover" />
            </div>
            {children}
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
}

export function SimBadge({ children = 'Simulated', className }: { children?: ReactNode; className?: string }) {
  return (
    <span className={cx('inline-flex shrink-0 items-center gap-1 rounded-full border border-amber/30 bg-amber/10 px-2 py-0.5 text-[11px] font-medium text-amber', className)}>
      {children}
    </span>
  );
}

export function SectionHeader({ icon: Icon, title, right, className }: { icon: LucideIcon; title: ReactNode; right?: ReactNode; className?: string }) {
  return (
    <div className={cx('flex flex-wrap items-center gap-3', className)}>
      <Icon size={22} className="text-amber" strokeWidth={2.1} />
      <h2 className="text-lg font-semibold tracking-tight">{title}</h2>
      <div className="ml-auto flex items-center gap-2">{right}</div>
    </div>
  );
}

export function Field({ label, hint, children, className }: { label: string; hint?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <div className={cx('flex flex-col gap-1.5', className)}>
      <span className="text-sm font-medium text-ink/90">{label}</span>
      {children}
      {hint && <span className="text-xs text-mute">{hint}</span>}
    </div>
  );
}

/** Settings row: label + description on the left, control on the right. */
export function Row({ title, desc, children }: { title: ReactNode; desc?: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-center gap-4 border-b border-line py-3.5 last:border-b-0">
      <div className="min-w-0 flex-1">
        <div className="text-[15px]">{title}</div>
        {desc && <div className="text-sm text-mute">{desc}</div>}
      </div>
      {children}
    </div>
  );
}

export function PageTitle({ title, desc, right }: { title: string; desc?: ReactNode; right?: ReactNode }) {
  return (
    <div className="mb-6 flex flex-wrap items-end gap-4">
      <div className="min-w-0 flex-1">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {desc && <p className="mt-1 text-sm text-mute">{desc}</p>}
      </div>
      {right}
    </div>
  );
}
