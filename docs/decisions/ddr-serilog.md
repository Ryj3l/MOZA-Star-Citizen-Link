# Dependency Decision Record: Serilog

## Package

- **Name:** Serilog
- **Version pinned to:** See Decision History below for version
- **Source:** NuGet (https://www.nuget.org/packages/Serilog)
- **License (SPDX identifier):** Apache-2.0
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained; foundational .NET logging library; regular releases
- **Repository URL:** https://github.com/serilog/serilog
- **Transitive dependency count:** 0 in core

## Purpose

- **What it does:** Structured logging core. Provides `ILogger`, log enrichers, sinks API, message templates.
- **Why we need it in this product:** Replaces the hand-rolled `AppLog` static class with structured, level-filtered, multi-sink logging that supports the four logging modes in PRP §8.1. Required for diagnostic mode, private beta capture, and developer-lab verbose tracing.
- **What it replaces or enables:** Replaces `AppLog.cs`. Enables `Serilog.Sinks.File`, `Serilog.Settings.Configuration`, future `Sentry` sink.

## Alternatives considered

- **Microsoft.Extensions.Logging only:** missing the structured-logging ergonomics and sink ecosystem; would require building sinks ourselves.
- **NLog:** comparable functionality, but Serilog has stronger structured-logging primitives and better tooling integration in 2025.
- **log4net:** older API, weaker structured logging.

## Risk review

- **Maintenance risk:** Very low.
- **License risk:** Apache-2.0, no copyleft.
- **Security history:** No critical CVEs in core in the past 24 months.
- **Runtime footprint:** ~200 KB assembly.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision History

### 2026-05-12 — Initial pin: 4.0.0

- **Version pinned to:** 4.0.0
- **Status:** Superseded by 2026-05-13 bump
- **Approver:** Senior Architect
- **Date approved:** 2026-05-12
- **Rationale:** Initial Serilog selection per PRP §8.1.

### 2026-05-13 — Bump to 4.2.0 (T-04)

- **Version pinned to:** 4.2.0
- **Status:** Active
- **Approver:** PENDING OPERATOR REVIEW (this bump is being reviewed as part of PR #11)
- **Date approved:** 2026-05-13 (pending)
- **Rationale:** Serilog.Settings.Configuration 9.0.0 requires Serilog >= 4.2.0. The 4.0.0 → 4.2.0 bump is a transitive constraint surfaced during T-04 execution. Still within the originally-evaluated 4.x family (Apache-2.0, low maintenance risk, no new CVEs).
- **Discovered during:** T-04 (PR #11)

## Validation

- [ ] Builds against `net8.0`
- [ ] Logging mode switching works per PRP §8.1
- [ ] File rolling and size cap behave correctly (verified in T-04)
- [ ] No new analyzer warnings
- [ ] `NOTICE.md` includes Apache-2.0 attribution
