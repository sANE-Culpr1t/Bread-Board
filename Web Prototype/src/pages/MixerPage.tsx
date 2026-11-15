import { AudioLines, Globe, Headphones, LayoutGrid, Mic, MicOff, TriangleAlert, Volume2, VolumeX, type LucideIcon } from 'lucide-react';
import type { ChannelId, Processing } from '../types';
import { useApp } from '../state/AppStore';
import { usePlayback } from '../state/Playback';
import { cx, patchChannel, patchProcessing } from '../lib/util';
import { INPUT_DEVICES, MONITOR_DEVICES, OUTPUT_DEVICES, isFeedbackLoop, isVirtualOutput } from '../lib/devices';
import { validateHotkey } from '../lib/hotkeys';
import { useToast } from '../state/Toast';
import { HotkeyInput } from '../components/HotkeyInput';
import { Fader, Field, LiveMeter, PageTitle, SimBadge, Slider, Toggle } from '../components/ui';

const META: Record<ChannelId, { label: string; icon: LucideIcon; desc: string }> = {
  mic: { label: 'Microphone', icon: Mic, desc: 'Physical input' },
  browser: { label: 'Browser', icon: Globe, desc: 'Web Player process' },
  soundboard: { label: 'Soundboard', icon: LayoutGrid, desc: 'Local sounds' },
  master: { label: 'Master', icon: AudioLines, desc: 'To virtual mic' },
};

function Strip({ id }: { id: ChannelId }) {
  const { data, setData } = useApp();
  const pb = usePlayback();
  const ch = data.channels[id];
  const m = META[id];
  const set = (patch: Parameters<typeof patchChannel>[2]) => setData((d) => patchChannel(d, id, patch));

  let status: string;
  if (id === 'mic') status = data.processing.pushToTalk.enabled ? (pb.pttDown ? 'Talking (PTT)' : 'PTT idle') : 'Live';
  else if (id === 'browser') status = pb.browserTrack ? 'Capturing' : 'Idle';
  else if (id === 'soundboard') status = `${pb.instances.length} voice${pb.instances.length === 1 ? '' : 's'}`;
  else status = data.devices.virtualMicEnabled ? 'Output on' : 'Virtual mic off';

  const MuteIcon = id === 'mic' ? (ch.muted ? MicOff : Mic) : ch.muted ? VolumeX : Volume2;

  return (
    <div className={cx('card flex flex-col items-center gap-4 p-5', id === 'master' && 'ring-1 ring-accent/20')}>
      <div className="flex w-full items-center gap-2.5">
        <m.icon size={20} className="text-amber" />
        <div className="min-w-0">
          <div className="font-medium">{m.label}</div>
          <div className="truncate text-xs text-mute">{m.desc}</div>
        </div>
      </div>
      <span className="self-start rounded-full bg-side px-2.5 py-1 text-xs text-ink/75">{status}</span>
      <div className="flex items-stretch gap-4">
        <Fader label={`${m.label} gain`} value={ch.gain} onChange={(v) => set({ gain: v })} height={190} />
        <LiveMeter channel={id} vertical className="!w-2" />
      </div>
      <div className="text-2xl font-semibold tabular-nums">{ch.gain}<span className="text-base text-mute">%</span></div>
      <button
        type="button" aria-pressed={ch.muted} onClick={() => set({ muted: !ch.muted })}
        className={cx('inline-flex h-10 w-full items-center justify-center gap-2 rounded-full text-sm transition-colors', ch.muted ? 'bg-bad/15 text-bad' : 'bg-raised hover:bg-hover')}
      >
        <MuteIcon size={16} /> {ch.muted ? 'Muted' : 'Mute'}
      </button>
      <div className="flex w-full flex-col gap-2 border-t border-line pt-3 text-sm">
        {id !== 'master' && (
          <label className="flex items-center justify-between gap-2">
            <span className="text-ink/80">To virtual mic</span>
            <Toggle small checked={ch.toVirtualMic} label={`Route ${m.label} to virtual mic`} onChange={(v) => set({ toVirtualMic: v })} />
          </label>
        )}
        <label className="flex items-center justify-between gap-2">
          <span className="flex items-center gap-1.5 text-ink/80"><Headphones size={14} /> Monitor</span>
          <Toggle small checked={ch.toMonitor} label={`Monitor ${m.label} in headphones`} onChange={(v) => set({ toMonitor: v })} />
        </label>
      </div>
    </div>
  );
}

function ProcCard<K extends Exclude<keyof Processing, 'pushToTalk'>>({
  k, title, desc, param, min, max, unit,
}: { k: K; title: string; desc: string; param: keyof Processing[K]; min: number; max: number; unit: string }) {
  const { data, setData } = useApp();
  const p = data.processing[k] as Processing[K] & { enabled: boolean };
  const val = p[param] as unknown as number;
  return (
    <div className="card flex flex-col gap-3 p-4">
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">
          <div className="font-medium">{title}</div>
          <div className="text-xs text-mute">{desc}</div>
        </div>
        <Toggle checked={p.enabled} label={title} onChange={(v) => setData((d) => patchProcessing(d, k, { enabled: v } as Partial<Processing[K]>))} />
      </div>
      <div className="flex items-center gap-3">
        <Slider small label={`${title} ${String(param)}`} min={min} max={max} value={val} disabled={!p.enabled}
          onChange={(v) => setData((d) => patchProcessing(d, k, { [param]: v } as Partial<Processing[K]>))} />
        <span className="w-12 text-right text-sm tabular-nums text-mute">{val}{unit}</span>
      </div>
    </div>
  );
}

export function MixerPage() {
  const { data, setData } = useApp();
  const pb = usePlayback();
  const toast = useToast();
  const dev = data.devices;
  const ptt = data.processing.pushToTalk;
  const feedback = isFeedbackLoop(dev.input, dev.output);
  const virtualOk = isVirtualOutput(dev.output);
  const setDev = (patch: Partial<typeof dev>) => setData((d) => ({ ...d, devices: { ...d.devices, ...patch } }));

  return (
    <div className="flex flex-col gap-8">
      <PageTitle title="Mixer" desc="Four channels mixed into one virtual microphone output." right={<SimBadge>Simulated levels</SimBadge>} />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {(['mic', 'browser', 'soundboard', 'master'] as const).map((id) => <Strip key={id} id={id} />)}
      </div>

      <section>
        <h2 className="mb-3 text-lg font-semibold">Processing</h2>
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <ProcCard k="noiseGate" title="Noise gate" desc="Silences the mic below a threshold" param="threshold" min={0} max={50} unit="%" />
          <ProcCard k="compressor" title="Compressor" desc="Evens out loud peaks" param="amount" min={0} max={100} unit="%" />
          <ProcCard k="limiter" title="Limiter" desc="Hard ceiling on the master output" param="ceiling" min={50} max={100} unit="%" />
          <ProcCard k="ducking" title="Voice ducking" desc="Lowers browser audio while you talk" param="amount" min={0} max={100} unit="%" />
        </div>
        <div className="card mt-4 flex flex-wrap items-center gap-4 p-4">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 font-medium">
              Push-to-talk
              {ptt.enabled && <span className={cx('rounded-full px-2 py-0.5 text-xs', pb.pttDown ? 'bg-ok/20 text-ok' : 'bg-side text-mute')}>{pb.pttDown ? 'Talking' : 'Idle'}</span>}
            </div>
            <div className="text-xs text-mute">Mic is only sent while the key is held (works while this tab is focused).</div>
          </div>
          <HotkeyInput
            label="Push-to-talk key" value={ptt.key}
            onChange={(v) => {
              if (v) {
                const err = validateHotkey(data, v, 'global:ptt');
                if (err) return toast(err, 'error');
              }
              setData((d) => patchProcessing(d, 'pushToTalk', { key: v }));
            }}
          />
          <Toggle checked={ptt.enabled} label="Push-to-talk" onChange={(v) => setData((d) => patchProcessing(d, 'pushToTalk', { enabled: v }))} />
        </div>
      </section>

      <section>
        <h2 className="mb-3 flex items-center gap-2 text-lg font-semibold">Devices <SimBadge>Simulated list</SimBadge></h2>
        <div className="card grid gap-4 p-5 md:grid-cols-3">
          <Field label="Microphone input">
            <select className="bb-field" value={dev.input} onChange={(e) => setDev({ input: e.target.value })}>
              {INPUT_DEVICES.map((d) => <option key={d}>{d}</option>)}
            </select>
          </Field>
          <Field label="Virtual mic output" hint={virtualOk ? 'Select “CABLE Output” as the mic in Discord / OBS.' : undefined}>
            <select className="bb-field" value={dev.output} onChange={(e) => setDev({ output: e.target.value })}>
              {OUTPUT_DEVICES.map((d) => <option key={d}>{d}</option>)}
            </select>
          </Field>
          <Field label="Monitor (your headphones)">
            <select className="bb-field" value={dev.monitor} onChange={(e) => setDev({ monitor: e.target.value })}>
              {MONITOR_DEVICES.map((d) => <option key={d}>{d}</option>)}
            </select>
          </Field>
          {(feedback || !virtualOk) && (
            <div className="flex items-start gap-2 rounded-xl bg-bad/10 px-3 py-2.5 text-sm text-bad md:col-span-3">
              <TriangleAlert size={16} className="mt-0.5 shrink-0" />
              {feedback
                ? 'Feedback loop: the input is the virtual cable’s own output. Pick your physical microphone as the input.'
                : 'The output is not a virtual audio device, so apps won’t see Bread Board as a microphone.'}
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
