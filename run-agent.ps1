<#
.SYNOPSIS
    Picks the lowest open 'ready'-tagged issue in the repository, parses its
    <model>-<effort> label, and invokes 'agy' with that model and effort,
    dangerously skipping permissions, streaming output in real-time, maintaining
    session context across runs, and automatically committing, pushing, and closing the issue.

.DESCRIPTION
    Workflow helper for Leed3D that automates sequential issue implementation.
    Features:
    - Finds lowest open issue labeled 'ready'.
    - Extracts model and effort from labels formatted as '<model>-<effort>' (e.g. 'gemini-4-argon-high', 'gemini-3.8-flash-medium').
    - Streams agent thinking, tool executions, and text responses to the console in real-time using NDJSON events.
    - Dangerously skips tool confirmation permissions (--dangerously-skip-permissions).
    - Preserves and re-uses conversation session ID in '.agy_session' so the agent retains context across issues.
    - Automatically verifies, commits, pushes, and closes the issue upon completion.
    - Supports running in a continuous loop (-Loop) or as a single pass (default).

.PARAMETER Loop
    If set, repeatedly processes the lowest 'ready' issue until none remain.

.PARAMETER NewSession
    If set, clears the stored session ID and starts a fresh conversation.

.PARAMETER Interactive
    If set, launches agy interactively (-i) instead of non-interactive streaming mode.

.PARAMETER SessionFile
    Path to the session file storing the conversation ID (default: .agy_session).

.EXAMPLE
    .\run-agent.ps1
    # Executes the lowest ready issue in a single pass with real-time output.

.EXAMPLE
    .\run-agent.ps1 -Loop
    # Runs continuously through all ready issues until done, committing and closing each.

.EXAMPLE
    .\run-agent.ps1 -NewSession
    # Discards previous context and starts fresh on the next issue.
#>

[CmdletBinding()]
param(
    [switch]$Loop,
    [switch]$NewSession,
    [switch]$Interactive,
    [string]$SessionFile = ".agy_session",
    [string]$DefaultModel = "gemini-4-argon",
    [string]$DefaultEffort = "high"
)

$ErrorActionPreference = "Stop"

# Ensure we are in the repository root directory
$repoRoot = $PSScriptRoot
if (-not (Test-Path (Join-Path $repoRoot ".git"))) {
    $repoRoot = (git rev-parse --show-toplevel 2>$null)
    if (-not $repoRoot) {
        Write-Error "Could not determine repository root. Please run from inside the git repository."
        exit 1
    }
}
Set-Location $repoRoot

# Ensure .agy_session is in .gitignore
$gitignorePath = Join-Path $repoRoot ".gitignore"
if (Test-Path $gitignorePath) {
    $giContent = Get-Content $gitignorePath -Raw
    if ($giContent -notmatch "(?m)^\.agy_session") {
        Add-Content -Path $gitignorePath -Value "`n# Antigravity CLI session tracking`n.agy_session`n"
        Write-Host "[git] Added .agy_session to .gitignore" -ForegroundColor DarkGray
    }
}

$sessionFilePath = Join-Path $repoRoot $SessionFile

if ($NewSession -and (Test-Path $sessionFilePath)) {
    Remove-Item $sessionFilePath -Force
    Write-Host "[session] Reset stored session ID (.agy_session cleared)." -ForegroundColor Yellow
}

function Get-StoredSessionId {
    if (Test-Path $sessionFilePath) {
        $id = (Get-Content $sessionFilePath -Raw).Trim()
        if ($id -match '^[0-9a-fA-F-]{36}$') {
            return $id
        }
    }
    return $null
}

function Save-SessionId ([string]$id) {
    if ($id -and $id -match '^[0-9a-fA-F-]{36}$') {
        Set-Content -Path $sessionFilePath -Value $id -NoNewline
        Write-Host "[session] Saved active conversation ID: $id" -ForegroundColor DarkCyan
    }
}

function Get-NextReadyIssue {
    Write-Host "[github] Querying open issues labeled 'ready'..." -ForegroundColor DarkGray
    $rawJson = gh issue list --state open --label "ready" --limit 100 --json number,title,labels
    if (-not $rawJson) {
        return $null
    }

    $issues = $rawJson | ConvertFrom-Json
    if (-not $issues -or $issues.Count -eq 0) {
        return $null
    }

    # Sort ascending by issue number to pick the lowest
    $sorted = $issues | Sort-Object { [int]$_.number }
    return $sorted[0]
}

function Parse-ModelAndEffort ($issue) {
    $model = $DefaultModel
    $effort = $DefaultEffort

    foreach ($lbl in $issue.labels) {
        $name = $lbl.name
        # Match pattern: <model>-<effort> where effort is low|medium|high|xhigh|max
        if ($name -match '^(.*)-(low|medium|high|xhigh|max)$') {
            $model = $Matches[1]
            $effort = $Matches[2]
            break
        }
    }

    return @{ Model = $model; Effort = $effort }
}

$iteration = 0

do {
    $iteration++
    Write-Host "`n========================================================" -ForegroundColor Magenta
    Write-Host " Leed3D Agent Runner — Pass #$iteration" -ForegroundColor Magenta
    Write-Host "========================================================" -ForegroundColor Magenta

    $issue = Get-NextReadyIssue
    if (-not $issue) {
        Write-Host "`nNo open issues labeled 'ready' found! Everything ready has been completed." -ForegroundColor Green
        break
    }

    $issueNum = $issue.number
    $issueTitle = $issue.title
    $spec = Parse-ModelAndEffort $issue
    $model = $spec.Model
    $effort = $spec.Effort

    Write-Host "`nTarget Issue : #$issueNum - $issueTitle" -ForegroundColor Yellow
    Write-Host "Model Target : $model" -ForegroundColor Cyan
    Write-Host "Effort Level : $effort" -ForegroundColor Cyan

    $sessionId = Get-StoredSessionId
    $hasExistingSession = [bool]$sessionId

    if ($hasExistingSession) {
        Write-Host "Session Mode : Resuming conversation $sessionId" -ForegroundColor Green
    } else {
        Write-Host "Session Mode : Starting fresh conversation" -ForegroundColor Yellow
    }

    $promptText = "Please implement issue #$($issueNum): $issueTitle. Follow the technical requirements, AGENTS.md, ROADMAP.md, and coding standards. Ensure all headless tests pass and dotnet build succeeds with 0 warnings. Once implementation and verification are complete, commit all changes with a clear message referencing #$issueNum, push to origin, close issue #$issueNum, and remove the 'ready' label."

    if ($Interactive) {
        # Interactive mode using standard terminal UI
        $agyArgs = @(
            "--model", $model,
            "--effort", $effort,
            "--dangerously-skip-permissions"
        )
        if ($hasExistingSession) {
            $agyArgs += @("--conversation", $sessionId)
        }
        $agyArgs += @("-i", $promptText)

        Write-Host "`n[executing interactive] agy $($agyArgs -join ' ')`n" -ForegroundColor DarkCyan
        & agy @agyArgs
    } else {
        # Real-time streaming mode using --output-format stream-json
        $agyArgList = @(
            "-p", "`"$promptText`"",
            "--output-format", "stream-json",
            "--model", $model,
            "--effort", $effort,
            "--dangerously-skip-permissions"
        )
        if ($hasExistingSession) {
            $agyArgList += @("--conversation", $sessionId)
        }

        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "agy.exe"
        $psi.Arguments = ($agyArgList -join " ")
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true

        Write-Host "`n--- [Real-time Agent Output Start] ---`n" -ForegroundColor DarkGray

        $proc = [System.Diagnostics.Process]::Start($psi)

        while (-not $proc.StandardOutput.EndOfStream) {
            $line = $proc.StandardOutput.ReadLine()
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                try {
                    $data = $line | ConvertFrom-Json -ErrorAction SilentlyContinue
                    if ($data) {
                        if ($data.event -eq "init") {
                            $capturedId = $data.conversation_id
                            if ($capturedId) {
                                Save-SessionId $capturedId
                            }
                        } elseif ($data.event -eq "step_update") {
                            $step = $data.step_update
                            if ($step.step_type -eq "tool") {
                                if ($step.state -eq "ACTIVE") {
                                    $p = $step.tool_info.parameters
                                    $desc = if ($p.CommandLine) {
                                        $p.CommandLine
                                    } elseif ($p.Path) {
                                        $p.Path
                                    } elseif ($p.query) {
                                        $p.query
                                    } else {
                                        $step.tool_name
                                    }
                                    Write-Host "`n> [tool: $($step.tool_name)] $desc" -ForegroundColor Cyan
                                } elseif ($step.state -eq "DONE") {
                                    $dur = if ($step.duration_seconds) { [math]::Round($step.duration_seconds, 2) } else { 0 }
                                    Write-Host "  [done in ${dur}s]" -ForegroundColor DarkGray
                                }
                            } elseif ($step.step_type -eq "agent_response" -and $step.text_delta) {
                                Write-Host -NoNewline $step.text_delta
                            }
                        } elseif ($data.event -eq "result") {
                            $dur = if ($data.result.duration_seconds) { [math]::Round($data.result.duration_seconds, 1) } else { 0 }
                            Write-Host "`n`n[turn complete: $($data.result.status) in ${dur}s]" -ForegroundColor Green
                        }
                    }
                } catch {
                    # If line is plain text rather than JSON, output directly
                    Write-Host $line
                }
            }
        }

        $proc.WaitForExit()
        $exitCode = $proc.ExitCode
        Write-Host "`n--- [Real-time Agent Output End] ---`n" -ForegroundColor DarkGray

        if ($exitCode -ne 0) {
            $err = $proc.StandardError.ReadToEnd()
            if ($err) {
                Write-Host "[stderr] $err" -ForegroundColor Red
            }
        }
    }

    # ========================================================
    # Post-Execution Safeguards: Commit, Push & Close Issue
    # ========================================================
    Write-Host "`n[safeguards] Checking git status and issue closure for #$issueNum..." -ForegroundColor DarkGray

    # 1. Check for uncommitted changes and commit if necessary
    $gitStatus = git status --porcelain
    if ($gitStatus) {
        Write-Host "[git] Uncommitted changes detected. Auto-committing for #$issueNum..." -ForegroundColor Yellow
        git add -A
        git commit -m "Implement issue #$issueNum - $issueTitle"
        Write-Host "[git] Committed changes successfully." -ForegroundColor Green
    }

    # 2. Push any committed work to remote
    try {
        Write-Host "[git] Pushing changes to origin/master..." -ForegroundColor DarkGray
        git push origin master | Out-Null
        Write-Host "[git] Push successful." -ForegroundColor Green
    } catch {
        Write-Host "[git warning] Push failed or branch is already up-to-date: $_" -ForegroundColor Yellow
    }

    # 3. Verify and close the issue if not already closed by the agent
    try {
        $issueView = gh issue view $issueNum --json state | ConvertFrom-Json
        if ($issueView.state -eq "OPEN") {
            Write-Host "[github] Issue #$issueNum is still OPEN. Auto-closing and clearing 'ready' label..." -ForegroundColor Yellow
            gh issue edit $issueNum --remove-label "ready" | Out-Null
            gh issue close $issueNum --comment "Resolved, committed, and pushed by automated agent runner." | Out-Null
            Write-Host "[github] Issue #$issueNum closed successfully." -ForegroundColor Green
        } else {
            Write-Host "[github] Issue #$issueNum was already closed by the agent." -ForegroundColor Green
        }
    } catch {
        Write-Host "[github warning] Could not verify issue status: $_" -ForegroundColor Yellow
    }

    if (-not $Loop) {
        Write-Host "`nSingle pass complete. Use -Loop to process all ready issues continuously." -ForegroundColor DarkGray
        break
    }

    Write-Host "`nPausing 3 seconds before next iteration..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 3

} while ($Loop)
