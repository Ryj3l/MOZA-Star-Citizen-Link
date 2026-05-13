<#
.SYNOPSIS
    Sets up a branch for a task and prints the exact Claude Code prompt to paste.

.DESCRIPTION
    Reads the task spec to extract the canonical branch name, checks out main,
    pulls latest, creates the branch, and prints the Claude Code Task Prompt
    Template with the task ID substituted.

    Refuses to run if the working tree is dirty or if the branch already exists
    locally (in that case, switch to it manually).

.PARAMETER TaskId
    The task identifier (e.g., T-01, T-07, T-23). Case-insensitive; accepts
    "T01" or "1" and normalizes to "T-NN".

.PARAMETER NoCheckout
    Just print the prompt; don't touch git state. Useful when resuming a task.

.EXAMPLE
    pwsh ./scripts/start-task.ps1 -TaskId T-01

.EXAMPLE
    pwsh ./scripts/start-task.ps1 -TaskId 7 -NoCheckout
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$TaskId,

    [switch]$NoCheckout,

    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

$ErrorActionPreference = "Stop"

# Normalize task ID: accept "T-01", "T01", "01", "1" → "T-01"
function Get-NormalizedTaskId {
    param([string]$Raw)
    $stripped = ($Raw -replace '[^0-9]', '')
    if (-not $stripped) { throw "Could not parse a task number from '$Raw'." }
    $n = [int]$stripped
    if ($n -lt 1 -or $n -gt 99) { throw "Task number $n is out of range (1..99)." }
    return ("T-{0:D2}" -f $n)
}

$id = Get-NormalizedTaskId -Raw $TaskId
$taskFile = Join-Path $RepoRoot "docs/tasks/$id.md"
if (-not (Test-Path $taskFile)) {
    throw "Task spec not found: docs/tasks/$id.md"
}

# Extract the branch from the task spec — line begins with "- **Branch:**"
$branchLine = (Get-Content $taskFile | Select-String -Pattern '^\s*-\s*\*\*Branch:\*\*' -SimpleMatch:$false | Select-Object -First 1)
if (-not $branchLine) {
    throw "No '- **Branch:**' line found in $id.md. Check the task spec format."
}
$branch = ($branchLine.Line -replace '^\s*-\s*\*\*Branch:\*\*\s*', '' -replace '`', '').Trim()
if (-not $branch) {
    throw "Branch name extracted from $id.md was empty."
}

# Extract the title from the first H1 or from the "Title:" frontmatter-like field
$titleLine = (Get-Content $taskFile | Select-String -Pattern '^\s*-\s*\*\*Title:\*\*' -SimpleMatch:$false | Select-Object -First 1)
$title = if ($titleLine) {
    ($titleLine.Line -replace '^\s*-\s*\*\*Title:\*\*\s*', '').Trim()
} else {
    "(title not detected)"
}

Write-Host ""
Write-Host "MOZA Star Citizen Link — Start Task" -ForegroundColor Cyan
Write-Host "  Task ID:     $id" -ForegroundColor White
Write-Host "  Title:       $title" -ForegroundColor White
Write-Host "  Branch:      $branch" -ForegroundColor White
Write-Host "  Spec file:   docs/tasks/$id.md" -ForegroundColor White
Write-Host ""

if (-not $NoCheckout) {
    Push-Location $RepoRoot
    try {
        # Guard: working tree must be clean
        $dirty = git status --porcelain
        if ($dirty) {
            Write-Host "✗ Working tree is not clean. Commit, stash, or reset before starting a new task." -ForegroundColor Red
            git status --short
            exit 1
        }

        Write-Host "→ Checking out main and pulling latest" -ForegroundColor DarkCyan
        git checkout main
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        git pull --ff-only
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ git pull --ff-only failed. Resolve manually." -ForegroundColor Red
            exit 1
        }

        # If branch exists locally, just check it out
        $localBranches = git branch --format='%(refname:short)'
        if ($localBranches -contains $branch) {
            Write-Host "  Branch $branch already exists locally; checking it out." -ForegroundColor Yellow
            git checkout $branch
        } else {
            Write-Host "→ Creating branch $branch from main" -ForegroundColor DarkCyan
            git checkout -b $branch
        }
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } finally {
        Pop-Location
    }
}

# Print the Claude Code prompt
$prompt = @"

═════════════════════════════════════════════════════════════════════════════
  COPY THE PROMPT BELOW INTO CLAUDE CODE
═════════════════════════════════════════════════════════════════════════════

Read ``CLAUDE.md``, then ``docs/AGENTIC_EXECUTION_RUNBOOK.md``, then ``docs/tasks/$id.md``. Confirm in your first reply that you have read all three and quote the source-of-truth hierarchy from ``CLAUDE.md``. Then execute $id exactly as specified in ``docs/tasks/$id.md``. Use the branch ``$branch`` (already created). When you finish, run the Stage 3 verify checks from the runbook. Report each verify command's exit code and the last line of output. Stop after verification. Do not start the next task. Do not regenerate spec, plan, tasks, roadmap, project, requirements, state, context, or constitution files — those are not part of this project.

═════════════════════════════════════════════════════════════════════════════
"@

Write-Host $prompt -ForegroundColor Green
Write-Host ""
Write-Host "After Claude finishes:" -ForegroundColor Cyan
Write-Host "  1. Hand-review every changed file (especially T-01 through T-07)." -ForegroundColor Gray
Write-Host "  2. git push -u origin $branch" -ForegroundColor Gray
Write-Host "  3. gh pr create --fill   # or use the GitHub web UI" -ForegroundColor Gray
Write-Host "  4. Fill out .github/PULL_REQUEST_TEMPLATE.md sections." -ForegroundColor Gray
Write-Host "  5. Merge to main." -ForegroundColor Gray
Write-Host ""
