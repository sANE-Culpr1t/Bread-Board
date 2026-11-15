import { Copy, Minus, Search, Square, X } from 'lucide-react';
import { useApp } from '../state/AppStore';
import { useToast } from '../state/Toast';
import { applySnapshot } from '../lib/profiles';
import { SimBadge } from './ui';

export function TopBar({
  query, setQuery, maximized, onMinimize, onToggleMax, onClose,
}: {
  query: string; setQuery: (q: string) => void; maximized: boolean;
  onMinimize: () => void; onToggleMax: () => void; onClose: () => void;
}) {
  const { data, setData } = useApp();
  const toast = useToast();

  const loadProfile = (id: string) => {
    const p = data.profiles.find((x) => x.id === id);
    if (!p) return;
    setData((d) => applySnapshot(d, p.snapshot, p.id));
    toast(`Loaded profile “${p.name}”`, 'success');
  };

  const winBtn = 'grid h-9 w-10 place-items-center rounded-lg text-ink/70 transition-colors hover:bg-raised hover:text-ink';

  return (
    <header className="flex items-center gap-3 px-5 pb-3 pt-5 lg:px-8">
      <label className="flex h-12 max-w-[600px] flex-1 items-center gap-3 rounded-full bg-raised px-5 transition-shadow focus-within:shadow-[0_0_0_2px_color-mix(in_srgb,var(--bb-accent)_45%,transparent)]">
        <Search size={20} className="shrink-0 text-mute" />
        <input
          value={query}
          onChange={(e) => {
            setQuery(e.target.value);
            if (data.page !== 'soundboard') setData((d) => ({ ...d, page: 'soundboard' }));
          }}
          onKeyDown={(e) => { if (e.key === 'Escape') setQuery(''); }}
          placeholder="Search sounds…"
          aria-label="Search sounds"
          className="min-w-0 flex-1 bg-transparent text-[15px] outline-none placeholder:text-mute"
        />
        {query && (
          <button type="button" aria-label="Clear search" onClick={() => setQuery('')} className="text-mute hover:text-ink">
            <X size={16} />
          </button>
        )}
      </label>

      <SimBadge className="hidden md:inline-flex">Prototype · simulated audio</SimBadge>

      <select
        aria-label="Active profile" title="Switch profile"
        value={data.activeProfileId ?? ''}
        onChange={(e) => loadProfile(e.target.value)}
        className="bb-field hidden !h-9 !w-auto !rounded-full text-sm xl:block"
      >
        {!data.activeProfileId && <option value="">No profile</option>}
        {data.profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
      </select>

      <div className="ml-auto flex items-center gap-0.5">
        <button type="button" className={winBtn} aria-label="Minimize" title="Minimize" onClick={onMinimize}><Minus size={18} /></button>
        <button type="button" className={winBtn} aria-label={maximized ? 'Restore' : 'Maximize'} title={maximized ? 'Restore' : 'Maximize'} onClick={onToggleMax}>
          {maximized ? <Copy size={15} /> : <Square size={15} />}
        </button>
        <button type="button" className={`${winBtn} hover:!bg-bad/80`} aria-label="Close to tray" title="Close to tray (simulated)" onClick={onClose}><X size={18} /></button>
      </div>
    </header>
  );
}
