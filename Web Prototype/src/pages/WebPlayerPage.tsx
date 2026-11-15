import { useEffect, useRef, useState, type FormEvent } from 'react';
import { motion } from 'motion/react';
import { ArrowLeft, ArrowRight, Globe, House, Info, Play, Plus, RotateCw, Square, Volume2, VolumeX, X } from 'lucide-react';
import { useApp } from '../state/AppStore';
import { usePlayback, type BrowserTrack } from '../state/Playback';
import { useToast } from '../state/Toast';
import { cx, patchChannel, uid } from '../lib/util';
import { IconButton, LiveMeter, PageTitle, SimBadge, Slider } from '../components/ui';

function normalizeUrl(raw: string): string | null {
  const s = raw.trim();
  if (!s) return null;
  if (/^https?:\/\//i.test(s)) {
    try { return new URL(s).toString(); } catch { return null; }
  }
  if (!/\s/.test(s) && /^[\w-]+(\.[\w-]+)+(:\d+)?(\/.*)?$/.test(s)) return `https://${s}`;
  return `https://duckduckgo.com/?q=${encodeURIComponent(s)}`;
}

const hostOf = (url: string) => { try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return url; } };

const INSTANTS = ['Sad Trombone', 'Bruh', 'Wow', 'Ding', 'Crowd Gasp', 'Rimshot', 'Record Scratch', 'Suspense', 'Boing', 'Cricket Chirps', 'Ta-da', 'Oof'];
const VIDEOS = ['Lo-fi beats to queue to', 'Epic trailer music mix', 'Rain ambience · 1 hour', 'Victory fanfare compilation', 'Retro arcade sound pack', 'Calm piano for focus'];

/** Simulated stand-in for a real page. Real sites load in WebView2 in the native app. */
function SimulatedPage({ url, track, onToggle }: { url: string; track: BrowserTrack | null; onToggle: (title: string) => void }) {
  const host = hostOf(url);
  const isOn = (t: string) => track?.title === t && track.site === host;

  if (host.includes('myinstants')) {
    return (
      <div className="grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-4">
        {INSTANTS.map((t) => (
          <button key={t} type="button" onClick={() => onToggle(t)} className="group flex flex-col items-center gap-2 rounded-xl p-2 text-center text-sm hover:bg-raised">
            <span className={cx('grid h-16 w-16 place-items-center rounded-full shadow-[inset_0_-6px_0_rgb(0_0_0/0.25)] transition-transform group-active:scale-95', isOn(t) ? 'bg-amber text-on-accent' : 'bg-[#a8574a] text-white/90')}>
              {isOn(t) ? <Square size={20} fill="currentColor" /> : <Play size={22} fill="currentColor" />}
            </span>
            {t}
          </button>
        ))}
      </div>
    );
  }
  if (host.includes('youtube')) {
    return (
      <div className="grid grid-cols-[repeat(auto-fill,minmax(200px,1fr))] gap-4">
        {VIDEOS.map((t, i) => (
          <button key={t} type="button" onClick={() => onToggle(t)} className="group text-left">
            <div className="relative grid aspect-video place-items-center overflow-hidden rounded-xl" style={{ background: `hsl(${20 + i * 18} 10% ${16 + (i % 3) * 3}%)` }}>
              <span className={cx('grid h-11 w-11 place-items-center rounded-full transition-transform group-hover:scale-110', isOn(t) ? 'bg-accent text-on-accent' : 'bg-black/50 text-white')}>
                {isOn(t) ? <Square size={16} fill="currentColor" /> : <Play size={18} fill="currentColor" />}
              </span>
            </div>
            <div className="mt-2 line-clamp-2 text-sm">{t}</div>
          </button>
        ))}
      </div>
    );
  }
  const t = `Media on ${host}`;
  return (
    <div className="flex flex-col items-center gap-4 py-14 text-center">
      <Globe size={36} className="text-mute" />
      <div className="max-w-sm text-sm text-mute">Simulated page for <span className="text-ink">{host}</span>. In the native app this is a real WebView2 page.</div>
      <button type="button" onClick={() => onToggle(t)} className="inline-flex h-10 items-center gap-2 rounded-full bg-raised px-4 text-sm hover:bg-hover">
        {isOn(t) ? <Square size={14} /> : <Play size={14} />} {isOn(t) ? 'Stop sample media' : 'Play sample media'}
      </button>
    </div>
  );
}

export function WebPlayerPage() {
  const { data, setData } = useApp();
  const pb = usePlayback();
  const toast = useToast();
  const [hist, setHist] = useState({ stack: [data.webHome], i: 0 });
  const url = hist.stack[hist.i];
  const [input, setInput] = useState(url);
  const [loading, setLoading] = useState(0); // incrementing key restarts the bar
  const [adding, setAdding] = useState(false);
  const [newName, setNewName] = useState('');
  const [newUrl, setNewUrl] = useState('');
  const loadTimer = useRef(0);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => setInput(url), [url]);
  useEffect(() => () => window.clearTimeout(loadTimer.current), []);

  const load = () => {
    setLoading((n) => n + 1);
    setIsLoading(true);
    window.clearTimeout(loadTimer.current);
    loadTimer.current = window.setTimeout(() => setIsLoading(false), 500);
  };
  const go = (raw: string) => {
    const u = normalizeUrl(raw);
    if (!u) return;
    setHist((h) => ({ stack: [...h.stack.slice(0, h.i + 1), u], i: h.i + 1 }));
    load();
  };
  const move = (dir: -1 | 1) => { setHist((h) => ({ ...h, i: h.i + dir })); load(); };

  const toggleTrack = (title: string) => {
    const site = hostOf(url);
    pb.setBrowserTrack(pb.browserTrack?.title === title && pb.browserTrack.site === site ? null : { title, site });
  };

  const addShortcut = (e: FormEvent) => {
    e.preventDefault();
    const u = normalizeUrl(newUrl);
    if (!newName.trim() || !u || u.includes('duckduckgo.com/?q=')) return toast('Enter a name and a valid web address.', 'error');
    setData((d) => ({ ...d, webShortcuts: [...d.webShortcuts, { id: uid(), name: newName.trim(), url: u }] }));
    setAdding(false); setNewName(''); setNewUrl('');
  };

  const ch = data.channels.browser;

  return (
    <div className="flex flex-col">
      <PageTitle
        title="Web Player"
        desc="Browse sound sites and route browser audio separately from your mic and soundboard."
        right={<SimBadge>Simulated websites</SimBadge>}
      />

      <div className="card flex flex-col gap-3 p-3">
        <div className="flex items-center gap-1.5">
          <IconButton size="sm" icon={ArrowLeft} label="Back" onClick={() => move(-1)} disabled={hist.i === 0} />
          <IconButton size="sm" icon={ArrowRight} label="Forward" onClick={() => move(1)} disabled={hist.i >= hist.stack.length - 1} />
          <IconButton size="sm" icon={RotateCw} label="Reload" onClick={load} />
          <IconButton size="sm" icon={House} label="Home" onClick={() => go(data.webHome)} />
          <form className="ml-1 flex-1" onSubmit={(e) => { e.preventDefault(); go(input); }}>
            <input
              value={input} onChange={(e) => setInput(e.target.value)} onFocus={(e) => e.target.select()}
              aria-label="Address or search" placeholder="Search or enter address"
              className="h-9 w-full rounded-full bg-side px-4 text-sm outline-none focus:shadow-[0_0_0_2px_color-mix(in_srgb,var(--bb-accent)_40%,transparent)]"
            />
          </form>
        </div>

        <div className="flex flex-wrap items-center gap-1.5">
          {data.webShortcuts.map((s) => (
            <span key={s.id} className="group inline-flex h-8 items-center rounded-full bg-side text-sm">
              <button type="button" onClick={() => go(s.url)} className="pl-3.5 pr-2 hover:text-accent">{s.name}</button>
              <button
                type="button" aria-label={`Remove ${s.name} shortcut`} title="Remove shortcut"
                onClick={() => setData((d) => ({ ...d, webShortcuts: d.webShortcuts.filter((x) => x.id !== s.id) }))}
                className="mr-1 grid h-6 w-6 place-items-center rounded-full text-mute opacity-0 transition-opacity hover:bg-hover hover:text-ink focus-visible:opacity-100 group-hover:opacity-100"
              >
                <X size={12} />
              </button>
            </span>
          ))}
          {adding ? (
            <form onSubmit={addShortcut} className="flex flex-wrap items-center gap-1.5">
              <input autoFocus value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="Name" aria-label="Shortcut name" className="h-8 w-28 rounded-full bg-side px-3 text-sm outline-none" />
              <input value={newUrl} onChange={(e) => setNewUrl(e.target.value)} placeholder="example.com" aria-label="Shortcut address" className="h-8 w-40 rounded-full bg-side px-3 text-sm outline-none" />
              <button type="submit" className="h-8 rounded-full bg-accent px-3 text-sm text-on-accent">Add</button>
              <button type="button" onClick={() => setAdding(false)} className="h-8 rounded-full px-3 text-sm text-mute hover:text-ink">Cancel</button>
            </form>
          ) : (
            <button type="button" onClick={() => setAdding(true)} className="inline-flex h-8 items-center gap-1 rounded-full px-3 text-sm text-mute hover:bg-side hover:text-ink">
              <Plus size={14} /> Shortcut
            </button>
          )}
          <button type="button" onClick={() => { setData((d) => ({ ...d, webHome: url })); toast('Home page updated', 'success'); }} className="ml-auto inline-flex h-8 items-center gap-1 rounded-full px-3 text-xs text-mute hover:bg-side hover:text-ink">
            <House size={13} /> Set as home
          </button>
        </div>
      </div>

      <div className="mt-4 grid gap-4 xl:grid-cols-[1fr_17rem]">
        <div className="card relative min-h-[360px] overflow-hidden p-5">
          {isLoading && (
            <motion.div key={loading} className="absolute left-0 top-0 h-0.5 bg-accent" initial={{ width: '0%' }} animate={{ width: '100%' }} transition={{ duration: 0.5, ease: 'easeOut' }} />
          )}
          <div className="mb-4 flex items-start gap-2 rounded-xl bg-side px-3 py-2.5 text-xs text-mute">
            <Info size={14} className="mt-px shrink-0 text-amber" />
            Websites are simulated in this prototype. The native app embeds a real browser (WebView2) and captures only its process audio.
          </div>
          <div className="mb-3 text-sm text-mute">{hostOf(url)}</div>
          <motion.div key={`${url}-${loading}`} initial={{ opacity: 0, y: 6 }} animate={{ opacity: isLoading ? 0.3 : 1, y: 0 }}>
            <SimulatedPage url={url} track={pb.browserTrack} onToggle={toggleTrack} />
          </motion.div>
        </div>

        <div className="card flex flex-col gap-4 p-5">
          <div className="flex items-center justify-between">
            <div className="font-medium">Browser Audio</div>
            <SimBadge />
          </div>
          <div className="text-xs leading-relaxed text-mute">
            Source: <span className="text-ink/80">msedgewebview2.exe</span><br />Capture: process loopback (this browser only)
          </div>
          <div className="rounded-xl bg-side px-3 py-3">
            <div className="text-xs text-mute">Now playing</div>
            <div className="mt-0.5 flex items-center gap-2">
              <span className="min-w-0 flex-1 truncate text-sm">{pb.browserTrack ? pb.browserTrack.title : 'Nothing'}</span>
              {pb.browserTrack && <IconButton size="xs" icon={Square} label="Stop browser audio" onClick={() => pb.setBrowserTrack(null)} />}
            </div>
            <LiveMeter channel="browser" className="mt-2.5" />
          </div>
          <div>
            <div className="mb-1 flex justify-between text-sm"><span>Volume</span><span className="tabular-nums text-mute">{ch.gain}%</span></div>
            <Slider label="Browser volume" value={ch.gain} onChange={(v) => setData((d) => patchChannel(d, 'browser', { gain: v }))} />
          </div>
          <button
            type="button" aria-pressed={ch.muted}
            onClick={() => setData((d) => patchChannel(d, 'browser', { muted: !ch.muted }))}
            className={cx('inline-flex h-10 items-center justify-center gap-2 rounded-full text-sm transition-colors', ch.muted ? 'bg-bad/15 text-bad' : 'bg-raised hover:bg-hover')}
          >
            {ch.muted ? <VolumeX size={16} /> : <Volume2 size={16} />} {ch.muted ? 'Browser muted' : 'Mute browser'}
          </button>
        </div>
      </div>
    </div>
  );
}
