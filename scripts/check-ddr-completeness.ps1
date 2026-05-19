<#
.SYNOPSIS
    Verifies that every PackageReference in the solution has a matching DDR
    in docs/decisions/ddr-*.md, OR is in the pre-approved list (PRP §4.2).

.DESCRIPTION
    PRP §4.0 hard rule: no package enters production code without a DDR.
    This script enforces that rule in CI.
#>

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Stop"

Write-Host "DDR completeness check running against $RepoRoot"

# Pre-approved packages from PRP §4.2 — these are documented in the PRP itself
# and have DDRs filed; we list them here for the canonical list the scanner uses.
$preApprovedPackages = @(
    "CommunityToolkit.Mvvm",
    "Serilog",
    "Serilog.Sinks.File",
    "Serilog.Settings.Configuration",
    "Serilog.Extensions.Hosting",
    "Serilog.Enrichers.Process",
    "Serilog.Enrichers.Thread",
    "Microsoft.Extensions.Hosting",
    "Microsoft.Extensions.DependencyInjection",
    "Microsoft.Extensions.Configuration",
    "Microsoft.Extensions.Configuration.Json",
    "Microsoft.Extensions.Logging",
    "Microsoft.Extensions.Logging.Abstractions",
    "Microsoft.CodeAnalysis.BannedApiAnalyzers",
    "Microsoft.CodeAnalysis.NetAnalyzers",
    "Microsoft.NET.Test.Sdk",
    "Vortice.DirectInput",
    "System.Text.Json",
    "xunit",
    "xunit.runner.visualstudio",
    "FluentAssertions",
    "NSubstitute",
    "NSubstitute.Analyzers.CSharp",
    "coverlet.collector"
)

# Find all PackageReference entries
$packageReferences = @()
Get-ChildItem -Path $RepoRoot -Recurse -Include "*.csproj","Directory.Packages.props" -ErrorAction SilentlyContinue | ForEach-Object {
    $xml = [xml](Get-Content $_.FullName -Raw)
    # PackageReference Include="..." or PackageVersion Include="..."
    foreach ($node in $xml.SelectNodes("//PackageReference[@Include] | //PackageVersion[@Include]")) {
        $packageReferences += [PSCustomObject]@{
            File    = $_.Name
            Package = $node.Include
        }
    }
}

$uniquePackages = $packageReferences | Select-Object -ExpandProperty Package -Unique | Sort-Object

Write-Host "Found $($uniquePackages.Count) unique package reference(s)."

# Find existing DDR files
$ddrFolder = Join-Path $RepoRoot "docs\decisions"
$ddrFiles = @()
if (Test-Path $ddrFolder) {
    $ddrFiles = Get-ChildItem -Path $ddrFolder -Filter "ddr-*.md" -ErrorAction SilentlyContinue | ForEach-Object { $_.BaseName }
}

$missing = @()
foreach ($pkg in $uniquePackages) {
    # Skip pre-approved
    if ($preApprovedPackages -contains $pkg) { continue }

    # Check for matching DDR (normalize: ddr-<pkg-lowercase-hyphenated>.md)
    $expectedDdr = "ddr-" + ($pkg -replace '\.','-').ToLowerInvariant()
    if ($ddrFiles -notcontains $expectedDdr) {
        $missing += [PSCustomObject]@{
            Package        = $pkg
            ExpectedDdrFile = "$expectedDdr.md"
        }
    }
}

if ($missing.Count -eq 0) {
    Write-Host "✓ DDR completeness check passed. All packages are either pre-approved or have DDRs." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "✗ DDR completeness check failed. The following packages need DDRs:" -ForegroundColor Red
Write-Host ""
$missing | Format-Table -AutoSize | Out-String | Write-Host

Write-Host "To resolve: copy docs/decisions/ddr-TEMPLATE.md to the expected filename and fill it in." -ForegroundColor Yellow

exit 1
