# T-23 Closeout Architect Call — Decision Brief ("R11")

> **R11** is the supervisor-session label for the T-23 closeout architect call. This brief is
> the desk artifact collecting its inputs in decision order. It is **pre-decision input**, not
> a decision record — outcomes belong in ADRs/DDRs and issue dispositions, not edits here.
>
> **Status: inputs COMPLETE — §(a) filled 2026-06-10 from the persistence sitting** (living-doc
> block @ `80a66be`). E2 (M9 re-acquire ≤5 s) was attempted in the same sitting and is
> **NOT closed** — no displacement occurred, nothing to measure; the probe as built cannot
> deliver it. App under test throughout: build pin `ded58cf`. Living doc:
> `docs/hardware/runs/T-23-AB9.md` (`feat/23-hardware-validation`, draft PR #74).

## Decision inputs, in order

### (a) #83 severity verdict — FILLED 2026-06-10 (persistence sitting; living doc @ `80a66be`)

Architectural tier (unconditional blocker-tier): unchanged. **Empirical tier — one finding,
three legs: force persists until an explicit FFB Reset; the incidental driver halt the no-op
disposition relies on does not exist at any acquisition boundary; SC's exact displacement
state remains unreproduced, so the end-to-end contended chain is inferred from a stronger
condition, not observed.**

- Persists through foreign exclusive acquisition (GUID-pinned Exclusive|Foreground on the
  app's exact instance, 21:05:01.895; sustained Exclusive|Background hold, 21:13:34.429).
  Acquisition alone never halts force.
- Persists through **owner-process death**: app closed mid-effect, process fully exited, the
  AB9 rendered the effect **ownerless** until the probe's FFB Reset halted it. The driver
  performs no cleanup at unacquire or at process exit.
- Fidelity boundary: the probe's acquires **coexisted** with the app's Exclusive|Background
  hold (the app's StopAlls ran clean, 3.11–3.79 ms, while the probe held; zero EventId 12 all
  sitting) — `DIERR_NOTEXCLUSIVEACQUIRED` was never reproduced. Higher fidelity requires SC
  itself or a probe that *uses* the device (improvement queue: per-GUID ACQUIRED lines ·
  file-backed log · input-polling mode).

**Matrix row: persists-until-Reset → conditional incidental**, sharper than the matrix
anticipated: a contender halts our force only by issuing its own FFB reset; merely taking the
device does not. Synthesis with 06-08 (22/22 contended no-ops): the no-op disposition's
rationale is **empirically void as a safety argument** — under SC contention, playing force
has neither a designed nor an incidental halt. Full record: #83 verdict comment
(`issuecomment-4677126283`); living-doc sitting block @ `80a66be`.

### (b) #82 ship-disposition — hotkey suppressed with SC foreground

The global e-stop hotkey never fires while SC holds foreground (display-mode-independent;
elevation ruled out; H1a input-capture confirmed). AC#1 blocker. The known fix family —
low-level keyboard hook (`WH_KEYBOARD_LL`) or rawer input capture — is **anti-cheat-risky up
to account bans** for users; that risk is borne by the user, not us. Primary proposal:
**document-and-ship as a serious limitation** under the permissive Phase-1 bar
(PRP §15, T-07 AC, PRP.md:1192 — "at least one of AB6 or AB9"; #82/#71 cite this as
"line 1190", same criterion). Decision: documented limitation vs. fix attempt. Interacts
with (a): the landed verdict (persists-until-Reset; no designed or incidental halt under
contention) makes the in-game e-stop story two-layered in substance (delivery AND efficacy),
strengthening document-honestly framing over fix-one-layer.

### (c) #71 hot-plug ship/halt

Post-T-27 architecture decides device selection once at startup; hot-plug/unplug/replug
absent (A3/A4/A5 regression vs. M14 — blocker per Fork 4 rubric, though a *deliberate
documented deferral*, which affects fix-prioritization framing, not severity). Decision:
ship Phase-1 under the permissive bar with static-once-at-startup documented, vs. halt for
the observer wiring.

### (d) Parent synthesis: e-stop reset is incomplete across stateful stages — now THREE siblings

#83 (device stop path no-ops under contention), #84 (limiter `_active` set never reset; ~2–4
e-stop cycles wedge FFB playback, restart-only recovery), and **#88** (post-e-stop muted
resurrection — filed from the 2026-06-10 sitting) are siblings in mechanism: **the e-stop
halts the device but resets no stage state — limiter, effect-cache, or device-side.** #88's
two properties, do not flatten: **silent failure** (post-e-stop cache-hit replay `Start()`
returns S_OK, renders nothing, app reports success — worse-shaped than #84's noisy rejection)
and **delayed surprise force** (an unrelated fresh CreateEffect un-mutes the e-stopped effect
minutes later; observed ×1, gap ~3 m 54 s). Axes: #83 safety · #84 availability · #88 both
(silent failure = availability; resurrection = safety-adjacent). Fix surfaces overlap across
all three: e-stop reset semantics · effect-cache invalidation on e-stop /
re-download-on-replay · replace-on-StateKey · #50 sustained-effect expiry.

### (e) Non-preemption finding

The e-stop bypass is **wake-immediate but execution-non-preemptive** (living doc,
`### Section C (emergency stop)` — characterization under its Methodology subsection, :556):
the bypass wakes immediately, but `StopAllAsync` runs only at the top of the loop iteration —
it does not interrupt an in-flight command or the drain of an already-readable queue.
Theoretical ceiling 64 × ~1 ms ≈ **64 ms** (> 50 ms budget) → finding — unchanged.

**Realistic bound, as corrected by the D2 branch ruling** (living doc D2 block, `b104c4b`;
#84 reconciliation comment `issuecomment-4674301478`): **no upstream dedup** (Branch A,
confirmed on both effects) — mashing builds admitted entries up to the limiter cap, so
realistic depth **≤ 4** (cap), realistic worst **~4 ms**; the **well-under-budget conclusion
survives**. Note for readers: Section C's frozen Results text (:595) retains the superseded
"realistic depth ≈ 1–2 (dedup-confirmed)" figure by accumulate-style design — the D2 block
is the current ruling; read the frozen figure as superseded, not contradictory.

Bounded-latency characterization, not a defect disposition — input to how strong a
halt-guarantee Phase-1 claims in writing.

### (f) Environment-capture process finding + rig-config preflight product issue

The 2026-06-09 env fault (Integrated-FFB mode + 0% in-mode gain → device ACKs, zero force,
multi-hour diagnosis) yielded two artifacts: the **process fix** (every hardware run records
the MOZA Cockpit state block up front — already operating), and the **product requirement**
(issue #86: first-run documentation of required Cockpit state + preflight warning where
detectable). Ask: prioritize #86 (docs-only vs. detection spike; Phase-1 vs. post).

**New R11 items from the 2026-06-10 sitting:** **#88** (muted resurrection — §(d) third
sibling; fix-priority call) and the **#78 severity upgrade** (the close path issues zero
shutdown lines and never stops the device; with no driver cleanup at process exit, app close
mid-effect = indefinite ownerless force until external Reset or power-off; the shutdown audit
must include the device-stop path, not only host-lifetime/thread-drain; was quality-of-life →
proposed safety-adjacent).

### (g) Section G soak sequencing — decide FIRST among rig actions

Soak-current-build vs. fix-then-soak. #84 wedges playback after ~2–4 e-stop cycles; a 4-hour
G3 soak that wedges at hour one wastes rig time. If soak-current is chosen, the G protocol
carries the #84 active-count drift ledger as an instrument. This decision gates the last rig
work, so it should be taken even if (a)–(f) dispositions are deferred.

### (h) Corrected strategic framing

MOZA has **not** shipped SC force feedback: SC is compatibility-listed only (no telemetry
tag), absent from the Cockpit telemetry app list (Elite Dangerous is present), zero effects
confirmed first-hand. **First-to-market window OPEN.** Structural point: SC exposes no
telemetry API, so MOZA's telemetry-fed model has no data source — our Game.log event parsing
is the path they haven't taken. That is a **head start, not a wall; speed matters.** The
mode question (DirectInput vs. Integrated) is an onboarding hazard — see (f) — not a
competitive conflict. Standing watch: MOZA firmware/Cockpit release cadence, recurring.

## Carried issues NOT inputs to this call

#30 (baseline reset posted; contention-attenuation open/untested) · #81 · #32 (quantum
slots restart-only stub). #78 moved INTO the call's inputs by its 2026-06-10 severity
upgrade — see the routing block under §(f). #80 is a UI-disposition sibling handled in
its own lane.
