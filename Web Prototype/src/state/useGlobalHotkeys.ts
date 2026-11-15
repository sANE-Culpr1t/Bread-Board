// SIMULATED global hotkeys: in the browser these only fire while this tab is focused.
// The native app (Prompt 4) registers system-wide hotkeys with RegisterHotKey.
import { useEffect, useRef } from 'react';
import { useApp } from './AppStore';
import { usePlayback } from './Playback';
import { captureState, comboFromEvent, keyName, mainKey } from '../lib/hotkeys';
import { patchChannel, patchMini } from '../lib/util';

const isTyping = (t: EventTarget | null) => {
  if (!(t instanceof HTMLElement)) return false;
  if (t.isContentEditable) return true;
  if (t instanceof HTMLInputElement) return !['range', 'checkbox', 'radio', 'button', 'file'].includes(t.type);
  return t.tagName === 'TEXTAREA' || t.tagName === 'SELECT';
};

export function useGlobalHotkeys() {
  const { data, setData } = useApp();
  const pb = usePlayback();
  const ref = useRef({ data, pb });
  ref.current = { data, pb };

  useEffect(() => {
    const down = (e: KeyboardEvent) => {
      if (captureState.active || e.repeat || isTyping(e.target)) return;
      const combo = comboFromEvent(e);
      if (!combo) return;
      const { data: d, pb: p } = ref.current;
      let handled = false;

      const ptt = d.processing.pushToTalk;
      if (ptt.enabled && ptt.key === combo) { p.setPtt(true); handled = true; }
      const g = d.globalHotkeys;
      if (g.stopAll === combo) { p.stopAll(); handled = true; }
      if (g.muteMic === combo) { setData((x) => patchChannel(x, 'mic', { muted: !x.channels.mic.muted })); handled = true; }
      if (g.toggleMini === combo) { setData((x) => patchMini(x, { open: !x.mini.open })); handled = true; }
      for (const s of d.sounds) {
        if (s.hotkey === combo) { p.trigger(s.id, 'down'); handled = true; }
      }
      if (handled) e.preventDefault();
    };

    const up = (e: KeyboardEvent) => {
      const k = keyName(e);
      if (!k) return;
      const { data: d, pb: p } = ref.current;
      for (const s of d.sounds) if (s.mode === 'hold' && s.hotkey && mainKey(s.hotkey) === k) p.trigger(s.id, 'up');
      const ptt = d.processing.pushToTalk;
      if (ptt.key && mainKey(ptt.key) === k) p.setPtt(false);
    };

    // Releasing focus would otherwise leave held sounds / push-to-talk stuck on.
    const blur = () => {
      const { data: d, pb: p } = ref.current;
      for (const s of d.sounds) if (s.mode === 'hold') p.trigger(s.id, 'up');
      p.setPtt(false);
    };

    window.addEventListener('keydown', down);
    window.addEventListener('keyup', up);
    window.addEventListener('blur', blur);
    return () => {
      window.removeEventListener('keydown', down);
      window.removeEventListener('keyup', up);
      window.removeEventListener('blur', blur);
    };
  }, [setData]);
}
