param(
    [string]$RepositoryRoot,
    [string]$DatabaseName,
    [string]$PostgresContainerName,
    [int]$PostgresPort = 5433,
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
$expectedSyntheticEvidencePath = Join-Path $expectedEvidenceRoot "synthetic-senior-citizen-id.png"
$stateSchemaVersion = 1
$walkthroughIdentity = "ExitPass.WebPay.StatutoryDiscount.LocalWalkthrough"
$ownershipLabel = "webpay-statutory-discount"
$expectedNetworkName = "exitpass-webpay-statutory-walkthrough"
$expectedContainerNames = @("exitpass-webpay-statutory-minio", "exitpass-webpay-statutory-clamav")

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

function Get-CanonicalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "STATE_MALFORMED: an owned path is missing." }
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Test-CanonicalPathEqual([string]$Left, [string]$Right) {
    return [string]::Equals((Get-CanonicalPath $Left), (Get-CanonicalPath $Right), [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-OwnedPath([string]$Path, [string]$ExpectedRoot, [string]$Description, [switch]$RequireExact) {
    $canonicalPath = Get-CanonicalPath $Path
    $canonicalRoot = Get-CanonicalPath $ExpectedRoot
    $comparison = [System.StringComparison]::OrdinalIgnoreCase
    if ($RequireExact) {
        if (-not [string]::Equals($canonicalPath, $canonicalRoot, $comparison)) {
            throw "STATE_OWNERSHIP_MISMATCH: $Description does not match the walkthrough-owned path."
        }
        return
    }
    $prefix = $canonicalRoot + [System.IO.Path]::DirectorySeparatorChar
    if (-not [string]::Equals($canonicalPath, $canonicalRoot, $comparison) -and -not $canonicalPath.StartsWith($prefix, $comparison)) {
        throw "STATE_PATH_ESCAPE: $Description is outside the walkthrough-owned root."
    }
}

function Get-RequiredStateProperty($Object, [string]$Name, [string]$Context) {
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) {
        throw "STATE_MALFORMED: $Context is missing required property '$Name'."
    }
    return $Object.PSObject.Properties[$Name].Value
}

function Assert-StateTimestamp([string]$Value, [string]$Name) {
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) {
        throw "STATE_MALFORMED: $Name is not a valid round-trip timestamp."
    }
    return $parsed
}

function Assert-ProcessRecordStructure($Record, [string[]]$AllowedNames) {
    $name = [string](Get-RequiredStateProperty $Record 'Name' 'process record')
    if ($AllowedNames -notcontains $name) { throw "STATE_OWNERSHIP_MISMATCH: process '$name' is not in the walkthrough allowlist." }
    if ([int](Get-RequiredStateProperty $Record 'Id' "process '$name'") -le 0) { throw "STATE_MALFORMED: process '$name' has an invalid PID." }
    $executablePath = [string](Get-RequiredStateProperty $Record 'ExecutablePath' "process '$name'")
    if ([string]::IsNullOrWhiteSpace($executablePath) -or -not [System.IO.Path]::IsPathRooted($executablePath)) {
        throw "STATE_MALFORMED: process '$name' lacks an absolute executable identity."
    }
    [void](Assert-StateTimestamp ([string](Get-RequiredStateProperty $Record 'StartTimeUtc' "process '$name'")) "process '$name' StartTimeUtc")
    $markers = @((Get-RequiredStateProperty $Record 'CommandLineMarkers' "process '$name'"))
    if ($markers.Count -eq 0 -or @($markers | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
        throw "STATE_MALFORMED: process '$name' lacks bounded command-line ownership markers."
    }
}

function Read-ValidatedWalkthroughState([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "STATE_NOT_FOUND: walkthrough state was not found at $Path. No resource was changed."
    }
    try { $state = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json }
    catch { throw "STATE_MALFORMED: state at $Path is not readable valid JSON. No resource was changed." }
    if ([int](Get-RequiredStateProperty $state 'StateSchemaVersion' 'walkthrough state') -ne $stateSchemaVersion) {
        throw "STATE_UNSUPPORTED_SCHEMA: walkthrough state schema is not supported. No resource was changed."
    }
    if ([string](Get-RequiredStateProperty $state 'WalkthroughIdentity' 'walkthrough state') -cne $walkthroughIdentity) {
        throw "STATE_OWNERSHIP_MISMATCH: walkthrough identity does not match. No resource was changed."
    }
    Assert-OwnedPath ([string](Get-RequiredStateProperty $state 'RepositoryRoot' 'walkthrough state')) $RepositoryRoot 'repository root' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $state 'StatePath' 'walkthrough state')) $statePath 'state path' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $state 'EvidenceRoot' 'walkthrough state')) $expectedEvidenceRoot 'evidence root' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $state 'SyntheticEvidencePath' 'walkthrough state')) $expectedEvidenceRoot 'synthetic evidence path'
    Assert-OwnedPath ([string]$state.SyntheticEvidencePath) $expectedSyntheticEvidencePath 'synthetic evidence path' -RequireExact
    $runId = [guid]::Empty
    if (-not [guid]::TryParse([string](Get-RequiredStateProperty $state 'RunId' 'walkthrough state'), [ref]$runId) -or $runId -eq [guid]::Empty) {
        throw "STATE_MALFORMED: RunId is not a non-empty GUID."
    }
    if ([string](Get-RequiredStateProperty $state 'StartupMode' 'walkthrough state') -cne 'FRESH' -or
        [string](Get-RequiredStateProperty $state 'LifecycleStatus' 'walkthrough state') -cne 'READY') {
        throw "STATE_NOT_RESTARTABLE: lifecycle metadata is not safely reconcilable."
    }
    $created = Assert-StateTimestamp ([string](Get-RequiredStateProperty $state 'CreatedAtUtc' 'walkthrough state')) 'CreatedAtUtc'
    $updated = Assert-StateTimestamp ([string](Get-RequiredStateProperty $state 'LastValidUpdateAtUtc' 'walkthrough state')) 'LastValidUpdateAtUtc'
    if ($updated -lt $created) { throw "STATE_MALFORMED: last valid update precedes creation." }
    Assert-SafeDatabaseName ([string](Get-RequiredStateProperty $state 'DatabaseName' 'walkthrough state'))
    if ([string](Get-RequiredStateProperty $state 'DatabaseHost' 'walkthrough state') -cne '127.0.0.1' -or
        [int](Get-RequiredStateProperty $state 'DatabasePort' 'walkthrough state') -ne $PostgresPort -or
        [string]::IsNullOrWhiteSpace([string](Get-RequiredStateProperty $state 'PostgresContainerId' 'walkthrough state'))) {
        throw "STATE_DATABASE_MISMATCH: recorded database identity is not local and exact."
    }
    $network = Get-RequiredStateProperty $state 'Network' 'walkthrough state'
    if ([string]$network.Name -cne $expectedNetworkName -or [string]$network.OwnershipLabel -cne $ownershipLabel -or
        [string]::IsNullOrWhiteSpace([string]$network.Id)) {
        throw "STATE_OWNERSHIP_MISMATCH: network identity does not match the walkthrough allowlist."
    }
    $containers = @((Get-RequiredStateProperty $state 'Containers' 'walkthrough state'))
    if ($containers.Count -ne $expectedContainerNames.Count) { throw "STATE_OWNERSHIP_MISMATCH: container set does not match the walkthrough allowlist." }
    foreach ($expectedName in $expectedContainerNames) {
        $matches = @($containers | Where-Object { [string]$_.Name -ceq $expectedName })
        if ($matches.Count -ne 1 -or [string]$matches[0].OwnershipLabel -cne $ownershipLabel -or [string]::IsNullOrWhiteSpace([string]$matches[0].Id)) {
            throw "STATE_OWNERSHIP_MISMATCH: container '$expectedName' lacks exact identity and ownership metadata."
        }
    }
    $processNames = @('central-pms', 'payment-orchestrator', 'webpay-ui', 'operator-console-ui')
    $launcherNames = @('central-pms-launcher', 'payment-orchestrator-launcher', 'webpay-ui-launcher', 'operator-console-ui-launcher')
    $processRecords = @($state.Processes)
    $launcherRecords = @($state.Launchers)
    if ($processRecords.Count -ne $processNames.Count -or $launcherRecords.Count -ne $launcherNames.Count) {
        throw "STATE_NOT_RESTARTABLE: recorded process set is incomplete or ambiguous."
    }
    foreach ($record in $processRecords) { Assert-ProcessRecordStructure $record $processNames }
    foreach ($record in $launcherRecords) { Assert-ProcessRecordStructure $record $launcherNames }
    foreach ($name in $processNames) {
        if (@($processRecords | Where-Object { [string]$_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: process '$name' is missing or duplicated." }
    }
    foreach ($name in $launcherNames) {
        if (@($launcherRecords | Where-Object { [string]$_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: launcher '$name' is missing or duplicated." }
    }
    [void](Get-RequiredStateProperty $state 'Fixture' 'walkthrough state')
    [void](Get-RequiredStateProperty $state 'Urls' 'walkthrough state')
    $json = $state | ConvertTo-Json -Depth 10 -Compress
    if ($json -match '(?i)"[^"\\]*(password|secret|token|connection.?string|provisioning|upload.?url)[^"\\]*"\s*:') {
        throw "STATE_SECRET_FIELD_PROHIBITED: state contains a prohibited secret-shaped property."
    }
    return $state
}

function Get-ProcessCommandLine([int]$Id) {
    try { return (Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop) }
    catch { return $null }
}

function Assert-ProcessOwnership($Record) {
    $runtime = Get-Process -Id ([int]$Record.Id) -ErrorAction SilentlyContinue
    if ($null -eq $runtime) {
        return $false
    }

    $details = Get-ProcessCommandLine ([int]$Record.Id)
    if ($null -eq $details -or [string]::IsNullOrWhiteSpace($details.ExecutablePath) -or [string]::IsNullOrWhiteSpace($details.CommandLine)) {
        throw "PROCESS_OWNERSHIP_MISMATCH: could not verify $($Record.Name) PID $($Record.Id); no further cleanup is allowed."
    }

    $recordedStart = [DateTimeOffset]::Parse([string]$Record.StartTimeUtc).UtcDateTime
    $actualStart = $runtime.StartTime.ToUniversalTime()
    if ([math]::Abs(($actualStart - $recordedStart).TotalSeconds) -gt 2) {
        throw "PROCESS_OWNERSHIP_MISMATCH: $($Record.Name) PID $($Record.Id) was reused; refusing to stop it and no further cleanup is allowed."
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Record.ExecutablePath) -and
        -not [string]::Equals([System.IO.Path]::GetFullPath($details.ExecutablePath), [System.IO.Path]::GetFullPath([string]$Record.ExecutablePath), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "PROCESS_OWNERSHIP_MISMATCH: $($Record.Name) PID $($Record.Id) executable changed; no further cleanup is allowed."
    }

    foreach ($marker in @($Record.CommandLineMarkers)) {
        if ($details.CommandLine -notlike "*$marker*") {
            throw "PROCESS_OWNERSHIP_MISMATCH: $($Record.Name) PID $($Record.Id) lacks an ownership marker; no further cleanup is allowed."
        }
    }

    return $true
}

function Stop-ValidatedProcess($Record) {
    if (-not (Assert-ProcessOwnership $Record)) {
        Write-Host "$($Record.Name) PID $($Record.Id) is not running."
        return
    }

    if ($DryRun) {
        Write-Host "DRY RUN: would stop validated $($Record.Name) PID $($Record.Id)."
        return
    }

    Write-Host "Stopping validated $($Record.Name) PID $($Record.Id)..." -ForegroundColor Yellow
    Stop-Process -Id ([int]$Record.Id) -Force
}

function Assert-ContainerOwnership($Record) {
    $currentId = docker inspect --format '{{.Id}}' $Record.Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    $currentLabel = docker inspect --format '{{index .Config.Labels "exitpass.walkthrough"}}' $Record.Name
    if ($currentId -ne $Record.Id -or $currentLabel -ne $ownershipLabel) {
        throw "CONTAINER_OWNERSHIP_MISMATCH: $($Record.Name) no longer matches recorded identity and ownership; no further cleanup is allowed."
    }
    return $true
}

function Remove-ValidatedContainer($Record) {
    if (-not (Assert-ContainerOwnership $Record)) {
        Write-Host "Walkthrough container $($Record.Name) is absent."
        return
    }
    if ($DryRun) {
        Write-Host "DRY RUN: would stop and remove owned container $($Record.Name)."
        return
    }
    docker rm -f $Record.Name | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not remove owned container $($Record.Name)." }
}

$state = Read-ValidatedWalkthroughState $statePath
if (-not [string]::IsNullOrWhiteSpace($DatabaseName) -and $DatabaseName -ne $state.DatabaseName) {
    throw "The requested database does not match recorded walkthrough ownership."
}
if (-not [string]::IsNullOrWhiteSpace($PostgresContainerName) -and $PostgresContainerName -ne $state.PostgresContainerName) {
    throw "The requested PostgreSQL container does not match recorded walkthrough state."
}
$DatabaseName = $state.DatabaseName
$PostgresContainerName = $state.PostgresContainerName
Assert-SafeDatabaseName $DatabaseName

# Reconcile the complete requested resource set before the first mutation. Each
# destructive helper repeats its own identity check immediately before acting.
foreach ($record in @($state.Processes) + @($state.Launchers)) {
    [void](Assert-ProcessOwnership $record)
}
if ($StopWalkthroughContainers) {
    foreach ($record in @($state.Containers)) { [void](Assert-ContainerOwnership $record) }
    $preflightNetworkId = docker network inspect --format '{{.Id}}' $state.Network.Name 2>$null
    if ($LASTEXITCODE -eq 0) {
        $preflightNetworkLabel = docker network inspect --format '{{index .Labels "exitpass.walkthrough"}}' $state.Network.Name 2>$null
        if ($preflightNetworkId -cne [string]$state.Network.Id -or $preflightNetworkLabel -cne $ownershipLabel) {
            throw "NETWORK_OWNERSHIP_MISMATCH: recorded network identity or ownership changed; cleanup is blocked."
        }
    }
}
if ($RemoveDisposableDatabase) {
    $preflightPostgresId = docker inspect --format '{{.Id}}' $PostgresContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or $preflightPostgresId -cne [string]$state.PostgresContainerId) {
        throw "DATABASE_OWNERSHIP_MISMATCH: PostgreSQL container identity cannot be reconciled; database cleanup is blocked."
    }
}

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

    $networkId = docker network inspect --format '{{.Id}}' $state.Network.Name 2>$null
    if ($LASTEXITCODE -eq 0) {
        $networkLabel = docker network inspect --format '{{index .Labels "exitpass.walkthrough"}}' $state.Network.Name 2>$null
        if ($networkId -cne [string]$state.Network.Id -or $networkLabel -cne $ownershipLabel) {
            throw "NETWORK_OWNERSHIP_MISMATCH: recorded network identity or ownership changed; no further cleanup is allowed."
        }
        if ($DryRun) { Write-Host "DRY RUN: would remove owned Docker network $($state.Network.Name)." }
        else {
            docker network rm $state.Network.Name | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not remove owned Docker network $($state.Network.Name)." }
        }
    }
}

if ($RemoveDisposableDatabase) {
    $currentPostgresId = docker inspect --format '{{.Id}}' $PostgresContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or $currentPostgresId -cne [string]$state.PostgresContainerId) {
        throw "DATABASE_OWNERSHIP_MISMATCH: PostgreSQL container identity cannot be reconciled; database cleanup is blocked."
    }
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
    if (-not $StopWalkthroughContainers -or -not $RemoveDisposableDatabase) {
        throw "STATE_CLEANUP_INCOMPLETE: state removal requires explicit container and disposable-database cleanup in the same governed invocation."
    }
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
