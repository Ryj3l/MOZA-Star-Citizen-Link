# Manual Hardware Test Checklist (Phase 1)

This checklist gates the merge of T-07 (Vortice.DirectInput migration) and is the primary deliverable of T-23 (Phase 1 hardware validation).

**Engineer performing test:** _________________________________
**Date:** _________________________________
**Build SHA:** _________________________________
**Device under test:** ☐ AB6 ☐ AB9
**Windows version:** _________________________________
**MOZA Pit House version (if installed):** _________________________________

Run all sections in order. For any failing item, attach a Serilog snippet (level Debug) from the run that produced the failure to the PR. Video evidence (a 30-second screen+haptic recording per failing item) is required for any failing item before re-test.

---

## Section A — Device detection

| # | Test | Pass | Notes |
|---|---|:---:|---|
| A1 | Boot Windows with device connected. Launch app. Within 5 seconds, the diagnostics panel reports the correct `DeviceModel` (MozaAb6 or MozaAb9). | ☐ | |
| A2 | Quit app. Disconnect device. Launch app. App enters preview mode without errors. | ☐ | |
| A3 | With app running in preview mode, hot-plug the device. Within 5 seconds, app transitions out of preview to the connected device. | ☐ | |
| A4 | With app running and device connected, unplug the device mid-idle. App transitions to `Disconnected` state without crashing. No exception dialog appears. | ☐ | |
| A5 | After A4, plug the device back in. App returns to `Ready` state within 5 seconds. | ☐ | |
| A6 | Launch a known-bad device (e.g., a Logitech wheel or non-MOZA controller) without the MOZA device present. App shows the device in diagnostics but reports "not in allowlist, no commands will be sent" and does NOT play any effect. | ☐ | |

---

## Section B — Test sequence (each of 7 effects)

Press the "Test sequence" button in the UI. The app should fire each effect in turn with ~500 ms gap.

| # | Effect | Played | Felt | Distinguishable from prior effect | Notes |
|---|---|:---:|:---:|:---:|---|
| B1 | quantum-spool-v1 | ☐ | ☐ | n/a (first) | Sustained ~8 sec, periodic ~34 Hz, gradual attack |
| B2 | quantum-jump-exit-v1 | ☐ | ☐ | ☐ | Short sharp pulse, lower-frequency than spool |
| B3 | atmosphere-entry-v1 | ☐ | ☐ | ☐ | Sustained low-frequency rumble (auto-stop after ~2 sec in test sequence; full sustained on real event) |
| B4 | atmosphere-exit-v1 | ☐ | ☐ | ☐ | Should produce no effect; verifies stop command path |
| B5 | landing-contact-v1 | ☐ | ☐ | ☐ | Short downward thump |
| B6 | weapon-fire-generic-v1 | ☐ | ☐ | ☐ | Brief high-frequency tick |
| B7 | vehicle-destruction-v1 | ☐ | ☐ | ☐ | Heavy long rumble |

**Subjective overall feel of the seven effects:** ☐ Distinct ☐ Confusable (note which pairs)

---

## Section C — Emergency stop

| # | Test | Pass | Notes |
|---|---|:---:|---|
| C1 | Start the quantum-spool effect (use Test Quantum button). While playing, press the emergency stop button in the UI. Effect halts immediately (perceptually < 100 ms). | ☐ | |
| C2 | Start the atmosphere effect (Test Atmosphere). While playing, press the global hotkey (default `Ctrl+Alt+F12`). Effect halts immediately. | ☐ | |
| C3 | Start an effect. Press emergency stop. Verify in the Serilog output that the latency from `Activate()` to last device command is < 50 ms. | ☐ | |
| C4 | After emergency stop, fire another effect immediately. Verify the new effect plays normally (i.e., emergency stop does not poison subsequent operation beyond its configured suppression window). | ☐ | |

---

## Section D — Sustained effect lifecycle

| # | Test | Pass | Notes |
|---|---|:---:|---|
| D1 | Trigger the quantum-spool event. Effect starts. After ~8 seconds, effect naturally stops (duration in catalog). | ☐ | |
| D2 | Trigger the quantum-spool event. Before its natural end, trigger it again. The old effect ends and a new one starts (no double-play). | ☐ | |
| D3 | Trigger the atmosphere-entered event. Effect starts and sustains. Trigger the atmosphere-exited event. Effect stops cleanly. | ☐ | |
| D4 | Trigger the atmosphere-entered event. Let it run for at least 60 seconds. Effect continues without interruption. | ☐ | |

---

## Section E — Effect cache and re-acquisition

| # | Test | Pass | Notes |
|---|---|:---:|---|
| E1 | Fire weapon-fire-generic-v1 ten times rapidly via Test Weapon. Inspect Serilog: only one effect-create call appears; subsequent triggers reuse the cached effect. | ☐ | |
| E2 | While the app is running with the device connected, open and close another DirectInput-using app (e.g., MOZA Pit House) that may briefly acquire the device. After the other app releases, our app re-acquires within 5 seconds and effects continue to play. | ☐ | |
| E3 | Force a `DIERR_NOTEXCLUSIVEACQUIRED` situation (acquire the device in another process by writing a small probe utility, or use Pit House). Trigger an effect. The app detects the error, re-acquires (logged), and the effect plays. | ☐ | |

---

## Section F — Gain stack and safety limiter

| # | Test | Pass | Notes |
|---|---|:---:|---|
| F1 | Default master gain is 0.6 on first launch. Visible in diagnostics. | ☐ | |
| F2 | Set master gain to 1.0 via settings. Trigger vehicle-destruction-v1. Effect is perceptibly stronger than at 0.6 but not above the device-recommended cap (0.85). | ☐ | |
| F3 | Set master gain to 0.0. Trigger any effect. No device output occurs (verified by feel and by Serilog showing finalIntensity=0). | ☐ | |
| F4 | Sustained effect intensity never exceeds 0.7 of device max for sustained duration > 5 seconds, even if catalog requests higher. (Verify by setting catalog atmosphere intensity to 0.95 temporarily; safety limiter clamps to 0.7.) | ☐ | |
| F5 | Trigger an effect with catalog `baseIntensity` deliberately set to 1.5 (out of range). Safety limiter clamps to 1.0 (logged as warning). No device damage. | ☐ | |

---

## Section G — Long-soak stability

| # | Test | Pass | Notes |
|---|---|:---:|---|
| G1 | Run the app for 1 hour with sustained atmosphere effect active. Process working set remains under 200 MB (Task Manager). | ☐ | |
| G2 | After G1, all effects still play correctly. | ☐ | |
| G3 | Run the app for 4 hours with intermittent effects (test sequence every 5 min). No memory growth above +10 MB from 1-hour baseline. | ☐ | |

---

## Section H — Diagnostic capture

| # | Test | Pass | Notes |
|---|---|:---:|---|
| H1 | Enable diagnostic logging mode in settings. Verify Serilog output level rises to Debug; new fields appear in log lines. | ☐ | |
| H2 | Export a diagnostic bundle from the diagnostics panel. Verify the export does NOT include raw audio, screenshots, or full game log. | ☐ | |
| H3 | Inspect the bundle's redacted log: no personal paths, no machine names, no user names appear. | ☐ | |

---

## Sign-off

By signing below, the engineer attests that all items above have been performed, results are as recorded, and any failures have been filed as issues with reproductions attached.

| Role | Name | Signature | Date |
|---|---|---|---|
| Engineer running test | | | |
| Reviewer (second engineer) | | | |
| Architect approval (required for T-23 PR merge) | | | |

---

**Failure log (attach issue numbers for any failed item):**

| Item | Issue # | Status |
|---|---|---|
| | | |
| | | |
