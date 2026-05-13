# Dependency Decision Record: <Package Name>

> File this as `docs/decisions/ddr-<package-name-lowercase>.md`. Every `<PackageReference>` in a `.csproj` must either be in the pre-approved Phase 1 list in PRP §4.2 or have a DDR in this folder. The CI DDR-completeness check enforces this.

## Package

- **Name:**
- **Version pinned to:**
- **Source:** (NuGet / GitHub / vendored)
- **License (SPDX identifier):**
- **License-compatible with proprietary distribution:** Yes / No
- **Maintainer activity:** (date of last meaningful release, open-issue ratio)
- **Repository URL:**
- **Transitive dependency count:**

## Purpose

- **What it does:**
- **Why we need it in this product:**
- **What it replaces or enables:**

## Alternatives considered

- **Option A:** (with brief tradeoff analysis)
- **Option B:**
- **Build it ourselves:**

## Risk review

- **Maintenance risk:**
- **License risk:**
- **Security history (known CVEs in the past 24 months):**
- **Runtime footprint (assembly size, native deps):**
- **Anti-cheat-adjacency risk:** (any chance this could trigger EAC heuristics?)
- **Cost (commercial/per-seat):**

## Decision

- **Approved / Rejected / Deferred**
- **Approver:**
- **Date:**

## Validation

- [ ] Builds against target framework
- [ ] Passes existing test suite
- [ ] Runtime smoke test passes
- [ ] No new analyzer warnings
- [ ] No new license-text addition required in `NOTICE.md` (or addition has been made)
- [ ] Transitive dependency tree reviewed (no surprise inclusions)

---

*Template version 1.*
