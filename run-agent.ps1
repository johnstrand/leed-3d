<#
.SYNOPSIS
    Picks the lowest open 'ready'-tagged issue in the repository, parses its
    <model>-<effort> label, and invokes 'agy' with that model and effort,
    dangerously skipping permissions, streaming output in real-time, maintaining
    session context across runs, and automatically committing, pushing, and closing the issue.

.DESCRIPTION
    Reusable Antigravity CLI workflow runner template (and local runner for the ideas repository)
    that automates sequential issue implementation.
    Features:
    - Automatically checks 'on hold' issues and promotes them to 'ready' once all 'Depends on:'
      prerequisites are closed (or closes tracking epics when all tracked sub-issues are closed).
    - Finds lowest open issue labeled 'ready'.
    - Updates the PowerShell terminal title to the current active issue for clear multi-window tracking.
    - Extracts model and effort from labels formatted as '<model>-<effort>' (e.g. 'gemini-4-argon-high').
    - Streams agent thinking, tool executions, and text responses to the console in real-time using NDJSON events.
    - Dangerously skips tool confirmation permissions (--dangerously-skip-permissions).
    - Preserves and re-uses conversation session ID in '.agy_session' so the agent retains context across issues.
    - Gracefully handles quota exhaustion and rate limits with exponential backoff and safe stopping without corrupting issue status.
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

.PARAMETER MaxQuotaRetries
    Number of times to retry an issue when rate limit or quota exhaustion is encountered
    before halting gracefully (default: 3). Set to 0 to exit immediately on quota error.

.PARAMETER QuotaRetryDelaySeconds
    Base delay in seconds to wait before retrying when quota / rate limit is hit (default: 60).
    Uses incremental backoff (delay * retryAttempt).

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
    [string]$DefaultEffort = "high",
    [int]$MaxQuotaRetries = 3,
    [int]$QuotaRetryDelaySeconds = 60
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
[System.Environment]::CurrentDirectory = $repoRoot
$repoName = Split-Path $repoRoot -Leaf

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

function Set-ConsoleTitle ([string]$title) {
    try {
        $Host.UI.RawUI.WindowTitle = $title
    } catch { }
    try {
        if (-not [Console]::IsOutputRedirected) {
            [Console]::Write("$([char]27)]0;$title$([char]7)")
        }
    } catch { }
}

function Unlock-UnblockedOnHoldIssues {
    Write-Host "[github] Checking 'on hold' issues for resolved dependencies..." -ForegroundColor DarkGray
    $rawOpen = gh issue list --state open --limit 200 --json number,title,body,labels
    if (-not $rawOpen) {
        return
    }

    $openIssues = @($rawOpen | ConvertFrom-Json)
    if ($openIssues.Count -eq 0) {
        return
    }

    $openNumberSet = [System.Collections.Generic.HashSet[int]]::new()
    foreach ($iss in $openIssues) {
        [void]$openNumberSet.Add([int]$iss.number)
    }

    $onHoldIssues = @($openIssues | Where-Object {
        $names = @($_.labels | ForEach-Object { $_.name })
        $names -contains "on hold"
    } | Sort-Object { [int]$_.number })

    foreach ($oh in $onHoldIssues) {
        $labelNames = @($oh.labels | ForEach-Object { $_.name })
        # Never auto-unblock issues waiting on human design input
        if ($labelNames -contains "review needed" -or $labelNames -contains "question") {
            continue
        }

        $bodyText = [string]$oh.body
        if ($bodyText -notmatch '(?im)^Depends on:\s*(.+)$') {
            continue
        }

        $depLine = $Matches[1]
        $depMatches = [regex]::Matches($depLine, '#(\d+)')
        if ($depMatches.Count -eq 0) {
            continue
        }

        $depNums = @($depMatches | ForEach-Object { [int]$_.Groups[1].Value } | Select-Object -Unique)
        $openDeps = @()

        foreach ($depNum in $depNums) {
            if ($openNumberSet.Contains($depNum)) {
                $openDeps += "#$depNum"
            }
        }

        $depListStr = ($depNums | ForEach-Object { "#$_" }) -join ", "
        if ($openDeps.Count -eq 0) {
            $isTrackingEpic = ($bodyText -match '(?im)^Tracking Epic:\s*true\b')
            if ($isTrackingEpic) {
                Write-Host "[github] All sub-issues ($depListStr) for tracking epic #$($oh.number) are CLOSED. Closing epic..." -ForegroundColor Green
                gh issue edit $oh.number --remove-label "on hold" | Out-Null
                gh issue close $oh.number --comment "All tracked sub-issues ($depListStr) have been resolved and closed." | Out-Null
                [void]$openNumberSet.Remove([int]$oh.number)
            } else {
                Write-Host "[github] All dependencies ($depListStr) for #$($oh.number) are CLOSED. Promoting 'on hold' -> 'ready'..." -ForegroundColor Green
                gh issue edit $oh.number --remove-label "on hold" --add-label "ready" | Out-Null
                gh issue comment $oh.number --body "All prerequisite issues ($depListStr) are now resolved. Removing 'on hold' and marking 'ready' for implementation." | Out-Null
            }
        } else {
            Write-Host "[github] Issue #$($oh.number) remains 'on hold' (waiting on: $($openDeps -join ', '))." -ForegroundColor DarkGray
        }
    }
}

function Get-NextReadyIssue {
    Unlock-UnblockedOnHoldIssues
    Write-Host "[github] Querying open issues labeled 'ready'..." -ForegroundColor DarkGray
    $rawJson = gh issue list --state open --label "ready" --limit 100 --json number,title,labels,body
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

function Test-IsQuotaError ([int]$code, [string]$errText, [string]$status) {
    $combined = "$errText $status"
    if ($combined -match '(?i)(quota|rate.?limit|resource.?exhausted|429|too many requests|tokens per minute|requests per minute)') {
        return $true
    }
    return $false
}

$iteration = 0

do {
    $iteration++
    Write-Host "`n========================================================" -ForegroundColor Magenta
    Write-Host " $repoName Agent Runner — Pass #$iteration" -ForegroundColor Magenta
    Write-Host "========================================================" -ForegroundColor Magenta

    Set-ConsoleTitle "[$repoName] Checking issues..."

    $issue = Get-NextReadyIssue
    if (-not $issue) {
        Write-Host "`nNo open issues labeled 'ready' found! Everything ready has been completed." -ForegroundColor Green
        Set-ConsoleTitle "[$repoName] Done (no ready issues)"
        break
    }

    $issueNum = $issue.number
    $issueTitle = "$($issue.title)".Trim()
    Set-ConsoleTitle "[$repoName] #$issueNum - $issueTitle"
    $spec = Parse-ModelAndEffort $issue
    $model = $spec.Model
    $effort = $spec.Effort

    Write-Host "`nTarget Issue : #$issueNum - $issueTitle" -ForegroundColor Yellow
    Write-Host "Model Target : $model" -ForegroundColor Cyan
    Write-Host "Effort Level : $effort" -ForegroundColor Cyan

    $promptText = "Working directly in the current git repository at '$repoRoot' (do NOT clone or work in any other directory), please implement issue #$($issueNum): $issueTitle. First read the full issue body and comments via 'gh issue view $issueNum --comments'. Follow the technical requirements, design documents, and coding standards. Ensure all headless tests pass and dotnet build succeeds with 0 warnings. Once implementation and verification are complete: (1) commit all changes with a clear message referencing #$issueNum and push to origin, (2) close issue #$issueNum and remove the 'ready' label, and (3) check any open 'on hold' issues (especially any listed under 'Blocks:' in #$issueNum, or via 'gh issue list --state open --label on-hold'): if an 'on hold' issue does not have 'review needed' or 'question' and all of its 'Depends on:' prerequisite issues are now CLOSED, remove the 'on hold' label and add the 'ready' label (or if it is marked 'Tracking Epic: true', remove 'on hold' and close the epic)."

    $quotaRetries = 0
    $issueCompletedSuccessfully = $false

    while (-not $issueCompletedSuccessfully) {
        $sessionId = Get-StoredSessionId
        $hasExistingSession = [bool]$sessionId

        if ($hasExistingSession) {
            Write-Host "Session Mode : Resuming conversation $sessionId" -ForegroundColor Green
        } else {
            Write-Host "Session Mode : Starting fresh conversation" -ForegroundColor Yellow
        }

        $exitCode = 0
        $capturedResultStatus = $null
        $capturedErrorOutput = ""

        if ($Interactive) {
            # Interactive mode using standard terminal UI
            $agyArgs = @(
                "--add-dir", $repoRoot,
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
            $exitCode = $LASTEXITCODE
        } else {
            # Real-time streaming mode using --output-format stream-json
            $agyArgList = @(
                "-p", "`"$promptText`"",
                "--add-dir", "`"$repoRoot`"",
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
            $psi.WorkingDirectory = $repoRoot
            $psi.Arguments = ($agyArgList -join " ")
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true

            Write-Host "`n--- [Real-time Agent Output Start] ---`n" -ForegroundColor DarkGray

            $proc = [System.Diagnostics.Process]::Start($psi)
            $stderrOutput = ""
            try {
                $stderrTask = $proc.StandardError.ReadToEndAsync()

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
                                        } elseif ($step.state -eq "ERROR") {
                                            $errStr = "$($step.error ?? $step.tool_info.error)"
                                            Write-Host "  [tool error: $errStr]" -ForegroundColor Red
                                            $capturedErrorOutput += "`n$errStr"
                                        }
                                    } elseif ($step.step_type -eq "agent_response" -and $step.text_delta) {
                                        Write-Host -NoNewline $step.text_delta
                                    }
                                } elseif ($data.event -eq "result") {
                                    $capturedResultStatus = $data.result.status
                                    $dur = if ($data.result.duration_seconds) { [math]::Round($data.result.duration_seconds, 1) } else { 0 }
                                    if ($data.result.status -eq "error") {
                                        $errStr = "$($data.result.error ?? $data.result.message ?? 'Unknown error in result event')"
                                        Write-Host "`n`n[turn error: $errStr in ${dur}s]" -ForegroundColor Red
                                        $capturedErrorOutput += "`n$errStr"
                                    } else {
                                        Write-Host "`n`n[turn complete: $($data.result.status) in ${dur}s]" -ForegroundColor Green
                                    }
                                } elseif ($data.event -eq "error") {
                                    $errStr = "$($data.message ?? $data.error ?? ($data | ConvertTo-Json -Compress))"
                                    Write-Host "`n[agent error] $errStr" -ForegroundColor Red
                                    $capturedErrorOutput += "`n$errStr"
                                }
                            }
                        } catch {
                            # If line is plain text rather than JSON, output directly
                            Write-Host $line
                            if ($line -match '(?i)quota|rate.?limit|resource.?exhausted|429|Too Many Requests') {
                                $capturedErrorOutput += "`n$line"
                            }
                        }
                    }
                }

                $proc.WaitForExit()
                $exitCode = $proc.ExitCode
                $stderrOutput = $stderrTask.GetAwaiter().GetResult()
            } finally {
                if ($proc -and -not $proc.HasExited) {
                    try {
                        $proc.Kill($true)
                    } catch {
                        try { $proc.Kill() } catch { }
                    }
                }
            }
            Write-Host "`n--- [Real-time Agent Output End] ---`n" -ForegroundColor DarkGray

            if ($stderrOutput) {
                Write-Host "[stderr] $stderrOutput" -ForegroundColor Red
                $capturedErrorOutput += "`n$stderrOutput"
            }
        }

        # Check execution status
        $isSuccess = ($exitCode -eq 0) -and ($capturedResultStatus -ne "error")

        if (-not $isSuccess) {
            $isQuota = Test-IsQuotaError $exitCode $capturedErrorOutput $capturedResultStatus
            if ($isQuota) {
                Write-Host "`n[QUOTA ERROR] Hit quota or rate limit during execution of issue #$issueNum." -ForegroundColor Red
                if ($quotaRetries -lt $MaxQuotaRetries) {
                    $quotaRetries++
                    $delay = $QuotaRetryDelaySeconds * $quotaRetries
                    Write-Host "[quota] Waiting $delay seconds before retry ($quotaRetries/$MaxQuotaRetries)..." -ForegroundColor Yellow
                    Set-ConsoleTitle "[$repoName] Quota Paused - Retrying in ${delay}s (#$issueNum)"
                    Start-Sleep -Seconds $delay
                    Write-Host "[quota] Retrying issue #$issueNum now..." -ForegroundColor Yellow
                    continue
                } else {
                    Write-Host "`n[QUOTA EXHAUSTED] Maximum retries reached ($MaxQuotaRetries). Stopping runner gracefully." -ForegroundColor Red
                    Write-Host "[QUOTA EXHAUSTED] Issue #$issueNum remains OPEN and labeled 'ready' for future resumption." -ForegroundColor Yellow
                    Write-Host "[QUOTA EXHAUSTED] Post-execution commit, push, and issue closure have been SKIPPED." -ForegroundColor Yellow
                    Set-ConsoleTitle "[$repoName] Quota Exhausted - Stopped at #$issueNum"
                    $global:LASTEXITCODE = 1
                    return
                }
            } else {
                Write-Host "`n[EXECUTION FAILED] Agent exited with code $exitCode (status: $capturedResultStatus)." -ForegroundColor Red
                Write-Host "[EXECUTION FAILED] Issue #$issueNum remains OPEN and labeled 'ready'." -ForegroundColor Yellow
                Write-Host "[EXECUTION FAILED] Post-execution commit, push, and issue closure have been SKIPPED." -ForegroundColor Yellow
                Set-ConsoleTitle "[$repoName] Failed at #$issueNum"
                $global:LASTEXITCODE = 1
                return
            }
        }

        $issueCompletedSuccessfully = $true
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
        $branch = (git rev-parse --abbrev-ref HEAD 2>$null).Trim()
        if (-not $branch) { $branch = "main" }
        Write-Host "[git] Pushing changes to origin/$branch..." -ForegroundColor DarkGray
        git push origin $branch | Out-Null
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

    # 4. Unblock any 'on hold' issues whose 'Depends on:' prerequisites are now all CLOSED
    try {
        Unlock-UnblockedOnHoldIssues
    } catch {
        Write-Host "[github warning] Could not evaluate 'on hold' dependencies: $_" -ForegroundColor Yellow
    }

    if (-not $Loop) {
        Write-Host "`nSingle pass complete. Use -Loop to process all ready issues continuously." -ForegroundColor DarkGray
        Set-ConsoleTitle "[$repoName] Done: #$issueNum - $issueTitle"
        break
    }

    Write-Host "`nPausing 3 seconds before next iteration..." -ForegroundColor DarkGray
    Start-Sleep -Seconds 3

} while ($Loop)
