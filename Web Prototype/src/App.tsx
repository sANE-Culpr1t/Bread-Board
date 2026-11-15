import { useCallback, useEffect, useState } from 'react';
import { AnimatePresence, MotionConfig, motion } from 'motion/react';
import { AppStoreProvider, useApp } from './state/AppStore';
import { PlaybackProvider } from './state/Playback';
import { TickProvider } from './state/Tick';
import { ToastProvider } from './state/Toast';
import { useGlobalHotkeys } from './state/useGlobalHotkeys';
import { applyAppearance } from './lib/theme';
import { cx } from './lib/util';
import { Sidebar } from './components/Sidebar';
import { TopBar } from './components/TopBar';
import { StatusBar } from './components/StatusBar';
import { MiniPlayer } from './components/MiniPlayer';
import { SoundEditor, type EditorState } from './components/SoundEditor';
import { SoundboardPage } from './pages/SoundboardPage';
import { MixerPage } from './pages/MixerPage';
import { ShortcutsPage } from './pages/ShortcutsPage';
import { SettingsPage } from './pages/SettingsPage';
import { AppearanceLabPage } from './pages/AppearanceLabPage';

type WindowState = 'normal' | 'max' | 'min' | 'tray';

/** Subtle desktop backdrop so the "window" reads as a desktop app. */
function Backdrop() {
  return (
    <svg className="pointer-events-none absolute inset-0 h-full w-full" preserveAspectRatio="none" viewBox="0 0 1600 900" aria-hidden>
      <path d="M0 620 C 380 520 620 760 1000 640 S 1500 520 1600 560 V 900 H 0 Z" fill="rgb(255 255 255 / 0.018)" />
      <path d="M0 200 C 300 120 520 300 860 180 S 1400 40 1600 120 V 0 H 0 Z" fill="rgb(255 255 255 / 0.014)" />
      <path d="M900 900 C 1100 760 1300 820 1600 700 V 900 Z" fill="rgb(255 255 255 / 0.02)" />
    </svg>
  );
}

function Shell() {
  const { data, setData } = useApp();
  const [win, setWin] = useState<WindowState>('normal');
  const [query, setQuery] = useState('');
  const [editor, setEditor] = useState<EditorState>(null);

  useGlobalHotkeys();
  useEffect(() => applyAppearance(data.appearance), [data.appearance]);

  const openMixer = useCallback(() => setData((d) => ({ ...d, page: 'mixer' })), [setData]);
  const onEdit = useCallback((id: string) => setEditor({ mode: 'edit', id }), []);
  const closeEditor = useCallback(() => setEditor(null), []);

  const motionLevel = data.appearance.motion;
  const visible = win === 'normal' || win === 'max';

  const page = (() => {
    switch (data.page) {
      case 'mixer': return <MixerPage />;
      case 'shortcuts': return <ShortcutsPage />;
      case 'settings': return <SettingsPage />;
      case 'appearance': return <AppearanceLabPage onEdit={onEdit} />;
      default: return <SoundboardPage query={query} onAdd={() => setEditor({ mode: 'add' })} onEdit={onEdit} onOpenMixer={openMixer} />;
    }
  })();

  return (
    <MotionConfig
      reducedMotion={motionLevel === 'full' ? 'user' : 'always'}
      transition={motionLevel === 'off' ? { duration: 0 } : undefined}
    >
      <div className="fixed inset-0 overflow-hidden bg-desk">
        <Backdrop />

        <AnimatePresence>
          {visible && (
            <motion.div
              key="window"
              initial={{ opacity: 0, y: 40, scale: 0.97 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              exit={{ opacity: 0, y: 60, scale: 0.94 }}
              transition={{ type: 'spring', stiffness: 260, damping: 30 }}
              className={cx(
                'absolute flex overflow-hidden border border-line bg-base shadow-window',
                win === 'max'
                  ? 'inset-0 rounded-none'
                  : 'inset-0 sm:inset-4 sm:rounded-[28px] lg:inset-6 2xl:inset-y-10 2xl:left-[max(2.5rem,calc((100vw-1480px)/2))] 2xl:right-[max(2.5rem,calc((100vw-1480px)/2))]',
              )}
            >
              <Sidebar />
              <main className="flex min-w-0 flex-1 flex-col">
                <TopBar
                  query={query} setQuery={setQuery} maximized={win === 'max'}
                  onMinimize={() => setWin('min')}
                  onToggleMax={() => setWin((w) => (w === 'max' ? 'normal' : 'max'))}
                  onClose={() => setWin('tray')}
                />
                <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-6 pt-3 lg:px-8">
                  <AnimatePresence mode="wait">
                    <motion.div
                      key={data.page}
                      initial={{ opacity: 0, y: 8 }}
                      animate={{ opacity: 1, y: 0 }}
                      exit={{ opacity: 0, y: -6 }}
                      transition={{ duration: 0.18, ease: 'easeOut' }}
                    >
                      {page}
                    </motion.div>
                  </AnimatePresence>
                </div>
                <StatusBar />
              </main>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Simulated taskbar / tray restore when the window is minimized or closed. */}
        <AnimatePresence>
          {!visible && (
            <motion.button
              type="button"
              initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, y: 20 }}
              onClick={() => setWin('normal')}
              className="absolute bottom-6 left-1/2 flex -translate-x-1/2 items-center gap-3 rounded-full border border-line bg-raised px-5 py-3 text-sm shadow-pop hover:bg-hover"
            >
              <span aria-hidden className="h-5 w-5 rounded-md border border-dashed border-line-strong" />
              {win === 'tray' ? 'Bread Board is running in the tray — click to open' : 'Bread Board (minimized) — click to restore'}
            </motion.button>
          )}
        </AnimatePresence>

        <AnimatePresence>{data.mini.open && <MiniPlayer key="mini" onOpenMixer={() => { openMixer(); setWin((w) => (w === 'max' ? w : 'normal')); }} />}</AnimatePresence>
        <SoundEditor state={editor} onClose={closeEditor} />
      </div>
    </MotionConfig>
  );
}

export default function App() {
  return (
    <AppStoreProvider>
      <ToastProvider>
        <PlaybackProvider>
          <TickProvider>
            <Shell />
          </TickProvider>
        </PlaybackProvider>
      </ToastProvider>
    </AppStoreProvider>
  );
}
