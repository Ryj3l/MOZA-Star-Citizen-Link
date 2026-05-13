# Dependency Decision Record: xUnit

## Package

- **Name:** xunit, xunit.runner.visualstudio
- **Version pinned to:** 2.9.2 (2.9.3 available as patch; 2.x is now security-only — xunit v3 migration is a separate future decision)
- **Source:** NuGet
- **License (SPDX identifier):** Apache-2.0 (xunit), MIT (xunit.runner.visualstudio)
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Active
- **Repository URL:** https://github.com/xunit/xunit
- **Transitive dependency count:** ~3

## Purpose

- **What it does:** Unit testing framework.
- **Why we need it in this product:** PRP §12 requires unit tests for all projects. xUnit is the de-facto modern .NET test framework.
- **What it replaces or enables:** Enables all `tests/*.Tests` projects.

## Alternatives considered

- **NUnit:** comparable; xUnit chosen for its preference for fresh instances per test, which catches state-leak bugs.
- **MSTest:** older API; less ergonomic in 2025.

## Risk review

- **Maintenance risk:** Very low.
- **License risk:** Apache-2.0 / MIT.
- **Security history:** Clean. (Test-only package; not in production binary.)
- **Runtime footprint:** Test-only.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] All test projects build
- [ ] `dotnet test` discovers and runs all tests
- [ ] CI coverage collection works (paired with coverlet)
