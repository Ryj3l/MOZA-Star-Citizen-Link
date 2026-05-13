# Dependency Decision Record: coverlet.collector

## Package

- **Name:** coverlet.collector
- **Version pinned to:** 6.0.2 (6.0.4 available as patch; no blocker — defer to next dependency sweep)
- **Source:** NuGet
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Active
- **Repository URL:** https://github.com/coverlet-coverage/coverlet
- **Transitive dependency count:** ~3

## Purpose

- **What it does:** Code coverage collection for .NET tests.
- **Why we need it in this product:** PRP §0.3 and §12.1 require coverage thresholds enforced in CI.

## Alternatives considered

- **dotCover (JetBrains):** commercial, license cost.
- **OpenCover:** older, less active.

## Risk review

- **Maintenance risk:** Low.
- **License risk:** MIT.
- **Security history:** Clean (test-only).
- **Runtime footprint:** Test-only.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] CI produces coverage reports in Cobertura XML format
- [ ] Coverage thresholds in `coverlet.runsettings` are enforced
- [ ] Reports merge across test projects correctly
