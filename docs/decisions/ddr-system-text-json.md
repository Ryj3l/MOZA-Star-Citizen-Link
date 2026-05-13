# Dependency Decision Record: System.Text.Json

## Package

- **Name:** System.Text.Json
- **Version pinned to:** 8.0.x (in-box with .NET 8 — explicit reference only if newer features needed)
- **Source:** NuGet / in-box
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Microsoft, in-box .NET
- **Repository URL:** https://github.com/dotnet/runtime
- **Transitive dependency count:** 0

## Purpose

- **What it does:** Native, high-performance JSON serialization for .NET.
- **Why we need it in this product:** All settings, profiles, catalogs, fusion rules, and diagnostic bundles are JSON. PRP §2.9 mandates `System.Text.Json` over `Newtonsoft.Json`.
- **What it replaces or enables:** Already used in the existing repository via `AppSettingsStore.cs` and `StarCitizenEventParser.cs`. Continuation, not new dependency.

## Alternatives considered

- **Newtonsoft.Json:** older, slower, larger; no reason to add it.
- **MessagePack/Protobuf:** binary formats; we want human-readable settings and catalogs for tunability.

## Risk review

- **Maintenance risk:** None.
- **License risk:** MIT.
- **Security history:** Generally clean; rare deserialization CVEs in older versions, fully patched in 8.x.
- **Runtime footprint:** In-box.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** Senior Architect
- **Date:** 2026-05-12

## Validation

- [ ] Schema versioning round-trips correctly
- [ ] Atomic writes via temp-file + `File.Move` work
- [ ] Comments-in-JSON tolerance configured per PRP §2.9
