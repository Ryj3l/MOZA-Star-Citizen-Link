# Dependency Decision Record: Microsoft.Extensions.Hosting

## Package

- **Name:** Microsoft.Extensions.Hosting
- **Version pinned to:** 8.0.x (LTS, matched to .NET 8)
- **Source:** NuGet
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Microsoft / .NET Foundation
- **Repository URL:** https://github.com/dotnet/runtime
- **Transitive dependency count:** ~5 (all Microsoft.Extensions.* sub-packages)

## Purpose

- **What it does:** Generic Host pattern: DI container, configuration, hosted background services, graceful shutdown.
- **Why we need it in this product:** The app has multiple long-running background services (log tailer, fusion engine, output worker, retention worker). Hosting them under `IHost` standardizes startup/shutdown, dependency injection, and cancellation propagation.
- **What it replaces or enables:** Replaces ad-hoc `Task.Run` background work in `MainViewModel`. Enables clean DI throughout.

## Alternatives considered

- **No host, manual lifecycle:** what the existing code does. Works at one-window scope but does not scale to 5+ background services with shared cancellation. Rejected.
- **Custom DI container (Autofac, etc.):** unnecessary; the built-in Microsoft.Extensions.DependencyInjection container is sufficient.

## Risk review

- **Maintenance risk:** None (in-box .NET).
- **License risk:** MIT.
- **Security history:** Clean for hosting package; CVEs in .NET runtime are tracked separately.
- **Runtime footprint:** Modest; mostly already loaded.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** Senior Architect
- **Date:** 2026-05-12

## Validation

- [ ] Background services start/stop cleanly on app shutdown
- [ ] Cancellation token propagates to all services
- [ ] Graceful shutdown completes within 5 seconds
