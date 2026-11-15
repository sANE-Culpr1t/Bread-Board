import type { Appearance } from '../types';

const DENSITY = {
  compact: { gap: 10, cardH: 108 },
  comfortable: { gap: 16, cardH: 136 },
  spacious: { gap: 22, cardH: 160 },
} as const;

function luminance(hex: string) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex);
  if (!m) return 0.6;
  const n = parseInt(m[1], 16);
  const ch = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
    const c = v / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * ch[0] + 0.7152 * ch[1] + 0.0722 * ch[2];
}

/** Writes Appearance Lab values into the CSS variables the whole UI reads. */
export function applyAppearance(a: Appearance) {
  const root = document.documentElement;
  const set = (k: string, v: string) => root.style.setProperty(k, v);
  const base = 9;
  const L = (l: number) => `hsl(30 5% ${(base + (l - base) * a.contrast).toFixed(1)}%)`;

  set('--bb-desktop', `hsl(30 6% ${Math.max(2, base - 3.5 * a.contrast).toFixed(1)}%)`);
  set('--bb-bg', L(9));
  set('--bb-sidebar', L(11.5));
  set('--bb-card', L(14));
  set('--bb-elevated', L(17));
  set('--bb-hover', L(20));
  set('--bb-track', L(23));
  set('--bb-line', `hsl(30 10% 90% / ${(0.06 * a.contrast).toFixed(3)})`);
  set('--bb-line-strong', `hsl(30 10% 90% / ${(0.12 * a.contrast).toFixed(3)})`);
  set('--bb-accent', a.accent);
  set('--bb-accent-2', a.accent2);
  set('--bb-on-accent', luminance(a.accent) > 0.4 ? '#1c1916' : '#f6f1ea');
  set('--bb-radius', `${a.radius}px`);
  const d = DENSITY[a.density];
  set('--bb-gap', `${d.gap}px`);
  set('--bb-card-h', `${d.cardH}px`);
  root.style.fontSize = `${16 * a.fontScale}px`;
  root.dataset.card = a.cardStyle;
  root.dataset.motion = a.motion;
}
