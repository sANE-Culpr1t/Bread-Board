import { ChartNoAxesColumn, LayoutGrid, Mic, MicOff, Volume2, VolumeX } from 'lucide-react';
import type { ChannelId } from '../types';
import { useApp } from '../state/AppStore';
import { patchChannel } from '../lib/util';
import { Button, IconButton, LiveMeter, SectionHeader, Slider } from './ui';

const ROWS: { id: ChannelId; label: string; icon: typeof Mic }[] = [
  { id: 'mic', label: 'Microphone', icon: Mic },
  { id: 'soundboard', label: 'Soundboard', icon: LayoutGrid },
];

export function QuickMixer({ onOpenMixer }: { onOpenMixer: () => void }) {
  const { data, setData } = useApp();
  return (
    <section>
      <SectionHeader
        icon={ChartNoAxesColumn} title="Audio Mixer"
        right={<Button variant="ghost" onClick={onOpenMixer}>Full mixer</Button>}
      />
      <div className="mt-3 flex flex-col gap-1">
        {ROWS.map(({ id, label, icon: Icon }) => {
          const ch = data.channels[id];
          const MuteIcon = id === 'mic' ? (ch.muted ? MicOff : Mic) : ch.muted ? VolumeX : Volume2;
          return (
            <div key={id} className="grid grid-cols-[1.75rem_minmax(0,7rem)_1fr_3.25rem_auto] items-center gap-3 py-1.5 sm:gap-5">
              <Icon size={22} className={ch.muted ? 'text-mute' : 'text-ink/80'} />
              <span className="truncate text-[15px] text-ink/85">{label}</span>
              <div className="flex flex-col gap-1">
                <Slider label={`${label} volume`} value={ch.gain} onChange={(v) => setData((d) => patchChannel(d, id, { gain: v }))} />
                <LiveMeter channel={id} className="mx-[2px] !h-[3px] opacity-80" />
              </div>
              <span className="text-right tabular-nums text-ink/85">{ch.gain}%</span>
              <IconButton
                icon={MuteIcon} active={ch.muted} danger
                label={ch.muted ? `Unmute ${label}` : `Mute ${label}`}
                onClick={() => setData((d) => patchChannel(d, id, { muted: !ch.muted }))}
              />
            </div>
          );
        })}
      </div>
    </section>
  );
}
