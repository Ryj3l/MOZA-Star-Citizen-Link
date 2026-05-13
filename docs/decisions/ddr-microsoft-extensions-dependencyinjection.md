# Dependency Decision Record: Microsoft.Extensions.DependencyInjection

## Package

- **Name:** Microsoft.Extensions.DependencyInjection
- **Version pinned to:** 8.0.1 (latest stable in 8.0.x as of 2026-05-13)
- **Source:** NuGet
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Microsoft / .NET Foundation
- **Repository URL:** https://github.com/dotnet/runtime
- **Transitive dependency count:** 1 (Microsoft.Extensions.DependencyInjection.Abstractions)

## Purpose

- **What it does:** Built-in DI container for .NET.
- **Why we need it in this product:** Ten project libraries, multiple sensors, fusion engine, resolver, output device — DI is mandatory for testability and lifecycle management.
- **What it replaces or enables:** Replaces ad-hoc `new` object graphs and static singletons in the existing code. Enables `IHost`-based service registration and constructor injection throughout all projects.

## Alternatives considered

- **Autofac, Lamar, SimpleInjector:** more features but unnecessary; the built-in container is more than sufficient for this app.
- **Service locator pattern:** anti-pattern; rejected.

## Risk review

- **Maintenance risk:** None.
- **License risk:** MIT.
- **Security history:** Clean.
- **Runtime footprint:** In-box.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] All services resolve correctly at startup
- [ ] Scoped vs singleton lifetimes verified
- [ ] No captive dependency anti-patterns
