# T-23 Closeout Architect Call — Decision Brief ("R11")

> **R11** is the supervisor-session label for the T-23 closeout architect call. This brief is
> the desk artifact collecting its inputs in decision order. It is **pre-decision input**, not
> a decision record — outcomes belong in ADRs/DDRs and issue dispositions, not edits here.
>
> **Status: DRAFT — input (a) pending the #83 persistence sitting.** All other inputs banked.
> App under test throughout: build pin `ded58cf`. Living doc:
> `docs/hardware/runs/T-23-AB9.md` (`feat/23-hardware-validation`, draft PR #74).

## Decision inputs, in order

### (a) #83 severity verdict — PLACEHOLDER, pending persistence sitting

#83: emergency-stop device-wide StopAll deliberately no-ops under contention
(`DIERR_NOTEXCLUSIVEACQUIRED`) while the play path re-acquires under the same condition —
no *designed* force-halt guarantee while SC holds the device (22/22 activations no-op'd,
2026-06-08). Severity is tiered: **architectural blocker-tier unconditionally**; the
**empirical tier is gated** on the persistence sitting (probe: `tools/Moza.ScLink.DiAcquireProbe`,
merged `dc5ba79`).

Outcome matrix (fills this section after the sitting):

| Observation at probe-acquire instant | Verdict |
|---|---|
| Force **halts on acquire** | Latent gap working by accident (driver stops effects on acquisition loss) |
| Force **persists until probe Reset** | Conditional incidental — halt depends on the contender's behavior |
| Force **persists through all** | **ACTIVE FAILURE** — e-stop does not halt real force in-game; weights #83 above #82 |

### (b) #82 ship-disposition — hotkey suppressed with SC foreground

The global e-stop hotkey never fires while SC holds foreground (display-mode-independent;
elevation ruled out; H1a input-capture confirmed). AC#1 blocker. The known fix family —
low-level keyboard hook (`WH_KEYBOARD_LL`) or rawer input capture — is **anti-cheat-risky up
to account bans** for users; that risk is borne by the user, not us. Primary proposal:
**document-and-ship as a serious limitation** under the permissive Phase-1 bar
(PRP §15, T-07 AC, PRP.md:1192 — "at least one of AB6 or AB9"; #82/#71 cite this as
"line 1190", same criterion). Decision: documented limitation vs. fix attempt. Interacts
with (a): an ACTIVE-FAILURE #83 verdict makes the in-game e-stop story two-layered
(delivery AND efficacy), strengthening document-honestly framing over fix-one-layer.

### (c) #71 hot-plug ship/halt

Post-T-27 architecture decides device selection once at startup; hot-plug/unplug/replug
absent (A3/A4/A5 regression vs. M14 — blocker per Fork 4 rubric, though a *deliberate
documented deferral*, which affects fix-prioritization framing, not severity). Decision:
ship Phase-1 under the permissive bar with static-once-at-startup documented, vs. halt for
the observer wiring.

### (d) Parent synthesis: e-stop reset is incomplete across stateful stages

#83 (device stop path no-ops under contention) and #84 (limiter `_active` set never reset;
~2–4 e-stop cycles wedge FFB playback, restart-only recovery) are siblings in mechanism:
**the e-stop bypass halts the device but resets no upstream stage state**. Axes differ —
#83 is safety, #84 is availability — do not flatten. #84 fix surfaces on the table:
e-stop reset of limiter state · replace-on-StateKey semantics · #50 sustained-effect expiry.

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
slots restart-only stub) · #78 (shutdown-hang). #80 is a UI-disposition sibling handled in
its own lane.
