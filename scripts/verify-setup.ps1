<#
.SYNOPSIS
    Pre-flight verification for the MOZA Star Citizen Link execution harness.

.DESCRIPTION
    Confirms required tools are installed at acceptable versions, the kit
    artifacts are present, and no competing framework files have been created
    in the repository. Run this once at the start of a session and again any
    time you suspect the repo has drifted.

    Returns exit code 0 on full success, 1 on any failure.

.EXAMPLE
    pwsh ./scripts/verify-setup.ps1
#>

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Continue"
$failures = @()
$warnings = @()

function Write-Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

function Test-Tool {
    param(
        [string]$Name,
        [string]$Command,
        [string]$VersionArg = "--version",
        [string]$MinVersionPattern,
        [switch]$Required
    )

    $cmd = Get-Command $Command -ErrorAction SilentlyContinue
    if (-not $cmd) {
        if ($Required) {
            $script:failures += "Missing required tool: $Name ($Command)"
            Write-Host "  ✗ $Name not found" -ForegroundColor Red
        } else {
            $script:warnings += "Missing optional tool: $Name ($Command)"
            Write-Host "  ! $Name not found (optional)" -ForegroundColor Yellow
        }
        return
    }

    try {
        $version = & $Command $VersionArg 2>&1 | Select-Object -First 1
        if ($MinVersionPattern -and $version -notmatch $MinVersionPattern) {
            $script:warnings += "${Name} version may be too old: '$version' (expected to match '$MinVersionPattern')"
            Write-Host "  ! ${Name}: $version (expected to match $MinVersionPattern)" -ForegroundColor Yellow
        } else {
            Write-Host "  ✓ ${Name}: $version" -ForegroundColor Green
        }
    } catch {
        $script:warnings += "$Name found but version check failed: $_"
        Write-Host "  ! $Name found but version check failed" -ForegroundColor Yellow
    }
}

function Test-File {
    param(
        [string]$RelativePath,
        [switch]$Required
    )

    $full = Join-Path $RepoRoot $RelativePath
    if (Test-Path $full) {
        Write-Host "  ✓ $RelativePath" -ForegroundColor Green
        return $true
    } else {
        if ($Required) {
            $script:failures += "Missing required file: $RelativePath"
            Write-Host "  ✗ $RelativePath" -ForegroundColor Red
        } else {
            Write-Host "  - $RelativePath (optional, not present)" -ForegroundColor DarkGray
        }
        return $false
    }
}

function Test-CompetingFile {
    param([string]$RelativePath, [string]$Reason)

    $full = Join-Path $RepoRoot $RelativePath
    if (Test-Path $full) {
        $script:failures += "Competing framework artifact detected: $RelativePath ($Reason)"
        Write-Host "  ✗ $RelativePath present — $Reason" -ForegroundColor Red
    } else {
        Write-Host "  ✓ $RelativePath absent" -ForegroundColor Green
    }
}

# =============================================================================
# Banner
# =============================================================================
Write-Host ""
Write-Host "MOZA Star Citizen Link — Execution Harness Verification" -ForegroundColor Cyan
Write-Host "Repo root: $RepoRoot"
Write-Host "Run date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"

# =============================================================================
# Section 1: Required tools
# =============================================================================
Write-Section "Required tools"
Test-Tool -Name "PowerShell 7+" -Command "pwsh" -VersionArg "--version" -MinVersionPattern "PowerShell 7" -Required
Test-Tool -Name ".NET SDK"      -Command "dotnet" -VersionArg "--version" -MinVersionPattern "^([89]|[1-9]\d)\." -Required
Test-Tool -Name "Git"           -Command "git" -VersionArg "--version" -Required

Write-Section "Optional tools"
Test-Tool -Name "GitHub CLI" -Command "gh" -VersionArg "--version"

# =============================================================================
# Section 2: Kit artifacts present
# =============================================================================
Write-Section "Canonical artifacts"
Test-File "docs/PRP.md" -Required
Test-File "docs/PRP-ADDENDUM-replay-harness.md" -Required
Test-File "docs/replay-bundle-schema.md" -Required
Test-File "docs/AGENTIC_EXECUTION_RUNBOOK.md" -Required
Test-File "CLAUDE.md" -Required
Test-File "HANDOFF.md" -Required

Write-Section "Task specs (24 Phase 1 + 2 addendum)"
$taskCount = 0
1..24 | ForEach-Object {
    $id = "T-{0:D2}" -f $_
    if (Test-File "docs/tasks/$id.md" -Required) { $taskCount++ }
}
# T-25 and T-26 are addendum tasks; required in the kit but not gating any Phase 1 task
$addendumCount = 0
25..26 | ForEach-Object {
    $id = "T-{0:D2}" -f $_
    if (Test-File "docs/tasks/$id.md" -Required) { $addendumCount++ }
}
Write-Host ""
Write-Host "  Phase 1 tasks present:  $taskCount / 24" -ForegroundColor $(if ($taskCount -eq 24) { "Green" } else { "Red" })
Write-Host "  Addendum tasks present: $addendumCount / 2 (T-25, T-26 — replay harness)" -ForegroundColor $(if ($addendumCount -eq 2) { "Green" } else { "Red" })

Write-Section "Decision records"
Test-File "docs/decisions/TEMPLATE.md" -Required
Test-File "docs/decisions/ddr-TEMPLATE.md" -Required
Test-File "docs/decisions/ADR-0001-vortice-directinput.md" -Required
$ddrFiles = Get-ChildItem -Path (Join-Path $RepoRoot "docs/decisions") -Filter "ddr-*.md" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "ddr-TEMPLATE.md" }
Write-Host "  DDRs present: $($ddrFiles.Count) (expected ≥ 12 for Phase 1)" -ForegroundColor $(if ($ddrFiles.Count -ge 12) { "Green" } else { "Yellow" })

Write-Section "MSBuild starters and catalogs"
Test-File "src/Directory.Build.props" -Required
Test-File "src/Directory.Packages.props" -Required
Test-File "src/.editorconfig" -Required
Test-File "src/BannedSymbols.txt" -Required
Test-File "src/coverlet.runsettings" -Required
Test-File "src/phase1-effect-catalog.json" -Required
Test-File "src/device-profiles.json" -Required
Test-File "src/device-allowlist.json" -Required

Write-Section "Operator scripts"
Test-File "scripts/verify-setup.ps1" -Required
Test-File "scripts/start-task.ps1" -Required
Test-File "scripts/setup-worktrees.ps1" -Required
Test-File "scripts/forbidden-api-scan.ps1" -Required
Test-File "scripts/check-ddr-completeness.ps1" -Required
Test-File "scripts/check-coverage.ps1" -Required
Test-File "scripts/validate-patterns.ps1" -Required

Write-Section "CI and PR template"
Test-File ".github/workflows/ci.yml" -Required
Test-File ".github/PULL_REQUEST_TEMPLATE.md" -Required

Write-Section "Hardware checklist"
Test-File "docs/hardware/T-23-checklist.md" -Required

# =============================================================================
# Section 3: No competing framework artifacts
# =============================================================================
Write-Section "No competing framework artifacts (collision check)"
Test-CompetingFile ".smith" "Smith framework not installed in this repo (see docs/AGENTIC_EXECUTION_RUNBOOK.md § What not to install)"
Test-CompetingFile ".specify" "Smith .specify/ not present"
Test-CompetingFile "constitution.md" "Smith constitution.md not present"
Test-CompetingFile "PROJECT.md" "GSD PROJECT.md not present"
Test-CompetingFile "REQUIREMENTS.md" "GSD REQUIREMENTS.md not present"
Test-CompetingFile "ROADMAP.md" "GSD ROADMAP.md not present"
Test-CompetingFile "STATE.md" "GSD STATE.md not present"
Test-CompetingFile "CONTEXT.md" "GSD CONTEXT.md not present"
Test-CompetingFile "spec.md" "Top-level spec.md not present (per-task specs live in docs/tasks/)"
Test-CompetingFile "plan.md" "Top-level plan.md not present"
Test-CompetingFile "tasks.md" "Top-level tasks.md not present"

# =============================================================================
# Section 4: Git state sanity
# =============================================================================
Write-Section "Git state"
Push-Location $RepoRoot
try {
    git rev-parse --is-inside-work-tree | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $branch = git rev-parse --abbrev-ref HEAD
        Write-Host "  ✓ Inside a git repo. Current branch: $branch" -ForegroundColor Green
        $dirty = git status --porcelain
        if ($dirty) {
            Write-Host "  ! Working tree is not clean:" -ForegroundColor Yellow
            $dirty -split "`n" | Select-Object -First 8 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
            $warnings += "Working tree dirty"
        } else {
            Write-Host "  ✓ Working tree clean" -ForegroundColor Green
        }
    } else {
        $failures += "Not inside a git repository"
        Write-Host "  ✗ Not a git repository" -ForegroundColor Red
    }
} catch {
    $failures += "git rev-parse failed: $_"
}
Pop-Location

# =============================================================================
# Summary
# =============================================================================
Write-Section "Summary"
if ($warnings.Count -gt 0) {
    Write-Host "  Warnings ($($warnings.Count)):" -ForegroundColor Yellow
    $warnings | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
}

if ($failures.Count -eq 0) {
    Write-Host ""
    Write-Host "✓ Pre-flight verification passed. Ready to run T-01." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next: pwsh ./scripts/start-task.ps1 -TaskId T-01" -ForegroundColor Cyan
    exit 0
}

Write-Host ""
Write-Host "✗ Pre-flight verification failed. $($failures.Count) issue(s):" -ForegroundColor Red
$failures | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
Write-Host ""
Write-Host "Fix the above before starting task execution." -ForegroundColor Yellow
exit 1
