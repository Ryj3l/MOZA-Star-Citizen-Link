# MOZA Star Citizen Link — Engineering Handoff Artifacts

> **Document role:** This is the bundle orientation document. It maps the artifact set and the task dependency graph. For operator instructions on how to *drive* execution, see `docs/AGENTIC_EXECUTION_RUNBOOK.md`. For the canonical product/architecture spec, see `docs/PRP.md`.

These files are the belt-and-suspenders companion to `docs/PRP.md`. They close every gap identified in the readiness review and make Phase 1 executable by Claude Opus agents (or human engineers) with minimal architect involvement.

## What's in this bundle

### Task specs (24 files in `docs/tasks/`)

One file per task in the PRP §15 decomposition. Each has: dependencies, phase, effort estimate, branch name, context, deliverables, acceptance criteria, implementation notes, non-goals.

| Task | Title | Effort | Dependencies |
|---|---|---|---|
| T-01 | Solution restructure with empty projects | S | — |
| T-02 | Move existing code to new project homes | M | T-01 |
| T-03 | Centralized package versions | S | T-02 |
| T-04 | Serilog bootstrap and AppLog shim | M | T-03 |
| T-05 | Pre-approved-dependency DDRs | S | T-03 |
| T-06 | Core domain types | M | T-02 |
| T-07 | Vortice.DirectInput output device | L | T-02, T-05 |
| T-08 | Device allowlist and detection | S | T-07 |
| T-09 | MVVM modernization | M | T-04 |
| T-10 | Channels-based event bus | M | T-06 |
| T-11 | Log sensor refactor | M | T-06, T-10 |
| T-12 | Fusion engine with rule loader | L | T-06, T-10, T-11 |
| T-13 | Phase 1 effect catalog | S | T-06 |
| T-14 | Effect resolver with gain stack | M | T-06, T-13 |
| T-15 | Safety limiter | M | T-06, T-14 |
| T-16 | Emergency stop end-to-end | M | T-07, T-10, T-15 |
| T-17 | Preview mode formalization | S | T-07, T-14 |
| T-18 | Diagnostics panel | M | T-09, T-10, T-11, T-12, T-16, T-17 |
| T-19 | Settings + diagnostic bundle | M | T-04, T-18 |
| T-20 | Pattern hot reload | S | T-11 |
| T-21 | Park screen-capture in legacy/ | S | T-02 |
| T-22 | CI pipeline | S | T-03, T-05 |
| T-23 | Hardware validation pass | L | T-07–T-22 |
| T-24 | Phase 1 documentation pack | M | T-23 |
| **T-25** | **Light replay CLI (addendum)** | **M** | **T-06, T-11** |
| **T-26** | **Deep replay sensors (addendum)** | **L** | **T-10, T-12, T-14, T-15, T-25** |

**Critical sequencing notes for kickoff:**

- T-01 → T-02 → T-03 → T-04 → T-05 should run **serial** on a single agent/engineer in one session. These are the foundation and parallelizing them will cost more in merge conflicts than serial execution saves.
- After T-05 lands, the work fans out in parallel along these tracks:
  - **Domain track:** T-06 → T-10, T-13 → T-14 → T-15
  - **DirectInput track:** T-07 → T-08 → T-17
  - **Log+fusion track:** T-11 → T-12
  - **MVVM/UI track:** T-09 → T-18
  - **Infrastructure track:** T-21, T-22 (can start immediately after T-05)
- T-16 (emergency stop) needs T-07, T-10, T-15 — relatively late.
- T-19 (settings + bundle) needs T-04 and T-18.
- T-20 needs T-11.
- T-23 is the integration gate; it waits for everything.
- T-24 is the documentation closeout; it waits for T-23.
- **T-25 (replay CLI, light)** is from the PRP addendum (`docs/PRP-ADDENDUM-replay-harness.md`). It can run in parallel with the rest of Phase 1 once T-11 merges. It is additive; deferring it to Phase 2 is fine.
- **T-26 (replay sensors, deep)** waits for T-10, T-12, T-14, T-15, and T-25. Most naturally executed at the start of Phase 2.

### Decision records (`docs/decisions/`)

- `TEMPLATE.md` — ADR template
- `ddr-TEMPLATE.md` — DDR template
- `ADR-0001-vortice-directinput.md` — worked example showing the ADR format
- 12 pre-filled DDRs for the Phase 1 dependencies (one per package per PRP §4.2)

### MSBuild starters (`src/`)

- `Directory.Build.props` — shared properties (warnings-as-errors, deterministic build, central package mgmt enabled)
- `Directory.Packages.props` — central package versions for all Phase 1 dependencies
- `BannedSymbols.txt` — forbidden API list for the Roslyn analyzer
- `.editorconfig` — code style enforcement
- `coverlet.runsettings` — coverage collection config

### CI workflow (`.github/workflows/`)

- `ci.yml` — build → test+coverage → coverage thresholds → forbidden-API scan → DDR completeness → pattern lint → PR preview build

### Scripts (`scripts/`)

- `forbidden-api-scan.ps1` — regex scan for SharpDX, banned Win32 P/Invokes, Newtonsoft.Json, FluentAssertions v7+
- `check-ddr-completeness.ps1` — verifies every PackageReference is pre-approved or has a DDR
- `check-coverage.ps1` — enforces per-project coverage thresholds (Cobertura parser)
- `validate-patterns.ps1` — regex compilation + sample-corpus regression for log patterns

### JSON catalogs (`src/`)

- `phase1-effect-catalog.json` — 7 Phase 1 effects with first-pass parameter values
- `device-profiles.json` — AB6 and AB9 device capability descriptors
- `device-allowlist.json` — allowlist enforcement for MOZA AB6/AB9

### Hardware test (`docs/hardware/`)

- `T-23-checklist.md` — 8-section manual hardware test checklist that gates T-07 PR merge and is the primary deliverable of T-23

## How to use this bundle

1. Read the PRP v2 (`MOZA_Star_Citizen_Link_PRP_v2.md`) first. The PRP is canonical; this bundle implements it.
2. Drop the contents of this bundle into the project repo, preserving the directory structure (`docs/`, `src/`, `scripts/`, `.github/`).
3. Run T-01 through T-05 serially.
4. Fan out from there per the dependency graph above.

## What this bundle does NOT replace

- The PRP v2 itself. Refer to it for: architectural rationale, interface contracts, the gain stack formula, the safety constants' justifications, the agent operating model, the 15-step migration sequence.
- Your judgment as the architect/reviewer. The task specs are detailed enough that an agent can execute them, but novel situations will arise. Have a human in the loop on each PR.
- The hardware test signoff. T-23 requires a real human with real MOZA hardware. No amount of documentation substitutes for that pass.
