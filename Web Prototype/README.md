# Bread Board — Web Prototype (Prompt 0)

Interactive browser prototype used to evaluate and finalise the Bread Board UI before the
native Windows build. **All audio, devices and hotkeys here are simulated.**

## Run it

```bash
cd "Web Prototype"
npm install
npm run dev
```

Open http://localhost:5173. Other scripts: `npm run build` (type-check + production build),
`npm run typecheck`, `npm run preview`.

Stack: React 19 · TypeScript · Vite 7 · Tailwind CSS 4 · Motion (`motion/react`) · Lucide React.

## What's in it

| Area | Notes |
|---|---|
| Soundboard | Grid of sound cards, search, category/favourite/recent filters, random play, add/edit/delete, per-sound volume, mode (one-shot, toggle, loop, hold), hotkey |
| Quick mixer | Mic + Soundboard gain/mute with live (simulated) meters |
| Web Player | Back/forward/reload/home, address & search bar, editable site shortcuts, set home page, simulated pages, separate browser volume/mute |
| Mixer | Four channel strips (Mic, Browser, Soundboard, Master) with faders, meters, mute, routing; noise gate, compressor, limiter, ducking, push-to-talk; device selection with feedback-loop and non-virtual-device warnings |
| Shortcuts | Capture shortcuts for every sound and global action; duplicates and Windows-reserved combos are refused, never silently overridden |
| Settings | Stacking, max voices, delete confirmation, profiles (load/update/save/delete), categories, export/import/reset |
| Appearance Lab | Accent presets + custom colours, radius, surface contrast, text size, card style, density, columns, motion level — applied live and saved |
| Mini-player (Gaming Mode) | Draggable, resizable, opacity, pin-on-top, minimise, favourites/recent/playing lists, mic & master mute, mini mixer, virtual-mic status |
| Window chrome | Minimise / maximise / close-to-tray are simulated inside the page |

Everything persists to `localStorage` (key `breadboard.prototype.v1`).

## Simulated vs real

- **No audio is decoded or played.** Imported files only contribute their name and length.
- Meter levels come from `src/lib/simulation.ts`.
- Hotkeys work only while the tab is focused; the native app will use system-wide `RegisterHotKey`.
- Websites are mock pages; the native app embeds WebView2 and captures its process audio.
- Device lists are fixed placeholders (`src/lib/devices.ts`).
- The logo area is intentionally empty until the final branding phase (Prompt 8).

## Structure

```
src/
  App.tsx              window shell, page switching, providers
  index.css            design tokens (CSS variables mapped into Tailwind)
  types.ts             data model
  lib/                 pure logic: defaults, storage, hotkeys, theme, simulation, profiles, devices
  state/               AppStore (persisted data), Playback (simulated voices), Tick (meters), Toast, global hotkeys
  components/          Sidebar, TopBar, StatusBar, SoundCard, SoundEditor, MiniPlayer, QuickMixer, HotkeyInput, ui primitives
  pages/               Soundboard, WebPlayer, Mixer, Shortcuts, Settings, AppearanceLab
```

## Design tokens (current)

| Token | Value |
|---|---|
| Background | `hsl(30 5% 9%)` deep charcoal |
| Sidebar | `hsl(30 5% 11.5%)` graphite |
| Card | `hsl(30 5% 14%)` matte dark grey |
| Elevated | `hsl(30 5% 17%)` |
| Accent | `#e3c8a4` warm beige |
| Secondary accent | `#d9a35b` muted amber |
| Text / muted | `#f4efe8` / `#8f8a84` |
| Success / danger | `#6fae7a` / `#cf6f5f` |
| Radius | 18 px cards, 14 px nav, full pills |
| Font | Segoe UI Variable (system) |
