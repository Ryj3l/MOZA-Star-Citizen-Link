# Agentic Execution Runbook

This document tells a human operator how to drive Claude Code through the MOZA Star Citizen Link project on **Windows 11 + VS Code + Claude Code Max**. It is the operator runbook. It is *not* a spec, plan, or replacement for any other document in this repository.

## Source-of-truth hierarchy

In every session, every decision, and every conflict, this order wins:

1. `docs/PRP.md` — product and architecture requirements. Canonical.
2. `docs/PRP-ADDENDUM-replay-harness.md` — approved additive addendum for the development-only replay harness (Rung 4). Canonical for T-25 and T-26.
3. `docs/tasks/T-NN.md` — the executable task currently being worked. Canonical for deliverables.
4. `docs/decisions/` — ADRs (`ADR-NNNN-*.md`) and DDRs (`ddr-*.md`). Canonical for architectural and dependency decisions.
5. `docs/replay-bundle-schema.md` — canonical schema for replay bundles (referenced by T-25 and T-26).
6. `CLAUDE.md` — standing instructions for Claude Code. Canonical for behavior.
7. `HANDOFF.md` — bundle orientation and dependency graph.
8. `docs/AGENTIC_EXECUTION_RUNBOOK.md` — this file. Operator runbook.

If a slash command, skill, framework, or external tool wants to create files that compete with the above (e.g., `spec.md`, `plan.md`, `tasks.md`, `PROJECT.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `STATE.md`, `CONTEXT.md`, `constitution.md`), the answer is no. The PRP and task bundle already cover those concerns.

## The execution model in one sentence

> Human approves PRP → PRP decomposed into 24 task specs (plus 2 addendum tasks T-25/T-26) → one Claude Code session per task → agent reads `CLAUDE.md` + one task file → agent executes → agent verifies → agent opens PR → human merges → next task.

## Borrowed patterns and their sources

This harness borrows patterns from three frameworks without installing any of them:

- **PRP methodology (PRPs-agentic-eng-style):** Goal/Why/What/Context/Blueprint/Validation-loop structure, one-pass implementation bias, explicit acceptance criteria, validation commands in every task. Already encoded in `docs/tasks/T-NN.md` and in `docs/PRP.md`.
- **Execution discipline (GSD-style):** Fresh executor context per task, task isolation via worktrees, atomic commits, executor/reviewer separation, and the **verify-work loop** (Stage 3 below).
- **Audit and guardrails (Smith-style):** Session log = PR description; security guards = `BannedSymbols.txt` + `scripts/forbidden-api-scan.ps1`; ledger = `docs/decisions/`. The patterns are imported; the tooling is not.

None of those frameworks owns this repository. The PRP does.

## Operator's first-day setup (Windows 11)

Run these once, in order. Each step is a verify-as-you-go check.

### 1. Required tools

```powershell
# PowerShell 7+ — the canonical shell for repo scripts
pwsh --version          # should report 7.x or higher; if missing, install from https://aka.ms/powershell

# .NET SDK 8
dotnet --version        # should report 8.0.x or higher

# Git
git --version

# GitHub CLI (optional but recommended for PR creation)
gh --version

# Claude Code (the Anthropic CLI / VS Code extension you already use)
```

If any of these is missing, install before continuing. PowerShell 7 is **not** Windows PowerShell 5.1; the scripts under `scripts/` target `pwsh`.

### 2. Verify the kit

```powershell
cd <path-to-this-repo>
pwsh ./scripts/verify-setup.ps1
```

The verifier confirms tool versions, lists kit files, and checks that no Smith/GSD-style competing files have been created in this repo. If it reports problems, fix them before T-00 begins.

### 3. Sanity-check the existing build

If the repo still contains the original single-project layout (`src/MozaStarCitizen.App/MozaStarCitizen.App.csproj` and `MozaStarCitizen.sln`):

```powershell
dotnet build MozaStarCitizen.sln --configuration Release
```

This should succeed cleanly. If it doesn't, fix that first — T-01 builds a new solution side-by-side and we need a working baseline to migrate *from*.

### 4. Land the kit on a foundation branch (T-00)

```powershell
git checkout main
git pull
git checkout -b chore/00-execution-harness
git add docs/ src/ scripts/ .github/ CLAUDE.md HANDOFF.md README.md .gitignore
git commit -m "chore: land PRP v2, task bundle, runbook, and Windows operator scripts"
git push -u origin chore/00-execution-harness
```

Open a PR titled `chore: T-00 execution harness`. Self-review. Merge to `main`.

This is the **only PR you fully self-review-and-merge**. After T-00 lands, every subsequent PR runs through the three-stage task loop (below).

The CI workflow will likely fail on this commit because the new `.sln` doesn't exist yet (T-01 creates it). That's expected. **Do not turn on branch protection until after T-22 merges.**

## Two-phase execution flow

### Phase A: Serial foundation (T-01 → T-05)

One working directory. One branch. One Claude Code session. One PR per task. Five tasks total. **Approximately 6–10 hours of operator time over 1–2 days.**

**Why serial:** these tasks all touch the solution skeleton, project references, package versions, logging bootstrap, and DDRs. Parallelizing them produces merge conflicts more expensive than the time saved.

**Loop per task:**

1. From the main working directory:

   ```powershell
   git checkout main
   git pull
   pwsh ./scripts/start-task.ps1 -TaskId T-NN
   ```

   The helper creates the right branch (using the slug from the task spec), confirms `main` is clean, and prints the Claude Code prompt to copy.

2. Open Claude Code in VS Code at the repo root.

3. Paste the **Task Prompt Template** (below), substituting the task ID.

4. Wait for the agent to confirm it has read the three required files and quoted the source-of-truth hierarchy. If it doesn't, stop and retry.

5. Let it execute, then verify (Stage 3 below).

6. Review the changes manually. For T-01 through T-05, hand-review every file. Use VS Code's source-control diff view; nothing is faster.

7. `git push` and open a PR using `gh pr create` or the GitHub web UI. The PR template (`.github/PULL_REQUEST_TEMPLATE.md`) is the audit log — fill in every section.

8. Merge to `main`.

9. Repeat for the next task.

### Phase B: Parallel worktrees (T-06 onward, except T-23 and T-24)

After T-05 merges, you can fan out. Use git worktrees so each Claude Code session has its own working tree.

Set up worktrees with the helper:

```powershell
pwsh ./scripts/setup-worktrees.ps1
```

This creates the following parallel worktrees as siblings of the repo directory (so they don't pollute the main checkout):

```
..\moza-domain          → branch feat/06-core-domain-types
..\moza-directinput     → branch feat/07-vortice-directinput
..\moza-logfusion       → branch feat/11-log-sensor
..\moza-mvvm            → branch feat/09-mvvm-modernization
..\moza-infra           → branch chore/22-ci-pipeline
```

Open a separate VS Code window per worktree (File → Open Folder → pick the worktree). Run a separate Claude Code session per VS Code window. Each session executes its track per the dependency graph in `HANDOFF.md`.

**Hard constraint:** never run two Claude Code sessions in the same working directory. If you need a second view of the main checkout, use a worktree.

**T-07 is the danger task.** Migrate DirectInput in a worktree, but hand-review every line of the PR yourself and gate merge on the hardware test checklist Sections A–E (`docs/hardware/T-23-checklist.md`).

**T-23 (hardware validation) and T-24 (documentation closeout) come last.** T-23 requires a human with real MOZA hardware; T-24 is mostly writing.

### Optional Phase 1-parallel: Replay harness (T-25)

The replay harness (PRP addendum `docs/PRP-ADDENDUM-replay-harness.md`) is **development-only tooling** that lets an engineer iterate on patterns, catalogs, and rules against recorded play sessions without launching Star Citizen or plugging in a wheel.

T-25 (the light CLI) can run **in parallel with Phase 1** as soon as T-11 (log sensor + pattern library) merges. It does not gate any Phase 1 task. Treat it as a quality-of-life feature that pays for itself the first time you need to test a pattern change.

T-26 (the deep `ISensor`-backed replay) waits for T-10 (event bus) and T-12 (fusion engine). It is most naturally executed at the start of Phase 2.

Operators who do not want the replay harness in Phase 1 can defer T-25 indefinitely without affecting any other Phase 1 task. The two replay tasks are additive, not load-bearing.

## Task Prompt Template

Use this for every task. The only thing that changes is the task ID. `scripts/start-task.ps1` prints this prompt with the task ID filled in.

> Read `CLAUDE.md`, then `docs/AGENTIC_EXECUTION_RUNBOOK.md`, then `docs/tasks/T-NN.md`. Confirm in your first reply that you have read all three and quote the source-of-truth hierarchy from `CLAUDE.md`. Then execute T-NN exactly as specified in `docs/tasks/T-NN.md`. Use the branch name in the task spec. When you finish, run the Stage 3 verify checks from the runbook. Report each verify command's exit code and the last line of output. Stop after verification. Do not start the next task. Do not regenerate spec, plan, tasks, roadmap, project, requirements, state, context, or constitution files — those are not part of this project.

## The three-stage task loop

Every task spec already has acceptance criteria. This is how to actually run a task end-to-end.

### Stage 1: Read (PRP-style)

Agent reads, in order:
1. `CLAUDE.md` — standing instructions.
2. `docs/AGENTIC_EXECUTION_RUNBOOK.md` — this file.
3. `docs/tasks/T-NN.md` — the task to execute.
4. Any files referenced by the task spec (existing source, related task specs, ADRs/DDRs).

Agent confirms by quoting the source-of-truth hierarchy. If it doesn't, the operator stops the session and restarts.

### Stage 2: Execute (PRP one-pass implementation bias)

Agent implements *exactly* what the deliverables list says. Not more. Not less. If something is ambiguous, agent asks the operator a single direct question rather than guessing or inventing scope.

Acceptable expansions of scope: none in Stage 2. File an issue for follow-up work instead.

### Stage 3: Verify (GSD-style verify-work loop)

After Stage 2 finishes, before declaring done, the agent runs these checks:

1. **Build:** `dotnet build MozaStarCitizenLink.sln --configuration Release` — must succeed with zero warnings (warnings-as-errors is on).
2. **Test:** `dotnet test MozaStarCitizenLink.sln --configuration Release --no-build` — all tests pass; coverage thresholds met per `scripts/check-coverage.ps1`.
3. **Forbidden-API scan:** `pwsh ./scripts/forbidden-api-scan.ps1` — zero violations.
4. **DDR completeness:** `pwsh ./scripts/check-ddr-completeness.ps1` — every PackageReference is pre-approved or has a DDR.
5. **Task acceptance criteria:** every checkbox in `docs/tasks/T-NN.md` § Acceptance criteria checked off in the PR description.

For T-01 through T-04 (where the new solution doesn't fully exist yet) some checks won't apply — the task spec lists exactly which ones do. For T-05 onward, all five should run on every task.

**Coverage gate maturity:** `scripts/check-coverage.ps1` currently runs in `-Mode Baseline` (report-only, exits 0 regardless). The gate is promoted to `-Mode Phase1` at T-20, which enforces PRP §0.3's split (70% on Core/Effects/Fusion/Profiles, 50% on I/O / adapter / UI projects). The `-Mode Final` (post-Phase-1 hand-tuned targets) is reviewed and possibly revised during T-24 docs closeout.

If any applicable check fails, the agent must either fix it or stop and report the failure. The agent does not declare success on a failing verify.

## Forbidden behavior checklist

Repeated from PRP §13.3 and `CLAUDE.md` for emphasis. If you see any of these in a PR diff, reject the PR.

- `using SharpDX*` anywhere.
- `OpenProcess`, `ReadProcessMemory`, `WriteProcessMemory`, `VirtualAllocEx`, `CreateRemoteThread`, `NtCreateThreadEx`, `SetWindowsHookEx`, or any DLL-injection technique.
- `using Newtonsoft.Json`.
- `<PackageReference Include="FluentAssertions" Version="7.*"` or any 7.x/8.x version.
- `<PackageReference>` without either pre-approval (PRP §4.2) or a DDR in `docs/decisions/`.
- Erasing behaviors listed in PRP §14.2 — DIERR_NOTEXCLUSIVEACQUIRED re-acquisition, log truncation detection, single-instance mutex, 750ms duplicate-impact suppression.
- Compiler warnings of any severity (warnings-as-errors is on).
- Generating `spec.md`, `plan.md`, `tasks.md`, `PROJECT.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `STATE.md`, `CONTEXT.md`, or `constitution.md` files.

## PR-as-session-log

Every PR description is the audit trail for its task. The repo provides `.github/PULL_REQUEST_TEMPLATE.md` — fill in every section. Empty sections fail PR review.

## What not to install

The following are explicitly out of scope for this project at this time. They were evaluated and rejected for fit-with-existing-artifacts and Windows-compatibility reasons.

- **Smith (ATTCKDigital/smith).** Reason: `/smith` per-project init generates competing `CLAUDE.md` and `constitution.md`; feature artifacts live at `specs/NNN-*/` rather than `docs/tasks/`; `/smith-migrate-specs` capabilities undocumented; scheduler is macOS-only; we are on Windows. Patterns borrowed: session-log-as-PR-description, security-guard mental model, ledger concept. Revisit after T-05 if skills-only install (`npx skills add ATTCKDigital/smith`) seems valuable for `/smith-vault` or `/smith-reflect`.
- **GSD (gsd-build/get-shit-done).** Reason: generates competing `PROJECT.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `STATE.md`, `CONTEXT.md` files. Patterns borrowed: verify-work loop, fresh executor context, atomic commits. Tooling not adopted.
- **PRPs-agentic-eng (Wirasm/PRPs-agentic-eng).** Reason: its methodology is already encoded in our `docs/PRP.md` and `docs/tasks/T-NN.md` template — we already implement it. Re-installing would be a no-op at best, a collision at worst.

If a future task makes a strong case for revisiting any of these, file an ADR in `docs/decisions/`.

## Sequence diagram

```
T-00 (this harness, operator self-merges)
  │
  ▼
T-01 (serial) → T-02 (serial) → T-03 (serial) → T-04 (serial) → T-05 (serial)
                                    │
            ┌───────────────┬───────┴───────┬──────────────┬──────────────┐
            ▼               ▼               ▼              ▼              ▼
   Domain track     DirectInput track  Log+fusion    MVVM/UI track  Infrastructure
   T-06            T-07              T-11           T-09          T-21
   T-10            T-08              T-12           T-18          T-22
   T-13            T-17                                            
   T-14                                  │
   T-15                                  ▼
                                  T-25 (replay CLI — light)
                                  parallel to Phase 1
                                    │
                          (when dependencies clear)
                                    ▼
                          T-16, T-19, T-20
                                    │
                                    ▼
                          T-23 (hardware — requires human + AB6/AB9)
                                    │
                                    ▼
                          T-24 (documentation closeout)
                                    │
                                    ▼
                              Phase 1 ships
                                    │
                                    ▼
                          T-26 (replay sensors — deep)
                          early-Phase 2, builds on T-25 + T-10 + T-12
```

**Replay harness sequencing notes.** T-25 (light CLI) is buildable as soon as T-11 (log sensor + pattern library) lands; it does not gate any Phase 1 task and ships in parallel with the Phase 1 fan-out. T-26 (deep replay) waits for T-10 (event bus), T-12 (fusion engine), and the rest of the pipeline; it is most naturally executed at the start of Phase 2, where it pays for itself immediately during audio/screen sensor development.

## Estimated wall-clock budget

| Phase | Tasks | Time |
|---|---|---|
| Setup | T-00 | 1 hour |
| Serial foundation | T-01 → T-05 | 1–2 days |
| Domain track | T-06, T-10, T-13, T-14, T-15 | 2–3 days |
| DirectInput track | T-07 (1–3 days), T-08, T-17 | 3–5 days |
| Log + fusion track | T-11, T-12 | 2–3 days |
| MVVM/UI track | T-09, T-18 | 1–2 days |
| Infrastructure | T-21, T-22 | 1 day |
| Mid-Phase 1 wiring | T-16, T-19, T-20 | 2 days |
| Hardware validation | T-23 | 1–2 days (wall-clock includes soak tests) |
| Docs closeout | T-24 | 0.5–1 day |
| Replay CLI (light) | T-25 | 0.5–1 day, parallel to Phase 1 |
| **Phase 1 total** | 25 tasks | **2–4 weeks** with one operator + parallel agent sessions |
| Replay sensors (deep) | T-26 | 1–3 days, early Phase 2 |

That's the full plan.
