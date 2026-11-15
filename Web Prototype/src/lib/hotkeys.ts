import type { AppData } from '../types';

/** Set while a HotkeyInput is listening, so global shortcuts don't fire during capture. */
export const captureState = { active: false };

const MODIFIER_KEYS = new Set(['Control', 'Shift', 'Alt', 'Meta', 'OS', 'AltGraph']);

const CODE_NAMES: Record<string, string> = {
  Space: 'Space', Enter: 'Enter', Tab: 'Tab', Escape: 'Esc', Backspace: 'Backspace', CapsLock: 'CapsLock',
  Backquote: '`', Minus: '-', Equal: '=', BracketLeft: '[', BracketRight: ']', Backslash: '\\',
  Semicolon: ';', Quote: "'", Comma: ',', Period: '.', Slash: '/',
  ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
  Insert: 'Insert', Delete: 'Delete', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  NumpadAdd: 'NumPlus', NumpadSubtract: 'NumMinus', NumpadMultiply: 'NumMul', NumpadDivide: 'NumDiv',
  NumpadDecimal: 'NumDot', NumpadEnter: 'NumEnter', Pause: 'Pause', ScrollLock: 'ScrollLock',
};

/** Layout-independent name for the non-modifier key of an event. */
export function keyName(e: KeyboardEvent): string | null {
  if (MODIFIER_KEYS.has(e.key)) return null;
  const c = e.code;
  if (/^Digit\d$/.test(c)) return c.slice(5);
  if (/^Key[A-Z]$/.test(c)) return c.slice(3);
  if (/^Numpad\d$/.test(c)) return 'Num' + c.slice(6);
  if (/^F\d{1,2}$/.test(c)) return c;
  return CODE_NAMES[c] ?? (e.key.length === 1 ? e.key.toUpperCase() : c || null);
}

export function comboFromEvent(e: KeyboardEvent): string | null {
  const k = keyName(e);
  if (!k) return null;
  const parts: string[] = [];
  if (e.ctrlKey) parts.push('Ctrl');
  if (e.altKey) parts.push('Alt');
  if (e.shiftKey) parts.push('Shift');
  if (e.metaKey) parts.push('Win');
  parts.push(k);
  return parts.join('+');
}

export const mainKey = (combo: string) => combo.split('+').pop() ?? combo;

const RESERVED: Record<string, string> = {
  'Alt+F4': 'Windows uses Alt+F4 to close the active window',
  'Alt+Tab': 'Windows uses Alt+Tab for the window switcher',
  'Alt+Esc': 'Windows uses Alt+Esc to cycle windows',
  'Ctrl+Esc': 'Windows uses Ctrl+Esc to open Start',
  'Ctrl+Alt+Delete': 'Reserved by Windows security',
  'Ctrl+Shift+Esc': 'Windows uses this for Task Manager',
};

/** Returns a reason when Windows reserves the combination (the native app could not register it). */
export function reservedReason(combo: string): string | null {
  if (RESERVED[combo]) return RESERVED[combo];
  if (combo.split('+').includes('Win')) return 'Windows reserves most Win-key shortcuts';
  return null;
}

export interface HotkeyOwner { key: string; label: string; combo: string }

/** Every place a shortcut can be bound, keyed so an owner can exclude itself. */
export function hotkeyOwners(d: AppData): HotkeyOwner[] {
  const out: HotkeyOwner[] = [];
  for (const s of d.sounds) if (s.hotkey) out.push({ key: `sound:${s.id}`, label: s.name, combo: s.hotkey });
  const g = d.globalHotkeys;
  if (g.stopAll) out.push({ key: 'global:stopAll', label: 'Stop all sounds', combo: g.stopAll });
  if (g.muteMic) out.push({ key: 'global:muteMic', label: 'Mute microphone', combo: g.muteMic });
  if (g.toggleMini) out.push({ key: 'global:toggleMini', label: 'Toggle mini-player', combo: g.toggleMini });
  const ptt = d.processing.pushToTalk;
  if (ptt.key) out.push({ key: 'global:ptt', label: 'Push-to-talk', combo: ptt.key });
  return out;
}

/** Error message if `combo` can't be assigned to `ownerKey`, otherwise null. */
export function validateHotkey(d: AppData, combo: string, ownerKey: string): string | null {
  const reserved = reservedReason(combo);
  if (reserved) return `${combo} can't be used: ${reserved}.`;
  const clash = hotkeyOwners(d).find((o) => o.combo === combo && o.key !== ownerKey);
  if (clash) return `${combo} is already used by “${clash.label}”.`;
  return null;
}

/** Combos bound more than once (possible after importing data). */
export function duplicateCombos(d: AppData): Set<string> {
  const seen = new Set<string>();
  const dup = new Set<string>();
  for (const o of hotkeyOwners(d)) (seen.has(o.combo) ? dup : seen).add(o.combo);
  return dup;
}
