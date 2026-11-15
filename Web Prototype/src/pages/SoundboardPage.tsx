import { useMemo, useState, type ReactNode } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import { Clock, Plus, SearchX, Shuffle, Star, Zap } from 'lucide-react';
import type { Sound } from '../types';
import { useApp } from '../state/AppStore';
import { usePlayback } from '../state/Playback';
import { cx } from '../lib/util';
import { SoundCard } from '../components/SoundCard';
import { QuickMixer } from '../components/QuickMixer';
import { Button, IconButton, SectionHeader } from '../components/ui';

function Chip({ on, onClick, children }: { on: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button" onClick={onClick} aria-pressed={on}
      className={cx(
        'inline-flex h-8 shrink-0 items-center gap-1.5 rounded-full px-3.5 text-sm transition-colors',
        on ? 'bg-accent/15 text-accent' : 'bg-card text-ink/70 hover:bg-raised hover:text-ink',
      )}
    >
      {children}
    </button>
  );
}

export function SoundboardPage({
  query, onAdd, onEdit, onOpenMixer,
}: { query: string; onAdd: () => void; onEdit: (id: string) => void; onOpenMixer: () => void }) {
  const { data } = useApp();
  const pb = usePlayback();
  const [filter, setFilter] = useState('all');

  const visible = useMemo(() => {
    let list: Sound[] = data.sounds;
    if (filter === 'favorites') list = list.filter((s) => s.favorite);
    else if (filter === 'recent') list = data.recent.map((id) => list.find((s) => s.id === id)).filter((s): s is Sound => !!s);
    else if (filter.startsWith('cat:')) list = list.filter((s) => s.category === filter.slice(4));
    const q = query.trim().toLowerCase();
    if (q) list = list.filter((s) => s.name.toLowerCase().includes(q) || s.category.toLowerCase().includes(q) || s.hotkey?.toLowerCase() === q);
    return list;
  }, [data.sounds, data.recent, filter, query]);

  const playRandom = () => {
    const pool = visible.filter((s) => s.mode !== 'hold');
    if (pool.length) pb.trigger(pool[Math.floor(Math.random() * pool.length)].id);
  };

  const cols = data.appearance.columns;
  const gridTemplate = cols
    ? `repeat(auto-fill, minmax(max(150px, calc((100% - ${cols - 1} * var(--bb-gap)) / ${cols})), 1fr))`
    : 'repeat(auto-fill, minmax(170px, 1fr))';

  return (
    <div className="flex flex-col gap-8">
      <section>
        <SectionHeader
          icon={Zap}
          title={query ? `Results for “${query}”` : 'Quick Access'}
          right={
            <>
              <IconButton icon={Shuffle} label="Play a random sound from this view" onClick={playRandom} disabled={!visible.some((s) => s.mode !== 'hold')} />
              <Button icon={Plus} onClick={onAdd}>Add Sound</Button>
            </>
          }
        />

        <div className="-mx-1 mt-4 flex gap-2 overflow-x-auto px-1 pb-1">
          <Chip on={filter === 'all'} onClick={() => setFilter('all')}>All</Chip>
          <Chip on={filter === 'favorites'} onClick={() => setFilter('favorites')}><Star size={13} />Favourites</Chip>
          <Chip on={filter === 'recent'} onClick={() => setFilter('recent')}><Clock size={13} />Recent</Chip>
          <span className="mx-1 w-px shrink-0 self-stretch bg-line" />
          {data.categories.map((c) => (
            <Chip key={c} on={filter === `cat:${c}`} onClick={() => setFilter(`cat:${c}`)}>{c}</Chip>
          ))}
        </div>

        <motion.div layout className="mt-4 grid" style={{ gridTemplateColumns: gridTemplate, gap: 'var(--bb-gap)' }}>
          <AnimatePresence mode="popLayout">
            {visible.map((s) => <SoundCard key={s.id} sound={s} onEdit={onEdit} />)}
          </AnimatePresence>
        </motion.div>

        {!visible.length && (
          <div className="card mt-2 flex flex-col items-center gap-3 py-10 text-center">
            <SearchX size={28} className="text-mute" />
            <div className="text-mute">
              {query ? 'No sounds match your search.' : filter === 'favorites' ? 'No favourites yet — hover a sound and tap the star.' : filter === 'recent' ? 'Nothing played yet.' : 'No sounds in this view.'}
            </div>
            <Button icon={Plus} onClick={onAdd}>Add Sound</Button>
          </div>
        )}
      </section>

      <QuickMixer onOpenMixer={onOpenMixer} />
    </div>
  );
}
