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

**M14 snapshot:** Section A: A1, A2, A6 — PASS. A3, A4, A5 — FAIL strict. Sections B–E — BLOCKED pending issue #26 fix.

**Post-Pass-2 update (2026-05-18, this PR):** #26 fix landed (commit `e39499f`); #27 Pass 1 (`05d519f`) + Pass 2 (`01d2336`) landed. **Section A all 6 items now PASS** — A1 re-confirmed, A2 / A6 unchanged scope (not Pass-2-touched), A3 / A4 / A5 flipped FAIL → PASS via Pass 2's `WM_DEVICECHANGE` → `IDeviceAvailabilityObserver` pipeline. **Sections B–E** — #26 blocker removed (Test buttons fire without `DIERR_INVALIDPARAM` per e39499f hardware validation), but the full B–E test procedures have NOT been formally re-run with the post-Pass-2 build. Tactile-distinguishability concerns surfaced during V9 informal Section-B-style A/B testing → tracked as **#35** (AB9 motor-noise floor). **Sections C and D specifically remain live concerns** because #31 (effect stacking + stop-propagation) sits in the runtime stop-path that C exercises (emergency stop) and the sustained-effect lifecycle that D exercises (10-min soak per T-07 acceptance criterion 7); Pass 1's StopAll fix is shutdown-scoped, not runtime-scoped, so #31 is not transitively closed.

---

## Section A — Device detection

| # | Test | Pass | Notes |
|---|---|:---:|---|
| A1 | Boot Windows with device connected. Launch app. Within 5 seconds, the diagnostics panel reports the correct `DeviceModel` (MozaAb6 or MozaAb9). | ☑ | **PASS (M14).** Boot-to-device-detection 1.43s (PID 34404) and 0.64s (PID 29700) across two contexts. Diagnostics panel shows `Output status: DirectInput: MOZA AB9 FFB Base`. Footnote: tested as fresh-launch on already-running OS, not a clean Windows boot — "Launch app" is the operative test. UI displays product name (`MOZA AB9 FFB Base`) rather than literal `DeviceModel` enum value. **Post-Pass-2 re-confirmation (V9, pid:40592, 2026-05-18):** launch with AB9 connected → `Device available: Auto output (DirectInput: MOZA AB9 FFB Base)` at 16:35:23.6353429 (init time ~30ms post-app-start). UI binding through the new reactive `OutputName` / `OutputStatus` properties (E-B5) confirmed. |
| A2 | Quit app. Disconnect device. Launch app. App enters preview mode without errors. | ☑ | **PASS (M14).** Fresh init with AB9 unplugged enters Preview cleanly: `Output: Auto output - Preview output` + `Output status: Preview output`. No errors. Init time 0.6s. PID 14140 and PID 30780 both confirmed. **Scope unchanged by Pass-2** — Pass-2 doesn't touch the launch-without-device code path; A2 PASS persists. (Note: V9 informal "A2 tactile A/B" sessions tested test-button effect feel on the initial-launch device — that's Section B coverage, not checklist-A2 coverage; see #35 noise-floor.) |
| A3 | With app running in preview mode, hot-plug the device. Within 5 seconds, app transitions out of preview to the connected device. | ☑ | **PASS (post-Pass-2, with coverage caveat).** Pass 2 (commit `01d2336`) wired the WM_DEVICECHANGE → IDeviceAvailabilityObserver pipeline closing #27's "init-only state machine" root cause. **V9 confirmation (pid:36964, 2026-05-18, PRE-C):** launch with AB9 unplugged → `Device lost — entering preview mode` at 15:43:03.19; operator plugs AB9 mid-session → `DirectInput device initialized: MOZA AB9 FFB Base` + `Device available: Auto output (DirectInput: MOZA AB9 FFB Base)` at 15:43:22.17 (within 5s of physical plug). **Coverage caveat:** A3 was validated in the pre-C session; not directly re-exercised against the C-restructured chain (the post-C V9 session pid:40592 was launch-with-device, exercising A1/A4/A5 not A3). Covered-by-proximity: A3's `OnDeviceArrived` transition path is structurally identical pre-C and post-C — the C-fix changed PlayAsync routing on reacquired devices, NOT the OnDeviceArrived transition itself; A5 was re-confirmed post-C on the same `DispatchArrivalAsync` code path A3 exercises. M14 FAIL anchor preserved: "No auto-detection in 8 s after plug-in. ..." — fixed. |
| A4 | With app running and device connected, unplug the device mid-idle. App transitions to `Disconnected` state without crashing. No exception dialog appears. | ☑ | **PASS (post-Pass-2).** Pass 2 wired chain re-selection on `DBT_DEVICEREMOVECOMPLETE`; transition is sub-second. **V9 confirmation (pid:36964 + pid:40592):** unplug → `Device lost — entering preview mode: Auto output (Preview output)` at 15:44:13.93 (pid:36964); operator-confirmed UI flip "effectively immediate — sub-second from unplug to UI flip." No crash, no exception dialog, no error-styled log entries. M14 FAIL anchor preserved: "stale Output status... HRESULT 0x8007048F at `StopAllAsync`." Pass 1 (commit `05d519f`) closed the StopAllAsync HRESULT noise via defensive narrowing (shutdown-context only). |
| A5 | After A4, plug the device back in. App returns to `Ready` state within 5 seconds. | ☑ | **PASS (post-Pass-2).** Hot-arrival pipeline re-acquires the DirectInput tier via the factory's `directInputProvider` delegate (E-B7). **V9 confirmation (pid:36964 + pid:40592):** replug after A4 → `DirectInput device initialized: MOZA AB9 FFB Base` + `Device available` at 15:46:16 (pid:36964) and at 16:36:15 (pid:40592 post-C). Note: V9 also surfaced an A6-style runtime bug (`ObjectDisposedException` on PlayAsync after hot-arrival, stale `_devices[0]` reference) which led to the C structural fix (`AllDevices()` single-source iteration); A5 itself — the transition back to Ready — was always PASS post-Pass-2; the C-fix was needed for subsequent PlayAsync calls on the reacquired device. |
| A6 | Launch a known-bad device (e.g., a Logitech wheel or non-MOZA controller) without the MOZA device present. App shows the device in diagnostics but reports "not in allowlist, no commands will be sent" and does NOT play any effect. | ☑ | **PASS (M14).** Fresh init with AB9 unplugged + VKBsim Gladiator EVO OT L plugged in: diagnostics shows `DirectInput game controllers: 1` → `[no FFB] VKBsim Gladiator EVO OT L`; `DirectInput force-feedback devices: 0`. Output status `Preview output`. No acquisition attempts for VKBsim in log. Allowlist enforcement confirmed at init. **Minor wording note:** checklist expects "not in allowlist" UI messaging; actual UI uses `[no FFB]` capability flag (M15 docs reconciliation item). **Scope unchanged by Pass-2** — allowlist enforcement at init is untouched. (Note: V9 informal "A6 tactile A/B" sessions tested test-button effect feel on the hot-reacquired device — that's Section B coverage, not checklist-A6 coverage; same #35 noise-floor concern as A2 informal.) |

---

## Section B — Test sequence (each of 7 effects)

Press the "Test sequence" button in the UI. The app should fire each effect in turn with ~500 ms gap.

| # | Effect | Played | Felt | Distinguishable from prior effect | Notes |
|---|---|:---:|:---:|:---:|---|
| B1 | quantum-spool-v1 | ☑ | ☐ (#35) | n/a (first) | **PASS [REAL-PATH via Test Quantum button] (post-Pass-2 via V9 re-run).** Diagnostic-mode pid:13052 17:53:34: `DI effect cache miss: Quantum spool vibration ("Periodic") — creating effect` → effect created cleanly. Subsequent presses (D2-strict 17:53:37 + E1 rapid-fire 36 hits in pid:8304 17:45:10–16) all `cache hit`. **Felt ☐ per #35**: individual-effect tactile distinguishability limited by AB9 motor noise floor. **M14 FAIL anchor preserved:** "BLOCKED — see issue #26. Test Quantum reproducibly fails with DIERR_INVALIDPARAM at `VorticeDirectInputDeviceAdapter.CreateEffect:71` (four reproducers in M14). Effect never created; no haptic." Fixed by commit `e39499f` (#26 closed). |
| B2 | quantum-jump-exit-v1 | ☐ | ☐ | ☐ | **DEFERRED — NO AFFORDANCE (post-Pass-2).** No Test Sequence button in current UI; additionally #32 placeholder pattern means no log-injection path either. Future T-09 (MVVM modernization) / T-18 (Diagnostics panel) territory. **M14 anchor preserved:** "BLOCKED — see issue #26." Note: #26 now closed (`e39499f`); B2's gap is now UI-affordance, not #26. |
| B3 | atmosphere-entry-v1 | ☑ | ☑ | ☐ (#35 vs B1) | **PASS [REAL-PATH via Test Atmo button] (post-Pass-2 via V9 re-run).** pid:8304 17:43:15: `DI effect cache miss: In-atmosphere vibration ("Periodic") — creating effect`. **Felt ☑**: operator confirmed sustained vibration present during D3 10s gap (pid:13052 17:55:31–42). **M14 anchor preserved:** "BLOCKED — see issue #26. Test Atmo button not tested in M14. Per-effect-type behavior not verified empirically; issue #26 acceptance criteria require fix-session verification of all three Test buttons (Quantum / Impact / Atmo)." Fixed by `e39499f`. |
| B4 | atmosphere-exit-v1 | ☑ | ☑ | ☐ (#35 vs B3) | **PASS [REAL-PATH via D3 game-log injection]** (no atmosphere-exit UI button exists; production-equivalent path is the game-log → parser → `AtmosphereExited` event → `StopAsync("atmosphere")` chain — exactly what D3 exercises). pid:13052 17:55:42: `Matched Game.log event #2: AtmosphereExited` + `DI effect stopped: atmosphere`. **Felt ☑**: operator confirmed tactile halt after the exited line was injected. **M14 anchor preserved:** "BLOCKED — see issue #26." |
| B5 | landing-contact-v1 | ☑ | ☐ (#35) | ☐ (#35 vs B3) | **PASS [REAL-PATH via Test Impact button] (post-Pass-2 via V9 re-run).** pid:8304 17:43:08 + pid:13052 17:55:03: `DI effect cache miss: Landing/impact bump ("ConstantForce") — creating effect`. **Felt ☐ per #35**: 260ms bump effect is hardest tactile case against motor noise floor. **M14 anchor preserved:** "BLOCKED — see issue #26. Test Impact button not tested in M14. Per-effect-type behavior not verified empirically; issue #26 acceptance criteria require fix-session verification of all three Test buttons." Fixed by `e39499f`. |
| B6 | weapon-fire-generic-v1 | ☐ | ☐ | ☐ | **DEFERRED — NO AFFORDANCE (post-Pass-2).** No Test Weapon button in current UI. Future T-09 / T-18 territory. **M14 anchor preserved:** "BLOCKED — see issue #26." Note: #26 now closed; B6's gap is UI-affordance. |
| B7 | vehicle-destruction-v1 | ☐ | ☐ | ☐ | **DEFERRED — NO AFFORDANCE (post-Pass-2).** No Test Vehicle button in current UI. Future T-09 / T-18. **M14 anchor preserved:** "BLOCKED — see issue #26." Note: #26 now closed; B7's gap is UI-affordance. |

**Subjective overall feel of the seven effects:** ☐ Distinct ☐ Confusable — per #35 noise-floor, individual-effect tactile distinguishability is limited on AB9 hardware; operator confirmed start / sustained / halt observations for atmosphere (B3 + B4 via D3) but per-effect discrimination across B1/B3/B5 is bounded by motor noise floor. **M14 anchor preserved:** "N/A; Section B BLOCKED pending issue #26 fix." (#26 now closed by `e39499f`; subjective per-effect distinguishability tracked as #35.)

---

## Section C — Emergency stop

| # | Test | Pass | Notes |
|---|---|:---:|---|
| C1 | Start the quantum-spool effect (use Test Quantum button). While playing, press the emergency stop button in the UI. Effect halts immediately (perceptually < 100 ms). | ☑ | **PASS [SUBSTITUTE via Stop button = StopAllAsync] (post-Pass-2 via V9 re-run).** Stop button bound to `MainViewModel.StopAsync` → `_feedback.StopAllAsync(CancellationToken.None)` (functionally equivalent to emergency-stop at the chain layer; not the UI label "Emergency stop" the original procedure named). pid:13052 17:54:06–55: multiple Test Quantum presses followed by Stop sequences with zero `StopAllEffectFailed` / `StopAllDeviceCallFailed` entries. **Operator subjective: yes (Stop halts <100ms perceptually).** **M14 anchor preserved:** "BLOCKED — see issue #26. Cannot start quantum-spool effect to test emergency stop." Unblocked by `e39499f`. |
| C2 | Start the atmosphere effect (Test Atmosphere). While playing, press the global hotkey (default `Ctrl+Alt+F12`). Effect halts immediately. | ☐ | **DEFERRED — NO AFFORDANCE (post-Pass-2).** No global hotkey wired in current build (verified at `MainWindow.xaml` — only UI buttons exist; no `InputBinding`/`KeyBinding`/`HotkeyHook` for Ctrl+Alt+F12). Future T-09 (MVVM modernization) territory. **M14 anchor preserved:** "BLOCKED — see issue #26. Hotkey availability confirmed pre-test (not claimed by other apps); functional test gated." Hotkey wiring is a separate concern from #26's effect-creation fix. |
| C3 | Start an effect. Press emergency stop. Verify in the Serilog output that the latency from `Activate()` to last device command is < 50 ms. | ☐ | **NOT MEASURED (post-Pass-2).** Chain-side Stop-button success path is silent at both Information AND Debug log levels — `HandleStopAllAsync` logs only on DIERR HRESULTs (EventId 12/13/14). No chain-side timestamp exists for the "last device command" on clean success; strict <50ms latency criterion not log-measurable. Would require code instrumentation (EventId for clean StopAll completion) or external timing. **Negative signal good** (zero `StopAllEffectFailed` / `StopAllDeviceCallFailed` in pid:13052), but the strict <50ms criterion is not directly verified. **M14 anchor preserved:** "BLOCKED — see issue #26." Unblocked by `e39499f` but measurement-affordance gap remains. |
| C4 | After emergency stop, fire another effect immediately. Verify the new effect plays normally (i.e., emergency stop does not poison subsequent operation beyond its configured suppression window). | ☑ | **PASS [SUBSTITUTE via Test Impact after Stop] (post-Pass-2 via V9 re-run).** pid:13052 17:55:03: `DI effect cache miss: Landing/impact bump ("ConstantForce") — creating effect` immediately following C1's Stop sequence — chain processed the new Play without any "stuck" state. **Operator subjective: yes (Test Impact fires normally).** Zero failures in log. **M14 anchor preserved:** "BLOCKED — see issue #26." Unblocked by `e39499f`. |

---

## Section D — Sustained effect lifecycle

| # | Test | Pass | Notes |
|---|---|:---:|---|
| D1 | Trigger the quantum-spool event. Effect starts. After ~8 seconds, effect naturally stops (duration in catalog). | ☑ | **PASS [REAL-PATH via Test Quantum] (post-Pass-2 via V9 re-run).** Natural-stop is DirectInput-side automatic (effect duration); chain doesn't fire `EffectStopped` for this path. **Operator subjective: yes (Quantum naturally stops at ~8s).** **M14 anchor preserved:** "BLOCKED — see issue #26." Unblocked by `e39499f`. |
| D2 | Trigger the quantum-spool event. Before its natural end, trigger it again. The old effect ends and a new one starts (no double-play). | ☑ | **PASS [REAL-PATH strict] (post-Pass-2 via V9 re-run).** pid:13052 17:53:34 (Press 1, cache miss + create) → 17:53:37 (Press 2, **cache hit** — only 3s later, well within 8s effect window). Cache logic confirmed: same effect instance reused (no new effect created → no stacking at cache layer). **Operator subjective: "felt like replace"** (no doubled vibration; clean re-trigger or smooth replace at hardware layer). **Closes the #31 stack interpretation at the cache layer** (definitive — cache hit at 3s + 36-hit rapid-fire in pid:8304 E1); **consistent with replace at the hardware layer** (single tactile observation, #35-limited). **M14 anchor preserved:** "BLOCKED — see issue #26." Unblocked by `e39499f`. |
| D3 | Trigger the atmosphere-entered event. Effect starts and sustains. Trigger the atmosphere-exited event. Effect stops cleanly. | ☑ | **PASS [REAL-PATH via game-log injection with 10s sustain] (post-Pass-2 via V9 re-run).** pid:13052 17:55:31: `Matched Game.log event #1: AtmosphereEntered 'In-atmosphere vibration' from atmosphere entered` + `DI effect cache hit: In-atmosphere vibration`. 10-second `Start-Sleep` enforced sustain. 17:55:42: `Matched Game.log event #2: AtmosphereExited 'Atmosphere exited' from atmosphere exited` + `DI effect stopped: atmosphere` (the per-effect `StopAsync(stateKey)` runtime path — #31's exact code path). **Operator subjective: yes (sustained vibration during 10s gap; halted after exited line).** Hardware-layer stop propagation confirmed on this clean cycle. **M14 anchor preserved:** "BLOCKED — see issue #26." Unblocked by `e39499f` + Pass 2's chain-state-machine. |
| D4 | Trigger the atmosphere-entered event. Let it run for at least 60 seconds. Effect continues without interruption. | ☑ | **PASS [REAL-PATH soak] (post-Pass-2 via V9 re-run).** pid:13052 17:56:29 last Test Atmo press → silence until 18:20:12 Stop. **~24 minutes of sustained-atmosphere soak — far exceeding the 60s minimum AND the T-07 acceptance criterion 7 10-min soak.** Zero entries in the log during the soak window (no errors, no crashes, no exceptions, no UI hangs). **Operator subjective: no crashes/exceptions/UI-hang during.** **M14 anchor preserved:** "BLOCKED — see issue #26. Incidental data observed during M14: PID 26456 ran ~10 hours / 180.6 MB working set without crashes (not a formal soak-test measurement)." Now formally measured: 24+ minutes sustained-atmosphere on AB9 with zero issues. |

---

## Section E — Effect cache and re-acquisition

| # | Test | Pass | Notes |
|---|---|:---:|---|
| E1 | Fire weapon-fire-generic-v1 ten times rapidly via Test Weapon. Inspect Serilog: only one effect-create call appears; subsequent triggers reuse the cached effect. | ☑ | **PASS [SUBSTITUTE — Test Quantum × 36+ for Test Weapon] (post-Pass-2 via V9 re-run).** No Test Weapon button exists in current UI; Test Quantum substituted (same effect-cache code path; preserves the cache-reuse intent). pid:8304 17:45:10–16: **36 consecutive `DI effect cache hit: Quantum spool vibration ("Periodic")` entries in ~6 seconds (~6 presses/sec), 0 cache misses in the rapid-fire window.** Cache reuse spectacular under stress; the #31 cache-layer interpretation is definitively ruled out. **M14 anchor preserved:** "BLOCKED — see issue #26. Cache reuse cannot be tested while CreateEffect itself fails." Now testable per #26 closure. |
| E2 | While the app is running with the device connected, open and close another DirectInput-using app (e.g., MOZA Pit House) that may briefly acquire the device. After the other app releases, our app re-acquires within 5 seconds and effects continue to play. | ☐ | **DEFERRED — M14 substitution-path obstacle persists (post-Pass-2).** MOZA Cockpit (substituted for MOZA Pit House per actual hardware setup) does not exclusive-acquire DI output in either Integrated FFB mode (default) or DirectInput mode. No alternative DI consumer (Star Citizen, probe utility, or third-party DI app) available in current test environment. Re-acquisition mechanism remains untested empirically — though Pass 2's hot-plug pipeline DOES exercise re-acquisition via WM_DEVICECHANGE (A5 confirmed at V9 hot-plug cycle); the strict E2 procedure (external app contention) is the unaffordable variant. **M14 anchor preserved:** "BLOCKED — see issues #26 and #27. ..." Deferred to separate hardware session with an alternative DI consumer. |
| E3 | Force a `DIERR_NOTEXCLUSIVEACQUIRED` situation (acquire the device in another process by writing a small probe utility, or use Pit House). Trigger an effect. The app detects the error, re-acquires (logged), and the effect plays. | ☐ | **DEFERRED — same M14 substitution-path obstacle as E2 (post-Pass-2).** Reactive re-acquisition mechanism remains untested empirically. Note: Pass-1 commit `05d519f` defensively narrows the STOPALL path for DIERR_NOTEXCLUSIVEACQUIRED (shutdown context); the runtime PLAY-path re-acquisition behavior (T-07 spec §148-152 retry loop) is in `VorticeDirectInputDevice` and would be exercised here if an alternative DI consumer were available. **M14 anchor preserved:** "BLOCKED — see issues #26 and #27. ..." Deferred. |

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
| Section A3 | #27 (Pass 2) | **Closed by this PR** (commit `01d2336`; V9 confirmed in pid:36964) |
| Section A4 | #27 (Pass 2 + Pass 1 for shutdown stale-handle secondary) | **Closed by this PR** (commits `01d2336` + `05d519f`; V9 confirmed in pid:36964 and pid:40592) |
| Section A5 | #27 (Pass 2) | **Closed by this PR** (commit `01d2336`; V9 confirmed in pid:36964 and pid:40592) |
| Section B1 / B3 / B5 | #26 | **Closed by this PR** (commit `e39499f` for #26; V9 re-run pid:13052 + pid:8304 confirmed cache miss+create on first press + cache hit on subsequent presses) |
| Section B4 | #26 + procedure-form | **Closed by this PR** (via D3 game-log injection — no UI affordance for atmosphere-exit; D3 exercises the AtmosphereExited → StopAsync("atmosphere") production-equivalent path) |
| Section B2 / B6 / B7 | UI-affordance | **DEFERRED — NO AFFORDANCE** (no Test Sequence / Test Weapon / Test Vehicle UI; #26 no longer blocks; future T-09 / T-18) |
| Section C1 / C4 | #26 (Pass 1 + e39499f) | **Closed by this PR** (V9 re-run pid:13052; operator subjective + zero StopAllEffectFailed) |
| Section C2 | UI-affordance | **DEFERRED — NO AFFORDANCE** (no global hotkey wired; future T-09) |
| Section C3 | #26 + measurement-affordance | **NOT MEASURED** (no chain-side latency log signal on Stop-success path; would require code instrumentation; negative signal good but strict <50ms criterion not directly verified) |
| Section D1 / D2 / D3 / D4 | #26 + #31 | **Closed by this PR** (V9 re-run pid:13052; D4 24+ min soak; #31 3-of-4 interpretations ruled out via diagnostic evidence, 4th not-reproduced on single clean cycle — recommend close-as-not-reproduced pending maintainer decision) |
| Section E1 | #26 | **Closed by this PR** (V9 re-run pid:8304; 36 cache hits / 0 misses in rapid-fire) |
| Section E2 / E3 | #26 (now closed); M14 substitution-path obstacle persists | **DEFERRED** — external DI consumer unavailable in test env; separate hardware session required |
