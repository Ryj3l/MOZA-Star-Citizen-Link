# Moza.ScLink.DiAcquireProbe

Development-only diagnostic tool for **issue #83** (emergency-stop StopAll no-ops under device
contention) and **T-23 Section E2** (external-contention re-acquire). It answers one empirical
question: **does force commanded by Moza.ScLink.App persist on the device after the app loses
exclusive acquisition?**

## Posture (read first)

- **Never ships.** This tool is not part of any release artifact and is not reachable from any
  `Moza.ScLink.*` runtime project. It exists solely for operator-at-rig hardware validation.
- **No project references.** Standalone WPF app; its only dependency is `Vortice.DirectInput`
  (pre-approved, ADR-0001), version-pinned by the repo's central package management.
- **It drives no force.** The probe never creates, downloads, or starts an effect. Its only
  device-mutating command is `ForceFeedbackCommand.Reset` — a halt-type command, sent only on
  explicit operator click, used to discriminate the halt mechanism when a force persists. Target
  is the allowlisted AB9 (highlighted bold in the device list via the same product-name patterns
  as `src/device-allowlist.json`).

## What it does

1. Enumerates attached DirectInput game controllers.
2. On **Acquire**, exclusive-acquires the selected device, forcing Moza.ScLink.App (which holds
   `Exclusive | Background`) to lose acquisition — the same condition Star Citizen creates
   in-game, reproduced without SC's own FFB output confounding the observation.
3. On **Reset FFB**, sends the halt-type reset (mechanism discrimination, persisting-force case).
4. On **Release**, unacquires and disposes, letting the app's M9 retry path re-acquire (E2).

Every step is logged with millisecond timestamps and HRESULTs for correlation against the app's
diagnostic log.

## Cooperative modes

- **Exclusive | Foreground (default, SC-faithful):** replicates a game's acquisition posture —
  the configuration empirically proven (by SC, T-23 2026-06-08: 22/22 contended no-ops) to take
  the device from the app's background-exclusive hold. Caveat: DirectInput auto-unacquires
  Foreground-mode devices when the probe window loses focus, so keep the probe foreground during
  the observation window and trigger the app's e-stop via its **global hotkey**, not its window.
  Window activation/deactivation transitions are logged for forensics.
- **Exclusive | Background:** holds acquisition regardless of focus (sustained-hold variant);
  also a data point on whether a background-exclusive acquire can take an already-held device.

## Running

```pwsh
dotnet run --project tools/Moza.ScLink.DiAcquireProbe
```

The test protocol (effect amplification, restart-between-repeats, outcome matrix) lives in the
T-23 living doc: `docs/hardware/runs/T-23-AB9.md`, with the spec recorded on issue #83.
