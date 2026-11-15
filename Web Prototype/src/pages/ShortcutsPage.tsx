import { CircleCheck, Info, TriangleAlert, X } from 'lucide-react';
import type { GlobalHotkeys } from '../types';
import { useApp } from '../state/AppStore';
import { useToast } from '../state/Toast';
import { SOUND_ICONS, TINTS } from '../lib/icons';
import { duplicateCombos, validateHotkey } from '../lib/hotkeys';
import { patchProcessing, patchSound } from '../lib/util';
import { HotkeyInput } from '../components/HotkeyInput';
import { IconButton, PageTitle, SimBadge } from '../components/ui';

const MODE_LABEL = { oneshot: 'One-shot', toggle: 'Toggle', loop: 'Loop', hold: 'Hold' } as const;

const GLOBALS: { key: keyof GlobalHotkeys; label: string; desc: string }[] = [
  { key: 'stopAll', label: 'Stop all sounds', desc: 'Silences every playing sound' },
  { key: 'muteMic', label: 'Mute microphone', desc: 'Toggles the mic channel' },
  { key: 'toggleMini', label: 'Toggle mini-player', desc: 'Shows or hides Gaming Mode' },
];

function Status({ combo, dup }: { combo: string | null; dup: Set<string> }) {
  if (!combo) return <span className="text-sm text-mute">Not set</span>;
  if (dup.has(combo)) return <span className="inline-flex items-center gap-1 text-sm text-bad"><TriangleAlert size={14} />Conflict</span>;
  return <span className="inline-flex items-center gap-1 text-sm text-ok"><CircleCheck size={14} />Ready</span>;
}

export function ShortcutsPage() {
  const { data, setData } = useApp();
  const toast = useToast();
  const dup = duplicateCombos(data);

  /** Assign after validation; never silently overrides another binding. */
  const assign = (ownerKey: string, combo: string | null, apply: (c: string | null) => void) => {
    if (combo) {
      const err = validateHotkey(data, combo, ownerKey);
      if (err) return toast(err, 'error');
    }
    apply(combo);
    if (combo) toast(`Shortcut set to ${combo}`, 'success');
  };

  const setGlobal = (k: keyof GlobalHotkeys, v: string | null) =>
    setData((d) => ({ ...d, globalHotkeys: { ...d.globalHotkeys, [k]: v } }));

  return (
    <div className="flex flex-col gap-8">
      <PageTitle title="Shortcuts" desc="Click a shortcut, then press the keys you want. Duplicates and Windows-reserved keys are refused." right={<SimBadge>Tab-focused only</SimBadge>} />

      <div className="flex items-start gap-2.5 rounded-2xl bg-amber/10 px-4 py-3 text-sm text-ink/85">
        <Info size={16} className="mt-0.5 shrink-0 text-amber" />
        In this browser prototype, shortcuts only work while this tab is focused. The native app registers system-wide hotkeys so they work in games and while minimized.
      </div>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Sounds</h2>
        <div className="card divide-y divide-line">
          {data.sounds.map((s) => {
            const Icon = SOUND_ICONS[s.icon] ?? SOUND_ICONS.volume;
            return (
              <div key={s.id} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <Icon size={18} style={{ color: TINTS[s.tint] }} />
                <div className="min-w-0 flex-1">
                  <div className="truncate">{s.name}</div>
                  <div className="text-xs text-mute">{s.category} · {MODE_LABEL[s.mode]}</div>
                </div>
                <Status combo={s.hotkey} dup={dup} />
                <HotkeyInput label={`Shortcut for ${s.name}`} value={s.hotkey} onChange={(v) => assign(`sound:${s.id}`, v, (c) => setData((d) => patchSound(d, s.id, { hotkey: c })))} />
                <IconButton size="sm" icon={X} label={`Clear shortcut for ${s.name}`} disabled={!s.hotkey} onClick={() => setData((d) => patchSound(d, s.id, { hotkey: null }))} />
              </div>
            );
          })}
          {!data.sounds.length && <div className="px-4 py-6 text-center text-sm text-mute">No sounds yet.</div>}
        </div>
      </section>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Global actions</h2>
        <div className="card divide-y divide-line">
          {GLOBALS.map((g) => {
            const v = data.globalHotkeys[g.key];
            return (
              <div key={g.key} className="flex flex-wrap items-center gap-3 px-4 py-3">
                <div className="min-w-0 flex-1">
                  <div>{g.label}</div>
                  <div className="text-xs text-mute">{g.desc}</div>
                </div>
                <Status combo={v} dup={dup} />
                <HotkeyInput label={g.label} value={v} onChange={(c) => assign(`global:${g.key}`, c, (x) => setGlobal(g.key, x))} />
                <IconButton size="sm" icon={X} label={`Clear ${g.label}`} disabled={!v} onClick={() => setGlobal(g.key, null)} />
              </div>
            );
          })}
          <div className="flex flex-wrap items-center gap-3 px-4 py-3">
            <div className="min-w-0 flex-1">
              <div>Push-to-talk {data.processing.pushToTalk.enabled ? '' : <span className="text-xs text-mute">(disabled in Mixer)</span>}</div>
              <div className="text-xs text-mute">Hold to send your microphone</div>
            </div>
            <Status combo={data.processing.pushToTalk.key} dup={dup} />
            <HotkeyInput label="Push-to-talk" value={data.processing.pushToTalk.key} onChange={(c) => assign('global:ptt', c, (x) => setData((d) => patchProcessing(d, 'pushToTalk', { key: x })))} />
            <IconButton size="sm" icon={X} label="Clear push-to-talk key" disabled={!data.processing.pushToTalk.key} onClick={() => setData((d) => patchProcessing(d, 'pushToTalk', { key: null }))} />
          </div>
        </div>
      </section>
    </div>
  );
}
