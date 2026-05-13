# Dependency Decision Record: Serilog.Sinks.File

## Package

- **Name:** Serilog.Sinks.File
- **Version pinned to:** 6.0.0 (latest stable in 6.x as of 2026-05-13)
- **Source:** NuGet
- **License (SPDX identifier):** Apache-2.0
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained, part of the Serilog org
- **Repository URL:** https://github.com/serilog/serilog-sinks-file
- **Transitive dependency count:** 1 (Serilog)

## Purpose

- **What it does:** File sink for Serilog with rolling, size-based archival, retention.
- **Why we need it in this product:** PRP §2.8 specifies daily-rolling local file logs at `%LOCALAPPDATA%\MozaStarCitizen\logs\` with 14-day retention and 50 MB per-file cap. This sink implements exactly that.
- **What it replaces or enables:** Replaces `File.AppendAllText` calls in `AppLog`.

## Alternatives considered

- **Custom file sink:** rejected. Solved problem; we should not maintain it.
- **`Serilog.Sinks.RollingFile`:** older sink, superseded by `Serilog.Sinks.File` with rolling support.

## Risk review

- **Maintenance risk:** Low.
- **License risk:** Apache-2.0.
- **Security history:** Clean.
- **Runtime footprint:** ~50 KB.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] Daily rolling works (verified by clock manipulation in T-04 tests)
- [ ] 50 MB cap triggers archive
- [ ] 14-day retention cleans old files
- [ ] No analyzer warnings
