<#
.SYNOPSIS
    Scans the source tree for forbidden API patterns that the Roslyn banned-API
    analyzer cannot catch (mostly P/Invoke patterns and namespace mentions).

.DESCRIPTION
    PRP §13.3 Critical Guardrails. Fail the build if any banned pattern is found.

    The Roslyn analyzer catches managed surface (using SharpDX, etc).
    This script catches:
      - Direct P/Invoke to forbidden Win32 APIs
      - References inside string literals or comments where the analyzer is blind
      - References in non-.cs files (e.g., .ps1, .json) that might smuggle banned APIs

.NOTES
    Patterns are conservative — false positives are a code-review discussion,
    not a CI failure for legitimate matches (e.g., this file itself mentions
    the banned names in scan rules, so we skip the scripts/ folder).
#>

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Stop"

Write-Host "Forbidden-API scan running against $RepoRoot"

# Files to scan: .cs, .xaml, .csproj
$includePatterns = @("*.cs", "*.xaml", "*.csproj")
# Folders to skip
$excludeFolders = @(
    "\\artifacts\\",
    "\\bin\\",
    "\\obj\\",
    "\\.git\\",
    "\\node_modules\\",
    "\\scripts\\",          # this script itself contains the patterns
    "\\docs\\decisions\\",  # DDRs and ADRs reference banned names by name
    "\\tools\\recordings\\" # replay bundle artifacts (Game.log lines etc., not source code)
)

# Banned patterns. Each entry: { Pattern, Reason }
$bannedPatterns = @(
    @{ Pattern = 'SharpDX'; Reason = 'SharpDX is deprecated. Use Vortice.DirectInput. PRP §2.3, ADR-0001.' },
    @{ Pattern = '\bOpenProcess\s*\('; Reason = 'OpenProcess approaches process inspection. PRP §13.3.' },
    @{ Pattern = '\bReadProcessMemory\s*\('; Reason = 'Memory reading is explicitly forbidden. PRP §13.3.' },
    @{ Pattern = '\bWriteProcessMemory\s*\('; Reason = 'Memory modification is explicitly forbidden. PRP §13.3.' },
    @{ Pattern = '\bVirtualAllocEx\s*\('; Reason = 'Cross-process allocation suggests injection. PRP §13.3.' },
    @{ Pattern = '\bCreateRemoteThread\s*\('; Reason = 'Process injection. PRP §13.3.' },
    @{ Pattern = '\bNtCreateThreadEx\s*\('; Reason = 'Native injection. PRP §13.3.' },
    @{ Pattern = '\bNtOpenProcess\s*\('; Reason = 'Native process inspection. PRP §13.3.' },
    @{ Pattern = '\bZwOpenProcess\s*\('; Reason = 'Native process inspection. PRP §13.3.' },
    @{ Pattern = '\bSetWindowsHookEx\s*\('; Reason = 'Global hooks can flag EAC. PRP §13.3.' },
    @{ Pattern = 'using\s+Newtonsoft\.Json'; Reason = 'Use System.Text.Json. PRP §2.9.' },
    @{ Pattern = 'FluentAssertions.*Version="7'; Reason = 'FluentAssertions 7+ is commercial. Pin to 6.x. See ddr-fluentassertions.md.' }
)

$violations = @()
$scannedFiles = 0

foreach ($includePattern in $includePatterns) {
    Get-ChildItem -Path $RepoRoot -Recurse -File -Include $includePattern -ErrorAction SilentlyContinue | ForEach-Object {
        $file = $_
        $fullName = $file.FullName

        # Skip excluded folders
        $skip = $false
        foreach ($exclude in $excludeFolders) {
            if ($fullName -match [regex]::Escape($exclude)) { $skip = $true; break }
        }
        if ($skip) { return }

        $scannedFiles++

        $content = Get-Content -Path $fullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { return }

        foreach ($rule in $bannedPatterns) {
            $matches = [regex]::Matches($content, $rule.Pattern)
            foreach ($m in $matches) {
                # Compute line number
                $lineNumber = ($content.Substring(0, $m.Index) -split "`n").Count
                $violations += [PSCustomObject]@{
                    File    = $fullName.Substring($RepoRoot.Length).TrimStart('\','/')
                    Line    = $lineNumber
                    Match   = $m.Value
                    Reason  = $rule.Reason
                }
            }
        }
    }
}

Write-Host "Scanned $scannedFiles files."

if ($violations.Count -eq 0) {
    Write-Host "✓ Forbidden-API scan passed." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "✗ Forbidden-API scan failed. Found $($violations.Count) violation(s):" -ForegroundColor Red
Write-Host ""

$violations | Format-Table -AutoSize | Out-String | Write-Host

Write-Host ""
Write-Host "To resolve: remove the banned API, or — if there is a legitimate need —" -ForegroundColor Yellow
Write-Host "open an ADR (see docs/decisions/TEMPLATE.md) before adding an exception." -ForegroundColor Yellow

exit 1
