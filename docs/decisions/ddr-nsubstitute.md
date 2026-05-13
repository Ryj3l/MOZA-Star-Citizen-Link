# Dependency Decision Record: NSubstitute

## Package

- **Name:** NSubstitute
- **Version pinned to:** 5.1.0 (5.3.0 available as minor; no breaking changes reported — defer bump to next dependency sweep)
- **Source:** NuGet
- **License (SPDX identifier):** BSD-3-Clause
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Active
- **Repository URL:** https://github.com/nsubstitute/NSubstitute
- **Transitive dependency count:** ~2

## Purpose

- **What it does:** Mocking library for unit tests.
- **Why we need it in this product:** Mocking the `IDirectInputAbstraction`, `IFusionEngine`, `IForceFeedbackDevice`, and clock abstractions for unit tests.

## Alternatives considered

- **Moq:** has had recent licensing and analytics-collection controversies in 2023; rejected for trust reasons.
- **FakeItEasy:** comparable. NSubstitute chosen for slightly cleaner syntax.
- **Hand-written test doubles:** acceptable for small numbers of dependencies but does not scale.

## Risk review

- **Maintenance risk:** Low.
- **License risk:** BSD-3.
- **Security history:** Clean (test-only).
- **Runtime footprint:** Test-only.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] Mocks compose with FluentAssertions
- [ ] Mocking of async methods works correctly
