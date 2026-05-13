# Dependency Decision Record: Serilog.Settings.Configuration

## Package

- **Name:** Serilog.Settings.Configuration
- **Version pinned to:** 9.0.0 (or latest 9.x stable)
- **Source:** NuGet
- **License (SPDX identifier):** Apache-2.0
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained
- **Repository URL:** https://github.com/serilog/serilog-settings-configuration
- **Transitive dependency count:** 2 (Serilog, Microsoft.Extensions.Configuration)

## Purpose

- **What it does:** Configures Serilog from `IConfiguration` (e.g., `appsettings.json`).
- **Why we need it in this product:** The four logging modes in PRP §8.1 are toggled at runtime; routing this through `Microsoft.Extensions.Configuration` lets the modes live in JSON, be hot-reloadable, and be overridable per user profile.
- **What it replaces or enables:** Enables declarative logging configuration.

## Alternatives considered

- **Code-only Serilog configuration:** valid for a fixed config, but our mode-switching needs hot reload.
- **Custom configuration plumbing:** rejected — solved problem.

## Risk review

- **Maintenance risk:** Low.
- **License risk:** Apache-2.0.
- **Security history:** Clean.
- **Runtime footprint:** ~70 KB.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** Senior Architect
- **Date:** 2026-05-12

## Validation

- [ ] Mode switching at runtime via configuration reload works
- [ ] Per-mode log levels apply correctly
- [ ] No analyzer warnings
