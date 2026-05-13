<#
.SYNOPSIS
    Enforces per-project line-coverage thresholds from PRP §0.3 / §12.1
    under one of three operating modes.

.DESCRIPTION
    coverlet's built-in <Threshold> applies a single global minimum.
    This script parses the cobertura.xml output and enforces per-assembly
    minimums per the PRP, with the mode controlling whether failures
    actually fail the build.

    Modes:
      Baseline  Report-only. Prints the would-fail table for visibility
                but always exits 0. Used T-02 through T-19 while the
                test suites for migrated code are still being authored.
      Phase1    Enforces Phase 1 thresholds per PRP §0.3:
                70% on domain projects (Core, Effects, Fusion, Profiles),
                50% on I/O / adapter / UI projects (Logs, Audio, Screen,
                DirectInput, Diagnostics, App, Input, Updater).
                Fails the build on shortfall. Used T-20 onward.
      Final     Enforces the post-Phase-1 hand-tuned targets (Core 80%,
                Effects 90%, etc.). Reviewed and possibly revised during
                T-24 docs closeout based on achieved coverage.

    Coverage gate Baseline -> Phase1 mode flip lands in T-20 per PRP §0.3
    and the runbook's Stage 3 maturity notes.
#>

param(
    [string]$ResultsPath = "./TestResults",
    [ValidateSet("Baseline", "Phase1", "Final")]
    [string]$Mode = "Baseline"
)

$ErrorActionPreference = "Stop"

# Phase 1 thresholds (PRP §0.3): 70% for domain, 50% elsewhere.
# Input and Updater don't have csprojs yet (Phase 2 per PRP §3); listing
# them here is a no-op now and protects against forgetting them later.
$phase1Thresholds = @{
    # Tighter gate (70%) — pure domain logic, easy to test in isolation
    "Moza.ScLink.Core"        = 70
    "Moza.ScLink.Effects"     = 70
    "Moza.ScLink.Fusion"      = 70
    "Moza.ScLink.Profiles"    = 70
    # Looser gate (50%) — I/O, adapter, and UI projects with platform deps
    "Moza.ScLink.Logs"        = 50
    "Moza.ScLink.Audio"       = 50
    "Moza.ScLink.Screen"      = 50
    "Moza.ScLink.DirectInput" = 50
    "Moza.ScLink.Diagnostics" = 50
    "Moza.ScLink.App"         = 50
    "Moza.ScLink.Input"       = 50
    "Moza.ScLink.Updater"     = 50
}

# Final thresholds — post-Phase-1 hand-tuned targets.
# Revisit during T-24 review based on actual achieved coverage.
$finalThresholds = @{
    "Moza.ScLink.Core"         = 80
    "Moza.ScLink.Effects"      = 90
    "Moza.ScLink.Fusion"       = 85
    "Moza.ScLink.Profiles"     = 80
    "Moza.ScLink.Diagnostics"  = 70
    "Moza.ScLink.Logs"         = 80
    "Moza.ScLink.DirectInput"  = 50
    "Moza.ScLink.Audio"        = 60
    "Moza.ScLink.Screen"       = 50
    "Moza.ScLink.App"          = 40
}

# Baseline reports against the Final targets so the table reflects the
# eventual aspiration; it just never fails the build.
$thresholds = switch ($Mode) {
    "Phase1" { $phase1Thresholds }
    "Final"  { $finalThresholds }
    default  { $finalThresholds }   # Baseline
}

Write-Host "Coverage threshold check (Mode=$Mode) running against $ResultsPath"

$coverageFiles = Get-ChildItem -Path $ResultsPath -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue

if ($coverageFiles.Count -eq 0) {
    Write-Host "✗ No coverage files found at $ResultsPath. Did 'dotnet test --collect:`"XPlat Code Coverage`"' run?" -ForegroundColor Red
    exit 1
}

$failures = @()

foreach ($file in $coverageFiles) {
    $xml = [xml](Get-Content $file.FullName -Raw)

    foreach ($package in $xml.coverage.packages.package) {
        $name = $package.name
        $rate = [double]$package.'line-rate' * 100

        if (-not $thresholds.ContainsKey($name)) {
            Write-Host "  (no threshold defined for $name; line rate = $([math]::Round($rate,1))%) — skipping" -ForegroundColor DarkGray
            continue
        }

        $required = $thresholds[$name]
        if ($rate -lt $required) {
            $failures += [PSCustomObject]@{
                Project   = $name
                Required  = "$required%"
                Actual    = "$([math]::Round($rate,1))%"
                Shortfall = "$([math]::Round($required - $rate, 1))%"
            }
        } else {
            Write-Host "  ✓ $name : $([math]::Round($rate,1))% (≥ $required%)" -ForegroundColor Green
        }
    }
}

if ($failures.Count -eq 0) {
    Write-Host ""
    Write-Host "✓ All coverage thresholds met." -ForegroundColor Green
    exit 0
}

Write-Host ""
if ($Mode -eq "Baseline") {
    Write-Host "Coverage shortfall (informational, not failing the build):" -ForegroundColor Yellow
} else {
    Write-Host "✗ Coverage thresholds not met:" -ForegroundColor Red
}
$failures | Format-Table -AutoSize | Out-String | Write-Host

if ($Mode -eq "Baseline") {
    Write-Host "Mode=Baseline — report-only. Coverage gate not enforcing build status. Flip to Phase1 in T-20 per runbook." -ForegroundColor Yellow
    exit 0
}

exit 1
