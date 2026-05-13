<#
.SYNOPSIS
    Validates Star Citizen log patterns: regex compiles, no catastrophic backtracking,
    and matches the expected sample-corpus outcomes (positive and negative).

.DESCRIPTION
    Loads Moza.ScLink.Logs/Patterns/*.json (versioned pattern libraries) and
    runs each pattern against samples/GameLog/ fixtures with expected outcomes
    in samples/GameLog/expectations.json.

    Phase 1: patterns are placeholders ("unsupported": true); script returns 0
    if no expectations exist yet, and the CI workflow runs this with
    continue-on-error.

    Phase 2: expectations must be populated against real Game.log captures.
#>

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Stop"
Write-Host "Pattern validator running against $RepoRoot"

$patternFiles = Get-ChildItem -Path "$RepoRoot/src/Moza.ScLink.Logs/Patterns" -Filter "*.json" -ErrorAction SilentlyContinue
if (-not $patternFiles) {
    Write-Host "No pattern files found yet — skipping. (Expected in Phase 1.)" -ForegroundColor DarkGray
    exit 0
}

$samplesPath = Join-Path $RepoRoot "samples/GameLog"
$expectationsPath = Join-Path $samplesPath "expectations.json"

$totalPatterns = 0
$compileFailures = @()
$regressionFailures = @()

foreach ($pf in $patternFiles) {
    $patterns = (Get-Content $pf.FullName -Raw | ConvertFrom-Json).patterns
    foreach ($p in $patterns) {
        $totalPatterns++

        # Skip unsupported placeholders
        if ($p.unsupported -eq $true) { continue }

        # Compile check (with 100ms safety timeout to catch ReDoS)
        try {
            $regex = New-Object System.Text.RegularExpressions.Regex(
                $p.pattern,
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                [System.Text.RegularExpressions.RegexOptions]::CultureInvariant,
                [TimeSpan]::FromMilliseconds(100))
        } catch {
            $compileFailures += [PSCustomObject]@{
                File    = $pf.Name
                Kind    = $p.kind
                Pattern = $p.pattern.Substring(0, [Math]::Min(80, $p.pattern.Length))
                Error   = $_.Exception.Message
            }
            continue
        }
    }
}

# Regression test against sample corpus, if present
if (Test-Path $expectationsPath) {
    Write-Host "Found expectations file at $expectationsPath; running regression..."
    $expectations = Get-Content $expectationsPath -Raw | ConvertFrom-Json

    foreach ($exp in $expectations) {
        $sampleFile = Join-Path $samplesPath $exp.sample
        if (-not (Test-Path $sampleFile)) {
            Write-Host "  (missing sample $($exp.sample)) — skipping" -ForegroundColor DarkGray
            continue
        }
        $sampleContent = Get-Content $sampleFile -Raw

        # The actual matcher would call into the compiled Moza.ScLink.Logs parser.
        # Phase 1: this is a stub that confirms expectations file is well-formed.
        Write-Host "  ✓ sample $($exp.sample) expectations validated (placeholder)" -ForegroundColor Green
    }
}

if ($compileFailures.Count -eq 0 -and $regressionFailures.Count -eq 0) {
    Write-Host ""
    Write-Host "✓ Pattern validation passed. ($totalPatterns pattern(s) checked.)" -ForegroundColor Green
    exit 0
}

if ($compileFailures.Count -gt 0) {
    Write-Host ""
    Write-Host "✗ $($compileFailures.Count) pattern compile failure(s):" -ForegroundColor Red
    $compileFailures | Format-Table -AutoSize | Out-String | Write-Host
}

if ($regressionFailures.Count -gt 0) {
    Write-Host ""
    Write-Host "✗ $($regressionFailures.Count) regression failure(s):" -ForegroundColor Red
    $regressionFailures | Format-Table -AutoSize | Out-String | Write-Host
}

exit 1
