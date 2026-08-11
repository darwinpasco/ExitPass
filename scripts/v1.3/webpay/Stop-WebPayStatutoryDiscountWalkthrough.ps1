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
$fixtureContextPath = Join-Path $stateRoot "fixture-context.json"
$logsRoot = Join-Path $stateRoot "logs"
$expectedEvidenceRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) "ExitPass\webpay-statutory-discount-walkthrough"))
$expectedSyntheticEvidencePath = Join-Path $expectedEvidenceRoot "synthetic-senior-citizen-id.png"
$stateSchemaVersion = 2
$walkthroughIdentity = "ExitPass.WebPay.StatutoryDiscount.LocalWalkthrough"
$ownershipLabel = "webpay-statutory-discount"
$expectedNetworkName = "exitpass-webpay-statutory-walkthrough"
$expectedContainerNames = @("exitpass-webpay-statutory-minio", "exitpass-webpay-statutory-clamav")

function Assert-SafeDatabaseName([string]$Name) {
    if ($Name -notmatch '^exitpass_webpay_local_walkthrough_statutory(_[a-z0-9_]+)?$') {
        throw "Refusing to remove database '$Name'. Only guarded WebPay statutory walkthrough databases are allowed."
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

function Assert-NoReparsePoint([string]$Path, [string]$GovernedRoot, [switch]$AllowMissingLeaf) {
    $canonicalPath = Get-CanonicalPath $Path
    $canonicalRoot = Get-CanonicalPath $GovernedRoot
    Assert-OwnedPath $canonicalPath $canonicalRoot 'governed path'
    $cursor = $canonicalPath
    while ($true) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "STATE_REPARSE_POINT_REJECTED: governed path contains a reparse point: $cursor"
            }
        }
        elseif (-not $AllowMissingLeaf -and [string]::Equals($cursor, $canonicalPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "STATE_PATH_NOT_FOUND: governed path does not exist: $canonicalPath"
        }
        if ([string]::Equals($cursor, $canonicalRoot, [System.StringComparison]::OrdinalIgnoreCase)) { break }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { throw "STATE_PATH_ESCAPE: governed path parent chain did not reach its root." }
        $cursor = Get-CanonicalPath $parent
    }
    return $canonicalPath
}

function Get-Sha256Hex([byte[]]$Bytes) {
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($algorithm.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-WalkthroughOperationLockName([string]$CanonicalRepositoryRoot) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Get-CanonicalPath $CanonicalRepositoryRoot).ToUpperInvariant())
    try { return "Local\ExitPass.WebPay.StatutoryDiscount.$((Get-Sha256Hex $bytes).Substring(0, 32))" }
    finally { [System.Array]::Clear($bytes, 0, $bytes.Length) }
}

function Enter-WalkthroughOperationLock([string]$CanonicalRepositoryRoot, [int]$TimeoutMilliseconds = 5000) {
    if ($TimeoutMilliseconds -lt 0 -or $TimeoutMilliseconds -gt 30000) { throw "STATE_LOCK_INVALID_TIMEOUT: invalid bounded lock timeout." }
    $name = Get-WalkthroughOperationLockName $CanonicalRepositoryRoot
    $mutex = New-Object System.Threading.Mutex($false, $name)
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne($TimeoutMilliseconds) }
        catch [System.Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw "STATE_LOCK_CONTENDED: another cooperating walkthrough operation holds the exclusive lock." }
        return [pscustomobject]@{ Name = $name; Mutex = $mutex; IsHeld = $true; OwnerThreadId = [System.Threading.Thread]::CurrentThread.ManagedThreadId }
    }
    catch { if (-not $acquired) { $mutex.Dispose() }; throw }
}

function Assert-WalkthroughOperationLockHeld($Lock) {
    if ($null -eq $Lock -or -not [bool]$Lock.IsHeld -or $null -eq $Lock.Mutex -or
        [int]$Lock.OwnerThreadId -ne [System.Threading.Thread]::CurrentThread.ManagedThreadId) {
        throw "STATE_LOCK_NOT_HELD: the exclusive walkthrough operation lock is not held by this execution thread."
    }
}

function Exit-WalkthroughOperationLock($Lock) {
    if ($null -eq $Lock) { return }
    try { if ([bool]$Lock.IsHeld) { $Lock.Mutex.ReleaseMutex(); $Lock.IsHeld = $false } }
    finally { $Lock.Mutex.Dispose() }
}

function Get-GovernedFileIdentity([string]$Path, [string]$ExpectedPath, [string]$GovernedRoot) {
    $canonicalPath = Get-CanonicalPath $Path
    if (-not (Test-CanonicalPathEqual $canonicalPath $ExpectedPath)) { throw "STATE_FILE_IDENTITY_MISMATCH: file path is not exact." }
    [void](Assert-NoReparsePoint $canonicalPath $GovernedRoot)
    $item = Get-Item -LiteralPath $canonicalPath -Force
    if ($item.PSIsContainer) { throw "STATE_FILE_IDENTITY_MISMATCH: expected a file." }
    return [pscustomobject]@{
        Path = $canonicalPath
        Length = [long]$item.Length
        CreationTimeUtcTicks = [long]$item.CreationTimeUtc.Ticks
        LastWriteTimeUtcTicks = [long]$item.LastWriteTimeUtc.Ticks
        Sha256 = Get-Sha256Hex ([System.IO.File]::ReadAllBytes($canonicalPath))
    }
}

function Test-FileIdentityEqual($Left, $Right) {
    return $null -ne $Left -and $null -ne $Right -and (Test-CanonicalPathEqual $Left.Path $Right.Path) -and
        [long]$Left.Length -eq [long]$Right.Length -and [long]$Left.CreationTimeUtcTicks -eq [long]$Right.CreationTimeUtcTicks -and
        [long]$Left.LastWriteTimeUtcTicks -eq [long]$Right.LastWriteTimeUtcTicks -and [string]$Left.Sha256 -ceq [string]$Right.Sha256
}

function Assert-FileIdentityEqual($Actual, $Expected, [string]$Classification) {
    if (-not (Test-FileIdentityEqual $Actual $Expected)) { throw "${Classification}: governed file identity or content changed." }
}

function Get-RequiredStateProperty($Object, [string]$Name, [string]$Context) {
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) { throw "STATE_MALFORMED: $Context is missing '$Name'." }
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
    if ($AllowedNames -notcontains $name) { throw "STATE_OWNERSHIP_MISMATCH: process '$name' is not allowlisted." }
    if ([int](Get-RequiredStateProperty $Record 'Id' "process '$name'") -le 0) { throw "STATE_MALFORMED: process '$name' has an invalid PID." }
    $executable = [string](Get-RequiredStateProperty $Record 'ExecutablePath' "process '$name'")
    if ([string]::IsNullOrWhiteSpace($executable) -or -not [System.IO.Path]::IsPathRooted($executable)) { throw "STATE_MALFORMED: process '$name' lacks executable identity." }
    [void](Assert-StateTimestamp ([string](Get-RequiredStateProperty $Record 'StartTimeUtc' "process '$name'")) "process '$name' StartTimeUtc")
    $markers = @((Get-RequiredStateProperty $Record 'CommandLineMarkers' "process '$name'"))
    if ($markers.Count -eq 0 -or @($markers | Where-Object { [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) { throw "STATE_MALFORMED: process '$name' lacks ownership markers." }
}

function Read-ValidatedWalkthroughState([string]$Path, $Lock) {
    Assert-WalkthroughOperationLockHeld $Lock
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "STATE_NOT_FOUND: walkthrough state was not found. No resource was changed." }
    [void](Assert-NoReparsePoint $Path $RepositoryRoot)
    $identityBefore = Get-GovernedFileIdentity $Path $Path $RepositoryRoot
    try { $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) }
    catch { throw "STATE_READ_FAILED: state could not be read safely. No resource was changed." }
    $identityAfter = Get-GovernedFileIdentity $Path $Path $RepositoryRoot
    Assert-FileIdentityEqual $identityAfter $identityBefore 'STATE_READ_RACE'
    try { $state = $raw | ConvertFrom-Json }
    catch { throw "STATE_MALFORMED: state is not valid JSON. No resource was changed." }
    if ([int](Get-RequiredStateProperty $state 'StateSchemaVersion' 'state') -ne $stateSchemaVersion) { throw "STATE_UNSUPPORTED_SCHEMA: unsupported state." }
    if ([string](Get-RequiredStateProperty $state 'WalkthroughIdentity' 'state') -cne $walkthroughIdentity) { throw "STATE_OWNERSHIP_MISMATCH: walkthrough identity differs." }
    Assert-OwnedPath ([string]$state.RepositoryRoot) $RepositoryRoot 'repository root' -RequireExact
    Assert-OwnedPath ([string]$state.StatePath) $statePath 'state path' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $state 'LogsRoot' 'state')) $logsRoot 'logs root' -RequireExact
    Assert-OwnedPath ([string]$state.EvidenceRoot) $expectedEvidenceRoot 'evidence root' -RequireExact
    Assert-OwnedPath ([string]$state.SyntheticEvidencePath) $expectedSyntheticEvidencePath 'synthetic evidence path' -RequireExact
    [void](Assert-NoReparsePoint $statePath $RepositoryRoot)
    if (Test-Path -LiteralPath $logsRoot) { [void](Assert-NoReparsePoint $logsRoot $RepositoryRoot) }
    if (Test-Path -LiteralPath $expectedEvidenceRoot) { [void](Assert-NoReparsePoint $expectedEvidenceRoot (Split-Path -Parent $expectedEvidenceRoot)) }
    $runId = [guid]::Empty
    if (-not [guid]::TryParse([string]$state.RunId, [ref]$runId) -or $runId -eq [guid]::Empty) { throw "STATE_MALFORMED: invalid RunId." }
    if ([string]$state.StartupMode -cne 'FRESH' -or [string]$state.LifecycleStatus -cne 'READY') { throw "STATE_NOT_RESTARTABLE: lifecycle is not safely reconcilable." }
    $created = Assert-StateTimestamp ([string]$state.CreatedAtUtc) 'CreatedAtUtc'
    $updated = Assert-StateTimestamp ([string]$state.LastValidUpdateAtUtc) 'LastValidUpdateAtUtc'
    if ($updated -lt $created) { throw "STATE_MALFORMED: update precedes creation." }
    Assert-SafeDatabaseName ([string]$state.DatabaseName)
    if ([string]$state.DatabaseHost -cne '127.0.0.1' -or [int]$state.DatabasePort -ne $PostgresPort -or
        [string]::IsNullOrWhiteSpace([string]$state.PostgresContainerId)) { throw "STATE_DATABASE_MISMATCH: database identity is not local and exact." }
    $network = Get-RequiredStateProperty $state 'Network' 'state'
    if ([string]$network.Name -cne $expectedNetworkName -or [string]$network.OwnershipLabel -cne $ownershipLabel -or [string]::IsNullOrWhiteSpace([string]$network.Id)) { throw "STATE_OWNERSHIP_MISMATCH: network identity differs." }
    $containers = @($state.Containers)
    if ($containers.Count -ne $expectedContainerNames.Count) { throw "STATE_OWNERSHIP_MISMATCH: container set differs." }
    foreach ($name in $expectedContainerNames) {
        $matches = @($containers | Where-Object { [string]$_.Name -ceq $name })
        if ($matches.Count -ne 1 -or [string]$matches[0].OwnershipLabel -cne $ownershipLabel -or [string]::IsNullOrWhiteSpace([string]$matches[0].Id)) { throw "STATE_OWNERSHIP_MISMATCH: container '$name' differs." }
    }
    $processNames = @('central-pms', 'payment-orchestrator', 'webpay-ui', 'operator-console-ui')
    $launcherNames = @('central-pms-launcher', 'payment-orchestrator-launcher', 'webpay-ui-launcher', 'operator-console-ui-launcher')
    if (@($state.Processes).Count -ne 4 -or @($state.Launchers).Count -ne 4) { throw "STATE_NOT_RESTARTABLE: process set is incomplete." }
    foreach ($record in @($state.Processes)) { Assert-ProcessRecordStructure $record $processNames }
    foreach ($record in @($state.Launchers)) { Assert-ProcessRecordStructure $record $launcherNames }
    foreach ($name in $processNames) { if (@($state.Processes | Where-Object { $_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: process '$name' is missing or duplicated." } }
    foreach ($name in $launcherNames) { if (@($state.Launchers | Where-Object { $_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: launcher '$name' is missing or duplicated." } }
    [void](Get-RequiredStateProperty $state 'Fixture' 'state')
    [void](Get-RequiredStateProperty $state 'Urls' 'state')
    if (($state | ConvertTo-Json -Depth 10 -Compress) -match '(?i)"[^"\\]*(password|secret|token|connection.?string|provisioning|upload.?url)[^"\\]*"\s*:') { throw "STATE_SECRET_FIELD_PROHIBITED: prohibited property." }
    return [pscustomobject]@{ State = $state; Identity = $identityAfter }
}

function Get-ProcessCommandLine([int]$Id) {
    try { return Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop }
    catch { return $null }
}

function Get-RuntimeProcessById([int]$Id) {
    try { return [System.Diagnostics.Process]::GetProcessById($Id) }
    catch [System.ArgumentException] { return $null }
    catch { throw "PROCESS_LOOKUP_FAILED: process $Id could not be queried safely." }
}

function Get-ValidatedProcess($Record) {
    $runtime = Get-RuntimeProcessById ([int]$Record.Id)
    if ($null -eq $runtime) { return $null }
    $retainedHandle = $runtime.SafeHandle
    if ($null -eq $retainedHandle -or $retainedHandle.IsInvalid -or $retainedHandle.IsClosed) {
        $runtime.Dispose()
        throw "PROCESS_OWNERSHIP_MISMATCH: a stable process handle could not be retained."
    }
    $details = Get-ProcessCommandLine ([int]$Record.Id)
    if ($runtime.HasExited -or $null -eq $details -or [string]::IsNullOrWhiteSpace($details.ExecutablePath) -or [string]::IsNullOrWhiteSpace($details.CommandLine)) {
        $runtime.Dispose()
        throw "PROCESS_OWNERSHIP_MISMATCH: process identity is unavailable or changed while its handle was retained."
    }
    $recordedStart = [DateTimeOffset]::Parse([string]$Record.StartTimeUtc).UtcDateTime
    if ([math]::Abs(($runtime.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -gt 2 -or
        -not (Test-CanonicalPathEqual $details.ExecutablePath $Record.ExecutablePath)) {
        $runtime.Dispose()
        throw "PROCESS_OWNERSHIP_MISMATCH: PID, executable, or start time changed."
    }
    foreach ($marker in @($Record.CommandLineMarkers)) {
        if ($details.CommandLine -notlike "*$marker*") {
            $runtime.Dispose()
            throw "PROCESS_OWNERSHIP_MISMATCH: command marker changed."
        }
    }
    if ($runtime.HasExited -or $retainedHandle.IsInvalid -or $retainedHandle.IsClosed) {
        $runtime.Dispose()
        throw "PROCESS_OWNERSHIP_MISMATCH: retained process identity changed before termination."
    }
    return $runtime
}

function Stop-ValidatedProcess($Record) {
    $runtime = Get-ValidatedProcess $Record
    if ($null -eq $runtime) { Write-Host "$($Record.Name) PID $($Record.Id) is absent."; return }
    try {
        if ($DryRun) { Write-Host "DRY RUN: would stop validated $($Record.Name) PID $($Record.Id)."; return }
        $runtime.Kill()
        if (-not $runtime.WaitForExit(10000)) { throw "Validated process did not exit within the bounded wait." }
    }
    finally { $runtime.Dispose() }
}

function Test-ContainerConfirmedAbsent([string]$Name) {
    $inventory = @(docker container ls -a --format '{{.ID}}|{{.Names}}')
    if ($LASTEXITCODE -ne 0) {
        throw "CONTAINER_INSPECTION_FAILED: Docker could not confirm whether container '$Name' is absent."
    }
    foreach ($line in $inventory) {
        if ([string]::IsNullOrWhiteSpace([string]$line)) { continue }
        $parts = ([string]$line).Trim() -split '\|', 2
        if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
            throw "CONTAINER_INSPECTION_FAILED: Docker returned malformed container inventory."
        }
        if ($parts[1] -ceq $Name) { return $false }
    }
    return $true
}

function Get-ValidatedContainerId($Record) {
    $byName = docker container inspect --format '{{.Id}}|{{.Name}}|{{index .Config.Labels "exitpass.walkthrough"}}' $Record.Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        if (Test-ContainerConfirmedAbsent ([string]$Record.Name)) { return $null }
        throw "CONTAINER_INSPECTION_FAILED: container '$($Record.Name)' exists but could not be inspected safely."
    }
    $parts = ([string]$byName).Trim() -split '\|', 3
    if ($parts.Count -ne 3 -or $parts[0] -cne [string]$Record.Id -or $parts[1].TrimStart('/') -cne [string]$Record.Name -or $parts[2] -cne $ownershipLabel) { throw "CONTAINER_OWNERSHIP_MISMATCH: name, ID, type, or label changed." }
    $byId = docker container inspect --format '{{.Id}}|{{.Name}}|{{index .Config.Labels "exitpass.walkthrough"}}' $Record.Id 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]$byId -cne [string]$byName) { throw "CONTAINER_OWNERSHIP_MISMATCH: immutable container ID no longer resolves identically." }
    return [string]$Record.Id
}

function Remove-ValidatedContainer($Record) {
    $id = Get-ValidatedContainerId $Record
    if ([string]::IsNullOrWhiteSpace($id)) { Write-Host "Walkthrough container $($Record.Name) is absent."; return }
    if ($DryRun) { Write-Host "DRY RUN: would remove owned container ID $id."; return }
    docker container rm -f $id | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not remove validated container ID $id." }
}

function Test-NetworkConfirmedAbsent([string]$Name) {
    $inventory = @(docker network ls --format '{{.ID}}|{{.Name}}')
    if ($LASTEXITCODE -ne 0) {
        throw "NETWORK_INSPECTION_FAILED: Docker could not confirm whether network '$Name' is absent."
    }
    foreach ($line in $inventory) {
        if ([string]::IsNullOrWhiteSpace([string]$line)) { continue }
        $parts = ([string]$line).Trim() -split '\|', 2
        if ($parts.Count -ne 2 -or [string]::IsNullOrWhiteSpace($parts[0]) -or [string]::IsNullOrWhiteSpace($parts[1])) {
            throw "NETWORK_INSPECTION_FAILED: Docker returned malformed network inventory."
        }
        if ($parts[1] -ceq $Name) { return $false }
    }
    return $true
}

function Get-ValidatedNetworkId($Record) {
    $byName = docker network inspect --format '{{.Id}}|{{.Name}}|{{index .Labels "exitpass.walkthrough"}}' $Record.Name 2>$null
    if ($LASTEXITCODE -ne 0) {
        if (Test-NetworkConfirmedAbsent ([string]$Record.Name)) { return $null }
        throw "NETWORK_INSPECTION_FAILED: network '$($Record.Name)' exists but could not be inspected safely."
    }
    $parts = ([string]$byName).Trim() -split '\|', 3
    if ($parts.Count -ne 3 -or $parts[0] -cne [string]$Record.Id -or $parts[1] -cne [string]$Record.Name -or $parts[2] -cne $ownershipLabel) { throw "NETWORK_OWNERSHIP_MISMATCH: name, ID, type, or label changed." }
    $byId = docker network inspect --format '{{.Id}}|{{.Name}}|{{index .Labels "exitpass.walkthrough"}}' $Record.Id 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]$byId -cne [string]$byName) { throw "NETWORK_OWNERSHIP_MISMATCH: immutable network ID no longer resolves identically." }
    return [string]$Record.Id
}

function Remove-ValidatedNetwork($Record) {
    $id = Get-ValidatedNetworkId $Record
    if ([string]::IsNullOrWhiteSpace($id)) { Write-Host "Walkthrough network $($Record.Name) is absent."; return }
    if ($DryRun) { Write-Host "DRY RUN: would remove owned network ID $id."; return }
    docker network rm $id | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not remove validated network ID $id." }
}

function Get-ValidatedPostgresContainerId($State) {
    Assert-SafeDatabaseName ([string]$State.DatabaseName)
    if ([string]$State.DatabaseHost -cne '127.0.0.1' -or [int]$State.DatabasePort -ne $PostgresPort) { throw "DATABASE_OWNERSHIP_MISMATCH: host or port changed." }
    $byName = docker container inspect --format '{{.Id}}|{{.Name}}' $State.PostgresContainerName 2>$null
    if ($LASTEXITCODE -ne 0) { throw "DATABASE_OWNERSHIP_MISMATCH: PostgreSQL container is absent." }
    $parts = ([string]$byName).Trim() -split '\|', 2
    if ($parts.Count -ne 2 -or $parts[0] -cne [string]$State.PostgresContainerId -or $parts[1].TrimStart('/') -cne [string]$State.PostgresContainerName) { throw "DATABASE_OWNERSHIP_MISMATCH: PostgreSQL name or immutable ID changed." }
    $byId = docker container inspect --format '{{.Id}}|{{.Name}}' $State.PostgresContainerId 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]$byId -cne [string]$byName) { throw "DATABASE_OWNERSHIP_MISMATCH: immutable PostgreSQL container ID no longer resolves identically." }
    return [string]$State.PostgresContainerId
}

function Invoke-ValidatedDisposableDatabaseCleanup($State, [scriptblock]$BetweenCommands) {
    Assert-SafeDatabaseName ([string]$State.DatabaseName)
    if ([string]$State.DatabaseHost -cne '127.0.0.1' -or [int]$State.DatabasePort -ne $PostgresPort) {
        throw "DATABASE_OWNERSHIP_MISMATCH: database host or port is not the exact local walkthrough identity."
    }
    $postgresId = Get-ValidatedPostgresContainerId $State
    docker exec -i $postgresId psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$($State.DatabaseName)';"
    if ($LASTEXITCODE -ne 0) { throw "Could not terminate walkthrough database connections." }
    if ($null -ne $BetweenCommands) { & $BetweenCommands }
    Assert-SafeDatabaseName ([string]$State.DatabaseName)
    if ([string]$State.DatabaseHost -cne '127.0.0.1' -or [int]$State.DatabasePort -ne $PostgresPort) {
        throw "DATABASE_OWNERSHIP_MISMATCH: database host or port changed before deletion."
    }
    $postgresId = Get-ValidatedPostgresContainerId $State
    docker exec -i $postgresId psql -v ON_ERROR_STOP=1 -U exitpass -d postgres -c "DROP DATABASE IF EXISTS $($State.DatabaseName);"
    if ($LASTEXITCODE -ne 0) { throw "Could not remove guarded disposable database." }
}

function Add-GovernedTreePlanEntry([string]$Path, [string]$GovernedRoot, $Directories, $Files) {
    $canonicalPath = Assert-NoReparsePoint $Path $GovernedRoot
    $item = Get-Item -LiteralPath $canonicalPath -Force
    if (-not $item.PSIsContainer) {
        [void]$Files.Add((Get-GovernedFileIdentity $canonicalPath $canonicalPath $GovernedRoot))
        return
    }
    [void]$Directories.Add([pscustomobject]@{
        Path = $canonicalPath
        CreationTimeUtcTicks = [long]$item.CreationTimeUtc.Ticks
    })
    foreach ($child in @(Get-ChildItem -LiteralPath $canonicalPath -Force)) {
        Add-GovernedTreePlanEntry $child.FullName $GovernedRoot $Directories $Files
    }
}

function Assert-CleanupTree([string]$Path, [string]$GovernedRoot) {
    $canonicalPath = Get-CanonicalPath $Path
    $canonicalRoot = Get-CanonicalPath $GovernedRoot
    if (-not (Test-Path -LiteralPath $canonicalPath)) {
        return [pscustomobject]@{ Exists = $false; Path = $canonicalPath; GovernedRoot = $canonicalRoot; Directories = @(); Files = @() }
    }
    if (Test-CanonicalPathEqual $canonicalPath $stateRoot) { throw "STATE_ROOT_RECURSIVE_DELETE_PROHIBITED: the walkthrough state root is never recursively deleted." }
    $directories = New-Object System.Collections.Generic.List[object]
    $files = New-Object System.Collections.Generic.List[object]
    Add-GovernedTreePlanEntry $canonicalPath $canonicalRoot $directories $files
    return [pscustomobject]@{
        Exists = $true
        Path = $canonicalPath
        GovernedRoot = $canonicalRoot
        Directories = @($directories.ToArray())
        Files = @($files.ToArray())
    }
}

function Assert-CleanupFile([string]$Path, [string]$GovernedRoot) {
    $canonicalPath = Get-CanonicalPath $Path
    $canonicalRoot = Get-CanonicalPath $GovernedRoot
    if (-not (Test-Path -LiteralPath $canonicalPath)) {
        return [pscustomobject]@{ Exists = $false; Path = $canonicalPath; GovernedRoot = $canonicalRoot; Identity = $null }
    }
    if (-not (Test-Path -LiteralPath $canonicalPath -PathType Leaf)) {
        throw "GOVERNED_FILE_IDENTITY_CHANGED: governed fixture path is not a file."
    }
    return [pscustomobject]@{
        Exists = $true
        Path = $canonicalPath
        GovernedRoot = $canonicalRoot
        Identity = Get-GovernedFileIdentity $canonicalPath $canonicalPath $canonicalRoot
    }
}

function Assert-GovernedFilePlanCurrent($Plan) {
    if (-not [bool]$Plan.Exists) {
        if (Test-Path -LiteralPath $Plan.Path) { throw "GOVERNED_FILE_SUBSTITUTED: an absent governed fixture appeared before cleanup." }
        return
    }
    if (-not (Test-Path -LiteralPath $Plan.Path -PathType Leaf)) {
        throw "GOVERNED_FILE_IDENTITY_CHANGED: governed fixture disappeared or changed type before cleanup."
    }
    $current = Get-GovernedFileIdentity $Plan.Path $Plan.Path $Plan.GovernedRoot
    Assert-FileIdentityEqual $current $Plan.Identity 'GOVERNED_FILE_IDENTITY_CHANGED'
}

function Remove-GovernedFile($Plan) {
    Assert-GovernedFilePlanCurrent $Plan
    if (-not [bool]$Plan.Exists) { return }
    $current = Get-GovernedFileIdentity $Plan.Path $Plan.Path $Plan.GovernedRoot
    Assert-FileIdentityEqual $current $Plan.Identity 'GOVERNED_FILE_IDENTITY_CHANGED'
    [System.IO.File]::Delete($Plan.Path)
    if (Test-Path -LiteralPath $Plan.Path) { throw "GOVERNED_FILE_DELETE_FAILED: governed fixture still exists after deletion." }
}

function Assert-GovernedTreePlanCurrent($Plan) {
    if (-not [bool]$Plan.Exists) {
        if (Test-Path -LiteralPath $Plan.Path) { throw "GOVERNED_TREE_SUBSTITUTED: an absent governed path appeared before cleanup." }
        return
    }
    $current = Assert-CleanupTree ([string]$Plan.Path) ([string]$Plan.GovernedRoot)
    if (-not [bool]$current.Exists -or $current.Directories.Count -ne $Plan.Directories.Count -or $current.Files.Count -ne $Plan.Files.Count) {
        throw "GOVERNED_TREE_SUBSTITUTED: governed directory contents changed before cleanup."
    }
    foreach ($expectedFile in @($Plan.Files)) {
        $matches = @($current.Files | Where-Object { Test-CanonicalPathEqual $_.Path $expectedFile.Path })
        if ($matches.Count -ne 1) { throw "GOVERNED_TREE_SUBSTITUTED: governed file set changed before cleanup." }
        Assert-FileIdentityEqual $matches[0] $expectedFile 'GOVERNED_FILE_IDENTITY_CHANGED'
    }
    foreach ($expectedDirectory in @($Plan.Directories)) {
        $matches = @($current.Directories | Where-Object { Test-CanonicalPathEqual $_.Path $expectedDirectory.Path })
        if ($matches.Count -ne 1 -or [long]$matches[0].CreationTimeUtcTicks -ne [long]$expectedDirectory.CreationTimeUtcTicks) {
            throw "GOVERNED_DIRECTORY_IDENTITY_CHANGED: governed directory identity changed before cleanup."
        }
    }
}

function Remove-GovernedTree($Plan) {
    Assert-GovernedTreePlanCurrent $Plan
    if (-not [bool]$Plan.Exists) { return }
    foreach ($file in @($Plan.Files)) {
        if (-not (Test-Path -LiteralPath $file.Path -PathType Leaf)) { continue }
        $currentFile = Get-GovernedFileIdentity $file.Path $file.Path $Plan.GovernedRoot
        Assert-FileIdentityEqual $currentFile $file 'GOVERNED_FILE_IDENTITY_CHANGED'
        [System.IO.File]::Delete($file.Path)
    }
    $orderedDirectories = @($Plan.Directories | Sort-Object { ([string]$_.Path).Length } -Descending)
    foreach ($directory in $orderedDirectories) {
        if (-not (Test-Path -LiteralPath $directory.Path -PathType Container)) { continue }
        [void](Assert-NoReparsePoint $directory.Path $Plan.GovernedRoot)
        $currentDirectory = Get-Item -LiteralPath $directory.Path -Force
        if ([long]$currentDirectory.CreationTimeUtc.Ticks -ne [long]$directory.CreationTimeUtcTicks) {
            throw "GOVERNED_DIRECTORY_IDENTITY_CHANGED: governed directory changed immediately before deletion."
        }
        if (@(Get-ChildItem -LiteralPath $directory.Path -Force).Count -ne 0) { throw "GOVERNED_DIRECTORY_NOT_EMPTY: governed directory changed during cleanup." }
        [System.IO.Directory]::Delete($directory.Path, $false)
    }
}

function Remove-ValidatedStateFileLast($Snapshot, $Lock, [scriptblock]$BeforeFinalValidation) {
    Assert-WalkthroughOperationLockHeld $Lock
    if (-not (Test-CanonicalPathEqual ([string]$Snapshot.State.StatePath) $statePath)) {
        throw "STATE_DELETE_PATH_MISMATCH: validated state is not bound to the exact destination."
    }
    if ($null -ne $BeforeFinalValidation) { & $BeforeFinalValidation $statePath }
    Assert-WalkthroughOperationLockHeld $Lock
    [void](Assert-NoReparsePoint $statePath $RepositoryRoot)
    $stateNow = Get-GovernedFileIdentity $statePath $statePath $RepositoryRoot
    Assert-FileIdentityEqual $stateNow $Snapshot.Identity 'STATE_DELETE_IDENTITY_CHANGED'
    Assert-WalkthroughOperationLockHeld $Lock
    [System.IO.File]::Delete($statePath)
    if (Test-Path -LiteralPath $statePath) { throw "STATE_DELETE_FAILED: validated state still exists after deletion." }
}

function Remove-GovernedGeneratedState($Snapshot, $Lock, $FixturePlan, [scriptblock]$AfterEvidenceCleanup, [scriptblock]$BeforeStateDelete) {
    $evidencePlan = Assert-CleanupTree $expectedEvidenceRoot (Split-Path -Parent $expectedEvidenceRoot)
    $logsPlan = Assert-CleanupTree $logsRoot $stateRoot
    Assert-GovernedFilePlanCurrent $FixturePlan
    Remove-GovernedTree $evidencePlan
    if ($null -ne $AfterEvidenceCleanup) { & $AfterEvidenceCleanup }
    Remove-GovernedTree $logsPlan
    Remove-GovernedFile $FixturePlan
    $remaining = @(Get-ChildItem -LiteralPath $stateRoot -Force | Where-Object { -not (Test-CanonicalPathEqual $_.FullName $statePath) })
    if ($remaining.Count -ne 0) { throw "STATE_CLEANUP_PLAN_REJECTED: state directory is not empty except for state.json." }
    Remove-ValidatedStateFileLast $Snapshot $Lock $BeforeStateDelete
    [void](Assert-NoReparsePoint $stateRoot $RepositoryRoot)
    if (@(Get-ChildItem -LiteralPath $stateRoot -Force).Count -ne 0) { throw "STATE_DIRECTORY_NOT_EMPTY: state was deleted but the directory gained another entry." }
    [System.IO.Directory]::Delete($stateRoot, $false)
}

$operationLock = Enter-WalkthroughOperationLock $RepositoryRoot
$stateDeleted = $false
$fixturePlan = $null
try {
    $snapshot = Read-ValidatedWalkthroughState $statePath $operationLock
    $state = $snapshot.State
    if (-not [string]::IsNullOrWhiteSpace($DatabaseName) -and $DatabaseName -ne $state.DatabaseName) { throw "Requested database differs from state." }
    if (-not [string]::IsNullOrWhiteSpace($PostgresContainerName) -and $PostgresContainerName -ne $state.PostgresContainerName) { throw "Requested PostgreSQL container differs from state." }
    $DatabaseName = [string]$state.DatabaseName
    $PostgresContainerName = [string]$state.PostgresContainerName

    foreach ($record in @($state.Processes) + @($state.Launchers)) { $process = Get-ValidatedProcess $record; if ($null -ne $process) { $process.Dispose() } }
    if ($StopWalkthroughContainers) {
        foreach ($record in @($state.Containers)) { [void](Get-ValidatedContainerId $record) }
        [void](Get-ValidatedNetworkId $state.Network)
    }
    if ($RemoveDisposableDatabase) { [void](Get-ValidatedPostgresContainerId $state) }
    if ($RemoveGeneratedState) {
        if (-not $StopWalkthroughContainers -or -not $RemoveDisposableDatabase) { throw "STATE_CLEANUP_INCOMPLETE: generated-state removal requires container and database cleanup." }
        Assert-CleanupTree $expectedEvidenceRoot (Split-Path -Parent $expectedEvidenceRoot)
        Assert-CleanupTree $logsRoot $stateRoot
        $fixturePlan = Assert-CleanupFile $fixtureContextPath $stateRoot
        foreach ($child in @(Get-ChildItem -LiteralPath $stateRoot -Force)) {
            if ($child.FullName -notin @($statePath, $fixtureContextPath, $logsRoot)) { throw "STATE_CLEANUP_PLAN_REJECTED: unexpected state-root entry '$($child.Name)'." }
        }
    }

    foreach ($record in @($state.Processes)) { Stop-ValidatedProcess $record }
    foreach ($record in @($state.Launchers)) { Stop-ValidatedProcess $record }

    if ($StopWalkthroughContainers) {
        foreach ($record in @($state.Containers)) { Remove-ValidatedContainer $record }
        Remove-ValidatedNetwork $state.Network
    }

    if ($RemoveDisposableDatabase) {
        if ($DryRun) { Write-Host "DRY RUN: would terminate connections and remove guarded database $DatabaseName through immutable container ID." }
        else {
            Invoke-ValidatedDisposableDatabaseCleanup $state
        }
    }

    if ($RemoveGeneratedState) {
        if ($DryRun) { Write-Host "DRY RUN: would delete governed evidence and logs individually, then state.json last, then the empty state directory." }
        else {
            Remove-GovernedGeneratedState $snapshot $operationLock $fixturePlan
            $stateDeleted = $true
        }
    }
    else {
        Write-Host "Walkthrough logs, state, and synthetic evidence were preserved for verification." -ForegroundColor Cyan
    }
}
catch {
    if (-not $stateDeleted -and (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw "PARTIAL_CLEANUP_BLOCKED_STATE_PRESERVED: $($_.Exception.Message)"
    }
    throw
}
finally {
    Exit-WalkthroughOperationLock $operationLock
}

Write-Host "Shared PostgreSQL, RabbitMQ, and mock-payment-provider containers were not stopped or removed."
Write-Host "No unrecorded process, container, network, database, bucket, or file was changed."
