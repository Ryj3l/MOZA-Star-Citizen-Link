<#
.SYNOPSIS
    Creates git worktrees for parallel Phase B execution.

.DESCRIPTION
    After T-05 merges, several tracks become independent and can be executed
    in parallel by separate Claude Code sessions. Each parallel session needs
    its own working tree to avoid conflicting writes.

    This script creates worktrees as siblings of the repo directory using the
    naming convention from docs/AGENTIC_EXECUTION_RUNBOOK.md § Phase B.

    REFUSES to run if main does not contain T-05's merge — running parallel
    sessions before the foundation lands is a known antipattern that produces
    merge conflicts.

.PARAMETER Tracks
    Which tracks to set up. Default is all five. Useful values:
        Domain, DirectInput, LogFusion, Mvvm, Infra, All

.PARAMETER Force
    Skip the T-05-merged guard. Use only if you have a documented reason.

.EXAMPLE
    pwsh ./scripts/setup-worktrees.ps1

.EXAMPLE
    pwsh ./scripts/setup-worktrees.ps1 -Tracks Domain,DirectInput

.NOTES
    Naming convention from the runbook:
        ../moza-domain         feat/06-core-domain-types
        ../moza-directinput    feat/07-vortice-directinput
        ../moza-logfusion      feat/11-log-sensor
        ../moza-mvvm           feat/09-mvvm-modernization
        ../moza-infra          chore/22-ci-pipeline

    Each worktree starts with the same branch the runbook's dependency graph
    lists as the first task in that track. Subsequent tasks in the same track
    create their own branches inside the same worktree.
#>

[CmdletBinding()]
param(
    [ValidateSet("Domain", "DirectInput", "LogFusion", "Mvvm", "Infra", "All")]
    [string[]]$Tracks = @("All"),

    [switch]$Force,

    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Stop"

$trackDefinitions = @{
    "Domain"      = @{ Folder = "moza-domain";      Branch = "feat/06-core-domain-types" }
    "DirectInput" = @{ Folder = "moza-directinput"; Branch = "feat/07-vortice-directinput" }
    "LogFusion"   = @{ Folder = "moza-logfusion";   Branch = "feat/11-log-sensor" }
    "Mvvm"        = @{ Folder = "moza-mvvm";        Branch = "feat/09-mvvm-modernization" }
    "Infra"       = @{ Folder = "moza-infra";       Branch = "chore/22-ci-pipeline" }
}

if ($Tracks -contains "All") {
    $tracksToProcess = $trackDefinitions.Keys
} else {
    $tracksToProcess = $Tracks
}

Push-Location $RepoRoot
try {
    # Guard 1: working tree clean
    $dirty = git status --porcelain
    if ($dirty -and -not $Force) {
        Write-Host "✗ Working tree is not clean. Commit or stash before creating worktrees." -ForegroundColor Red
        git status --short
        exit 1
    }

    # Guard 2: T-05 merged check (look for evidence that the foundation tasks landed)
    if (-not $Force) {
        Write-Host "→ Checking that T-05 has merged to main..." -ForegroundColor DarkCyan
        $hasFoundationBuild = Test-Path (Join-Path $RepoRoot "MozaStarCitizenLink.sln")
        $hasCentralPackages = Test-Path (Join-Path $RepoRoot "src/Directory.Packages.props")
        # Look for a Serilog reference anywhere under src/ as a proxy for T-04 having shipped
        $hasSerilog = $false
        if (Test-Path (Join-Path $RepoRoot "src")) {
            $hasSerilog = (Get-ChildItem -Path (Join-Path $RepoRoot "src") -Recurse -Include "*.csproj","appsettings*.json" -ErrorAction SilentlyContinue |
                Select-String -Pattern "Serilog" -SimpleMatch -Quiet)
        }

        if (-not ($hasFoundationBuild -and $hasCentralPackages -and $hasSerilog)) {
            Write-Host "✗ Foundation does not look complete:" -ForegroundColor Red
            Write-Host "    MozaStarCitizenLink.sln exists:           $hasFoundationBuild" -ForegroundColor Gray
            Write-Host "    src/Directory.Packages.props exists:      $hasCentralPackages" -ForegroundColor Gray
            Write-Host "    Serilog referenced under src/:            $hasSerilog" -ForegroundColor Gray
            Write-Host ""
            Write-Host "  Parallel worktrees should be created only after T-01 through T-05 have merged." -ForegroundColor Yellow
            Write-Host "  Pass -Force to override (you'd better have a reason)." -ForegroundColor Yellow
            exit 1
        }
        Write-Host "  ✓ Foundation indicators present" -ForegroundColor Green
    }

    # Guard 3: must be on main with latest
    git checkout main 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Host "✗ Could not check out main." -ForegroundColor Red; exit 1 }
    git pull --ff-only
    if ($LASTEXITCODE -ne 0) { Write-Host "✗ git pull --ff-only failed." -ForegroundColor Red; exit 1 }

    $repoName = Split-Path $RepoRoot -Leaf
    $parent = Split-Path $RepoRoot -Parent

    foreach ($trackName in $tracksToProcess) {
        $track = $trackDefinitions[$trackName]
        $folder = $track.Folder
        $branch = $track.Branch
        $worktreePath = Join-Path $parent $folder

        Write-Host ""
        Write-Host "── Track: $trackName ──" -ForegroundColor Cyan
        Write-Host "  Path:   $worktreePath"
        Write-Host "  Branch: $branch"

        if (Test-Path $worktreePath) {
            Write-Host "  ! Path already exists; skipping." -ForegroundColor Yellow
            continue
        }

        # If branch already exists, attach the worktree to it; otherwise create
        $localBranches = git branch --format='%(refname:short)'
        if ($localBranches -contains $branch) {
            Write-Host "  → git worktree add $worktreePath $branch" -ForegroundColor DarkCyan
            git worktree add $worktreePath $branch
        } else {
            Write-Host "  → git worktree add -b $branch $worktreePath main" -ForegroundColor DarkCyan
            git worktree add -b $branch $worktreePath main
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ✗ Worktree creation failed for $trackName" -ForegroundColor Red
        } else {
            Write-Host "  ✓ Worktree created" -ForegroundColor Green
        }
    }

    Write-Host ""
    Write-Host "Worktrees ready. To open a session per track:" -ForegroundColor Cyan
    foreach ($trackName in $tracksToProcess) {
        $folder = $trackDefinitions[$trackName].Folder
        Write-Host "  code $parent\$folder" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Inside each worktree, run: pwsh ./scripts/start-task.ps1 -TaskId T-NN -NoCheckout" -ForegroundColor Cyan
    Write-Host "(use -NoCheckout because the worktree is already on the branch)" -ForegroundColor DarkGray
    Write-Host ""

} finally {
    Pop-Location
}
