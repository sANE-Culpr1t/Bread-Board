# Bread Board — Win Version

Windows desktop app (C# / .NET 10, WPF). Progress is tracked per iteration in [CHANGELOG.md](CHANGELOG.md).

Run the app:

```bash
dotnet run --project src/BreadBoard.App
```

Release build: `dotnet publish src/BreadBoard.App -c Release -o artifacts/release` → `artifacts/release/BreadBoard.exe`.

App switches (for testing): `--data-dir <folder>` keep all data in one folder · `--smoke-test` · `--perf <seconds>` · `--screenshots <folder>` · `--export-icon <file.ico> [--export-png <folder>]`.

Features: soundboard with global shortcuts (one-shot / toggle / loop / hold, per-sound volume and route,
sequences, random), sound import from files or Myinstants links, App Audio (any running app), live mixer
(mic, app, soundboard, master; gate, compressor, limiter, ducking; push-to-talk), device choice and
monitor-only fallback, profiles, and a floating mini-player for gaming.

Module layout:

| Project | Responsibility |
|---|---|
| BreadBoard.App | Startup/lifecycle (`Lifecycle/`), soundboard controller (`Services/`), UI and navigation (`Views/`, `Navigation/`, `Theme/`), diagnostics |
| BreadBoard.Core | Configuration and persistence, logging, hotkey rules, web-link rules (no Windows APIs) |
| BreadBoard.Audio | Audio engine and audio device management |
| BreadBoard.Platform | Global hotkeys, single instance (Win32) |
| App Audio | Pick any running app on the Mixer page; only its sound joins the virtual mic (process loopback, follows the app across restarts) |
| BreadBoard.Cli | `bb` test host for the audio engine |

## Requirements

- Windows 10 2004+ / Windows 11 (process loopback needs build 19041+)
- .NET 10 SDK
- [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) — Bread Board plays into **CABLE Input**
  (Windows may label it "Speakers (VB-Audio Virtual Cable)"); other apps pick **CABLE Output** as their microphone.

## Build, test, try

```bash
cd "Win Version"
dotnet build
dotnet test
dotnet run --project src/BreadBoard.Cli -- selftest
```

CLI (`bb`) commands:

| Command | What it does |
|---|---|
| `bb devices` | Lists microphones/outputs and the detected virtual cable |
| `bb apps` | Lists running apps (windows + audio sessions) that can be captured |
| `bb download <link>` | Downloads a Myinstants page's sound or a direct .mp3/.wav/.ogg into the library |
| `bb add <file\|link>` | Adds a sound to the library config |
| `bb run [--app brave] [--no-mic]` | Live: mic + app audio + soundboard → virtual mic. Keys 1–9 play sounds, `s` stop all, `m` mute mic, `v` virtual mic on/off |
| `bb probe [--app brave] [--no-mic] [--no-gate] [--seconds 4]` | Same as run, non-interactive; reports levels and what arrived on CABLE Output |
| `bb selftest` | Automated end-to-end check (silent on your speakers) |
| `bb tone [--seconds N] [--speakers]` | Plays a tone (into the virtual cable by default); a stand-in app for App Audio tests |

Data: config `%APPDATA%\BreadBoard\config.json`, sounds `%LOCALAPPDATA%\BreadBoard\Sounds`, logs `%LOCALAPPDATA%\BreadBoard\Logs`.

## Architecture

```
src/BreadBoard.Core     (net10.0, no audio deps)
  Models/AppConfig      settings model (mirrors the prototype's data)
  Config/ConfigStore    JSON (source-generated), atomic save, corrupt-file backup
  Logging/Log           non-blocking daily file log
  Hotkeys/              hotkey parsing, reserved/duplicate rules
  Sounds/SoundLink      which web links can be imported
  Sounds/SoundLibrary   import (copied into the library), rename/edit, remove, favourites, recent, categories, relocate

src/BreadBoard.Audio    (NAudio 3.3 + NVorbis)
  AudioHost             owns the graph; applies settings; restarts failed parts (device unplugged, app closed)
  Devices/DeviceService endpoint enumeration, virtual-cable detection, feedback-loop guard, change notifications
  Sources/CaptureSource mic capture and per-app process-loopback capture → lock-free ring buffer
  Sources/AudioAppEnumerator  running apps with windows/audio sessions (browser child processes mapped to the main process)
  Soundboard/           decode once to 48 kHz float (WAV/MP3 via Media Foundation, OGG via NVorbis); voice engine with one-shot/toggle/loop/hold
  Mixing/MixerEngine    mic → gate → push-to-talk → compressor → fader; app → ducking → fader; soundboard → fader; master → limiter; optional monitor mix
  Output/OutputDevice   shared-mode event-driven WASAPI output (resamples only if the endpoint isn't 48 kHz)
  Import/SoundDownloader  website import with 25 MB cap and content-type checks

src/BreadBoard.Cli      `bb` test host
tests/BreadBoard.Tests  xUnit
```

Signal flow:

```
Microphone ──capture──► ring ─► gate ─► PTT ─► compressor ─► Mic fader ──┐
Chosen app ──process loopback──► ring ─► ducking ─► App fader ───────────┼─► Master ─► limiter ─► CABLE Input ⇒ "CABLE Output" mic in Discord/OBS
Soundboard voices (decoded clips) ─────────────────► Board fader ────────┘        └─► monitor mix ─► headphones (optional)
```

Real-time rules: the render thread never locks or allocates in steady state; UI/hotkey threads talk to it
through volatile fields and a command queue; live inputs are held ~40 ms behind real time and trimmed if they drift past 120 ms.

## Status (verified on the development PC)

- `bb selftest`: virtual cable found, engine output running, soundboard tone received on CABLE Output (rms 0.28),
  process loopback capture working (rms 0.22), virtual-mic switch sends silence.
- `bb probe --app brave`: Brave's YouTube audio captured and heard on CABLE Output (rms 0.13), mic running.
- `bb download <myinstants link>`: file found, downloaded and decoded.
- `dotnet test`: 79 passing (real launch with import/play/stop/remove, real capture of an app that closes and restarts, monitor-only fallback, device switching, push-to-talk key watch).

Global hotkeys: `Platform/Hotkeys/GlobalHotkeyService` (RegisterHotKey on its own message thread; hold-to-play watches the
key only while it is held). `bb run` binds each sound's hotkey, or Num1–Num9 by default, plus Stop all / Mute mic.

Tip: set VB-Cable to 48 kHz (Sound settings → "CABLE Input"/"Speakers (VB-Audio…)" and "CABLE Output" →
Properties → Advanced → 2 channel, 24 bit, 48000 Hz). At other rates Bread Board resamples, which costs a little quality.

All six iterations are built; see CHANGELOG.md for what each one verified.
Not yet tested: Discord / Valorant / OBS end to end (in progress), long-running stability.
