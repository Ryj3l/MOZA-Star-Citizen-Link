# Project: MOZA Star Citizen Link

## What you are working on

A Windows WPF desktop app that converts Star Citizen gameplay signals into force-feedback effects on MOZA AB6/AB9 flight bases. Currently mid-migration from a single-project hand-rolled implementation to the layered architecture specified in `docs/PRP.md`.

## Agentic execution model

This repository uses a PRP-first execution model. The methodology is inspired by PRPs-agentic-eng, with selected execution patterns from GSD and selected audit/guardrail patterns from Smith. Those frameworks are not installed in this repository and do not own any artifacts. The PRP and task bundle are canonical.

Source-of-truth hierarchy (highest authority first):

1. `docs/PRP.md` — product and architecture requirements
2. `docs/PRP-ADDENDUM-replay-harness.md` — additive addendum for the development-only replay harness (covers T-25 and T-26)
3. `docs/tasks/T-NN.md` — the task currently being executed
4. `docs/decisions/` — ADRs and DDRs
5. `docs/replay-bundle-schema.md` — replay bundle format (referenced by T-25 and T-26)
6. `CLAUDE.md` — this file
7. `HANDOFF.md` — bundle orientation
8. `docs/AGENTIC_EXECUTION_RUNBOOK.md` — operator runbook

Do not generate competing `spec.md`, `plan.md`, `tasks.md`, `PROJECT.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `STATE.md`, `CONTEXT.md`, or `constitution.md` files. If a slash command or skill suggests creating them, decline and reference this section. The PRP and task bundle already cover their concerns.

Execute exactly one task per branch. Run Stage 3 verify checks from `docs/AGENTIC_EXECUTION_RUNBOOK.md` after every task. Stop after verification — do not chain into the next task.

## Replay harness (development only)

T-25 and T-26 add a development-only replay capability under `tools/Moza.ScLink.Replay/` and `src/Moza.ScLink.Replay.Sensors/`. The harness records or consumes bundles of past play sessions and replays them through the sensor stack offline. It is **not a runtime feature** — no replay code path is reachable from the `Moza.ScLink.App` Release build, and no replay artifact ever ships in a user-facing release. Bundles stay on the recording machine by default (`.gitignore` excludes `tools/recordings/`). See `docs/PRP-ADDENDUM-replay-harness.md` for the full posture and `docs/replay-bundle-schema.md` for the on-disk format.

## Hard rules (these are not negotiable)

1. **No SharpDX.** Use Vortice.DirectInput. See `docs/decisions/ADR-0001-vortice-directinput.md`.
2. **No memory inspection of the Star Citizen process.** No `OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, `NtCreateThreadEx`, `SetWindowsHookEx`, or any DLL-injection technique.
3. **No Newtonsoft.Json.** Use `System.Text.Json`.
4. **FluentAssertions pinned to 6.x.** Version 7+ went commercial. Do not bump. See `docs/decisions/ddr-fluentassertions.md`.
5. **Every `<PackageReference>` is either pre-approved (PRP §4.2) or has a DDR in `docs/decisions/`.**
6. **Existing behaviors listed in PRP §14.2 must be preserved during migration** — DirectInput re-acquisition on `DIERR_NOTEXCLUSIVEACQUIRED`, log truncation detection, single-instance mutex, 750 ms duplicate-impact suppression, effect-cache composite keying, two-axis Cartesian setup with Exclusive+Background cooperative level.
7. **TreatWarningsAsErrors=true and WarningLevel=5.** No warnings of any severity are acceptable.
8. **No force commands to non-allowlisted devices.** The allowlist is `src/device-allowlist.json`. Driving anything else is forbidden.

## Working agreement

- **Branch naming:** `feat/<task-id>-<slug>`, `chore/<task-id>-<slug>`, `fix/<task-id>-<slug>`.
- **Conventional Commits.** Every commit message starts with `feat:`, `fix:`, `chore:`, `docs:`, `test:`, or `refactor:`.
- **One task per branch.** Do not bundle T-06 and T-07 into the same PR.
- **Definition of Done:** the acceptance criteria checklist at the bottom of the task spec must all be checked.
- **Before opening a PR**, run locally: `dotnet build`, `dotnet test`, `pwsh ./scripts/forbidden-api-scan.ps1`, `pwsh ./scripts/check-ddr-completeness.ps1`.

## Style

- File-scoped namespaces.
- `var` everywhere it's idiomatic.
- Private fields `_camelCase`. Public PascalCase.
- See `src/.editorconfig` for the enforced rules.

## When you are uncertain

Ask. Do not invent. The PRP, the task specs, and the DDRs cover most of it; if they don't cover your situation, that is exactly the moment to stop and check with the human operator.

## Platform note

The human operator is on Windows 11 + VS Code + Claude Code Max. PowerShell 7+ (`pwsh`) is the canonical shell for repo scripts. Bash/curl-only tools should not be added to the build path without an ADR.
