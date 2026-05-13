# ADR-0001: Use Vortice.DirectInput instead of hand-rolled COM interop or SharpDX

- **Status:** Accepted
- **Date:** 2026-05-12
- **Deciders:** Senior Architect, Engineering Lead
- **Task ID:** T-07
- **Related PRP section:** §2.3, §3.1

## Context

The existing repository implements DirectInput by hand-marshaling COM interfaces (`IDirectInput8W`, `IDirectInputDevice8W`, `IDirectInputEffect`) and structs (`DirectInputEffect`, `DirectInputPeriodic`, `DirectInputConstantForce`) directly against `dinput8.dll`. The interop code lives in `src/MozaStarCitizen.App/ForceFeedback/DirectInput/` and totals roughly 350 lines of `[ComImport]` declarations, manual `Marshal.StructureToPtr`/`AllocHGlobal`/`FreeHGlobal` pairs, and P/Invoke declarations.

This code works on real hardware (AB6 confirmed) but is hard to test, hard to mock, fragile in the face of HRESULT edge cases, and reinvents wheels that an actively maintained library has already turned. SharpDX was the canonical .NET DirectInput library for years; it was deprecated in 2019 and is no longer maintained. Its author (Amer Koleci) now maintains Vortice, which is API-compatible at the conceptual level, MIT-licensed, targets .NET 8 natively, and is actively maintained as of 2025.

The PRP v2 strategic posture is "modern stack, no SharpDX." We need to pick the replacement.

## Decision

We will use **Vortice.DirectInput** (NuGet package: `Vortice.DirectInput`) as the sole DirectInput wrapper in `Moza.ScLink.DirectInput`. We will delete the hand-rolled COM interop. We will not use SharpDX. We will introduce an `IDirectInputAbstraction` seam in the DirectInput project so the Vortice calls can be mocked in unit tests without requiring hardware.

## Alternatives considered

- **Keep the hand-rolled interop.** Rejected because it is untestable without hardware, has a high defect surface area, and produces no benefit over a maintained library.
- **Use SharpDX.** Rejected because SharpDX is deprecated, has no .NET 8 build target, and is on the PRP v2 forbidden-API list.
- **Use a different third-party wrapper (e.g., `SlimDX`, `MDX`).** Rejected: SlimDX is also abandoned; MDX is even older. Vortice is the only actively maintained option.
- **Write a thin internal wrapper over `dinput8.dll` ourselves.** Rejected because this is exactly what we have today, and it has all the same drawbacks.

## Consequences

**Easier.** Effect creation becomes high-level API calls instead of `Marshal.StructureToPtr` ballet. Unit tests against the abstraction layer become feasible. Error paths are typed exceptions instead of HRESULTs.

**Harder.** The team must learn Vortice's API surface (modest — it mirrors DirectInput concepts). Vortice is a third-party dependency, so it needs a DDR (filed as `ddr-vortice-directinput.md`).

**Follow-up work.** The CI forbidden-API scanner (T-22) must reject any `SharpDX*` or `using SharpDX*` reference. The migration in T-07 must preserve all behaviors listed in PRP §14.2 (re-acquisition, two-axis Cartesian, exclusive+background cooperative level, effect caching).

## Validation

We will know this decision was correct if (a) all seven Phase 1 effects play on AB6 and AB9 hardware after T-07 ships, (b) the unit-test coverage of the DirectInput project rises above 50%, and (c) no DirectInput regressions are reported during Phase 1 hardware validation (T-23).

We would revisit if Vortice's maintainer stopped releasing for more than 12 months, if a critical security CVE went unpatched, or if MOZA released a flight-base SDK that superseded DirectInput entirely (in which case Vortice becomes the diagnostic-only path and the MOZA SDK becomes the active output).
