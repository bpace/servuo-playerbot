[CmdletBinding()]
param(
    [string]$UpstreamPath = 'C:\dev\uo-offline\_Klein187',
    [switch]$Fetch,
    [switch]$MarkReviewed
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$markerPath = Join-Path $repoRoot '.upstream\last-reviewed-uo-offline.txt'
$reportDir = Join-Path $repoRoot 'research\upstream-diffs'

if (!(Test-Path (Join-Path $UpstreamPath '.git'))) {
    throw "UO Offline tracking checkout was not found at $UpstreamPath."
}

if ($Fetch) {
    git -C $UpstreamPath fetch upstream --prune
    if ($LASTEXITCODE -ne 0) { throw 'Could not fetch UO Offline upstream.' }
}

$head = (git -C $UpstreamPath rev-parse upstream/main).Trim()
if ($LASTEXITCODE -ne 0) { throw 'The tracking checkout has no upstream/main remote branch.' }

if ($MarkReviewed) {
    New-Item -ItemType Directory -Force (Split-Path -Parent $markerPath) | Out-Null
    Set-Content -LiteralPath $markerPath -Value $head -NoNewline
    Write-Host "Marked UO Offline $head as reviewed."
    exit 0
}

if (!(Test-Path $markerPath)) {
    throw "No reviewed revision marker exists. Inspect $head first, then run this script with -MarkReviewed."
}

$base = (Get-Content -LiteralPath $markerPath -Raw).Trim()
if ($base -eq $head) {
    Write-Host "No UO Offline changes since reviewed revision $base."
    exit 0
}

$changes = @(git -C $UpstreamPath diff --name-status "$base..$head" -- playerbots/source/CustomBots tools/map playerbots/data)
if ($LASTEXITCODE -ne 0) { throw 'Could not compare UO Offline revisions.' }

New-Item -ItemType Directory -Force $reportDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$reportPath = Join-Path $reportDir ("uo-offline-$stamp.md")
$lines = @(
    '# UO Offline conversion review',
    '',
    "- Previous reviewed revision: ``$base``",
    "- Upstream revision: ``$head``",
    "- Tracking checkout: ``$UpstreamPath``",
    '',
    '## Changed files',
    ''
)

if ($changes.Count -eq 0) {
    $lines += '_No PlayerBot, map-editor, or data changes in this range._'
} else {
    foreach ($change in $changes) {
        $parts = $change -split "`t", 2
        $status = $parts[0]
        $path = if ($parts.Count -gt 1) { $parts[1] } else { $change }
        $suggestion = if ($path -like 'tools/map/*') { 'Adapt: browser/editor behavior, not Python server code.' }
            elseif ($path -like 'playerbots/data/*') { 'Reference: review data shape and validate against shard maps.' }
            elseif ($path -like '*AdminPanel*') { 'Adapt: compare GM workflow separately from browser dashboard.' }
            else { 'Review: choose Port, Adapt, Reference, or Decline.' }
        $lines += "- [$status] ``$path``  |  $suggestion"
    }
}

$lines += @('', '## Decisions', '', '| File or feature | Outcome | ServUO issue / test |', '| --- | --- | --- |', '| _fill in_ | _Port / Adapt / Reference / Decline_ | _fill in_ |')
Set-Content -LiteralPath $reportPath -Value $lines
Write-Host "Created conversion report: $reportPath"
