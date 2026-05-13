<#
.SYNOPSIS
    Enforces per-project line-coverage thresholds from PRP §12.1.

.DESCRIPTION
    coverlet's built-in <Threshold> applies a single global minimum.
    This script parses the cobertura.xml output and enforces per-assembly
    minimums per the PRP.
#>

param(
    [string]$ResultsPath = "./TestResults",
    [hashtable]$Thresholds = @{
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
)

$ErrorActionPreference = "Stop"

Write-Host "Coverage threshold check running against $ResultsPath"

$coverageFiles = Get-ChildItem -Path $ResultsPath -Recurse -Filter "coverage.cobertura.xml" -ErrorAction SilentlyContinue

if ($coverageFiles.Count -eq 0) {
    Write-Host "✗ No coverage files found at $ResultsPath. Did 'dotnet test --collect:\"XPlat Code Coverage\"' run?" -ForegroundColor Red
    exit 1
}

$failures = @()

foreach ($file in $coverageFiles) {
    $xml = [xml](Get-Content $file.FullName -Raw)

    foreach ($package in $xml.coverage.packages.package) {
        $name = $package.name
        $rate = [double]$package.'line-rate' * 100

        if (-not $Thresholds.ContainsKey($name)) {
            Write-Host "  (no threshold defined for $name; line rate = $([math]::Round($rate,1))%) — skipping" -ForegroundColor DarkGray
            continue
        }

        $required = $Thresholds[$name]
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
Write-Host "✗ Coverage thresholds not met:" -ForegroundColor Red
$failures | Format-Table -AutoSize | Out-String | Write-Host

exit 1
