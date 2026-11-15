import type { SyntheticEvent } from 'react';
import { motion } from 'motion/react';
import { Hand, Pencil, Repeat, Star, ToggleRight } from 'lucide-react';
import type { Sound } from '../types';
import { SOUND_ICONS, TINTS } from '../lib/icons';
import { cx, patchSound } from '../lib/util';
import { useApp } from '../state/AppStore';
import { usePlayback, type Instance } from '../state/Playback';
import { useTick } from '../state/Tick';

const MODE_META = {
  loop: { icon: Repeat, label: 'Loop — press again to stop' },
  hold: { icon: Hand, label: 'Hold — plays while held' },
  toggle: { icon: ToggleRight, label: 'Toggle — press again to stop' },
} as const;

function CardProgress({ inst }: { inst: Instance }) {
  const { now } = useTick();
  const el = Math.max(0, now - inst.start);
  const p = inst.loop ? (el % inst.duration) / inst.duration : Math.min(1, el / inst.duration);
  return (
    <div className="absolute inset-x-5 bottom-3 h-1 overflow-hidden rounded-full bg-track">
      <div className="h-full origin-left rounded-full bg-accent" style={{ transform: `scaleX(${p})` }} />
    </div>
  );
}

export function SoundCard({ sound, onEdit }: { sound: Sound; onEdit: (id: string) => void }) {
  const pb = usePlayback();
  const { setData } = useApp();
  const inst = pb.instances.filter((i) => i.soundId === sound.id);
  const playing = inst.length > 0;
  const Icon = SOUND_ICONS[sound.icon] ?? SOUND_ICONS.volume;
  const tint = TINTS[sound.tint] ?? TINTS.beige;
  const mode = sound.mode !== 'oneshot' ? MODE_META[sound.mode] : null;

  const down = () => pb.trigger(sound.id, 'down');
  const up = () => { if (sound.mode === 'hold') pb.trigger(sound.id, 'up'); };
  const stopEvt = (e: SyntheticEvent) => e.stopPropagation();

  return (
    <motion.div
      layout
      role="button" tabIndex={0}
      aria-pressed={playing}
      aria-label={`${sound.name}${sound.hotkey ? `, shortcut ${sound.hotkey}` : ''}`}
      onPointerDown={(e) => {
        if (e.button !== 0) return;
        e.currentTarget.setPointerCapture(e.pointerId);
        down();
      }}
      onPointerUp={up}
      onPointerCancel={up}
      onKeyDown={(e) => { if ((e.key === 'Enter' || e.key === ' ') && !e.repeat) { e.preventDefault(); down(); } }}
      onKeyUp={(e) => { if (e.key === 'Enter' || e.key === ' ') up(); }}
      onContextMenu={(e) => { e.preventDefault(); onEdit(sound.id); }}
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.94 }}
      whileHover={{ y: -3 }}
      whileTap={{ scale: 0.97 }}
      transition={{ type: 'spring', stiffness: 420, damping: 30 }}
      className={cx(
        'card group relative flex cursor-pointer select-none flex-col items-center justify-center gap-3 px-4 outline-none transition-shadow',
        playing && 'is-playing',
      )}
      style={{ height: 'var(--bb-card-h)' }}
    >
      <button
        type="button"
        aria-label={sound.favorite ? `Remove ${sound.name} from favourites` : `Add ${sound.name} to favourites`}
        title={sound.favorite ? 'Remove from favourites' : 'Add to favourites'}
        onPointerDown={stopEvt}
        onKeyDown={stopEvt}
        onClick={(e) => { e.stopPropagation(); setData((d) => patchSound(d, sound.id, { favorite: !sound.favorite })); }}
        className={cx(
          'absolute left-3 top-3 grid h-7 w-7 place-items-center rounded-lg transition-opacity hover:bg-raised',
          sound.favorite ? 'text-amber opacity-100' : 'text-mute opacity-0 group-hover:opacity-100 focus-visible:opacity-100',
        )}
      >
        <Star size={15} fill={sound.favorite ? 'currentColor' : 'none'} />
      </button>

      {sound.hotkey && (
        <span className="absolute right-3 top-3 max-w-[60%] truncate rounded-lg bg-raised px-2 py-0.5 text-sm font-semibold text-ink/90">
          {sound.hotkey}
        </span>
      )}

      <motion.div
        animate={playing ? { scale: [1, 1.1, 1] } : { scale: 1 }}
        transition={playing ? { duration: 0.65, repeat: Infinity, ease: 'easeInOut' } : { duration: 0.2 }}
      >
        <Icon size={42} strokeWidth={1.6} style={{ color: tint }} />
      </motion.div>
      <div className="max-w-full truncate text-[15px] font-medium">{sound.name}</div>

      {mode && (
        <span className="absolute bottom-3 left-3 text-mute" title={mode.label} aria-label={mode.label}>
          <mode.icon size={14} />
        </span>
      )}
      <button
        type="button" aria-label={`Edit ${sound.name}`} title="Edit sound (or right-click)"
        onPointerDown={stopEvt}
        onKeyDown={stopEvt}
        onClick={(e) => { e.stopPropagation(); onEdit(sound.id); }}
        className="absolute bottom-2 right-2 grid h-7 w-7 place-items-center rounded-lg text-mute opacity-0 transition-opacity hover:bg-raised hover:text-ink focus-visible:opacity-100 group-hover:opacity-100"
      >
        <Pencil size={14} />
      </button>

      {playing && <CardProgress inst={inst[inst.length - 1]} />}
    </motion.div>
  );
}
