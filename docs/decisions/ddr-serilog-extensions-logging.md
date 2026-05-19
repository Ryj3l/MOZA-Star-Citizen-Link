# Dependency Decision Record: Serilog.Extensions.Logging

## Package

- **Name:** Serilog.Extensions.Logging
- **Version pinned to:** 8.0.0 (pinned via `src/Directory.Packages.props`; Central Package Management governs the version, not this DDR)
- **Source:** NuGet
- **License (SPDX identifier):** Apache-2.0
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained by the Serilog organization
- **Repository URL:** https://github.com/serilog/serilog-extensions-logging
- **Transitive dependency count:** 2 (Serilog, Microsoft.Extensions.Logging.Abstractions)

## Purpose

- **What it does:** Bridges Serilog's `ILogger` to `Microsoft.Extensions.Logging.ILogger<T>`. Concretely, it provides `SerilogLoggerProvider` / `AddSerilog()` so MEL-shaped call sites (`ILogger<T>` injected via DI) emit through Serilog's pipeline.
- **Why we need it in this product:** `Moza.ScLink.App` uses `Microsoft.Extensions.Hosting`, which expects MEL `ILogger<T>` for DI-resolved loggers. Production code in the layered libraries (DirectInput, Effects, Profiles, Telemetry) is written against `ILogger<T>` for testability. Serilog is our chosen sink/enrichment stack per `ddr-serilog.md`. Without this bridge, those two halves cannot be wired together.
- **What it replaces or enables:** Enables Serilog to satisfy the MEL `ILoggerProvider` contract. Avoids forking call sites between Serilog's static `Log.Logger` API and MEL's DI-resolved `ILogger<T>`.

## Alternatives considered

- **Option A — Serilog `ILogger` direct, no MEL bridge:** Call `Log.ForContext<T>()` or inject `Serilog.ILogger` directly throughout production code. Rejected because (1) `Microsoft.Extensions.Hosting` registers MEL `ILogger<T>` in DI by default and would need a custom replacement to inject Serilog's type; (2) MEL `ILogger<T>` is the .NET-ecosystem-standard logging contract and is what analyzer rules (CA1848 `LoggerMessage`) and most third-party packages assume; (3) tests written against `ILogger<T>` are portable and don't bind to Serilog's API surface.
- **Option B — MEL only, no Serilog at all:** Drop Serilog and use MEL's built-in providers (Console, EventSource, Debug). Rejected because the four logging modes in PRP §8.1 require structured logging, message templates, file rolling with size caps (PRP §14.2 truncation detection), and the enricher ecosystem (process/thread). MEL's built-ins do not provide structured-logging primitives; rebuilding that ourselves was rejected in `ddr-serilog.md` already.
- **Option C — chosen bridge (this package):** Adopt `Serilog.Extensions.Logging` so MEL `ILogger<T>` call sites are first-class while Serilog handles enrichment, sinks, and mode switching. Single small package (~30 KB) with no native deps; maintained by the Serilog org itself.

## Risk review

- **Maintenance risk:** Low. Maintained by the Serilog organization in lockstep with Serilog itself.
- **License risk:** Apache-2.0, no copyleft, attribution already covered by the Serilog parent entry in `NOTICE.md`.
- **Security history:** No critical CVEs in the past 24 months.
- **Runtime footprint:** ~30 KB managed assembly; no native dependencies.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (filed as part of #22 mixed-disposition side-PR; downstream beneficiary: PR #36)
- **Date:** 2026-05-19

## Validation

- [x] Builds against `net8.0` — `dotnet build` Release succeeds with TWAE=true / WarningLevel=5 in PR #36's CI pipeline (build step green; only the DDR-completeness gate failed prior to this PR).
- [x] No new analyzer warnings — TWAE+WL5 build clean across all projects on PR #36.
- [x] Bridge exercised and works for app-startup and sustained-emission paths — `Moza.ScLink.App` startup wires `AddSerilog()` and resolves `ILogger<T>` via DI. The T-07 V9 hardware re-runs (commit `233f4a5`, Sections B/C/D/E) exercised those paths; D4's 24-min soak emitted continuously without bridge error. Not a claim of comprehensive bridge-API validation — V9 covered the code paths the hardware loop exercises.
- [x] Transitive dependency tree reviewed — adds `Serilog` (already DDR'd) and `Microsoft.Extensions.Logging.Abstractions` (pre-approved as abstractions-sister in this same change set). No surprise transitive inclusions.
- [x] No `NOTICE.md` change required — Apache-2.0 attribution for the Serilog org is already present via the parent `Serilog` entry; the bridge package does not introduce a separately attributable license.
