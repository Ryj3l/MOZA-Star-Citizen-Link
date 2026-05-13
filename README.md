# MOZA Star Citizen Link

Windows desktop app that converts Star Citizen gameplay signals into force-feedback effects on MOZA AB6 / AB9 flight bases.

> **Status:** Phase 1 in flight. Single-project repo is being migrated to a 12-project layered architecture per `docs/PRP.md`. Phase 1 = log-driven force feedback for 7 effects (quantum spool, quantum jump exit, atmosphere entry/exit, landing contact, weapon fire, vehicle destruction). Phase 2 adds audio + screen sensors with a fusion engine.

## For end users

Phase 1 ships as a private beta when T-24 lands. Public-facing user docs live under `docs/user/` (created in T-24). Until then, this repo is engineering-only.

## For engineers and operators

You are most likely here because Claude Code is going to do most of the work. Start here:

1. Read `HANDOFF.md` — the bundle orientation and dependency graph.
2. Read `docs/PRP.md` — the canonical Product Requirements Prompt.
3. Read `docs/AGENTIC_EXECUTION_RUNBOOK.md` — the operator runbook for driving Claude Code through the 24-task migration.
4. Read `CLAUDE.md` — standing instructions for any Claude Code session in this repo.

Then run the setup verifier:

```powershell
pwsh ./scripts/verify-setup.ps1
```

And start T-01:

```powershell
pwsh ./scripts/start-task.ps1 -TaskId T-01
```

The helper prints the exact prompt to paste into Claude Code.

## Repo layout

```
docs/
  PRP.md                                Canonical Product Requirements Prompt
  PRP-ADDENDUM-replay-harness.md        Addendum: development-only replay harness (T-25, T-26)
  replay-bundle-schema.md               Canonical schema for replay bundles
  AGENTIC_EXECUTION_RUNBOOK.md          Operator runbook for Claude Code execution
  tasks/                                26 executable task specs (T-01 … T-24, T-25, T-26)
  decisions/                            ADRs and DDRs
  hardware/                             Manual hardware test checklist
src/
  Directory.Build.props                 Shared MSBuild properties
  Directory.Packages.props              Centralized package versions
  .editorconfig                         Code-style enforcement
  BannedSymbols.txt                     Forbidden API list for Roslyn analyzer
  coverlet.runsettings                  Coverage collection config
  phase1-effect-catalog.json            7 Phase 1 effects with parameter values
  device-profiles.json                  AB6 / AB9 device profiles
  device-allowlist.json           Device allowlist enforcement
scripts/
  verify-setup.ps1                Pre-flight check for operators
  start-task.ps1                  Branch creation + Claude Code prompt
  setup-worktrees.ps1             Parallel-execution worktree helper
  forbidden-api-scan.ps1          Banned-pattern regex scanner
  check-ddr-completeness.ps1      DDR audit for every PackageReference
  check-coverage.ps1              Per-project coverage threshold enforcement
  validate-patterns.ps1           Log-pattern regex validity check
.github/
  workflows/ci.yml                Build → test+coverage → forbidden-API → DDR → preview ZIP
  PULL_REQUEST_TEMPLATE.md        PR-as-session-log template
CLAUDE.md                         Standing instructions for Claude Code
HANDOFF.md                        Bundle orientation and dependency graph
```

## Hard rules

These are repeated in `CLAUDE.md` and are not negotiable:

1. No SharpDX. Use Vortice.DirectInput.
2. No memory inspection or DLL injection against Star Citizen.
3. No Newtonsoft.Json. Use System.Text.Json.
4. FluentAssertions pinned to 6.x (7.x went commercial).
5. Every `<PackageReference>` is either pre-approved or has a DDR.
6. Preserve existing behaviors listed in PRP §14.2 during migration.
7. TreatWarningsAsErrors=true.
8. Force commands only to allowlisted devices (`src/device-allowlist.json`).

## Requirements

- Windows 11
- VS Code with Claude Code Max
- .NET SDK 8
- PowerShell 7+ (`pwsh`)
- Git
- GitHub CLI (`gh`) — optional but recommended for PR creation
- MOZA AB6 or AB9 hardware (required for T-23 hardware validation; preview mode works without)

## Development

### Replay harness (T-25, T-26)

Once T-25 lands, engineers can iterate on patterns, catalogs, and rules against recorded play sessions without launching Star Citizen:

```powershell
# Light CLI (T-25): runs Game.log through the pattern library, emits events.jsonl
dotnet run --project tools/Moza.ScLink.Replay -- replay tools/recordings/<bundle>

# Deep replay (T-26): also runs the fusion engine, resolver, and safety limiter
dotnet run --project tools/Moza.ScLink.Replay -- replay tools/recordings/<bundle> --deep

# Validate a bundle's structure
dotnet run --project tools/Moza.ScLink.Replay -- validate tools/recordings/<bundle>

# Strip a rich bundle to feature-only for sharing
dotnet run --project tools/Moza.ScLink.Replay -- strip tools/recordings/<rich> tools/recordings/<stripped>
```

See `docs/PRP-ADDENDUM-replay-harness.md` for the full design and `docs/replay-bundle-schema.md` for the bundle format. Replay is **development-only tooling** — no replay code path is reachable from the App's Release build.

## License

(To be added in T-24.)
