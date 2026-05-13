# PRP Addendum: Replay Harness (Rung 4)

**Status:** Approved for execution after Phase 1 foundation lands (T-06 minimum).
**Date:** 2026-05-13
**Author:** Senior Architect
**Relationship to PRP:** This is an additive addendum to `docs/PRP.md`. It does not modify any existing PRP section. New tasks (T-25, T-26) extend the §15 decomposition without altering T-01 through T-24.

## Purpose

The replay harness is a **development-only capability** that records live Star Citizen play sessions and feeds them back through the sensor stack offline. It exists so engineers can iterate on:

- Log pattern regexes (Phase 1)
- Audio FFT-band rules (Phase 2)
- Screen ROI detectors (Phase 2)
- Effect catalog parameters
- Fusion engine rules and weights

…without needing a wheelbase plugged in, Star Citizen running, or even hardware capable of running the game. One operator with hardware records a representative bundle; everyone else iterates against it.

It is **not** a runtime feature. The replay capability does not ship in the user-facing product. No replay code path is reachable from the `Moza.ScLink.App` UI in a Release build.

## Non-goals

- Not a learning system. No model training, no parameter optimization, no online adaptation. Replay produces deterministic, inspectable output the same way live sensors do.
- Not telemetry. Bundles are produced explicitly by a developer using the recorder; nothing is captured during normal user operation.
- Not a redistributable feature. Bundles are internal artifacts unless explicitly stripped to feature-only form (see §Bundle format).
- Not Phase 1. The light CLI (T-25) can ship in parallel with Phase 1 because it depends only on `ISensor` and the log path. The deep `ISensor`-backed replay (T-26) waits for T-10 and T-12.

## Design constraints

These come from the same project posture documented in PRP §13.3:

1. **No gameplay data leaves the recording machine.** Bundles are written to a local `tools/recordings/` directory by default, which is in `.gitignore`. Sharing a bundle is an explicit operator act, and the recommended sharing path is the stripped (feature-only) bundle.
2. **No process inspection.** The audio recorder captures the system audio loopback or a developer-selected input device — never the Star Citizen process directly. The screen recorder uses Windows Graphics Capture API on the Star Citizen window — public, sanctioned API only. Identical posture to the Phase 2 live sensors.
3. **EAC posture unchanged.** Everything the replay recorder does, a Phase 2 live sensor does. The recorder is just the Phase 2 sensor stack with its output going to disk instead of the event bus.
4. **Determinism preserved.** Given the same bundle and the same catalog/rule files, replay produces the same `GameEvent` sequence every time. No randomness, no clock dependency beyond the bundle's recorded timestamps.

## Bundle format (record-rich, strip-for-sharing)

A bundle is a directory containing files written by the recorder and consumed by the replayer. Two variants of the same schema:

### Rich bundle (development use)

```
recordings/2026-05-13_141530_quantum-test/
├── manifest.json              Schema version, capture metadata, sensor inventory
├── game.log                   Verbatim Game.log copy (start/end byte offsets in manifest)
├── audio.wav                  Stereo WAV, 44.1 kHz, 16-bit, system-loopback capture
├── audio-features.jsonl       FFT band-power time series (one JSON line per frame)
├── screen-rois.mp4            H.264 of only the HUD regions of interest
├── screen-features.jsonl      Per-ROI pixel-feature time series
├── screen-snapshots/          Sparse full-resolution PNG snapshots
│   ├── 0001-baseline-001s.png
│   ├── 0042-impact-flash.png
│   └── …
└── README.md                  Human-written notes: ship, location, build version, intent
```

Typical size for a 10-minute capture: ~150–200 MB. Audio dominates; screen ROIs are second; everything else is small.

### Stripped bundle (sharing / fixtures use)

```
recordings/2026-05-13_141530_quantum-test.stripped/
├── manifest.json              Same shape, with `stripped: true` flag
├── game.log                   Verbatim Game.log copy
├── audio-features.jsonl       FFT band-power time series only
├── screen-features.jsonl      Per-ROI pixel-feature time series only
├── screen-snapshots/          Sparse PNG snapshots (kept for debugging)
└── README.md                  Same notes
```

Typical size: ~2–10 MB. Raw audio and the screen-ROI video are gone; only the numeric features the sensors actually consume remain.

The stripper (`tools/Moza.ScLink.Replay/strip` subcommand) is a one-way function. There is no path from stripped → rich.

### Manifest shape

`manifest.json` is the canonical entry point. Schema versioned. Detailed fields specified in `docs/replay-bundle-schema.md`.

## Architecture

```
                       ┌──────────────────────────┐
                       │   Moza.ScLink.Recorder   │
                       │   (Phase 2 dev tool)     │
   live Game.log ──────┤                          │
   audio loopback ─────┤                          ├──→ recordings/<name>/
   SC window capture ──┤                          │
                       └──────────────────────────┘

                       ┌──────────────────────────┐
   recordings/<name>/ ─┤   Moza.ScLink.Replay     │
                       │   (light CLI, T-25)      ├──→ events.jsonl, commands.jsonl
                       └──────────────────────────┘

                       ┌──────────────────────────┐
   recordings/<name>/ ─┤   Replay*Sensor          │
                       │   (deep ISensor, T-26)   ├──→ event bus → fusion → resolver
                       └──────────────────────────┘
```

Three projects, all under `tools/` (so they're clearly separate from `src/` which is the shipped product):

- `tools/Moza.ScLink.Recorder/` — records live sessions. Phase 2 task (deferred — see §Scope).
- `tools/Moza.ScLink.Replay/` — light CLI replayer + bundle stripper. **T-25.**
- `Moza.ScLink.Replay.Sensors/` (under `src/`, but Phase 2-conditional) — deep replay sensors that implement `ISensor`. **T-26.**

The split between `tools/` and `src/` matters: nothing under `tools/` ships in the Release build of `Moza.ScLink.App`. The `Replay.Sensors` project does live under `src/` (because it implements `ISensor` and may eventually be useful for soak testing in CI), but it is excluded from the App's project references in Release configuration.

## Scope split between tasks

### T-25: Light replay CLI

**Phase:** Can run in parallel with Phase 1 after T-06 (core domain types) merges.

**Owns:**

- The `tools/Moza.ScLink.Replay/` project (.NET 8 console app).
- The bundle format readers (manifest + game.log only for T-25; audio and screen features are stubbed for T-26 to flesh out).
- The CLI subcommands:
  - `replay <bundle-path>` — feed the bundle's Game.log through the log parser and pattern library, emit a `events.jsonl` stream of detected events.
  - `validate <bundle-path>` — verify the bundle's manifest, file presence, and schema version.
  - `strip <rich-bundle> <stripped-output>` — produce a feature-only copy. For T-25 this only handles game.log (verbatim copy) and the manifest; audio/screen stripping is T-26.
- A minimal text-format event report so a developer can eyeball "did pattern X match line Y?"
- Unit tests against fixture bundles in `tools/Moza.ScLink.Replay.Tests/Fixtures/`.

**Does not own:**

- Audio or screen replay (T-26).
- The recorder (separate Phase 2 task; see §Future work).
- Hardware playback (replay never drives a real device by default; a `--with-device` flag may be added in a future task with hard guards).

### T-26: Deep replay sensors

**Phase:** After T-10 (event bus) and T-12 (fusion engine) merge. Likely mid-to-late Phase 2.

**Owns:**

- `src/Moza.ScLink.Replay.Sensors/` (.NET 8 class library).
- `ReplayLogSensor`, `ReplayAudioSensor`, `ReplayScreenSensor` — each implements `ISensor` (PRP §5.3) and feeds the real event bus from bundle data at the bundle's recorded timestamps.
- A bundle-time clock abstraction: replay sensors emit events according to the bundle's wall-clock offsets, not real time. The fusion engine sees the same temporal structure it would have seen live.
- Extensions to the light CLI (T-25) so `replay --deep <bundle>` invokes the full sensor stack and writes both `events.jsonl` and `commands.jsonl` (the output of the resolver + safety limiter).
- Audio and screen feature-extraction in the bundle stripper.
- Integration tests that load a fixture bundle and assert the full pipeline produces the expected `commands.jsonl`.

**Does not own:**

- The recorder (still a separate Phase 2 task).
- Live hardware playback during replay.
- Performance optimization for replay-at-faster-than-real-time (a `--speed 4x` flag can be a future task).

## Future work (not in this addendum's scope)

- **Recorder** (`tools/Moza.ScLink.Recorder/`) — Phase 2 task, depends on Phase 2 audio and screen sensors existing. Without it, bundles can only be hand-assembled (Game.log + manually-recorded audio/screen). That's acceptable for the first iteration; the architect can record one good bundle in a one-off session.
- **`--with-device` flag** — drive real MOZA hardware from a replay. Useful for "does this catalog feel right?" iteration without launching Star Citizen. Requires hard guards: device allowlist check, master gain cap, explicit `--i-have-a-clear-bench` flag, banner in the UI saying "replay-driving-device mode active." Defer until T-25 and T-26 have shipped and proven stable.
- **Bundle replay in CI** — once stripped bundles are small enough (~5 MB), check a fixture bundle into the repo and have CI replay it as a regression test. Catches pattern regressions automatically.
- **Replay-driven catalog tuning UI** — load a bundle, play it back with live catalog editing, see effect parameter changes update immediately. This is the Rung 1 ("user-facing tuning UI") feature from the Rungs ladder, with replay as the iteration substrate.
- **Pooled-fixture libraries** — multiple operators contribute stripped bundles representing different ships, locations, builds. The fixture library becomes the regression test suite for the pattern library.

## Acceptance summary

After T-25 + T-26 merge:

- A developer can record (manually, by hand-assembling) or receive a bundle, place it under `tools/recordings/`, run `dotnet run --project tools/Moza.ScLink.Replay -- replay tools/recordings/<bundle> --deep`, and see the full event → command stream that would have fired during that play session.
- A developer can edit `phase1-effect-catalog.json` or `phase1-rules.json` and re-run replay to see the change's effect on the same bundle, deterministically.
- No replay code path is reachable from a Release build of `Moza.ScLink.App`.
- Bundles stay local by default (`.gitignore` covers `tools/recordings/`).
- The stripper produces feature-only bundles that contain no raw audio and no screen video, only numeric feature time series.

## Risk register

- **Audio loopback capture on Windows is fiddly.** WASAPI loopback works but requires care with sample rates, channel counts, and the "Stereo Mix" device permissions. Acceptable risk; well-documented; NAudio handles most of it. Phase 2 recorder task budgets a half-day for this.
- **Screen capture during gameplay can hit GPU contention.** Windows Graphics Capture is generally fine but specific Star Citizen graphics settings (fullscreen-exclusive, certain DX12 paths) may interact poorly. The recorder will need a fallback to GDI BitBlt on the windowed renderer. Acceptable risk; lives entirely in the recorder, which is Phase 2 anyway.
- **Bundle drift.** A bundle recorded against Star Citizen build 4.0 may not represent build 4.5's log format. The manifest records the SC build version so the replayer can warn on mismatch. Long-term, the pattern library's `minStarCitizenBuildVersion` / `maxStarCitizenBuildVersion` fields (already designed into the Phase 2 pattern format per T-11) gate which patterns run against which bundles.
- **Bundle bit-rot.** If `Moza.ScLink.Core` changes the `SensorEvent` schema, old bundles still work (bundles store raw inputs, not parsed events), but `events.jsonl` outputs from old replay runs may be inconsistent with new ones. Acceptable; bundles are inputs, outputs are regenerable.
- **Scope creep into a learning system.** The replay harness is one short hop from "let's add a thumbs-down button and have replay automatically tune catalogs." Resist that until a deliberate Phase 3 product decision is made. The PRP's `docs/decisions/` is the right venue for that decision, not an opportunistic T-27.
