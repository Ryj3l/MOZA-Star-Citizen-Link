# Dependency Decision Record: coverlet.collector

## Package

- **Name:** coverlet.collector
- **Version pinned to:** 6.0.x
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
- **Approver:** Senior Architect
- **Date:** 2026-05-12

## Validation

- [ ] CI produces coverage reports in Cobertura XML format
- [ ] Coverage thresholds in `coverlet.runsettings` are enforced
- [ ] Reports merge across test projects correctly
