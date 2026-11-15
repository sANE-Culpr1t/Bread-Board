# Changelog — Bread Board (Win Version)

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
