# Dependency Decision Record: Vortice.DirectInput

## Package

- **Name:** Vortice.DirectInput
- **Version pinned to:** 3.6.2 (3.8.3 is latest in 3.x as of 2026-05-13; bump requires re-testing effects on AB6/AB9 hardware — defer to T-07/T-23)
- **Source:** NuGet (https://www.nuget.org/packages/Vortice.DirectInput)
- **License (SPDX identifier):** MIT
- **License-compatible with proprietary distribution:** Yes
- **Maintainer activity:** Actively maintained by Amer Koleci (former SharpDX maintainer); regular releases through 2025
- **Repository URL:** https://github.com/amerkoleci/Vortice.Windows
- **Transitive dependency count:** 1 (Vortice.Win32, in-org)

## Purpose

- **What it does:** Modern, .NET 8-native managed wrappers for DirectInput 8 (`IDirectInput8`, `IDirectInputDevice8`, `IDirectInputEffect`).
- **Why we need it in this product:** Replaces the hand-rolled COM interop in the existing repository. Required for testability of the DirectInput output path. Required by the PRP §2.3 modern-stack mandate.
- **What it replaces or enables:** Replaces `DirectInputNative.cs`, `DirectInputComInterfaces.cs`, `DirectInputStructures.cs`. Enables `IDirectInputAbstraction` seam for unit tests.

## Alternatives considered

- **SharpDX:** Deprecated since 2019, no .NET 8 build target, explicitly forbidden by PRP §13.3. Rejected.
- **Hand-rolled interop (status quo):** Untestable without hardware, high defect surface area, reinvents wheels. Rejected per ADR-0001.
- **SlimDX, MDX, other legacy wrappers:** All abandoned. Rejected.
- **Build a thin internal wrapper:** Effectively what we have today; same rejection reasons.

See ADR-0001 for full rationale.

## Risk review

- **Maintenance risk:** Low. Active single-maintainer project; if maintenance lapsed, we could fork it because it's MIT and the source is small (~5k lines for DirectInput).
- **License risk:** MIT, fully compatible.
- **Security history:** No known CVEs.
- **Runtime footprint:** ~300 KB managed assembly + dependency on `dinput8.dll` (already shipping on Windows).
- **Anti-cheat-adjacency risk:** None. DirectInput is a public, sanctioned Windows API. Nothing about Vortice changes that.
- **Cost:** Free.

## Decision

- **Approved**
- **Approver:** PENDING OPERATOR REVIEW (initial pre-approval bundle commit via T-05)
- **Date:** 2026-05-13

## Validation

- [ ] All seven Phase 1 effects play on AB6 hardware (T-07, T-23)
- [ ] All seven Phase 1 effects play on AB9 hardware (T-07, T-23)
- [ ] Re-acquisition on `DIERR_NOTEXCLUSIVEACQUIRED` works (mocked unit test)
- [ ] Re-acquisition on `DIERR_NOTDOWNLOADED` works (mocked unit test)
- [ ] Effect cache reduces effect-creation calls under repeated triggers
- [ ] No SharpDX references in the solution
- [ ] No new analyzer warnings
- [ ] `NOTICE.md` includes MIT attribution
