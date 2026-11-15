import { AudioWaveform, Square } from 'lucide-react';
import { useApp } from '../state/AppStore';
import { usePlayback } from '../state/Playback';
import { Button } from './ui';
import { cx } from '../lib/util';

export function Eq({ on }: { on: boolean }) {
  return (
    <div className={cx('eq', on && 'on')} aria-hidden>
      {[0.5, 0.8, 1, 0.7, 0.45].map((h, i) => <span key={i} style={{ height: `${h * 100}%` }} />)}
    </div>
  );
}

export function StatusBar() {
  const { data } = useApp();
  const pb = usePlayback();
  const voices = pb.instances;
  const names = [...new Set(voices.map((v) => data.sounds.find((s) => s.id === v.soundId)?.name).filter(Boolean))];
  const playing = voices.length > 0;

  let sub = 'Press a key or click a sound.';
  if (playing) sub = data.channels.soundboard.muted
    ? 'Soundboard channel is muted'
    : `${voices.length} voice${voices.length > 1 ? 's' : ''} active`;

  return (
    <div className="card mx-5 mb-5 flex items-center gap-4 rounded-2xl px-5 py-3.5 lg:mx-8">
      <Eq on={playing} />
      <div className="min-w-0 flex-1" aria-live="polite">
        <div className="truncate font-medium">
          {playing ? `Playing: ${names.slice(0, 2).join(', ')}${names.length > 2 ? ` +${names.length - 2}` : ''}` : 'Ready to play!'}
        </div>
        <div className="truncate text-sm text-mute">{sub}</div>
      </div>
      {playing && <Button icon={Square} onClick={pb.stopAll}>Stop all</Button>}
      <div className="hidden items-center gap-2 rounded-full bg-raised px-4 py-2 text-sm text-ink/80 sm:flex" title="Target native pipeline format">
        <AudioWaveform size={16} className="text-mute" />
        48 kHz · 32-bit float
      </div>
    </div>
  );
}
