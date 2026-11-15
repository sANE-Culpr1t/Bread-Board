import { motion } from 'motion/react';
import { Gamepad2, Globe, Heart, Keyboard, LayoutGrid, Palette, Settings, SlidersHorizontal, type LucideIcon } from 'lucide-react';
import type { PageId } from '../types';
import { useApp } from '../state/AppStore';
import { cx, patchMini } from '../lib/util';
import { isFeedbackLoop, isVirtualOutput } from '../lib/devices';
import { Toggle } from './ui';

const NAV: { id: PageId; label: string; icon: LucideIcon }[] = [
  { id: 'soundboard', label: 'Soundboard', icon: LayoutGrid },
  { id: 'web', label: 'Web Player', icon: Globe },
  { id: 'mixer', label: 'Mixer', icon: SlidersHorizontal },
  { id: 'shortcuts', label: 'Shortcuts', icon: Keyboard },
  { id: 'settings', label: 'Settings', icon: Settings },
  { id: 'appearance', label: 'Appearance Lab', icon: Palette },
];

export function Sidebar() {
  const { data, setData } = useApp();
  const dev = data.devices;
  const vm = dev.virtualMicEnabled;
  const healthy = vm && isVirtualOutput(dev.output) && !isFeedbackLoop(dev.input, dev.output);
  const vmText = !vm ? 'Off' : healthy ? 'Active' : isFeedbackLoop(dev.input, dev.output) ? 'Feedback risk' : 'No virtual device';

  return (
    <aside className="flex w-[76px] shrink-0 flex-col bg-side px-3 py-6 lg:w-[248px] lg:px-5">
      {/* Branding area intentionally empty: the final logo arrives in Prompt 8. */}
      <div className="flex flex-col items-center text-center">
        <div aria-hidden title="Reserved for the final logo" className="h-11 w-11 rounded-2xl border border-dashed border-line-strong lg:h-24 lg:w-24" />
        <div className="mt-3 hidden lg:block">
          <div className="text-[1.7rem] font-bold leading-tight tracking-tight text-accent">Bread Board</div>
          <div className="text-sm text-mute">Soundboard</div>
        </div>
      </div>

      <nav className="mt-8 flex flex-col gap-1.5" aria-label="Main">
        {NAV.map((n) => {
          const on = data.page === n.id;
          return (
            <button
              key={n.id} type="button" title={n.label} aria-current={on ? 'page' : undefined}
              onClick={() => setData((d) => ({ ...d, page: n.id }))}
              className={cx(
                'relative flex h-12 items-center justify-center gap-4 rounded-[14px] px-3.5 text-[15px] transition-colors lg:justify-start',
                on ? 'font-medium text-on-accent' : 'text-ink/75 hover:bg-raised hover:text-ink',
              )}
            >
              {on && (
                <motion.span
                  layoutId="nav-pill"
                  className="absolute inset-0 rounded-[14px] bg-accent shadow-[0_8px_22px_-10px_var(--bb-accent)]"
                  transition={{ type: 'spring', stiffness: 450, damping: 36 }}
                />
              )}
              <n.icon size={21} strokeWidth={1.9} className="relative shrink-0" />
              <span className="relative hidden lg:inline">{n.label}</span>
            </button>
          );
        })}
      </nav>

      <div className="mt-auto flex flex-col gap-3 pt-6">
        <button
          type="button" title="Gaming Mode: floating mini-player"
          aria-pressed={data.mini.open}
          onClick={() => setData((d) => patchMini(d, { open: !d.mini.open, collapsed: false }))}
          className={cx(
            'flex h-11 items-center justify-center gap-3 rounded-[14px] border text-sm transition-colors lg:justify-start lg:px-4',
            data.mini.open ? 'border-accent/40 bg-accent/10 text-accent' : 'border-line-strong text-ink/80 hover:bg-raised',
          )}
        >
          <Gamepad2 size={19} />
          <span className="hidden lg:inline">Gaming Mode</span>
          <span className="ml-auto hidden text-xs text-mute lg:inline">{data.mini.open ? 'On' : 'Off'}</span>
        </button>

        <div className="card flex items-center justify-center gap-2 rounded-2xl px-2 py-3 lg:justify-between lg:px-4">
          <div className="hidden min-w-0 lg:block" title={vm ? dev.output : 'Virtual microphone disabled'}>
            <div className="flex items-center gap-2 text-sm font-medium">
              <span className={cx('h-2.5 w-2.5 rounded-full', healthy ? 'bg-ok' : vm ? 'bg-amber' : 'bg-mute')} />
              Virtual Mic
            </div>
            <div className="truncate pl-[18px] text-xs text-mute">{vmText}</div>
          </div>
          <Toggle
            checked={vm} label="Virtual microphone output"
            onChange={(v) => setData((d) => ({ ...d, devices: { ...d.devices, virtualMicEnabled: v } }))}
          />
        </div>

        <div className="hidden items-center justify-center gap-1.5 text-xs text-mute lg:flex">
          Made for gamers <Heart size={12} />
        </div>
      </div>
    </aside>
  );
}
