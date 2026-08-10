param(
    [string]$RepositoryRoot,
    [string]$DatabaseName,
    [string]$PostgresContainerName,
    [switch]$StopWalkthroughContainers,
    [switch]$RemoveDisposableDatabase,
    [switch]$RemoveGeneratedState,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$stateRoot = Join-Path $RepositoryRoot ".local\webpay-statutory-discount-walkthrough"
$statePath = Join-Path $stateRoot "state.json"
$expectedEvidenceRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) "ExitPass\webpay-statutory-discount-walkthrough"))
$ownershipLabel = "webpay-statutory-discount"

function Assert-SafeDatabaseName([string]$Name) {
    if ($Name -notmatch '^exitpass_webpay_local_walkthrough_statutory(_[a-z0-9_]+)?$') {
        throw "Refusing to remove database '$Name'. Only guarded WebPay statutory walkthrough databases are allowed."
    }
}

function Assert-SafeGeneratedPath([string]$Path, [string]$Expected) {
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not [string]::Equals($full.TrimEnd('\'), $Expected.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected path: $full"
    }
}

function Get-ProcessCommandLine([int]$Id) {
    try { return (Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop) }
    catch { return $null }
}

function Stop-ValidatedProcess($Record) {
    $runtime = Get-Process -Id ([int]$Record.Id) -ErrorAction SilentlyContinue
    if ($null -eq $runtime) {
        Write-Host "$($Record.Name) PID $($Record.Id) is not running."
        return
    }

    $details = Get-ProcessCommandLine ([int]$Record.Id)
    if ($null -eq $details -or [string]::IsNullOrWhiteSpace($details.ExecutablePath) -or [string]::IsNullOrWhiteSpace($details.CommandLine)) {
        Write-Warning "Could not verify $($Record.Name) PID $($Record.Id); leaving it running."
        return
    }

    $recordedStart = [DateTimeOffset]::Parse([string]$Record.StartTimeUtc).UtcDateTime
    $actualStart = $runtime.StartTime.ToUniversalTime()
    if ([math]::Abs(($actualStart - $recordedStart).TotalSeconds) -gt 2) {
        Write-Warning "$($Record.Name) PID $($Record.Id) start time changed; refusing to stop a potentially reused PID."
        return
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Record.ExecutablePath) -and
        -not [string]::Equals([System.IO.Path]::GetFullPath($details.ExecutablePath), [System.IO.Path]::GetFullPath([string]$Record.ExecutablePath), [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning "$($Record.Name) PID $($Record.Id) executable changed; refusing to stop it."
        return
    }

    foreach ($marker in @($Record.CommandLineMarkers)) {
        if ($details.CommandLine -notlike "*$marker*") {
            Write-Warning "$($Record.Name) PID $($Record.Id) lacks recorded marker '$marker'; refusing to stop it."
            return
        }
    }

    if ($DryRun) {
        Write-Host "DRY RUN: would stop validated $($Record.Name) PID $($Record.Id)."
        return
    }

    Write-Host "Stopping validated $($Record.Name) PID $($Record.Id)..." -ForegroundColor Yellow
    Stop-Process -Id ([int]$Record.Id) -Force
}

function Remove-ValidatedContainer($Record) {
    $currentId = docker inspect --format '{{.Id}}' $Record.Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Walkthrough container $($Record.Name) is absent."
        return
    }
    $currentLabel = docker inspect --format '{{index .Config.Labels "exitpass.walkthrough"}}' $Record.Name
    if ($currentId -ne $Record.Id -or $currentLabel -ne $ownershipLabel) {
        Write-Warning "Container $($Record.Name) no longer matches recorded identity and ownership; leaving it unchanged."
        return
    }
    if ($DryRun) {
        Write-Host "DRY RUN: would stop and remove owned container $($Record.Name)."
        return
    }
    docker rm -f $Record.Name | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not remove owned container $($Record.Name)." }
}

if (-not (Test-Path -LiteralPath $statePath)) {
    throw "Walkthrough state was not found at $statePath. No process, container, database, or file was changed."
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if (-not [string]::IsNullOrWhiteSpace($DatabaseName) -and $DatabaseName -ne $state.DatabaseName) {
    throw "The requested database does not match recorded walkthrough ownership."
}
if (-not [string]::IsNullOrWhiteSpace($PostgresContainerName) -and $PostgresContainerName -ne $state.PostgresContainerName) {
    throw "The requested PostgreSQL container does not match recorded walkthrough state."
}
$DatabaseName = $state.DatabaseName
$PostgresContainerName = $state.PostgresContainerName
Assert-SafeDatabaseName $DatabaseName

foreach ($record in @($state.Processes)) {
    Stop-ValidatedProcess $record
}
foreach ($record in @($state.Launchers)) {
    Stop-ValidatedProcess $record
}

if ($StopWalkthroughContainers) {
    foreach ($record in @($state.Containers)) {
        Remove-ValidatedContainer $record
    }

    $networkLabel = docker network inspect --format '{{index .Labels "exitpass.walkthrough"}}' $state.Network 2>$null
    if ($LASTEXITCODE -eq 0 -and $networkLabel -eq $ownershipLabel) {
        if ($DryRun) { Write-Host "DRY RUN: would remove owned Docker network $($state.Network)." }
        else {
            docker network rm $state.Network | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not remove owned Docker network $($state.Network)." }
        }
    }
}

if ($RemoveDisposableDatabase) {
    if ($DryRun) {
        Write-Host "DRY RUN: would remove guarded disposable database $DatabaseName."
    }
    else {
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U exitpass -d postgres `
            -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DatabaseName';"
        if ($LASTEXITCODE -ne 0) { throw "Could not terminate walkthrough database connections." }
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U exitpass -d postgres `
            -c "DROP DATABASE IF EXISTS $DatabaseName;"
        if ($LASTEXITCODE -ne 0) { throw "Could not remove guarded disposable database." }
    }
}

if ($RemoveGeneratedState) {
    Assert-SafeGeneratedPath $stateRoot ([System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot ".local\webpay-statutory-discount-walkthrough")))
    if (-not [string]::IsNullOrWhiteSpace([string]$state.EvidenceRoot)) {
        Assert-SafeGeneratedPath ([string]$state.EvidenceRoot) $expectedEvidenceRoot
    }

    if ($DryRun) {
        Write-Host "DRY RUN: would remove walkthrough-only logs, state, and synthetic evidence."
    }
    else {
        if (Test-Path -LiteralPath $expectedEvidenceRoot) {
            Remove-Item -LiteralPath $expectedEvidenceRoot -Recurse -Force
        }
        if (Test-Path -LiteralPath $stateRoot) {
            Remove-Item -LiteralPath $stateRoot -Recurse -Force
        }
    }
}
else {
    Write-Host "Walkthrough logs, state, and synthetic evidence were preserved for verification." -ForegroundColor Cyan
    Write-Host "Use -RemoveGeneratedState only after collecting the local evidence checklist."
}

Write-Host "Shared PostgreSQL, RabbitMQ, and mock-payment-provider containers were not stopped or removed."
Write-Host "No unrecorded process, container, network, database, bucket, or file was changed."
