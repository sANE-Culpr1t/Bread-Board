# Changelog — Bread Board (Win Version)

## Iteration 3 — App Audio (v0.4.0)

Replaces the original "browser integration" iteration (decision: any app, no Web Player). Pick any running
app — a browser, Spotify, a game — and only that app's sound joins your virtual mic.

### Added
- **App picker** (`Views/Dialogs/AppPickerDialog`): running apps with their icons and friendly names,
  "Playing now" / "Has audio" badges (apps playing sound listed first), search, refresh, "No app",
  keyboard navigation. Warns before capturing voice-chat apps (Discord, Teams, Zoom, …) because their
  audio would send people's voices back to them. Bread Board itself and system audio (audiodg, Voicemeeter)
  are never offered.
- **Live App Audio strip** on the Mixer page: chosen app with icon and status (Capturing / Connecting /
  Waiting for … to start / … closed / error), gain fader 0–200 %, live level meter, Mute, "To virtual mic".
  Monitor stays off on purpose (you already hear the app; a delayed copy would echo) and says why.
  Bottom bar: live "App · <name>" meter.
- **Follows the app** (`AudioHost`): remembered by executable name, so it works after restarts. When the
  app closes, the OS process-exit signal tears the capture down at once; while it isn't running, a cheap
  name check every 3 s notices it starting again and reconnects automatically. Child processes (browser
  audio services) are included. Apps with no window yet are still found.
- `bb tone` CLI helper: a stand-in app that plays a tone into the virtual cable, used by the tests.

### Changed
- The audio engine starts when there are sounds **or** an app is chosen.
- App capture is no longer restarted when the app's window title changes (it's tracked by process).

### Tests (73 passing)
- Rules: self/system audio blocked, voice-chat apps warned, normal apps allowed.
- Real capture (`bb tone` as the app): waits while the app isn't running → captures it when it starts,
  its sound reaches the App channel → app killed: noticed → app restarted: captured again automatically →
  mute silences it → choosing no app stops capture.
- Enumeration: a playing app is listed with its audio session, "playing" state and executable path;
  found by name with or without ".exe"; Bread Board's own process is never listed.
- Manual (UI Automation on the real app): Mixer → Choose app → picker lists the helper as "Playing now" →
  "Use bb" → strip shows Capturing and the bottom meter moves; selection saved; closing the helper shows
  "bb closed"; restarting it shows Capturing again.

### Performance (Release, 3 runs, 10 s idle, app chosen, no sounds)
| Metric | Capturing an app | Chosen app closed (waiting) |
|---|---|---|
| First frame | 741–861 ms | 728–761 ms |
| Idle CPU | 0.11–0.23 % | 0.07–0.19 % |
| Working set | ~124–125 MB | ~119–120 MB |

### Known issues
- Same as Iteration 2: without a virtual cable nothing is sent anywhere (device choice: Iteration 4).
- Apps that play through a separate helper process that isn't their child (rare) need that helper chosen.
- The microphone isn't in the mix yet — the virtual mic carries the soundboard and the app (Iteration 4).

## Iteration 2 — Soundboard and Global Hotkeys (v0.3.0)

### Added
- **Sound library** (`Core/Sounds/SoundLibrary`): import WAV/MP3/OGG (copied into the library so moving the
  original doesn't break a pad), add from a Myinstants page or direct link, edit, remove (deletes only the
  library copy, never files elsewhere), favourites, recently played (newest first, 12 kept), categories
  (pick or type a new one), relocate a missing file.
- **Soundboard page**: real pads with name, shortcut, duration, mode, live progress, favourite and problem
  badges; click / Enter / Space to play, hold-mode sounds play while the mouse or key is held; right-click
  menu (Play/Stop, Edit, Favourite, Show file, Locate file, Remove with confirmation); drag-and-drop files to
  import; filters All / Favourites / Recent / per category / Missing files; Stop all button; search.
- **Add / Edit sound dialog**: from file or from website, name, category, play mode (one-shot, toggle, loop,
  hold) with explanation, shortcut recorder, per-sound volume, icon and colour, favourite.
- **Global shortcuts** registered at startup (RegisterHotKey, no polling): every sound's shortcut plus
  Stop all and Toggle mini-player. Duplicates and reserved combos (Alt+F4, Win+…) are refused in the editor;
  combos another program already owns are reported on the pad, on the Shortcuts page and in a message.
  Shortcuts pause while you record a new one. Mute mic and push-to-talk are listed for Iteration 4.
- **Shortcuts page**: every shortcut, whether Windows accepted it, and Set/Change.
- **Playback engine wiring** (`Services/SoundboardController`): the audio engine starts only once the
  library has sounds; sounds go to the virtual cable and, optionally, your default speakers ("Hear sounds on
  this PC"). Clips are decoded once in the background. Mic and app capture stay off until Iterations 3–4.
- **Settings → Playback**: hear on this PC, allow stacking, maximum simultaneous sounds, output status.
- **Mini-player**: Previous/Next step through favourites (or all sounds), Play/Stop the selected sound with
  its progress, Stop all, live engine dot.
- **Main window**: live Soundboard meter, engine status in the bottom bar and Virtual Mic card, message toasts.

### Missing and broken files
- A sound whose file is gone shows as dimmed with a warning, is listed under "Missing files", and can't be
  triggered (a message explains why); right-click → Locate file… fixes it. Files that can't be decoded are
  rejected at import instead of leaving a broken pad.

### Tests (60 passing)
- Library: import/copy/no-overwrite, unsupported and missing files, edit normalisation, remove (library copy
  only), recent ordering and cap, favourites, missing detection + relocate, persistence across restart.
- Playback: decode to 48 kHz stereo, unsupported format, simultaneous sounds mix, 500 rapid triggers stay
  within the voice limit, stop all / stop one, progress, live mode and volume changes, unknown sound ignored.
- Hotkeys: key-name round trip, reserved combos, conflict with another program and release, a real key press
  delivered system-wide (works while another window has focus, i.e. while minimised) including hold release.
- App launch: imports a generated sound, assigns a shortcut, plays it through the real engine, sees progress,
  stops it, removes it.
- Manual (UI Automation on the real app): Add Sound dialog opens; shortcut recorder captures Ctrl+Shift+K from
  real key presses; Alt+F4 is refused with a message and doesn't close the dialog; a Myinstants link is
  downloaded and named automatically; the new sound's global shortcut plays it; settings are saved.

### Performance (Release, 3 runs, 10 s idle)
| Metric | Empty library | 9 sounds (engine running) |
|---|---|---|
| First frame | 729–805 ms | 766–1283 ms |
| Idle CPU | 0.01–0.04 % | 0.13–0.37 % |
| Working set | ~114–116 MB | ~144–150 MB |

### Known issues
- Without a virtual cable nothing plays, not even on your speakers (the cable output drives the mix).
  Iteration 4 adds device choice and monitor-only playback.
- On this PC, Ctrl+Alt+Shift+F17–F21 are owned by another program; Bread Board reports this correctly.
- Sounds longer than 3 minutes are cut at 3 minutes (memory cap); not yet shown in the UI.

## Iteration 1 — Main UI and Navigation (v0.2.0)

Decision: no Web Player. "Browser audio" became **App Audio** — capture any running app's window
(Brave, Spotify, …) and mix it with the mic. Browser/WebView2 work is dropped from the plan.

### Added
- **Design system** (`Theme/Tokens.xaml`, `Theme/Controls.xaml`): matte charcoal surfaces, beige/amber
  accents, 12–16 px radii; styles for primary/soft/ghost/icon/caption buttons, toggle switch, sliders
  (horizontal + vertical faders), level meters, combo boxes, text fields, thin scrollbars, tooltips,
  keyboard focus ring. Icons: Segoe Fluent Icons (built into Windows, no icon package).
- **Main window**: custom title bar with search and caption buttons, Windows 11 rounded corners,
  dark frame, correct maximize bounds; sidebar with reserved logo area, navigation, Gaming Mode button
  and Virtual Mic status card; bottom audio section with Microphone / App audio / Soundboard / Master
  meters and engine status.
- **Responsive layout**: sidebar collapses to an icon rail below 1080 px; sound grid reflows
  (`Controls/AdaptiveGrid`).
- **Soundboard page**: pads show name, shortcut, duration, mode/playback status, progress and favourite;
  10 clearly labelled sample pads; working search (from the title bar) and category/favourite filters;
  keyboard navigation between pads.
- **Mixer page**: four channel strips (gain fader, meter, value, mute, routing), App Audio source picker,
  processing cards, device pickers — disabled with "Iteration N" notes.
- **Shortcuts page** and **Settings page** (Gaming Mode preferences work now; playback, profiles
  shown as pending; data-folder "Open" buttons work).
- **Floating mini-player** (`Views/MiniPlayerWindow`): drag by header, resize from edges,
  always-on-top toggle, collapse, close; position/size/pin remembered; transport and mute buttons
  disabled until audio is connected.
- `--screenshots DIR` renders every page (wide + compact) and the mini-player to PNG.

### Honesty rules applied
- Nothing pretends to work: audio-dependent controls are disabled and say which iteration enables them;
  meters read zero with "Audio engine offline".

### Tests
- 36 passing; app smoke test now visits 4 pages, opens the mini-player (checks always-on-top) and
  closes it; persistence across restart re-verified.
- Visual review of all screenshots at 1440×900 and 960×720.

### Performance (Release, 3 runs, 10 s idle)
| Metric | Iteration 0 | Iteration 1 |
|---|---|---|
| First frame | 825–1032 ms | 944–1170 ms |
| Idle CPU | 0.02–0.16 % | 0.03–0.08 % (one 0.8 % outlier) |
| Working set | ~100–111 MB | ~121–125 MB |

### Known issues
- Card drop shadows add ~20 MB; consider cheaper shadows in Iteration 6.
- Sample pads are static; they are replaced by the real library in Iteration 2.

## Iteration 0 — Foundation and Performance (v0.2.0)

### Added
- **BreadBoard.App** (WPF, .NET 10): application shell with sidebar navigation (Soundboard, Web Player,
  Mixer, Shortcuts, Settings), reserved logo area, dark title bar, per-monitor DPI.
- **Lifecycle** (`Lifecycle/AppHost`): composition root; audio engine and hotkey thread are created lazily
  on first use; ordered, idempotent shutdown with a 3 s guard on audio disposal.
- **Single instance** (`Platform/SingleInstance`): one instance per data folder; a second launch brings
  the first to the front (OS wait, no polling).
- **Crash handling**: UI, AppDomain and unobserved-task exceptions are logged; settings are flushed.
- **ConfigService**: live settings with debounced save (one-shot timer) and immediate flush on exit;
  window placement and last page persist across restarts.
- **Structured logging**: JSON-lines (`t, lvl, cat, msg, thread, ex`), category = source file,
  non-blocking writer, 7-day retention.
- **Diagnostics**: `--smoke-test` (visit all pages, clean exit, result file) and `--perf N`
  (startup + idle CPU/RAM report). `--data-dir` isolates all data for tests.
- **BreadBoard.Platform** project; global hotkey service moved here from the audio project.

### Changed
- Pages are created on first visit only. WebView2 is not referenced yet (Iteration 3).
- Noise-gate default 1 %; mic gain may go to 200 % (from the Discord test).

### Performance baseline (Release, ReadyToRun, this PC, 3 runs, 10 s idle)
| Metric | Result |
|---|---|
| Process start → first frame | 825–1032 ms |
| Idle CPU | 0.02–0.16 % |
| Working set / private | ~100–111 MB / ~68–73 MB |
| Managed heap | 1.6 MB |
| Threads | 21 |
| Publish size (framework-dependent) | 28 MB |

### Tests
- 36 automated tests passing (`dotnet test`), including two real launches of the app:
  clean exit code 0, all pages visited, no errors logged, audio/hotkeys/WebView2 not loaded at startup,
  last page restored on the second launch.
- `bb selftest` still passes (virtual cable, process loopback, virtual-mic off = silence).

### Known issues
- Working set is typical for WPF but higher than the target "minimal"; revisit in Iteration 6
  (trimming resources, avoiding unused assemblies).
- First launch after a build is slower (~2.3 s, Debug, cold disk cache).
