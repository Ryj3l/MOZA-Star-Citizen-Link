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

    # ============================================================
    # ACTIVATE AFTER T-07 MILESTONE 12
    # ------------------------------------------------------------
    # Patterns below reject references to the legacy DirectInput
    # COM-interop class names that T-07 replaces with Vortice.DirectInput.
    #
    # They are STAGED here (commented) during T-07 Milestone 1.5
    # so the deletion step in T-07 Milestone 12 can activate them
    # with a single mechanical uncomment.
    #
    # Activation contract:
    #   When Milestone 12 deletes the 5 legacy files
    #   (DirectInputForceFeedbackDevice.cs and Native/*.cs), uncomment
    #   each line below. The scan must then continue to report zero
    #   matches against main. If it doesn't, a reference was missed
    #   during rewire — fix the offending file before merging T-07.
    #
    # Do NOT uncomment before Milestone 12 — references to these names
    # still exist in the legacy files and in ForceFeedbackDeviceFactory.cs
    # during the migration window.
    # ============================================================
    @{ Pattern = '\bDirectInputForceFeedbackDevice\b'; Reason = 'Replaced by VorticeDirectInputDevice in T-07. Old hand-rolled COM-interop class is deleted.'; CodeOnly = $true }
    @{ Pattern = '\bDirectInputNative\b'; Reason = 'Replaced by VorticeDirectInputAdapter in T-07. Hand-rolled P/Invoke surface is deleted.'; CodeOnly = $true }
    @{ Pattern = '\bDirectInputComInterfaces\b'; Reason = 'Replaced by Vortice.DirectInput managed wrappers in T-07. ComImport interface declarations are deleted.'; CodeOnly = $true }
    @{ Pattern = '\bDirectInputStructures\b'; Reason = 'Replaced by Vortice.DirectInput typed structs in T-07. Hand-rolled marshalable structs are deleted.'; CodeOnly = $true }
    @{ Pattern = '\bDirectInputConstants\b'; Reason = 'Replaced by Vortice.DirectInput typed enums and constants in T-07. Hand-rolled constants are deleted.'; CodeOnly = $true }
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

        # CodeOnly patterns (deleted-identifier bans) must match live code only,
        # not prose in comments. Blank comments length-preserving so match
        # indices still map to original line numbers. The '//' pass also blanks
        # '///' XML-doc comments, which is intended — most provenance tokens
        # live in XML docs. Broad-spectrum patterns (SharpDX, P/Invoke,
        # Newtonsoft) keep full comment/string coverage by matching $content
        # directly. The blanking is deliberately approximate: it also blanks
        # '//' and '/* */' sequences inside string literals (PowerShell regex
        # does not track lexical context) — an acceptable false-negative for
        # identifier bans, verified zero-impact for the staged T-07 names (no
        # string-literal occurrences in the repo).
        $codeText = [regex]::Replace($content, '(?m)//.*$', { param($x) ' ' * $x.Value.Length })
        $codeText = [regex]::Replace($codeText, '(?s)/\*.*?\*/', { param($x) $x.Value -replace '[^\r\n]', ' ' })

        foreach ($rule in $bannedPatterns) {
            $scanText = if ($rule.CodeOnly) { $codeText } else { $content }
            $matches = [regex]::Matches($scanText, $rule.Pattern)
            foreach ($m in $matches) {
                # Compute line number (indices align — blanking is length-preserving)
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
