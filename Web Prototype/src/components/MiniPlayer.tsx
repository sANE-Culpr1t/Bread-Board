import { useEffect, useMemo, useRef, useState, type PointerEvent } from 'react';
import { motion } from 'motion/react';
import {
  ChevronDown, ChevronUp, Contrast, Mic, MicOff, Pin, PinOff, Play, SkipBack, SkipForward,
  SlidersVertical, Square, Volume2, VolumeX, X,
} from 'lucide-react';
import type { MiniTab, Sound } from '../types';
import { SOUND_ICONS, TINTS } from '../lib/icons';
import { clamp, cx, patchChannel, patchMini } from '../lib/util';
import { isVirtualOutput } from '../lib/devices';
import { useApp } from '../state/AppStore';
import { usePlayback } from '../state/Playback';
import { useTick } from '../state/Tick';
import { IconButton, LiveMeter, Segmented, Slider } from './ui';

const MIN_W = 270;
const MIN_H = 330;

function clampPos(x: number, y: number, w: number) {
  return {
    x: clamp(x, 8 - w + 80, window.innerWidth - 80),
    y: clamp(y, 8, window.innerHeight - 48),
  };
}

function SelectedProgress({ soundId }: { soundId: string }) {
  const { now } = useTick();
  const pb = usePlayback();
  const inst = [...pb.instances].reverse().find((i) => i.soundId === soundId);
  let p = 0;
  if (inst) {
    const el = Math.max(0, now - inst.start);
    p = inst.loop ? (el % inst.duration) / inst.duration : Math.min(1, el / inst.duration);
  }
  return (
    <div className="h-1.5 overflow-hidden rounded-full bg-track">
      <div className="h-full origin-left rounded-full bg-accent" style={{ transform: `scaleX(${p})` }} />
    </div>
  );
}

function Chip({ sound }: { sound: Sound }) {
  const pb = usePlayback();
  const playing = pb.instances.some((i) => i.soundId === sound.id);
  const Icon = SOUND_ICONS[sound.icon] ?? SOUND_ICONS.volume;
  return (
    <button
      type="button" title={sound.hotkey ? `${sound.name} (${sound.hotkey})` : sound.name}
      onPointerDown={(e) => { e.currentTarget.setPointerCapture(e.pointerId); pb.trigger(sound.id, 'down'); }}
      onPointerUp={() => sound.mode === 'hold' && pb.trigger(sound.id, 'up')}
      onKeyDown={(e) => { if ((e.key === 'Enter' || e.key === ' ') && !e.repeat) { e.preventDefault(); pb.trigger(sound.id, 'down'); } }}
      onKeyUp={(e) => { if ((e.key === 'Enter' || e.key === ' ') && sound.mode === 'hold') pb.trigger(sound.id, 'up'); }}
      className={cx(
        'flex min-w-0 items-center gap-2 rounded-xl px-2.5 py-2 text-left text-sm transition-colors',
        playing ? 'bg-accent/15 text-accent' : 'bg-card hover:bg-hover',
      )}
    >
      <Icon size={15} style={{ color: playing ? undefined : TINTS[sound.tint] }} className="shrink-0" />
      <span className="truncate">{sound.name}</span>
    </button>
  );
}

export function MiniPlayer({ onOpenMixer }: { onOpenMixer: () => void }) {
  const { data, setData } = useApp();
  const pb = usePlayback();
  const m = data.mini;
  const [pos, setPos] = useState({ x: m.x, y: m.y });
  const [size, setSize] = useState({ w: m.w, h: m.h });
  const [showOpacity, setShowOpacity] = useState(false);
  const drag = useRef<{ dx: number; dy: number } | null>(null);
  const resize = useRef<{ sx: number; sy: number; w: number; h: number } | null>(null);

  // Keep local position in sync with stored state when not interacting.
  useEffect(() => { if (!drag.current) setPos({ x: m.x, y: m.y }); }, [m.x, m.y]);
  useEffect(() => { if (!resize.current) setSize({ w: m.w, h: m.h }); }, [m.w, m.h]);

  // First run: place at the right edge.
  useEffect(() => {
    if (m.x < 0) setData((d) => patchMini(d, { x: Math.max(16, window.innerWidth - d.mini.w - 28), y: 96 }));
  }, [m.x, setData]);

  useEffect(() => {
    const onResize = () => setData((d) => d.mini.x < 0 ? d : patchMini(d, clampPos(d.mini.x, d.mini.y, d.mini.w)));
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [setData]);

  const queue = useMemo(() => {
    const fav = data.sounds.filter((s) => s.favorite);
    return fav.length ? fav : data.sounds;
  }, [data.sounds]);
  const selected = queue.find((s) => s.id === m.selectedId) ?? queue[0];
  const selPlaying = !!selected && pb.instances.some((i) => i.soundId === selected.id);

  const step = (dir: 1 | -1) => {
    if (!queue.length) return;
    const i = Math.max(0, queue.findIndex((s) => s.id === selected?.id));
    const next = queue[(i + dir + queue.length) % queue.length];
    setData((d) => patchMini(d, { selectedId: next.id }));
  };
  const playSelected = () => {
    if (!selected) return;
    if (selPlaying) pb.stop(selected.id);
    else pb.trigger(selected.id, 'down');
  };

  const listFor = (tab: MiniTab): Sound[] => {
    if (tab === 'favorites') return data.sounds.filter((s) => s.favorite);
    if (tab === 'recent') return data.recent.map((id) => data.sounds.find((s) => s.id === id)).filter((s): s is Sound => !!s);
    const ids = [...new Set(pb.instances.map((i) => i.soundId))];
    return ids.map((id) => data.sounds.find((s) => s.id === id)).filter((s): s is Sound => !!s);
  };
  const list = listFor(m.tab);

  const onHeaderDown = (e: PointerEvent<HTMLDivElement>) => {
    if ((e.target as HTMLElement).closest('button')) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    drag.current = { dx: e.clientX - pos.x, dy: e.clientY - pos.y };
  };
  const onHeaderMove = (e: PointerEvent<HTMLDivElement>) => {
    if (drag.current) setPos(clampPos(e.clientX - drag.current.dx, e.clientY - drag.current.dy, size.w));
  };
  const onHeaderUp = () => {
    if (!drag.current) return;
    drag.current = null;
    setData((d) => patchMini(d, pos));
  };

  const onResizeDown = (e: PointerEvent<HTMLDivElement>) => {
    e.stopPropagation();
    e.currentTarget.setPointerCapture(e.pointerId);
    resize.current = { sx: e.clientX, sy: e.clientY, w: size.w, h: size.h };
  };
  const onResizeMove = (e: PointerEvent<HTMLDivElement>) => {
    const r = resize.current;
    if (!r) return;
    setSize({
      w: clamp(r.w + e.clientX - r.sx, MIN_W, 560),
      h: clamp(r.h + e.clientY - r.sy, MIN_H, window.innerHeight - 24),
    });
  };
  const onResizeUp = () => {
    if (!resize.current) return;
    resize.current = null;
    setData((d) => patchMini(d, size));
  };

  if (m.x < 0) return null;

  const mic = data.channels.mic;
  const master = data.channels.master;
  const vmOk = data.devices.virtualMicEnabled && isVirtualOutput(data.devices.output);
  const SelIcon = selected ? SOUND_ICONS[selected.icon] ?? SOUND_ICONS.volume : null;

  return (
    <motion.div
      role="dialog" aria-label="Bread Board mini-player"
      initial={{ opacity: 0, scale: 0.94, y: 12 }}
      animate={{ opacity: m.opacity, scale: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.94, y: 12 }}
      transition={{ type: 'spring', stiffness: 380, damping: 32 }}
      className="fixed flex flex-col overflow-hidden rounded-[22px] border border-line bg-side shadow-pop"
      style={{ left: pos.x, top: pos.y, width: size.w, height: m.collapsed ? undefined : size.h, zIndex: m.pinned ? 70 : 45 }}
    >
      <div
        onPointerDown={onHeaderDown} onPointerMove={onHeaderMove} onPointerUp={onHeaderUp} onPointerCancel={onHeaderUp}
        className="flex shrink-0 cursor-grab touch-none select-none items-center gap-2 border-b border-line px-3.5 py-2.5 active:cursor-grabbing"
      >
        <span aria-hidden title="Reserved for the final logo" className="h-6 w-6 shrink-0 rounded-md border border-dashed border-line-strong" />
        <span className="min-w-0 flex-1 truncate text-[15px] font-medium">Bread Board</span>
        <span
          className={cx('h-2 w-2 shrink-0 rounded-full', vmOk ? 'bg-ok' : 'bg-mute')}
          title={vmOk ? 'Virtual mic active (simulated)' : 'Virtual mic inactive'}
        />
        <IconButton size="xs" icon={m.pinned ? Pin : PinOff} label={m.pinned ? 'Unpin (stop keeping on top)' : 'Pin on top'} active={m.pinned} className="bg-transparent" onClick={() => setData((d) => patchMini(d, { pinned: !d.mini.pinned }))} />
        <IconButton size="xs" icon={m.collapsed ? ChevronDown : ChevronUp} label={m.collapsed ? 'Expand' : 'Minimize'} className="bg-transparent" onClick={() => setData((d) => patchMini(d, { collapsed: !d.mini.collapsed }))} />
        <IconButton size="xs" icon={X} label="Close mini-player" className="bg-transparent" onClick={() => setData((d) => patchMini(d, { open: false }))} />
      </div>

      {m.collapsed ? (
        <div className="flex items-center gap-2 px-3.5 py-2.5">
          <IconButton size="sm" icon={selPlaying ? Square : Play} label={selPlaying ? 'Stop' : 'Play'} onClick={playSelected} disabled={!selected} />
          <span className="min-w-0 flex-1 truncate text-sm">{selected?.name ?? 'No sounds'}</span>
          <IconButton size="sm" icon={mic.muted ? MicOff : Mic} label={mic.muted ? 'Unmute microphone' : 'Mute microphone'} active={mic.muted} danger onClick={() => setData((d) => patchChannel(d, 'mic', { muted: !mic.muted }))} />
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 flex-col gap-3 px-4 pb-4 pt-4">
          <div className="flex items-center justify-center gap-6">
            <IconButton icon={SkipBack} label="Previous sound" className="bg-transparent" onClick={() => step(-1)} disabled={queue.length < 2} />
            <motion.button
              type="button" whileTap={{ scale: 0.92 }} onClick={playSelected} disabled={!selected}
              aria-label={selPlaying ? `Stop ${selected?.name}` : `Play ${selected?.name ?? ''}`}
              className={cx('grid h-16 w-16 place-items-center rounded-full bg-card shadow-[0_10px_24px_-10px_rgb(0_0_0/0.8),inset_0_1px_0_rgb(255_255_255/0.05)]', selPlaying ? 'text-accent' : 'text-ink')}
            >
              {selPlaying ? <Square size={24} fill="currentColor" /> : <Play size={28} fill="currentColor" className="translate-x-0.5" />}
            </motion.button>
            <IconButton icon={SkipForward} label="Next sound" className="bg-transparent" onClick={() => step(1)} disabled={queue.length < 2} />
          </div>

          <div className="flex items-center justify-center gap-2 text-[15px] font-medium">
            {SelIcon && selected && <SelIcon size={16} style={{ color: TINTS[selected.tint] }} />}
            <span className="truncate">{selected?.name ?? 'No sounds yet'}</span>
            {selected?.hotkey && <span className="rounded-md bg-card px-1.5 text-xs text-mute">{selected.hotkey}</span>}
          </div>
          {selected && <SelectedProgress soundId={selected.id} />}

          <div className="flex items-center justify-around">
            <IconButton icon={mic.muted ? MicOff : Mic} label={mic.muted ? 'Unmute microphone' : 'Mute microphone'} active={mic.muted} danger className="bg-transparent" onClick={() => setData((d) => patchChannel(d, 'mic', { muted: !mic.muted }))} />
            <IconButton icon={master.muted ? VolumeX : Volume2} label={master.muted ? 'Unmute master output' : 'Mute master output'} active={master.muted} danger className="bg-transparent" onClick={() => setData((d) => patchChannel(d, 'master', { muted: !master.muted }))} />
            <IconButton icon={SlidersVertical} label="Quick mixer" active={m.showMixer} className="bg-transparent" onClick={() => setData((d) => patchMini(d, { showMixer: !d.mini.showMixer }))} />
            <IconButton icon={Contrast} label="Window opacity" active={showOpacity} className="bg-transparent" onClick={() => setShowOpacity((v) => !v)} />
          </div>

          {showOpacity && (
            <div className="flex items-center gap-3 rounded-xl bg-card px-3 py-2 text-sm">
              <span className="text-mute">Opacity</span>
              <Slider small label="Mini-player opacity" min={30} max={100} value={Math.round(m.opacity * 100)} onChange={(v) => setData((d) => patchMini(d, { opacity: v / 100 }))} />
              <span className="w-9 text-right tabular-nums">{Math.round(m.opacity * 100)}%</span>
            </div>
          )}

          {m.showMixer && (
            <div className="flex flex-col gap-2 rounded-xl bg-card px-3 py-2.5 text-sm">
              {(['mic', 'soundboard', 'master'] as const).map((id) => (
                <div key={id} className="grid grid-cols-[4.5rem_1fr_2.5rem] items-center gap-2">
                  <span className="capitalize text-mute">{id === 'soundboard' ? 'Board' : id}</span>
                  <div className="flex flex-col gap-0.5">
                    <Slider small label={`${id} volume`} value={data.channels[id].gain} onChange={(v) => setData((d) => patchChannel(d, id, { gain: v }))} />
                    <LiveMeter channel={id} className="!h-[3px]" />
                  </div>
                  <span className="text-right tabular-nums">{data.channels[id].gain}%</span>
                </div>
              ))}
              <button type="button" onClick={onOpenMixer} className="self-end text-xs text-accent hover:underline">Open full mixer</button>
            </div>
          )}

          <div className="flex min-h-0 flex-1 flex-col gap-2">
            <Segmented
              label="Mini-player list" value={m.tab} onChange={(v) => setData((d) => patchMini(d, { tab: v }))}
              options={[{ value: 'favorites', label: 'Favourites' }, { value: 'recent', label: 'Recent' }, { value: 'playing', label: 'Playing' }]}
            />
            <div className="grid min-h-0 flex-1 auto-rows-min grid-cols-2 gap-1.5 overflow-y-auto pr-0.5">
              {list.map((s) => <Chip key={s.id} sound={s} />)}
              {!list.length && (
                <div className="col-span-2 py-4 text-center text-sm text-mute">
                  {m.tab === 'favorites' ? 'Star a sound to pin it here.' : m.tab === 'recent' ? 'Nothing played yet.' : 'Nothing playing.'}
                </div>
              )}
            </div>
          </div>
        </div>
      )}

      {!m.collapsed && (
        <div
          role="separator" aria-label="Resize mini-player" title="Drag to resize"
          onPointerDown={onResizeDown} onPointerMove={onResizeMove} onPointerUp={onResizeUp} onPointerCancel={onResizeUp}
          className="absolute bottom-0 right-0 h-5 w-5 cursor-nwse-resize touch-none"
        >
          <svg viewBox="0 0 10 10" className="absolute bottom-1.5 right-1.5 h-2.5 w-2.5 text-mute" aria-hidden>
            <path d="M9 1 1 9M9 5 5 9" stroke="currentColor" strokeWidth="1.2" strokeLinecap="round" />
          </svg>
        </div>
      )}
    </motion.div>
  );
}
