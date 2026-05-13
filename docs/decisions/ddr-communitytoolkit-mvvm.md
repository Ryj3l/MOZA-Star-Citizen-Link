# Dependency Decision Record: CommunityToolkit.Mvvm

## Package

- **Name:** CommunityToolkit.Mvvm
- **Version pinned to:** 8.4.0 (8.4.2 available as patch; no blocker to adopting — defer to next dependency sweep)
- **Source:** NuGet (https://www.nuget.org/packages/CommunityToolkit.Mvvm)
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained by Microsoft / .NET Foundation; regular releases through 2025
- **Repository URL:** https://github.com/CommunityToolkit/dotnet
- **Transitive dependency count:** 1 (System.ComponentModel.Annotations via target framework)

## Purpose

- **What it does:** Source-generated `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`, `IMessenger` for MVVM.
- **Why we need it in this product:** Eliminates hand-rolled `INotifyPropertyChanged` boilerplate in view models; produces zero-allocation property notifications via source generators; aligns with current .NET MVVM best practice.
- **What it replaces or enables:** Replaces the hand-rolled `INotifyPropertyChanged` and `RelayCommand` patterns in the existing `MainViewModel.cs` and `RelayCommand.cs`.

## Alternatives considered

- **ReactiveUI:** powerful but heavier and pulls in `System.Reactive`. Overkill for this app's needs and adds a learning curve.
- **Prism:** larger framework with regions, navigation, modularity — too much surface for a single-window app.
- **Hand-rolled `INotifyPropertyChanged`:** what the existing code does. Verbose, error-prone (string property names), no source-gen optimization. Rejected.

## Risk review

- **Maintenance risk:** Very low. Microsoft-stewarded, .NET Foundation project.
- **License risk:** MIT, no copyleft.
- **Security history:** No CVEs in the past 24 months.
- **Runtime footprint:** ~150 KB assembly, no native dependencies.
- **Anti-cheat-adjacency risk:** None.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] Builds against `net8.0-windows10.0.19041.0`
- [ ] View model refactor in T-09 passes existing UI smoke tests
- [ ] Runtime smoke test: property change notifications fire as expected in WPF binding
- [ ] No new analyzer warnings
- [ ] No new `NOTICE.md` entry required beyond standard MIT attribution
