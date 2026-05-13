# Replay Bundle Schema

This document specifies the on-disk format of a replay bundle. A bundle is a directory whose canonical entry point is `manifest.json`. T-25 and T-26 implement readers and writers against this schema.

The schema is **versioned**. Current version: **1**. Bundles with `schemaVersion: 2+` are rejected by older replayers with a clear error. Backwards compatibility is not promised — bundles are inputs to a dev tool, not long-term artifacts.

## Variants

A bundle is either **rich** (development-side, contains raw captures) or **stripped** (sharing-side, contains only the numeric features sensors actually consume). The manifest's `stripped` flag distinguishes them. Stripping is a one-way operation; there is no rich-from-stripped path.

## Directory layout

### Rich bundle

```
<bundle-name>/
├── manifest.json
├── game.log
├── audio.wav
├── audio-features.jsonl
├── screen-rois.mp4
├── screen-features.jsonl
├── screen-snapshots/
│   ├── 0001-<label>.png
│   └── ...
└── README.md
```

### Stripped bundle

```
<bundle-name>.stripped/
├── manifest.json                  (stripped: true)
├── game.log
├── audio-features.jsonl
├── screen-features.jsonl
├── screen-snapshots/              (optional; some keepers retained)
│   └── ...
└── README.md
```

The `<bundle-name>` convention is `<UTC-timestamp>_<short-slug>`, e.g., `2026-05-13T141530Z_quantum-test`. The replayer does not parse the directory name; it reads everything from `manifest.json`.

## `manifest.json`

```json
{
  "$schema": "https://raw.githubusercontent.com/<org>/<repo>/main/docs/schemas/replay-bundle.schema.json",
  "schemaVersion": 1,
  "bundleId": "2026-05-13T141530Z_quantum-test",
  "stripped": false,
  "createdAtUtc": "2026-05-13T14:15:30Z",
  "createdBy": "developer-handle-or-machine-id",
  "intent": "Quantum-spool effect tuning against Crusader → Yela jump",
  "starCitizenBuildVersion": "4.0.1-LIVE-9999999",
  "appBuildSha": "abc1234deadbeef",

  "duration": {
    "wallClockStartUtc": "2026-05-13T14:15:30.000Z",
    "wallClockEndUtc":   "2026-05-13T14:25:42.413Z",
    "durationMs": 612413
  },

  "sources": {
    "gameLog": {
      "path": "game.log",
      "startByteOffset": 0,
      "endByteOffset": 1842371,
      "lineCount": 4218,
      "encoding": "utf-8"
    },
    "audio": {
      "rawPath": "audio.wav",
      "featuresPath": "audio-features.jsonl",
      "sampleRateHz": 44100,
      "channels": 2,
      "bitsPerSample": 16,
      "captureSource": "wasapi-loopback",
      "fftFrameSizeSamples": 1024,
      "fftHopSamples": 512,
      "fftWindowFunction": "hann"
    },
    "screen": {
      "roiVideoPath": "screen-rois.mp4",
      "featuresPath": "screen-features.jsonl",
      "captureSource": "windows-graphics-capture",
      "targetWindowTitle": "Star Citizen",
      "frameRateHz": 30,
      "rois": [
        {
          "name": "altimeter",
          "x": 1820,
          "y": 980,
          "width": 80,
          "height": 24
        }
      ],
      "snapshotsDirectory": "screen-snapshots"
    }
  },

  "annotations": [
    {
      "tWallClockMs": 12450,
      "tag": "quantum-spool-start",
      "note": "Confirmed visually — engaged drive at 0:12.45"
    }
  ],

  "ledger": {
    "patternLibraryVersionAtRecord": "v0",
    "effectCatalogVersionAtRecord": "phase1",
    "ruleLibraryVersionAtRecord": "phase1-rules"
  }
}
```

### Field semantics

- **`schemaVersion`** — must be `1` for the current implementation. Replayers reject unknown versions with a clear error.
- **`bundleId`** — unique within a recordings collection. Convention: `<UTC-timestamp>_<slug>`. The replayer does not parse it; it's for human reference.
- **`stripped`** — `false` for rich bundles, `true` for stripped. Stripped bundles must not contain `audio.wav` or `screen-rois.mp4`; the validator enforces this.
- **`createdBy`** — free-form, optional. Recommended: developer handle or hostname. Never a real email address (don't put identifying info in a bundle that might be shared).
- **`intent`** — free-form. Why the recording was made. Future operators reading the bundle will be grateful.
- **`starCitizenBuildVersion`** — the build version reported by Star Citizen at recording time. Read from the Game.log header. The replayer warns (does not fail) if the active pattern library was authored for a different build range.
- **`appBuildSha`** — the Moza.ScLink.Recorder build SHA at recording time. Useful for tracking which recorder produced the bundle.

### `duration`

Wall-clock start and end of the recording. All time offsets in feature files are computed against `wallClockStartUtc`. The replay clock anchors here.

### `sources.gameLog`

- **`path`** — relative to bundle root.
- **`startByteOffset`** and **`endByteOffset`** — record exactly the byte range that was the live log during recording. The recorder copies the whole file but the replayer can choose to ignore content outside this range (so a developer doesn't accidentally replay events from a prior session that happened to be in the same Game.log).
- **`lineCount`** — for quick validation. The validator counts lines in `game.log` and compares.
- **`encoding`** — typically `utf-8`. Star Citizen writes UTF-8 with BOM in some configurations; reader handles both.

### `sources.audio`

- **`rawPath`** — absent in stripped bundles.
- **`featuresPath`** — JSONL where each line is a single FFT frame's band-power vector at a recorded wall-clock offset. Schema for the JSONL lines:

  ```json
  {"tMs": 125, "bands": [0.012, 0.087, 0.341, 0.622, 0.418, 0.103, 0.041, 0.018]}
  ```

  The number of bands and their frequency ranges are recorder-defined and live in the manifest as future work (current default: 8 bands logarithmically spaced 20 Hz – 16 kHz). T-26 task spec finalizes the per-band schema before merge.

- **`sampleRateHz`**, **`channels`**, **`bitsPerSample`** — WAV format details. Required even in stripped bundles (the features were derived from this audio).
- **`captureSource`** — `wasapi-loopback` is the primary path. `manual` is allowed for hand-assembled bundles where audio was captured by a separate tool.
- **`fftFrameSizeSamples`**, **`fftHopSamples`**, **`fftWindowFunction`** — exact reproduction parameters so a replayer can verify the features.

### `sources.screen`

- **`roiVideoPath`** — absent in stripped bundles.
- **`featuresPath`** — JSONL where each line is a single frame's per-ROI feature vector:

  ```json
  {"tMs": 100, "rois": {"altimeter": {"meanLuminance": 0.31, "edgeDensity": 0.78}}}
  ```

  ROI-specific feature schemas are defined per ROI in the manifest (future work; T-26 spec finalizes).

- **`captureSource`** — `windows-graphics-capture` is the primary path. `gdi-bitblt` is the fallback. `manual` is allowed for hand-assembled bundles.
- **`rois`** — list of named regions. Coordinates are in screen pixels at recording time. Resolution scaling is not currently handled — bundles are recorded and replayed at the same resolution.
- **`snapshotsDirectory`** — directory containing sparse PNG snapshots. Optional. Useful for visual ground-truth annotations.

### `annotations`

Optional list of human-added markers. Format:

```json
{"tWallClockMs": <int>, "tag": "<short-slug>", "note": "<free text>"}
```

Annotations are not consumed by the sensor stack. They are for human navigation and for writing assertion-style tests against bundles ("at tag 'quantum-spool-start' + 200 ms, the resolver should emit a `quantum-spool-v1` PlayEffectCommand").

### `ledger`

Records which catalog/rule versions were active when the bundle was recorded. The replayer prints these on load so a developer reading old `events.jsonl` outputs can match them to specific catalog generations.

## Validation rules

The replayer's `validate` subcommand enforces:

1. `manifest.json` exists and parses as JSON.
2. `schemaVersion == 1`.
3. Every file path in the manifest resolves under the bundle directory.
4. `game.log` is present and its line count matches `sources.gameLog.lineCount`.
5. If `stripped == false`: `audio.wav` and `screen-rois.mp4` exist.
6. If `stripped == true`: `audio.wav` and `screen-rois.mp4` are absent.
7. `audio-features.jsonl` line count matches an expected count derived from `durationMs / (fftHopSamples / sampleRateHz * 1000)` ± 5%.
8. `screen-features.jsonl` line count matches `durationMs / (1000 / frameRateHz)` ± 5%.

Validation failure produces a structured error report; the replayer exits non-zero.

## Stripping rules

The stripper (`tools/Moza.ScLink.Replay -- strip <rich> <stripped>`) performs:

1. Copy `manifest.json`, setting `stripped: true`.
2. Copy `game.log` verbatim.
3. Copy `audio-features.jsonl` and `screen-features.jsonl` verbatim.
4. Copy `README.md` if present.
5. Optionally copy `screen-snapshots/` — operator chooses with `--keep-snapshots` flag (default: drop them, smallest output).
6. **Never** copy `audio.wav` or `screen-rois.mp4`. The stripper refuses to overwrite an existing stripped bundle (`--force` overrides).

The stripper writes a `STRIPPED.txt` marker file at the output root containing the strip timestamp, the source bundle's `bundleId`, and the stripper version, so a recipient can verify provenance.

## Sharing posture

Even stripped bundles are **internal artifacts** by default. They are not redistributed to end users. The fixture-library use case (CI regression bundles) is the only sanctioned sharing path within the project, and those fixtures live under `tools/Moza.ScLink.Replay.Tests/Fixtures/` and are reviewed before commit.

Operators who want to share a bundle with another developer should:

1. Strip it.
2. Inspect the resulting `manifest.json`, `game.log`, and feature files manually for anything inadvertently identifying.
3. Transmit it through an internal channel (private repo, internal share), never a public one.

The `.gitignore` shipped in this repo excludes `tools/recordings/` so accidental commits of either variant are prevented.
