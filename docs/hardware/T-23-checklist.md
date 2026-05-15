# Manual Hardware Test Checklist (Phase 1)

This checklist gates the merge of T-07 (Vortice.DirectInput migration) and is the primary deliverable of T-23 (Phase 1 hardware validation).

**Engineer performing test:** Roderick Taylor
**Date:** 2026-05-14 to 2026-05-15 (M14 close-out session)
**Build SHA:** 55f26b7f01ae95c41a1afbd575eb315e25439935
**Device under test:** ☐ AB6 ☑ AB9
**Windows version:** Windows 11 Pro 10.0.26200
**MOZA Pit House version (if installed):** N/A — not installed; MOZA Cockpit (firmware 1.1.3.4) used as substitute candidate. See issue #27 / Section E notes for substitution-path findings.

Run all sections in order. For any failing item, attach a Serilog snippet (level Debug) from the run that produced the failure to the PR. Video evidence (a 30-second screen+haptic recording per failing item) is required for any failing item before re-test.

**M14 scope:** This sign-off covers T-07 M14 hardware validation (Sections A–E only). Sections F, G, H remain T-23 ownership and were not exercised in this M14 run.

Two M14 findings filed as GitHub issues:
- **#26** — `VorticeDirectInputDeviceAdapter.CreateEffect fails with DIERR_INVALIDPARAM on real AB9 hardware — deterministic; blocks all effect playback`. Blocks Sections B, C, D, E.
- **#27** — `Device-availability state machine is init-only — no reaction to hot-add/hot-remove/replug; stale handle persists; shutdown StopAllAsync non-defensive`. Blocks Sections A3, A4, A5.

Section A: A1, A2, A6 — PASS. A3, A4, A5 — FAIL strict. Sections B–E — BLOCKED pending issue #26 fix.

---

## Section A — Device detection

| # | Test | Pass | Notes |
|---|---|:---:|---|
| A1 | Boot Windows with device connected. Launch app. Within 5 seconds, the diagnostics panel reports the correct `DeviceModel` (MozaAb6 or MozaAb9). | ☑ | **PASS.** Boot-to-device-detection 1.43s (PID 34404) and 0.64s (PID 29700) across two contexts. Diagnostics panel shows `Output status: DirectInput: MOZA AB9 FFB Base`. Footnote: tested as fresh-launch on already-running OS, not a clean Windows boot — "Launch app" is the operative test. UI displays product name (`MOZA AB9 FFB Base`) rather than literal `DeviceModel` enum value. |
| A2 | Quit app. Disconnect device. Launch app. App enters preview mode without errors. | ☑ | **PASS.** Fresh init with AB9 unplugged enters Preview cleanly: `Output: Auto output - Preview output` + `Output status: Preview output`. No errors. Init time 0.6s. PID 14140 and PID 30780 both confirmed. |
| A3 | With app running in preview mode, hot-plug the device. Within 5 seconds, app transitions out of preview to the connected device. | ☐ | **FAIL — see issue #27.** No auto-detection in 8 s after plug-in. Manual Refresh detects AB9 in DI enumeration but Output status stays `Preview output` (Refresh and output-selection are decoupled). Root cause: state machine is purely init-driven; no polling or device-availability event subscription. |
| A4 | With app running and device connected, unplug the device mid-idle. App transitions to `Disconnected` state without crashing. No exception dialog appears. | ☐ | **FAIL strict on transition, PASS on no-crash + no-exception — see issue #27.** Unplug produced no log entry, no crash, no exception dialog. Output status stayed at `DirectInput: MOZA AB9 FFB Base` (stale). Subsequent graceful shutdown surfaced stale-handle observation (HRESULT 0x8007048F DEVICE_NOT_CONNECTED at `StopAllAsync`) — folded into issue #27 as secondary concern. |
| A5 | After A4, plug the device back in. App returns to `Ready` state within 5 seconds. | ☐ | **FAIL — see issue #27.** Replug produced no auto-detection in 8 s. Output status never re-acquired Ready state. Second-context reproduction of issue #27's root cause. |
| A6 | Launch a known-bad device (e.g., a Logitech wheel or non-MOZA controller) without the MOZA device present. App shows the device in diagnostics but reports "not in allowlist, no commands will be sent" and does NOT play any effect. | ☑ | **PASS.** Fresh init with AB9 unplugged + VKBsim Gladiator EVO OT L plugged in: diagnostics shows `DirectInput game controllers: 1` → `[no FFB] VKBsim Gladiator EVO OT L`; `DirectInput force-feedback devices: 0`. Output status `Preview output`. No acquisition attempts for VKBsim in log. Allowlist enforcement confirmed at init. **Minor wording note:** checklist expects "not in allowlist" UI messaging; actual UI uses `[no FFB]` capability flag (M15 docs reconciliation item). |

---

## Section B — Test sequence (each of 7 effects)

Press the "Test sequence" button in the UI. The app should fire each effect in turn with ~500 ms gap.

| # | Effect | Played | Felt | Distinguishable from prior effect | Notes |
|---|---|:---:|:---:|:---:|---|
| B1 | quantum-spool-v1 | ☐ | ☐ | n/a (first) | **BLOCKED — see issue #26.** Test Quantum reproducibly fails with DIERR_INVALIDPARAM at `VorticeDirectInputDeviceAdapter.CreateEffect:71` (four reproducers in M14). Effect never created; no haptic. |
| B2 | quantum-jump-exit-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** |
| B3 | atmosphere-entry-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** Test Atmo button not tested in M14. Per-effect-type behavior not verified empirically; issue #26 acceptance criteria require fix-session verification of all three Test buttons (Quantum / Impact / Atmo). |
| B4 | atmosphere-exit-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** |
| B5 | landing-contact-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** Test Impact button not tested in M14. Per-effect-type behavior not verified empirically; issue #26 acceptance criteria require fix-session verification of all three Test buttons. |
| B6 | weapon-fire-generic-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** |
| B7 | vehicle-destruction-v1 | ☐ | ☐ | ☐ | **BLOCKED — see issue #26.** |

**Subjective overall feel of the seven effects:** ☐ Distinct ☐ Confusable (note which pairs) — N/A; Section B BLOCKED pending issue #26 fix.

---

## Section C — Emergency stop

| # | Test | Pass | Notes |
|---|---|:---:|---|
| C1 | Start the quantum-spool effect (use Test Quantum button). While playing, press the emergency stop button in the UI. Effect halts immediately (perceptually < 100 ms). | ☐ | **BLOCKED — see issue #26.** Cannot start quantum-spool effect to test emergency stop. |
| C2 | Start the atmosphere effect (Test Atmosphere). While playing, press the global hotkey (default `Ctrl+Alt+F12`). Effect halts immediately. | ☐ | **BLOCKED — see issue #26.** Hotkey availability confirmed pre-test (not claimed by other apps); functional test gated. |
| C3 | Start an effect. Press emergency stop. Verify in the Serilog output that the latency from `Activate()` to last device command is < 50 ms. | ☐ | **BLOCKED — see issue #26.** |
| C4 | After emergency stop, fire another effect immediately. Verify the new effect plays normally (i.e., emergency stop does not poison subsequent operation beyond its configured suppression window). | ☐ | **BLOCKED — see issue #26.** |

---

## Section D — Sustained effect lifecycle

| # | Test | Pass | Notes |
|---|---|:---:|---|
| D1 | Trigger the quantum-spool event. Effect starts. After ~8 seconds, effect naturally stops (duration in catalog). | ☐ | **BLOCKED — see issue #26.** |
| D2 | Trigger the quantum-spool event. Before its natural end, trigger it again. The old effect ends and a new one starts (no double-play). | ☐ | **BLOCKED — see issue #26.** |
| D3 | Trigger the atmosphere-entered event. Effect starts and sustains. Trigger the atmosphere-exited event. Effect stops cleanly. | ☐ | **BLOCKED — see issue #26.** |
| D4 | Trigger the atmosphere-entered event. Let it run for at least 60 seconds. Effect continues without interruption. | ☐ | **BLOCKED — see issue #26.** Incidental data observed during M14: PID 26456 ran ~10 hours / 180.6 MB working set without crashes (not a formal soak-test measurement). |

---

## Section E — Effect cache and re-acquisition

| # | Test | Pass | Notes |
|---|---|:---:|---|
| E1 | Fire weapon-fire-generic-v1 ten times rapidly via Test Weapon. Inspect Serilog: only one effect-create call appears; subsequent triggers reuse the cached effect. | ☐ | **BLOCKED — see issue #26.** Cache reuse cannot be tested while CreateEffect itself fails. |
| E2 | While the app is running with the device connected, open and close another DirectInput-using app (e.g., MOZA Pit House) that may briefly acquire the device. After the other app releases, our app re-acquires within 5 seconds and effects continue to play. | ☐ | **BLOCKED — see issues #26 and #27.** Substitution-path investigation in M14: MOZA Cockpit (substituted for Pit House per actual hardware setup) does not exclusive-acquire DI output in either Integrated FFB mode (default) or DirectInput mode. No alternative DI consumer immediately available in test environment. Deferred to fix-session re-run with an alternative DI consumer (Star Citizen, probe utility, or third-party DI app TBD). |
| E3 | Force a `DIERR_NOTEXCLUSIVEACQUIRED` situation (acquire the device in another process by writing a small probe utility, or use Pit House). Trigger an effect. The app detects the error, re-acquires (logged), and the effect plays. | ☐ | **BLOCKED — see issues #26 and #27.** Same substitution-path obstacle as E2. Reactive re-acquisition mechanism is untestable until an alternative DI consumer is available; codebase's reactive-on-`DIERR_NOTEXCLUSIVEACQUIRED` behavior remains unverified empirically. |

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
| Engineer running test | Roderick Taylor | Attested in M14 close-out session (this checklist edit) | 2026-05-15 |
| Reviewer (second engineer) | _(deferred)_ | _(deferred to fix-session re-run after issues #26 and #27 land)_ | — |
| Architect approval (required for T-23 PR merge) | _(deferred)_ | _(deferred to T-23 PR per orientation: T-23 PR merge gate; not M14 close-out)_ | — |

---

**Failure log (attach issue numbers for any failed item):**

| Item | Issue # | Status |
|---|---|---|
| Section A3 | #27 | Open |
| Section A4 | #27 (incl. shutdown stale-handle secondary concern) | Open |
| Section A5 | #27 | Open |
| Section B1–B7 | #26 | Open |
| Section C1–C4 | #26 | Open |
| Section D1–D4 | #26 | Open |
| Section E1 | #26 | Open |
| Section E2–E3 | #26 + #27 (substitution-path investigation deferred) | Open |
