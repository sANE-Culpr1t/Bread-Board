# Changelog — Bread Board (Win Version)

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
