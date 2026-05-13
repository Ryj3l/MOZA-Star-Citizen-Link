# Dependency Decision Record: FluentAssertions

## Package

- **Name:** FluentAssertions
- **Version pinned to:** 6.12.2 (latest stable in 6.x as of 2026-05-13; 6.x line is at end-of-life following 7.0 commercial pivot)

> **⚠️ Licensing note (read before adoption):** FluentAssertions 7.0+ changed to a paid commercial license. **We pin to the 6.x line, which remains Apache-2.0.** If the 6.x line is later abandoned, re-open this DDR and consider migration to `Shouldly` (BSD) or `xUnit`'s built-in `Assert`.

- **Source:** NuGet
- **License (SPDX identifier):** Apache-2.0 (for 6.x line)
- **License-compatible with proprietary distribution:** Yes (6.x)
- **Maintainer activity:** 6.x line maintenance status uncertain after the 7.0 commercial pivot; monitor
- **Repository URL:** https://github.com/fluentassertions/fluentassertions
- **Transitive dependency count:** ~2

## Purpose

- **What it does:** Fluent-style assertions for tests (`actual.Should().Be(expected)`).
- **Why we need it in this product:** Test readability. Optional — we could use xUnit's built-in `Assert`.

## Alternatives considered

- **xUnit Assert (built-in):** less ergonomic but zero new dependency. Acceptable fallback if FluentAssertions 6.x becomes unmaintained.
- **Shouldly:** BSD-licensed, similar ergonomics. Viable migration target.

## Risk review

- **Maintenance risk:** Elevated. The 7.0 commercial pivot suggests the maintainer wants paid support; 6.x maintenance is best-effort. Plan a migration if 6.x becomes stale.
- **License risk:** Low for 6.x (Apache-2.0). **Do not upgrade past 6.x without re-doing this DDR.**
- **Security history:** Clean (test-only).
- **Runtime footprint:** Test-only.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free for 6.x.

## Decision

- **Approved with pin to 6.x and migration plan**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] Version pinned to 6.12.x exactly in `Directory.Packages.props`
- [ ] CI fails if a 7.x version is introduced (forbidden-API scanner regex)
- [ ] Migration target (Shouldly) documented for future reference
