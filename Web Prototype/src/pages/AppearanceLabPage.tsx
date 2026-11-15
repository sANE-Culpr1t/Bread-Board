import { RotateCcw } from 'lucide-react';
import type { Appearance } from '../types';
import { useApp } from '../state/AppStore';
import { useToast } from '../state/Toast';
import { DEFAULT_APPEARANCE } from '../lib/defaults';
import { cx } from '../lib/util';
import { SoundCard } from '../components/SoundCard';
import { Button, Field, LiveMeter, PageTitle, Segmented, Slider, Toggle } from '../components/ui';

const ACCENTS = [
  { name: 'Warm beige', accent: '#e3c8a4', accent2: '#d9a35b' },
  { name: 'Sand', accent: '#d8c3a5', accent2: '#c99a62' },
  { name: 'Honey', accent: '#e6c08a', accent2: '#d4934a' },
  { name: 'Clay', accent: '#dcb39c', accent2: '#c9805f' },
  { name: 'Oat', accent: '#e8dcc6', accent2: '#c8a774' },
  { name: 'Sage', accent: '#c9d1b4', accent2: '#a9a16a' },
];

export function AppearanceLabPage({ onEdit }: { onEdit: (id: string) => void }) {
  const { data, setData } = useApp();
  const toast = useToast();
  const a = data.appearance;
  const set = (patch: Partial<Appearance>) => setData((d) => ({ ...d, appearance: { ...d.appearance, ...patch } }));

  return (
    <div className="flex flex-col gap-8">
      <PageTitle
        title="Appearance Lab"
        desc="Tune the design live. Every change applies to the whole app and is saved."
        right={<Button icon={RotateCcw} onClick={() => set({ ...DEFAULT_APPEARANCE })}>Reset to approved defaults</Button>}
      />

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <div className="card flex flex-col gap-6 p-6">
          <Field label="Accent">
            <div className="flex flex-wrap gap-2">
              {ACCENTS.map((p) => {
                const on = p.accent === a.accent && p.accent2 === a.accent2;
                return (
                  <button
                    key={p.name} type="button" title={p.name} aria-label={p.name} aria-pressed={on}
                    onClick={() => set({ accent: p.accent, accent2: p.accent2 })}
                    className={cx('flex h-10 items-center gap-1 rounded-full bg-side px-2 transition-shadow', on && 'shadow-[0_0_0_2px_var(--bb-accent)]')}
                  >
                    <span className="h-6 w-6 rounded-full" style={{ background: p.accent }} />
                    <span className="h-6 w-6 rounded-full" style={{ background: p.accent2 }} />
                  </button>
                );
              })}
            </div>
            <div className="mt-2 flex flex-wrap gap-4 text-sm">
              <label className="flex items-center gap-2">Primary <input type="color" value={a.accent} onChange={(e) => set({ accent: e.target.value })} className="h-8 w-10 cursor-pointer rounded border-0 bg-transparent" /></label>
              <label className="flex items-center gap-2">Secondary <input type="color" value={a.accent2} onChange={(e) => set({ accent2: e.target.value })} className="h-8 w-10 cursor-pointer rounded border-0 bg-transparent" /></label>
            </div>
          </Field>

          <Field label={`Corner radius · ${a.radius}px`}>
            <Slider label="Corner radius" min={6} max={28} value={a.radius} onChange={(v) => set({ radius: v })} />
          </Field>
          <Field label={`Surface contrast · ${a.contrast.toFixed(2)}×`} hint="How far cards and panels separate from the background.">
            <Slider label="Surface contrast" min={60} max={160} value={Math.round(a.contrast * 100)} onChange={(v) => set({ contrast: v / 100 })} />
          </Field>
          <Field label={`Text size · ${Math.round(a.fontScale * 100)}%`}>
            <Slider label="Text size" min={90} max={115} value={Math.round(a.fontScale * 100)} onChange={(v) => set({ fontScale: v / 100 })} />
          </Field>
          <Field label="Card style">
            <Segmented label="Card style" value={a.cardStyle} onChange={(v) => set({ cardStyle: v })}
              options={[{ value: 'soft', label: 'Soft matte' }, { value: 'flat', label: 'Flat' }, { value: 'outlined', label: 'Outlined' }]} />
          </Field>
          <Field label="Density">
            <Segmented label="Density" value={a.density} onChange={(v) => set({ density: v })}
              options={[{ value: 'compact', label: 'Compact' }, { value: 'comfortable', label: 'Comfortable' }, { value: 'spacious', label: 'Spacious' }]} />
          </Field>
          <Field label="Soundboard columns">
            <Segmented label="Soundboard columns" value={a.columns} onChange={(v) => set({ columns: v })}
              options={[{ value: 0, label: 'Auto' }, { value: 3, label: '3' }, { value: 4, label: '4' }, { value: 5, label: '5' }, { value: 6, label: '6' }]} />
          </Field>
          <Field label="Motion" hint="“Reduced” keeps fades but drops movement; “Off” removes all animation.">
            <Segmented label="Motion" value={a.motion} onChange={(v) => set({ motion: v })}
              options={[{ value: 'full', label: 'Full' }, { value: 'reduced', label: 'Reduced' }, { value: 'off', label: 'Off' }]} />
          </Field>
        </div>

        <div className="flex flex-col gap-4">
          <div className="text-sm text-mute">Live preview</div>
          <div className="grid grid-cols-2 sm:grid-cols-3" style={{ gap: 'var(--bb-gap)' }}>
            {data.sounds.slice(0, 3).map((s) => <SoundCard key={s.id} sound={s} onEdit={onEdit} />)}
          </div>
          <div className="card flex flex-col gap-4 p-5">
            <div className="flex items-center justify-between">
              <span className="font-medium">Controls</span>
              <Toggle checked={data.devices.virtualMicEnabled} label="Virtual microphone (preview)" onChange={(v) => setData((d) => ({ ...d, devices: { ...d.devices, virtualMicEnabled: v } }))} />
            </div>
            <LiveMeter channel="master" />
            <div className="flex flex-wrap gap-2">
              {(['primary', 'soft', 'ghost', 'danger'] as const).map((v) => (
                <Button key={v} variant={v} onClick={() => toast(`“${v}” button style`)}>{v[0].toUpperCase() + v.slice(1)}</Button>
              ))}
            </div>
            <div className="text-xs text-mute">Style samples — clicking one names its variant.</div>
          </div>
          <div className="card p-5 text-sm">
            <div className="text-[1.6rem] font-bold leading-tight text-accent">Bread Board</div>
            <div className="text-mute">Typography sample · Segoe UI Variable</div>
            <p className="mt-3 text-ink/85">Matte graphite surfaces, warm beige accents, soft elevation. Avoid neon, heavy blur and strong gradients.</p>
          </div>
        </div>
      </div>
    </div>
  );
}
