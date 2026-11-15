import { useRef, useState, type ReactNode } from 'react';
import { Download, FolderPlus, RotateCcw, Save, Trash2, Upload } from 'lucide-react';
import { useApp } from '../state/AppStore';
import { useToast } from '../state/Toast';
import { applySnapshot, makeSnapshot } from '../lib/profiles';
import { clearData, migrate } from '../lib/storage';
import { defaultData } from '../lib/defaults';
import { cx, uid } from '../lib/util';
import { Button, IconButton, Modal, PageTitle, Row, Slider, Toggle } from '../components/ui';

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section>
      <h2 className="mb-3 text-lg font-semibold">{title}</h2>
      <div className="card px-5 py-1">{children}</div>
    </section>
  );
}

export function SettingsPage() {
  const { data, setData, replaceData } = useApp();
  const toast = useToast();
  const [profileName, setProfileName] = useState('');
  const [newCat, setNewCat] = useState('');
  const [confirmReset, setConfirmReset] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);
  const st = data.settings;
  const setSettings = (patch: Partial<typeof st>) => setData((d) => ({ ...d, settings: { ...d.settings, ...patch } }));

  const saveProfile = () => {
    const name = profileName.trim();
    if (!name) return;
    const id = uid();
    setData((d) => ({ ...d, activeProfileId: id, profiles: [...d.profiles, { id, name, updatedAt: Date.now(), snapshot: makeSnapshot(d) }] }));
    setProfileName('');
    toast(`Saved profile “${name}”`, 'success');
  };

  const renameCategory = (oldName: string, next: string) => {
    const name = next.trim();
    if (!name || name === oldName) return;
    if (data.categories.includes(name)) return toast('That category already exists.', 'error');
    setData((d) => ({
      ...d,
      categories: d.categories.map((c) => (c === oldName ? name : c)),
      sounds: d.sounds.map((s) => (s.category === oldName ? { ...s, category: name } : s)),
    }));
  };

  const deleteCategory = (name: string) => {
    if (data.categories.length <= 1) return toast('Keep at least one category.', 'error');
    const fallback = data.categories.find((c) => c !== name)!;
    setData((d) => ({
      ...d,
      categories: d.categories.filter((c) => c !== name),
      sounds: d.sounds.map((s) => (s.category === name ? { ...s, category: fallback } : s)),
    }));
    toast(`Deleted “${name}”; its sounds moved to “${fallback}”`);
  };

  const exportData = () => {
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'bread-board-prototype.json';
    a.click();
    URL.revokeObjectURL(a.href);
  };

  const importData = async (f: File | undefined) => {
    if (!f) return;
    try {
      const parsed = JSON.parse(await f.text());
      if (parsed?.version !== 1) throw new Error('version');
      replaceData(migrate(parsed));
      toast('Data imported', 'success');
    } catch {
      toast('That file is not a Bread Board prototype export.', 'error');
    }
    if (fileRef.current) fileRef.current.value = '';
  };

  return (
    <div className="flex flex-col gap-8">
      <PageTitle title="Settings" desc="Playback behaviour, profiles, library and stored data." />

      <Section title="Playback">
        <Row title="Allow sound stacking" desc="One-shot sounds play over themselves when pressed repeatedly.">
          <Toggle checked={st.allowStacking} label="Allow sound stacking" onChange={(v) => setSettings({ allowStacking: v })} />
        </Row>
        <Row title={`Maximum simultaneous sounds · ${st.maxVoices}`} desc="Oldest sound stops when the limit is reached.">
          <div className="w-44"><Slider label="Maximum simultaneous sounds" min={1} max={16} value={st.maxVoices} onChange={(v) => setSettings({ maxVoices: v })} /></div>
        </Row>
        <Row title="Confirm before deleting sounds">
          <Toggle checked={st.confirmDelete} label="Confirm before deleting sounds" onChange={(v) => setSettings({ confirmDelete: v })} />
        </Row>
      </Section>

      <Section title="Profiles">
        <div className="divide-y divide-line">
          {data.profiles.map((p) => {
            const active = p.id === data.activeProfileId;
            return (
              <div key={p.id} className="flex flex-wrap items-center gap-3 py-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2">
                    {p.name}
                    {active && <span className="rounded-full bg-accent/15 px-2 py-0.5 text-xs text-accent">Active</span>}
                  </div>
                  <div className="text-xs text-mute">Saved {new Date(p.updatedAt).toLocaleString()} · {p.snapshot.sounds.length} sounds</div>
                </div>
                <Button variant={active ? 'ghost' : 'soft'} onClick={() => { setData((d) => applySnapshot(d, p.snapshot, p.id)); toast(`Loaded “${p.name}”`, 'success'); }}>Load</Button>
                <Button variant="ghost" icon={Save} onClick={() => {
                  setData((d) => ({ ...d, activeProfileId: p.id, profiles: d.profiles.map((x) => x.id === p.id ? { ...x, updatedAt: Date.now(), snapshot: makeSnapshot(d) } : x) }));
                  toast(`Updated “${p.name}” with current settings`, 'success');
                }}>Update</Button>
                <IconButton size="sm" icon={Trash2} label={`Delete profile ${p.name}`} onClick={() => setData((d) => ({ ...d, profiles: d.profiles.filter((x) => x.id !== p.id), activeProfileId: d.activeProfileId === p.id ? null : d.activeProfileId }))} />
              </div>
            );
          })}
          <form className="flex gap-2 py-3" onSubmit={(e) => { e.preventDefault(); saveProfile(); }}>
            <input className="bb-field" placeholder="New profile name (saves current setup)" value={profileName} maxLength={32} onChange={(e) => setProfileName(e.target.value)} aria-label="New profile name" />
            <Button type="submit" variant="primary" disabled={!profileName.trim()}>Save as profile</Button>
          </form>
        </div>
      </Section>

      <Section title="Library categories">
        <div className="divide-y divide-line">
          {data.categories.map((c) => (
            <div key={c} className="flex items-center gap-3 py-2.5">
              <input
                className="bb-field !h-9 flex-1" defaultValue={c} aria-label={`Rename category ${c}`}
                onBlur={(e) => { renameCategory(c, e.target.value); e.target.value = c; }}
                onKeyDown={(e) => { if (e.key === 'Enter') (e.target as HTMLInputElement).blur(); }}
              />
              <span className="w-20 text-right text-xs text-mute">{data.sounds.filter((s) => s.category === c).length} sounds</span>
              <IconButton size="sm" icon={Trash2} label={`Delete category ${c}`} onClick={() => deleteCategory(c)} />
            </div>
          ))}
          <form className="flex gap-2 py-3" onSubmit={(e) => {
            e.preventDefault();
            const n = newCat.trim();
            if (!n) return;
            if (data.categories.includes(n)) return toast('That category already exists.', 'error');
            setData((d) => ({ ...d, categories: [...d.categories, n] }));
            setNewCat('');
          }}>
            <input className="bb-field" placeholder="New category" value={newCat} maxLength={24} onChange={(e) => setNewCat(e.target.value)} aria-label="New category name" />
            <Button type="submit" icon={FolderPlus} disabled={!newCat.trim()}>Add</Button>
          </form>
        </div>
      </Section>

      <Section title="Data">
        <Row title="Export" desc="Download all prototype data as JSON.">
          <Button icon={Download} onClick={exportData}>Export</Button>
        </Row>
        <Row title="Import" desc="Replace current data with an exported file.">
          <Button icon={Upload} onClick={() => fileRef.current?.click()}>Import</Button>
          <input ref={fileRef} type="file" accept="application/json,.json" className="sr-only" onChange={(e) => importData(e.target.files?.[0])} />
        </Row>
        <Row title="Reset everything" desc="Restore default sounds, mixer, profiles and appearance.">
          <Button variant="danger" icon={RotateCcw} onClick={() => setConfirmReset(true)}>Reset</Button>
        </Row>
      </Section>

      <Section title="About">
        <Row title="Bread Board — interactive web prototype" desc="Version 0.1.0 · Prompt 0. No real audio, devices or system hotkeys are used.">
          <span className={cx('rounded-full bg-amber/10 px-3 py-1 text-xs text-amber')}>Prototype</span>
        </Row>
      </Section>

      <Modal open={confirmReset} onClose={() => setConfirmReset(false)} title="Reset all data?" width={420}>
        <p className="text-sm text-mute">This deletes your sounds, shortcuts, profiles and appearance settings stored in this browser. It can’t be undone.</p>
        <div className="mt-6 flex justify-end gap-2">
          <Button variant="ghost" onClick={() => setConfirmReset(false)}>Cancel</Button>
          <Button variant="danger" onClick={() => { clearData(); replaceData(defaultData()); setConfirmReset(false); toast('Everything was reset'); }}>Reset everything</Button>
        </div>
      </Modal>
    </div>
  );
}
