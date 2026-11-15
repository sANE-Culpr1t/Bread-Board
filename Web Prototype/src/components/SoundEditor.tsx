import { useEffect, useState } from 'react';
import { FileAudio, Plus, Trash2 } from 'lucide-react';
import type { PlayMode, Sound } from '../types';
import { SOUND_ICONS, TINTS, type IconKey, type TintKey } from '../lib/icons';
import { validateHotkey } from '../lib/hotkeys';
import { cx, patchSound, removeSound, uid } from '../lib/util';
import { useApp } from '../state/AppStore';
import { useToast } from '../state/Toast';
import { HotkeyInput } from './HotkeyInput';
import { Button, Field, Modal, Segmented, SimBadge, Slider, Toggle } from './ui';

export type EditorState = { mode: 'add' } | { mode: 'edit'; id: string } | null;

const MODES: { value: PlayMode; label: string; hint: string }[] = [
  { value: 'oneshot', label: 'One-shot', hint: 'Every press plays a new copy (stacks).' },
  { value: 'toggle', label: 'Toggle', hint: 'Press to play, press again to stop.' },
  { value: 'loop', label: 'Loop', hint: 'Loops until pressed again.' },
  { value: 'hold', label: 'Hold', hint: 'Plays only while the key or button is held.' },
];

const ACCEPT = ['wav', 'mp3', 'ogg'];

const blank = (category: string): Sound => ({
  id: '', name: '', icon: 'volume', tint: 'beige', category, hotkey: null,
  volume: 85, mode: 'oneshot', favorite: false, durationMs: 1500, fileName: null,
});

/** Reads only the file's duration metadata; no audio is decoded or stored. */
function readDuration(f: File): Promise<number | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(f);
    const a = new Audio();
    let done = false;
    const finish = (v: number | null) => {
      if (done) return;
      done = true;
      URL.revokeObjectURL(url);
      resolve(v);
    };
    a.preload = 'metadata';
    a.onloadedmetadata = () => finish(Number.isFinite(a.duration) && a.duration > 0 ? Math.round(a.duration * 1000) : null);
    a.onerror = () => finish(null);
    window.setTimeout(() => finish(null), 3000);
    a.src = url;
  });
}

export function SoundEditor({ state, onClose }: { state: EditorState; onClose: () => void }) {
  const { data, setData } = useApp();
  const toast = useToast();
  const editing = state?.mode === 'edit' ? data.sounds.find((s) => s.id === state.id) : undefined;
  const [form, setForm] = useState<Sound>(() => blank('General'));
  const [error, setError] = useState<string | null>(null);
  const [confirmDel, setConfirmDel] = useState(false);
  const [newCat, setNewCat] = useState<string | null>(null);

  useEffect(() => {
    if (!state) return;
    setForm(editing ? { ...editing } : blank(data.categories[0] ?? 'General'));
    setError(null);
    setConfirmDel(false);
    setNewCat(null);
    // Re-initialise only when the dialog opens for a different target.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state]);

  const set = (patch: Partial<Sound>) => setForm((f) => ({ ...f, ...patch }));
  const ownerKey = `sound:${editing?.id ?? 'new'}`;
  const hotkeyError = form.hotkey ? validateHotkey(data, form.hotkey, ownerKey) : null;

  const onFile = async (f: File | undefined) => {
    if (!f) return;
    const ext = f.name.split('.').pop()?.toLowerCase() ?? '';
    if (!ACCEPT.includes(ext)) return setError('Only WAV, MP3 and OGG files are supported.');
    setError(null);
    const dur = await readDuration(f);
    setForm((p) => ({
      ...p,
      fileName: f.name,
      name: p.name.trim() ? p.name : f.name.replace(/\.[^.]+$/, '').slice(0, 40),
      durationMs: dur ?? p.durationMs,
    }));
  };

  const save = () => {
    const name = form.name.trim();
    if (!name) return setError('Give the sound a name.');
    if (hotkeyError) return setError(hotkeyError);
    const category = (newCat ?? form.category).trim() || 'General';
    const sound: Sound = { ...form, name, category };
    setData((d) => {
      const categories = d.categories.includes(category) ? d.categories : [...d.categories, category];
      if (editing) return { ...patchSound(d, editing.id, sound), categories };
      return { ...d, categories, sounds: [...d.sounds, { ...sound, id: uid() }] };
    });
    toast(editing ? `Saved “${name}”` : `Added “${name}”`, 'success');
    onClose();
  };

  const del = () => {
    if (!editing) return;
    if (data.settings.confirmDelete && !confirmDel) return setConfirmDel(true);
    setData((d) => removeSound(d, editing.id));
    toast(`Deleted “${editing.name}”`);
    onClose();
  };

  return (
    <Modal open={!!state} onClose={onClose} title={editing ? 'Edit sound' : 'Add sound'} width={600}>
      <div className="flex flex-col gap-5">
        <label
          className="flex cursor-pointer items-center gap-4 rounded-2xl border border-dashed border-line-strong bg-side px-4 py-4 transition-colors hover:border-accent/50"
          onDragOver={(e) => e.preventDefault()}
          onDrop={(e) => { e.preventDefault(); onFile(e.dataTransfer.files[0]); }}
        >
          <FileAudio size={26} className="text-amber" />
          <div className="min-w-0 flex-1">
            <div className="truncate text-sm font-medium">{form.fileName ?? 'Choose or drop a WAV, MP3 or OGG file'}</div>
            <div className="text-xs text-mute">
              Length {(form.durationMs / 1000).toFixed(1)} s{form.fileName ? ' (from file)' : ' (simulated)'} · the prototype keeps only the file name
            </div>
          </div>
          <SimBadge>No audio stored</SimBadge>
          <input type="file" accept=".wav,.mp3,.ogg,audio/wav,audio/mpeg,audio/ogg" className="sr-only" onChange={(e) => onFile(e.target.files?.[0])} />
        </label>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label="Name">
            <input className="bb-field" value={form.name} maxLength={40} onChange={(e) => set({ name: e.target.value })} placeholder="e.g. Airhorn" autoFocus />
          </Field>
          <Field label="Category">
            {newCat === null ? (
              <div className="flex gap-2">
                <select className="bb-field" value={form.category} onChange={(e) => set({ category: e.target.value })}>
                  {[...new Set([...data.categories, form.category])].map((c) => <option key={c}>{c}</option>)}
                </select>
                <button type="button" className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-side text-mute hover:text-ink" title="New category" aria-label="New category" onClick={() => setNewCat('')}>
                  <Plus size={16} />
                </button>
              </div>
            ) : (
              <input className="bb-field" value={newCat} onChange={(e) => setNewCat(e.target.value)} placeholder="New category name" maxLength={24} />
            )}
          </Field>
        </div>

        <Field label="Icon">
          <div className="flex flex-wrap gap-1.5">
            {(Object.keys(SOUND_ICONS) as IconKey[]).map((k) => {
              const I = SOUND_ICONS[k];
              return (
                <button
                  key={k} type="button" aria-label={k} title={k} aria-pressed={form.icon === k}
                  onClick={() => set({ icon: k })}
                  className={cx('grid h-9 w-9 place-items-center rounded-xl transition-colors', form.icon === k ? 'bg-accent text-on-accent' : 'bg-side text-ink/75 hover:bg-hover')}
                >
                  <I size={17} />
                </button>
              );
            })}
          </div>
        </Field>

        <Field label="Tint">
          <div className="flex gap-2">
            {(Object.keys(TINTS) as TintKey[]).map((k) => (
              <button
                key={k} type="button" aria-label={k} title={k} aria-pressed={form.tint === k}
                onClick={() => set({ tint: k })}
                className={cx('h-7 w-7 rounded-full transition-transform', form.tint === k && 'scale-110 ring-2 ring-ink/70 ring-offset-2 ring-offset-[var(--bb-elevated)]')}
                style={{ background: TINTS[k] }}
              />
            ))}
          </div>
        </Field>

        <Field label="Playback mode" hint={MODES.find((m) => m.value === form.mode)?.hint}>
          <Segmented label="Playback mode" value={form.mode} onChange={(v) => set({ mode: v })} options={MODES.map(({ value, label }) => ({ value, label }))} />
        </Field>

        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={`Volume · ${form.volume}%`}>
            <Slider label="Sound volume" value={form.volume} onChange={(v) => set({ volume: v })} />
          </Field>
          <Field label="Shortcut" hint={hotkeyError ? <span className="text-bad">{hotkeyError}</span> : 'Click, then press keys. Backspace clears.'}>
            <HotkeyInput label="Sound shortcut" value={form.hotkey} onChange={(v) => set({ hotkey: v })} className="self-start" />
          </Field>
        </div>

        <div className="flex items-center gap-3">
          <Toggle checked={form.favorite} onChange={(v) => set({ favorite: v })} label="Favourite" />
          <span className="text-sm">Favourite</span>
        </div>

        {error && <div className="rounded-xl bg-bad/10 px-3 py-2 text-sm text-bad">{error}</div>}

        <div className="flex flex-wrap items-center gap-2 border-t border-line pt-5">
          {editing && (
            <Button variant="danger" icon={Trash2} onClick={del}>{confirmDel ? 'Click again to delete' : 'Delete'}</Button>
          )}
          <div className="ml-auto flex gap-2">
            <Button variant="ghost" onClick={onClose}>Cancel</Button>
            <Button variant="primary" onClick={save} disabled={!!hotkeyError}>{editing ? 'Save changes' : 'Add sound'}</Button>
          </div>
        </div>
      </div>
    </Modal>
  );
}
