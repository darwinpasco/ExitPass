[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop', 'Status', 'Reset')]
    [string] $Action = 'Start',
    [switch] $ConfirmDeveloperReset
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'RuntimeProfile.ps1')
. (Join-Path $PSScriptRoot 'DeveloperRuntime.ps1')
$profile = Get-ExitPassRuntimeProfile -RepositoryRoot $repoRoot
if ($profile -ne 'DEVELOPER') { throw 'Start-DeveloperRuntime.ps1 requires EXITPASS_RUNTIME_PROFILE=DEVELOPER.' }

if ($Action -eq 'Start') {
    [void](Initialize-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot)
    Write-Host 'Developer dependencies are ready: database, WireMock HikCentral, and Site Adapter.'
    exit 0
}
if ($Action -eq 'Stop') {
    Stop-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot
    Write-Host 'Developer application dependencies stopped; Developer database volume preserved.'
    exit 0
}
if ($Action -eq 'Status') {
    & docker.exe ps --filter 'label=com.exitpass.resource-owner=developer-runtime' --format 'table {{.Names}}\t{{.Status}}'
    exit $LASTEXITCODE
}

if (-not $ConfirmDeveloperReset) {
    throw 'Developer reset requires -ConfirmDeveloperReset. Only exitpass-dev-synthetic resources are eligible.'
}
$state = Get-ExitPassDeveloperRuntimeState -RepositoryRoot $repoRoot
Stop-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot
Assert-ExitPassDeveloperOwnedResource -Type volume -Name 'exitpass-dev-synthetic-data'
& docker.exe volume rm 'exitpass-dev-synthetic-data' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Developer database volume reset failed.' }
[void](Initialize-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot)
Write-Host 'Developer synthetic database and deterministic fixtures were recreated.'
