param(
    [string]$RepositoryRoot,
    [string]$CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2",
    [string]$DatabaseName = "exitpass_webpay_local_walkthrough_statutory",
    [string]$PostgresContainerName = "exitpass-postgres",
    [string]$DatabaseUser = "exitpass",
    [int]$PostgresPort = 5433,
    [int]$CentralPmsPort = 8080,
    [int]$PaymentOrchestratorPort = 8082,
    [int]$MockPaymentProviderPort = 8084,
    [int]$WebPayPort = 5174,
    [int]$OperatorConsolePort = 5175,
    [int]$MinioApiPort = 19000,
    [int]$MinioConsolePort = 19001,
    [int]$ClamAvPort = 13310,
    [string]$MinioImage = "minio/minio:latest",
    [string]$MinioClientImage = "minio/mc:latest",
    [string]$ClamAvImage = "clamav/clamav:stable",
    [switch]$RestartServicesOnly,
    [switch]$AllowExistingPorts,
    [switch]$VisibleServiceWindows,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$CanonicalDatabaseRepository = [System.IO.Path]::GetFullPath($CanonicalDatabaseRepository)
$stateRoot = Join-Path $RepositoryRoot ".local\webpay-statutory-discount-walkthrough"
$statePath = Join-Path $stateRoot "state.json"
$fixtureContextPath = Join-Path $stateRoot "fixture-context.json"
$logsRoot = Join-Path $stateRoot "logs"
$evidenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ExitPass\webpay-statutory-discount-walkthrough"
$syntheticEvidencePath = Join-Path $evidenceRoot "synthetic-senior-citizen-id.png"
$canonicalSql = Join-Path $CanonicalDatabaseRepository "build\generated\exitpass-full-object.generated.sql"
$canonicalValidator = Join-Path $CanonicalDatabaseRepository "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$paymentRoutingPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql"
$payMongoRailPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql"
$ordinarySeed = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql"
$pilotSeed = Join-Path $RepositoryRoot "scripts\operator-console\Seed-StatutoryDiscountPilotFixture.sql"
$rbacSource = Join-Path $RepositoryRoot "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql"
$statutorySeed = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql"
$statutoryVerify = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"
$centralPmsProject = Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"
$paymentOrchestratorProject = Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj"
$webPayRoot = Join-Path $RepositoryRoot "src\Services\WebPayUi"
$operatorConsoleRoot = Join-Path $RepositoryRoot "src\Services\OperatorConsoleUi"
$minioContainerName = "exitpass-webpay-statutory-minio"
$clamAvContainerName = "exitpass-webpay-statutory-clamav"
$networkName = "exitpass-webpay-statutory-walkthrough"
$bucketName = "exitpass-webpay-statutory-evidence"
$stateSchemaVersion = 2
$walkthroughIdentity = "ExitPass.WebPay.StatutoryDiscount.LocalWalkthrough"
$ownershipLabelName = "exitpass.walkthrough"
$ownershipLabelValue = "webpay-statutory-discount"
$ownershipLabel = "exitpass.walkthrough=webpay-statutory-discount"
$allowedWalkthroughContainerNames = @($minioContainerName, $clamAvContainerName)

function Assert-SafeDatabaseName([string]$Name) {
    if ($Name -notmatch '^exitpass_webpay_local_walkthrough_statutory(_[a-z0-9_]+)?$') {
        throw "Refusing database '$Name'. Use exitpass_webpay_local_walkthrough_statutory or a suffixed disposable variant."
    }
}

function Assert-PathExists([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Assert-Tool([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$Name' was not found in PATH."
    }
}

function Get-RequiredEnvironmentValue([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Set $Name in the current shell. Its value is not printed or persisted by this walkthrough."
    }
    return $value
}

function New-CryptographicRandomBytes([int]$Length) {
    if ($Length -le 0) {
        throw "Cryptographic random byte length must be greater than zero."
    }

    $bytes = New-Object byte[] $Length
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return ,$bytes
    }
    finally {
        $generator.Dispose()
    }
}

function Get-Sha256HashBytes([byte[]]$Bytes) {
    if ($null -eq $Bytes) {
        throw "SHA-256 input is required."
    }

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($Bytes)
        return ,$hash
    }
    finally {
        $algorithm.Dispose()
    }
}

function ConvertTo-LowercaseHex([byte[]]$Bytes) {
    if ($null -eq $Bytes) {
        throw "Hexadecimal input is required."
    }

    return ([System.BitConverter]::ToString($Bytes) -replace '-', '').ToLowerInvariant()
}

function New-CryptographicRandomLowercaseHex([int]$Length) {
    $bytes = New-CryptographicRandomBytes $Length
    try {
        return ConvertTo-LowercaseHex $bytes
    }
    finally {
        [System.Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Assert-CryptographicRuntimeCompatibility {
    $randomBytes = $null
    $hashBytes = $null
    try {
        $randomBytes = New-CryptographicRandomBytes 32
        if ($randomBytes.Length -ne 32) {
            throw "Cryptographic random generation returned an unexpected byte length."
        }

        $hexProbe = ConvertTo-LowercaseHex ([byte[]](0, 15, 16, 171, 255))
        if ($hexProbe -cne '000f10abff') {
            throw "Lowercase hexadecimal conversion did not match the required format."
        }

        $hashBytes = Get-Sha256HashBytes ([System.Text.Encoding]::ASCII.GetBytes('abc'))
        $hashHex = ConvertTo-LowercaseHex $hashBytes
        if ($hashHex -cne 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad') {
            throw "SHA-256 runtime validation did not match the expected test vector."
        }
    }
    catch {
        throw "Cryptographic runtime compatibility validation failed safely: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $randomBytes) {
            [System.Array]::Clear($randomBytes, 0, $randomBytes.Length)
        }
        if ($null -ne $hashBytes) {
            [System.Array]::Clear($hashBytes, 0, $hashBytes.Length)
        }
    }
}

function Get-WalkthroughOperationLockName([string]$CanonicalRepositoryRoot) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((Get-CanonicalPath $CanonicalRepositoryRoot).ToUpperInvariant())
    $hash = $null
    try {
        $hash = Get-Sha256HashBytes $bytes
        return "Local\ExitPass.WebPay.StatutoryDiscount.$((ConvertTo-LowercaseHex $hash).Substring(0, 32))"
    }
    finally {
        if ($null -ne $hash) { [System.Array]::Clear($hash, 0, $hash.Length) }
        [System.Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Enter-WalkthroughOperationLock([string]$CanonicalRepositoryRoot, [int]$TimeoutMilliseconds = 5000) {
    if ($TimeoutMilliseconds -lt 0 -or $TimeoutMilliseconds -gt 30000) {
        throw "STATE_LOCK_INVALID_TIMEOUT: lock timeout must be between 0 and 30000 milliseconds."
    }
    $name = Get-WalkthroughOperationLockName $CanonicalRepositoryRoot
    $mutex = New-Object System.Threading.Mutex($false, $name)
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne($TimeoutMilliseconds) }
        catch [System.Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) {
            throw "STATE_LOCK_CONTENDED: another cooperating walkthrough startup, restart, or stop operation holds the exclusive lock."
        }
        return [pscustomobject]@{ Name = $name; Mutex = $mutex; IsHeld = $true; OwnerThreadId = [System.Threading.Thread]::CurrentThread.ManagedThreadId }
    }
    catch {
        if (-not $acquired) { $mutex.Dispose() }
        throw
    }
}

function Assert-WalkthroughOperationLockHeld($Lock) {
    if ($null -eq $Lock -or -not [bool]$Lock.IsHeld -or $null -eq $Lock.Mutex -or
        [int]$Lock.OwnerThreadId -ne [System.Threading.Thread]::CurrentThread.ManagedThreadId) {
        throw "STATE_LOCK_NOT_HELD: the exclusive walkthrough operation lock is not held by this execution thread."
    }
}

function Exit-WalkthroughOperationLock($Lock) {
    if ($null -eq $Lock) { return }
    try {
        if ([bool]$Lock.IsHeld) {
            $Lock.Mutex.ReleaseMutex()
            $Lock.IsHeld = $false
        }
    }
    finally { $Lock.Mutex.Dispose() }
}

function Get-CanonicalPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "A canonical path value is required."
    }
    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
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
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) {
            throw "STATE_PATH_ESCAPE: governed path parent chain did not reach its root."
        }
        $cursor = Get-CanonicalPath $parent
    }
    return $canonicalPath
}

function Get-GovernedFileIdentity([string]$Path, [string]$ExpectedPath, [string]$GovernedRoot) {
    $canonicalPath = Get-CanonicalPath $Path
    if (-not (Test-CanonicalPathEqual $canonicalPath $ExpectedPath)) {
        throw "STATE_FILE_IDENTITY_MISMATCH: file path is not the exact expected path."
    }
    [void](Assert-NoReparsePoint $canonicalPath $GovernedRoot)
    $item = Get-Item -LiteralPath $canonicalPath -Force
    if ($item.PSIsContainer) { throw "STATE_FILE_IDENTITY_MISMATCH: expected a file but found a directory." }
    return [pscustomobject]@{
        Path = $canonicalPath
        Length = [long]$item.Length
        CreationTimeUtcTicks = [long]$item.CreationTimeUtc.Ticks
        LastWriteTimeUtcTicks = [long]$item.LastWriteTimeUtc.Ticks
        Sha256 = (Get-StateFileHash $canonicalPath)
    }
}

function Test-FileIdentityEqual($Left, $Right) {
    return $null -ne $Left -and $null -ne $Right -and
        (Test-CanonicalPathEqual ([string]$Left.Path) ([string]$Right.Path)) -and
        [long]$Left.Length -eq [long]$Right.Length -and
        [long]$Left.CreationTimeUtcTicks -eq [long]$Right.CreationTimeUtcTicks -and
        [long]$Left.LastWriteTimeUtcTicks -eq [long]$Right.LastWriteTimeUtcTicks -and
        [string]$Left.Sha256 -ceq [string]$Right.Sha256
}

function Assert-FileIdentityEqual($Actual, $Expected, [string]$Classification) {
    if (-not (Test-FileIdentityEqual $Actual $Expected)) {
        throw "${Classification}: governed file identity or content changed."
    }
}

function Test-ByteArrayEqual([byte[]]$Left, [byte[]]$Right) {
    if ($null -eq $Left -or $null -eq $Right -or $Left.Length -ne $Right.Length) { return $false }
    for ($index = 0; $index -lt $Left.Length; $index++) {
        if ($Left[$index] -ne $Right[$index]) { return $false }
    }
    return $true
}

function Protect-WalkthroughFileAcl([string]$Path) {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $security = New-Object System.Security.AccessControl.FileSecurity
    $security.SetAccessRuleProtection($true, $false)
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        $identity.User,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow)
    [void]$security.AddAccessRule($rule)
    [System.IO.File]::SetAccessControl($Path, $security)
}

function Remove-InvocationOwnedFile($Identity, [string]$GovernedRoot) {
    if ($null -eq $Identity -or -not (Test-Path -LiteralPath $Identity.Path -PathType Leaf)) { return }
    $current = Get-GovernedFileIdentity $Identity.Path $Identity.Path $GovernedRoot
    Assert-FileIdentityEqual $current $Identity 'STATE_TEMPORARY_IDENTITY_MISMATCH'
    [System.IO.File]::Delete($Identity.Path)
}

function Test-CanonicalPathEqual([string]$Left, [string]$Right) {
    return [string]::Equals(
        (Get-CanonicalPath $Left),
        (Get-CanonicalPath $Right),
        [System.StringComparison]::OrdinalIgnoreCase)
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
    if (-not [string]::Equals($canonicalPath, $canonicalRoot, $comparison) -and
        -not $canonicalPath.StartsWith($prefix, $comparison)) {
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
    if (-not [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsed)) {
        throw "STATE_MALFORMED: $Name is not a valid round-trip timestamp."
    }
    return $parsed
}

function Assert-ProcessRecordStructure($Record, [string[]]$AllowedNames) {
    $name = [string](Get-RequiredStateProperty $Record 'Name' 'process record')
    if ($AllowedNames -notcontains $name) {
        throw "STATE_OWNERSHIP_MISMATCH: process '$name' is not in the walkthrough allowlist."
    }
    $id = [int](Get-RequiredStateProperty $Record 'Id' "process '$name'")
    if ($id -le 0) { throw "STATE_MALFORMED: process '$name' has an invalid PID." }
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

function Assert-WalkthroughStateContract(
    $State,
    [string]$ExpectedRepositoryRoot,
    [string]$ExpectedStatePath,
    [string]$ExpectedDatabaseName,
    [string]$ExpectedDatabaseHost,
    [int]$ExpectedDatabasePort,
    [string]$ExpectedPostgresContainerName,
    [string]$ExpectedLogsRoot,
    [string]$ExpectedEvidenceRoot,
    [string]$ExpectedSyntheticEvidencePath,
    [string]$ExpectedNetworkName,
    [string[]]$ExpectedContainerNames
) {
    if ([int](Get-RequiredStateProperty $State 'StateSchemaVersion' 'walkthrough state') -ne $stateSchemaVersion) {
        throw "STATE_UNSUPPORTED_SCHEMA: walkthrough state schema is not supported."
    }
    if ([string](Get-RequiredStateProperty $State 'WalkthroughIdentity' 'walkthrough state') -cne $walkthroughIdentity) {
        throw "STATE_OWNERSHIP_MISMATCH: walkthrough identity does not match."
    }
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'RepositoryRoot' 'walkthrough state')) $ExpectedRepositoryRoot 'repository root' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'StatePath' 'walkthrough state')) $ExpectedStatePath 'state path' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'LogsRoot' 'walkthrough state')) $ExpectedLogsRoot 'logs root' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'EvidenceRoot' 'walkthrough state')) $ExpectedEvidenceRoot 'evidence root' -RequireExact
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'SyntheticEvidencePath' 'walkthrough state')) $ExpectedEvidenceRoot 'synthetic evidence path'
    Assert-OwnedPath ([string](Get-RequiredStateProperty $State 'SyntheticEvidencePath' 'walkthrough state')) $ExpectedSyntheticEvidencePath 'synthetic evidence path' -RequireExact

    $runId = [guid]::Empty
    if (-not [guid]::TryParse([string](Get-RequiredStateProperty $State 'RunId' 'walkthrough state'), [ref]$runId) -or $runId -eq [guid]::Empty) {
        throw "STATE_MALFORMED: RunId is not a non-empty GUID."
    }
    if ([string](Get-RequiredStateProperty $State 'StartupMode' 'walkthrough state') -cne 'FRESH') {
        throw "STATE_MALFORMED: StartupMode is not supported."
    }
    if ([string](Get-RequiredStateProperty $State 'LifecycleStatus' 'walkthrough state') -cne 'READY') {
        throw "STATE_NOT_RESTARTABLE: lifecycle status is not restartable."
    }
    $createdAt = Assert-StateTimestamp ([string](Get-RequiredStateProperty $State 'CreatedAtUtc' 'walkthrough state')) 'CreatedAtUtc'
    $updatedAt = Assert-StateTimestamp ([string](Get-RequiredStateProperty $State 'LastValidUpdateAtUtc' 'walkthrough state')) 'LastValidUpdateAtUtc'
    if ($updatedAt -lt $createdAt) { throw "STATE_MALFORMED: last valid update precedes creation." }

    Assert-SafeDatabaseName ([string](Get-RequiredStateProperty $State 'DatabaseName' 'walkthrough state'))
    if ([string]$State.DatabaseName -cne $ExpectedDatabaseName -or
        [string](Get-RequiredStateProperty $State 'DatabaseHost' 'walkthrough state') -cne $ExpectedDatabaseHost -or
        [int](Get-RequiredStateProperty $State 'DatabasePort' 'walkthrough state') -ne $ExpectedDatabasePort -or
        [string](Get-RequiredStateProperty $State 'PostgresContainerName' 'walkthrough state') -cne $ExpectedPostgresContainerName) {
        throw "STATE_DATABASE_MISMATCH: recorded database identity does not match this walkthrough invocation."
    }
    if ([string]::IsNullOrWhiteSpace([string](Get-RequiredStateProperty $State 'PostgresContainerId' 'walkthrough state'))) {
        throw "STATE_MALFORMED: PostgreSQL container identity is missing."
    }

    $network = Get-RequiredStateProperty $State 'Network' 'walkthrough state'
    if ([string](Get-RequiredStateProperty $network 'Name' 'network record') -cne $ExpectedNetworkName -or
        [string](Get-RequiredStateProperty $network 'OwnershipLabel' 'network record') -cne $ownershipLabelValue -or
        [string]::IsNullOrWhiteSpace([string](Get-RequiredStateProperty $network 'Id' 'network record'))) {
        throw "STATE_OWNERSHIP_MISMATCH: network identity does not match the walkthrough allowlist."
    }

    $containers = @((Get-RequiredStateProperty $State 'Containers' 'walkthrough state'))
    if ($containers.Count -ne $ExpectedContainerNames.Count) {
        throw "STATE_OWNERSHIP_MISMATCH: recorded container set does not match the walkthrough allowlist."
    }
    foreach ($expectedName in $ExpectedContainerNames) {
        $matches = @($containers | Where-Object { [string]$_.Name -ceq $expectedName })
        if ($matches.Count -ne 1 -or [string]$matches[0].OwnershipLabel -cne $ownershipLabelValue -or
            [string]::IsNullOrWhiteSpace([string]$matches[0].Id)) {
            throw "STATE_OWNERSHIP_MISMATCH: container '$expectedName' lacks exact identity and ownership metadata."
        }
    }

    $processNames = @('central-pms', 'payment-orchestrator', 'webpay-ui', 'operator-console-ui')
    $launcherNames = @('central-pms-launcher', 'payment-orchestrator-launcher', 'webpay-ui-launcher', 'operator-console-ui-launcher')
    $processRecords = @((Get-RequiredStateProperty $State 'Processes' 'walkthrough state'))
    $launcherRecords = @((Get-RequiredStateProperty $State 'Launchers' 'walkthrough state'))
    if ($processRecords.Count -ne $processNames.Count -or $launcherRecords.Count -ne $launcherNames.Count) {
        throw "STATE_NOT_RESTARTABLE: recorded process set is incomplete or ambiguous."
    }
    foreach ($record in $processRecords) {
        Assert-ProcessRecordStructure $record $processNames
    }
    foreach ($record in $launcherRecords) {
        Assert-ProcessRecordStructure $record $launcherNames
    }
    foreach ($name in $processNames) {
        if (@($processRecords | Where-Object { [string]$_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: process '$name' is missing or duplicated." }
    }
    foreach ($name in $launcherNames) {
        if (@($launcherRecords | Where-Object { [string]$_.Name -ceq $name }).Count -ne 1) { throw "STATE_NOT_RESTARTABLE: launcher '$name' is missing or duplicated." }
    }

    [void](Get-RequiredStateProperty $State 'Fixture' 'walkthrough state')
    [void](Get-RequiredStateProperty $State 'Urls' 'walkthrough state')

    $json = $State | ConvertTo-Json -Depth 10 -Compress
    if ($json -match '(?i)"[^"\\]*(password|secret|token|connection.?string|provisioning|upload.?url)[^"\\]*"\s*:') {
        throw "STATE_SECRET_FIELD_PROHIBITED: state contains a prohibited secret-shaped property."
    }
    return $State
}

function Read-ValidatedWalkthroughState(
    [string]$Path,
    $Lock,
    [string]$ExpectedRepositoryRoot,
    [string]$ExpectedDatabaseName,
    [string]$ExpectedDatabaseHost,
    [int]$ExpectedDatabasePort,
    [string]$ExpectedPostgresContainerName,
    [string]$ExpectedLogsRoot,
    [string]$ExpectedEvidenceRoot,
    [string]$ExpectedSyntheticEvidencePath,
    [string]$ExpectedNetworkName,
    [string[]]$ExpectedContainerNames
) {
    Assert-WalkthroughOperationLockHeld $Lock
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "STATE_NOT_FOUND: validated restart requires state at $Path."
    }
    [void](Assert-NoReparsePoint $Path $ExpectedRepositoryRoot)
    $identityBefore = Get-GovernedFileIdentity $Path $Path $ExpectedRepositoryRoot
    try {
        $raw = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    }
    catch {
        throw "STATE_READ_FAILED: state at $Path could not be read safely."
    }
    $identityAfter = Get-GovernedFileIdentity $Path $Path $ExpectedRepositoryRoot
    Assert-FileIdentityEqual $identityAfter $identityBefore 'STATE_READ_RACE'
    try { $state = $raw | ConvertFrom-Json }
    catch { throw "STATE_MALFORMED: state at $Path is not valid JSON." }
    $validated = Assert-WalkthroughStateContract $state $ExpectedRepositoryRoot $Path $ExpectedDatabaseName $ExpectedDatabaseHost $ExpectedDatabasePort $ExpectedPostgresContainerName $ExpectedLogsRoot $ExpectedEvidenceRoot $ExpectedSyntheticEvidencePath $ExpectedNetworkName $ExpectedContainerNames
    return [pscustomobject]@{ State = $validated; Identity = $identityAfter }
}

function Assert-FreshStateAbsent([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        throw "EXISTING_STATE_CONFLICT: fresh startup refused because walkthrough state exists at $Path. Use validated restart or separately governed cleanup; the file was not read or changed."
    }
}

function New-InvocationOwnedStateFile([byte[]]$Bytes, [string]$Parent, [string]$RunId, [string]$Purpose) {
    $stream = $null
    for ($attempt = 0; $attempt -lt 8; $attempt++) {
        $identity = $null
        $createdByInvocation = $false
        $random = New-CryptographicRandomLowercaseHex 16
        $path = Join-Path $Parent (".state.{0}.{1}.{2}.tmp" -f $RunId, $random, $Purpose)
        try {
            $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            $createdByInvocation = $true
            $stream.Write($Bytes, 0, $Bytes.Length)
            $stream.Flush()
            $stream.Dispose()
            $stream = $null
            Protect-WalkthroughFileAcl $path
            $identity = Get-GovernedFileIdentity $path $path $Parent
            $readback = [System.IO.File]::ReadAllBytes($path)
            if (-not (Test-ByteArrayEqual $readback $Bytes)) {
                throw "STATE_TEMPORARY_CONTENT_MISMATCH: temporary state bytes do not match validated serialization."
            }
            return [pscustomobject]@{ Path = $path; Identity = $identity; Bytes = $Bytes; CreatedByInvocation = $true }
        }
        catch [System.IO.IOException] {
            if ($null -ne $stream) { $stream.Dispose(); $stream = $null }
            if (-not $createdByInvocation -and (Test-Path -LiteralPath $path)) { continue }
            throw
        }
        catch {
            if ($null -ne $stream) { $stream.Dispose(); $stream = $null }
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                try {
                    $candidateIdentity = Get-GovernedFileIdentity $path $path $Parent
                    if ($null -ne $identity -and (Test-FileIdentityEqual $candidateIdentity $identity)) { [System.IO.File]::Delete($path) }
                }
                catch { }
            }
            throw
        }
    }
    throw "STATE_TEMPORARY_CREATE_FAILED: could not reserve an unpredictable invocation-owned temporary state file."
}

function Write-WalkthroughStateAtomically($State, [string]$Path, $Lock, [scriptblock]$BeforeAtomicCommit) {
    Assert-WalkthroughOperationLockHeld $Lock
    Assert-FreshStateAbsent $Path
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "ATOMIC_STATE_CREATE_FAILED: the validated state directory does not exist."
    }
    $governedRoot = [string](Get-RequiredStateProperty $State 'RepositoryRoot' 'walkthrough state')
    [void](Assert-NoReparsePoint $parent $governedRoot)
    $runId = [string](Get-RequiredStateProperty $State 'RunId' 'walkthrough state')
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($State | ConvertTo-Json -Depth 10))
    $temporary = $null
    try {
        $temporary = New-InvocationOwnedStateFile $bytes $parent $runId 'create'
        if ($null -ne $BeforeAtomicCommit) { & $BeforeAtomicCommit $Path }
        Assert-WalkthroughOperationLockHeld $Lock
        Assert-FreshStateAbsent $Path
        [void](Assert-NoReparsePoint $parent $governedRoot)
        $currentTemporary = Get-GovernedFileIdentity $temporary.Path $temporary.Path $parent
        Assert-FileIdentityEqual $currentTemporary $temporary.Identity 'STATE_TEMPORARY_IDENTITY_MISMATCH'
        if (-not (Test-ByteArrayEqual ([System.IO.File]::ReadAllBytes($temporary.Path)) $bytes)) {
            throw "STATE_TEMPORARY_CONTENT_MISMATCH: temporary state changed before atomic creation."
        }
        [System.IO.File]::Move($temporary.Path, $Path)
        $temporary.CreatedByInvocation = $false
    }
    catch {
        if ($null -ne $temporary -and $temporary.CreatedByInvocation) {
            Remove-InvocationOwnedFile $temporary.Identity $parent
        }
        throw "ATOMIC_STATE_CREATE_FAILED: state was not committed at $Path because create-if-absent ownership could not be established. Existing state was not overwritten."
    }
}

function Get-StateFileHash([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $hash = $null
    try {
        $hash = Get-Sha256HashBytes $bytes
        return ConvertTo-LowercaseHex $hash
    }
    finally {
        if ($null -ne $hash) { [System.Array]::Clear($hash, 0, $hash.Length) }
        if ($null -ne $bytes) { [System.Array]::Clear($bytes, 0, $bytes.Length) }
    }
}

function Update-WalkthroughStateAtomically(
    $State,
    [string]$Path,
    $ExpectedDestinationIdentity,
    $Lock,
    [scriptblock]$BeforeFinalVerification,
    [scriptblock]$BeforeReplace,
    [scriptblock]$BeforeBackupCleanup
) {
    Assert-WalkthroughOperationLockHeld $Lock
    $parent = Split-Path -Parent $Path
    $runId = [string](Get-RequiredStateProperty $State 'RunId' 'walkthrough state')
    if (-not (Test-CanonicalPathEqual ([string]$State.StatePath) $Path)) {
        throw "ATOMIC_STATE_UPDATE_FAILED: validated state is not bound to the expected destination."
    }
    $governedRoot = [string](Get-RequiredStateProperty $State 'RepositoryRoot' 'walkthrough state')
    [void](Assert-NoReparsePoint $parent $governedRoot)
    $bytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes(($State | ConvertTo-Json -Depth 10))
    $temporary = $null
    $backupIdentity = $null
    $replacementSucceeded = $false
    $backupPath = Join-Path $parent (".state.{0}.{1}.backup.tmp" -f $runId, (New-CryptographicRandomLowercaseHex 16))
    try {
        if (Test-Path -LiteralPath $backupPath) { throw "ATOMIC_STATE_UPDATE_FAILED: unpredictable backup path is unexpectedly occupied." }
        $temporary = New-InvocationOwnedStateFile $bytes $parent $runId 'update'
        if ($null -ne $BeforeFinalVerification) {
            & $BeforeFinalVerification $Path $temporary.Path $backupPath
        }
        Assert-WalkthroughOperationLockHeld $Lock
        [void](Assert-NoReparsePoint $Path $governedRoot)
        [void](Assert-NoReparsePoint $temporary.Path $parent)
        $destinationNow = Get-GovernedFileIdentity $Path $ExpectedDestinationIdentity.Path $governedRoot
        Assert-FileIdentityEqual $destinationNow $ExpectedDestinationIdentity 'ATOMIC_STATE_DESTINATION_CHANGED'
        $sourceNow = Get-GovernedFileIdentity $temporary.Path $temporary.Identity.Path $parent
        Assert-FileIdentityEqual $sourceNow $temporary.Identity 'STATE_TEMPORARY_IDENTITY_MISMATCH'
        if (-not (Test-ByteArrayEqual ([System.IO.File]::ReadAllBytes($temporary.Path)) $bytes)) {
            throw "STATE_TEMPORARY_CONTENT_MISMATCH: temporary state changed before replacement."
        }
        if ($null -ne $BeforeReplace) {
            & $BeforeReplace $Path $temporary.Path $backupPath
        }
        Assert-WalkthroughOperationLockHeld $Lock
        [void](Assert-NoReparsePoint $Path $governedRoot)
        [void](Assert-NoReparsePoint $temporary.Path $parent)
        $destinationFinal = Get-GovernedFileIdentity $Path $ExpectedDestinationIdentity.Path $governedRoot
        Assert-FileIdentityEqual $destinationFinal $ExpectedDestinationIdentity 'ATOMIC_STATE_DESTINATION_CHANGED'
        $sourceFinal = Get-GovernedFileIdentity $temporary.Path $temporary.Identity.Path $parent
        Assert-FileIdentityEqual $sourceFinal $temporary.Identity 'STATE_TEMPORARY_IDENTITY_MISMATCH'
        if (-not (Test-ByteArrayEqual ([System.IO.File]::ReadAllBytes($temporary.Path)) $bytes)) {
            throw "STATE_TEMPORARY_CONTENT_MISMATCH: temporary state changed immediately before replacement."
        }
        if (Test-Path -LiteralPath $backupPath) {
            throw "ATOMIC_STATE_BACKUP_PATH_OCCUPIED: replacement backup path became occupied."
        }
        Assert-WalkthroughOperationLockHeld $Lock
        [System.IO.File]::Replace($temporary.Path, $Path, $backupPath, $true)
        $replacementSucceeded = $true
        $temporary.CreatedByInvocation = $false
    }
    catch {
        if ($null -ne $temporary -and $temporary.CreatedByInvocation) {
            Remove-InvocationOwnedFile $temporary.Identity $parent
        }
        throw "ATOMIC_STATE_UPDATE_FAILED: restart metadata was not committed safely; preserve resources and use governed diagnosis."
    }

    $committed = Get-GovernedFileIdentity $Path $Path $governedRoot
    $expectedCommittedHash = ConvertTo-LowercaseHex (Get-Sha256HashBytes $bytes)
    if ([string]$committed.Sha256 -cne $expectedCommittedHash) {
        throw "ATOMIC_STATE_UPDATE_POSTCOMMIT_VERIFICATION_FAILED: replacement completed but committed bytes differ from validated state."
    }
    if ($replacementSucceeded -and (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
        try {
            if ($null -ne $BeforeBackupCleanup) { & $BeforeBackupCleanup $backupPath }
            Protect-WalkthroughFileAcl $backupPath
            $backupIdentity = Get-GovernedFileIdentity $backupPath $backupPath $parent
            if ([string]$backupIdentity.Sha256 -cne [string]$ExpectedDestinationIdentity.Sha256) {
                throw "backup content does not match the validated previous state"
            }
            Remove-InvocationOwnedFile $backupIdentity $parent
        }
        catch {
            Write-Warning "STATE_REPLACEMENT_COMMITTED_BACKUP_PRESERVED: replacement succeeded; backup cleanup did not complete and the backup remains at $backupPath."
        }
    }
    return [pscustomobject]@{ ReplacementCommitted = $true; BackupPath = if (Test-Path -LiteralPath $backupPath) { $backupPath } else { $null } }
}

function Get-RuntimeProcessById([int]$Id) {
    try { return [System.Diagnostics.Process]::GetProcessById($Id) }
    catch [System.ArgumentException] { return $null }
    catch { throw "PROCESS_LOOKUP_FAILED: process $Id could not be queried safely." }
}

function Assert-RecordedProcessRestartable($Record) {
    $runtime = Get-RuntimeProcessById ([int]$Record.Id)
    if ($null -eq $runtime) { return }
    try {
        try { $details = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$Record.Id)" -ErrorAction Stop }
        catch { throw "STATE_RESOURCE_MISMATCH: recorded PID $($Record.Id) cannot be queried safely." }
        if ($null -eq $details -or [string]::IsNullOrWhiteSpace([string]$details.ExecutablePath) -or
            [string]::IsNullOrWhiteSpace([string]$details.CommandLine)) {
            throw "STATE_RESOURCE_MISMATCH: recorded PID $($Record.Id) cannot be reconciled safely."
        }
        $recordedStart = [DateTimeOffset]::Parse([string]$Record.StartTimeUtc).UtcDateTime
        if ([math]::Abs(($runtime.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -gt 2 -or
            -not (Test-CanonicalPathEqual ([string]$details.ExecutablePath) ([string]$Record.ExecutablePath))) {
            throw "STATE_RESOURCE_MISMATCH: recorded PID $($Record.Id) was reused or changed identity."
        }
        foreach ($marker in @($Record.CommandLineMarkers)) {
            if ($details.CommandLine -notlike "*$marker*") {
                throw "STATE_RESOURCE_MISMATCH: recorded PID $($Record.Id) no longer has its ownership markers."
            }
        }
        throw "STATE_NOT_RESTARTABLE: recorded process '$($Record.Name)' is still running; validated shutdown is required before restart."
    }
    finally { $runtime.Dispose() }
}

function Assert-RestartResourceOwnership($State) {
    $postgresId = docker inspect --format '{{.Id}}' $State.PostgresContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or $postgresId -cne [string]$State.PostgresContainerId) {
        throw "STATE_RESOURCE_MISMATCH: PostgreSQL container identity cannot be reconciled."
    }
    foreach ($record in @($State.Containers)) {
        $currentId = docker inspect --format '{{.Id}}' $record.Name 2>$null
        $currentLabel = docker inspect --format '{{index .Config.Labels "exitpass.walkthrough"}}' $record.Name 2>$null
        $running = docker inspect --format '{{.State.Running}}' $record.Name 2>$null
        if ($LASTEXITCODE -ne 0 -or $currentId -cne [string]$record.Id -or
            $currentLabel -cne $ownershipLabelValue -or $running -cne 'true') {
            throw "STATE_RESOURCE_MISMATCH: container '$($record.Name)' cannot be reconciled as running walkthrough-owned state."
        }
    }
    $networkId = docker network inspect --format '{{.Id}}' $State.Network.Name 2>$null
    $networkLabel = docker network inspect --format '{{index .Labels "exitpass.walkthrough"}}' $State.Network.Name 2>$null
    if ($LASTEXITCODE -ne 0 -or $networkId -cne [string]$State.Network.Id -or $networkLabel -cne $ownershipLabelValue) {
        throw "STATE_RESOURCE_MISMATCH: Docker network identity cannot be reconciled."
    }
    foreach ($record in @($State.Processes) + @($State.Launchers)) {
        Assert-RecordedProcessRestartable $record
    }
}

function Assert-StateRuntimeOwnershipCurrent($State) {
    $postgresId = docker container inspect --format '{{.Id}}' $State.PostgresContainerName 2>$null
    if ($LASTEXITCODE -ne 0 -or $postgresId -cne [string]$State.PostgresContainerId) {
        throw "STATE_RESOURCE_MISMATCH: PostgreSQL container changed before state commit."
    }
    foreach ($record in @($State.Containers)) {
        $byName = docker container inspect --format '{{.Id}}|{{.Name}}|{{index .Config.Labels "exitpass.walkthrough"}}' $record.Name 2>$null
        if ($LASTEXITCODE -ne 0) { throw "STATE_RESOURCE_MISMATCH: container '$($record.Name)' disappeared before state commit." }
        $parts = ([string]$byName).Trim() -split '\|', 3
        if ($parts.Count -ne 3 -or $parts[0] -cne [string]$record.Id -or $parts[1].TrimStart('/') -cne [string]$record.Name -or $parts[2] -cne $ownershipLabelValue) {
            throw "STATE_RESOURCE_MISMATCH: container '$($record.Name)' changed before state commit."
        }
        $byId = docker container inspect --format '{{.Id}}|{{.Name}}|{{index .Config.Labels "exitpass.walkthrough"}}' $record.Id 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]$byId -cne [string]$byName) { throw "STATE_RESOURCE_MISMATCH: immutable container identity changed before state commit." }
    }
    $networkByName = docker network inspect --format '{{.Id}}|{{.Name}}|{{index .Labels "exitpass.walkthrough"}}' $State.Network.Name 2>$null
    if ($LASTEXITCODE -ne 0) { throw "STATE_RESOURCE_MISMATCH: network disappeared before state commit." }
    $networkParts = ([string]$networkByName).Trim() -split '\|', 3
    if ($networkParts.Count -ne 3 -or $networkParts[0] -cne [string]$State.Network.Id -or
        $networkParts[1] -cne [string]$State.Network.Name -or $networkParts[2] -cne $ownershipLabelValue) {
        throw "STATE_RESOURCE_MISMATCH: network changed before state commit."
    }
    $networkById = docker network inspect --format '{{.Id}}|{{.Name}}|{{index .Labels "exitpass.walkthrough"}}' $State.Network.Id 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]$networkById -cne [string]$networkByName) { throw "STATE_RESOURCE_MISMATCH: immutable network identity changed before state commit." }
    foreach ($record in @($State.Processes) + @($State.Launchers)) {
        $current = Get-ProcessRecord $record.Name ([int]$record.Id) @($record.CommandLineMarkers) ([int]$record.Port)
        if (-not (Test-CanonicalPathEqual $current.ExecutablePath $record.ExecutablePath) -or $current.StartTimeUtc -cne [string]$record.StartTimeUtc) {
            throw "STATE_RESOURCE_MISMATCH: process '$($record.Name)' changed before state commit."
        }
    }
}

function Test-PortOpen([int]$Port) {
    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Assert-PortAvailable([int]$Port, [string]$Purpose) {
    if (-not $AllowExistingPorts -and (Test-PortOpen $Port)) {
        throw "Port $Port is already listening for $Purpose. Stop the existing listener or select another port."
    }
}

function Invoke-PostgresSql([string]$Database, [string]$Sql) {
    docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed for database $Database." }
}

function Invoke-PostgresFile([string]$Database, [string]$Path) {
    Get-Content -LiteralPath $Path -Raw | docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL file failed for database $Database`: $Path" }
}

function Invoke-StatutorySeed(
    [guid]$ChallengeReference,
    [string]$ChallengeHash,
    [string]$PlaceholderVerifierHex,
    [string]$PlaceholderSaltHex
) {
    $preamble = @(
        "\set reviewer_challenge_reference '$($ChallengeReference.ToString('D'))'",
        "\set reviewer_challenge_hash '$ChallengeHash'",
        "\set placeholder_verifier_hex '$PlaceholderVerifierHex'",
        "\set placeholder_salt_hex '$PlaceholderSaltHex'"
    )
    @($preamble; Get-Content -LiteralPath $statutorySeed) |
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $DatabaseName
    if ($LASTEXITCODE -ne 0) { throw "Statutory walkthrough seed failed." }
}

function Invoke-PostgresQueryText([string]$Sql) {
    $value = docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -t -A -U $DatabaseUser -d $DatabaseName -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL query failed for database $DatabaseName." }
    return (($value | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
}

function Ensure-SharedContainerRunning([string]$Name) {
    $exists = docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $Name }
    if (-not $exists) { throw "Required shared local container '$Name' does not exist. Use the current ordinary WebPay local-integration prerequisites first." }
    $running = docker inspect --format '{{.State.Running}}' $Name
    if ($running -ne 'true') {
        docker start $Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not start required shared container '$Name'." }
    }
}

function Assert-WalkthroughContainerAbsent([string]$Name) {
    if (docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $Name }) {
        $label = docker inspect --format '{{index .Config.Labels "exitpass.walkthrough"}}' $Name
        if ($label -ne 'webpay-statutory-discount') {
            throw "Container '$Name' exists without the walkthrough ownership label. It will not be changed."
        }
        throw "Walkthrough container '$Name' already exists. Run the stop script with -StopWalkthroughContainers before a fresh start."
    }
}

function Wait-HttpReady([string]$Name, [string]$Url, [int]$TimeoutSeconds = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 5 -UseBasicParsing
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) { return }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch { $lastError = $_.Exception.Message }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "$Name was not ready at $Url. Last error: $lastError"
}

function Wait-TcpReady([string]$Name, [int]$Port, [int]$TimeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync('127.0.0.1', $Port)
            if ($task.Wait(1000) -and $client.Connected) { return }
        }
        catch { }
        finally { $client.Dispose() }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "$Name did not listen on 127.0.0.1:$Port within $TimeoutSeconds seconds."
}

function Start-WalkthroughProcess([string]$Name, [string]$WorkingDirectory, [string]$Command) {
    $stdout = Join-Path $logsRoot "$Name.stdout.log"
    $stderr = Join-Path $logsRoot "$Name.stderr.log"
    $windowStyle = if ($VisibleServiceWindows) { 'Normal' } else { 'Hidden' }
    return Start-Process powershell -ArgumentList @('-NoProfile', '-Command', $Command) `
        -WorkingDirectory $WorkingDirectory -WindowStyle $windowStyle -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
}

function Get-ProcessRecord([string]$Name, [int]$Id, [string[]]$Markers, [int]$Port = 0) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop
    foreach ($marker in $Markers) {
        if ($process.CommandLine -notlike "*$marker*") {
            throw "$Name PID $($process.ProcessId) does not contain expected command marker '$marker'."
        }
    }
    $runtime = Get-Process -Id $process.ProcessId -ErrorAction Stop
    return [pscustomobject]@{
        Name = $Name
        Id = [int]$process.ProcessId
        Port = $Port
        ExecutablePath = $process.ExecutablePath
        CommandLineMarkers = $Markers
        StartTimeUtc = $runtime.StartTime.ToUniversalTime().ToString('o')
    }
}

function Get-ListenerRecord([string]$Name, [int]$Port, [string[]]$Markers) {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop | Select-Object -First 1
    return Get-ProcessRecord $Name ([int]$connection.OwningProcess) $Markers $Port
}

function New-SyntheticEvidenceImage([string]$Path) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    & icacls.exe $directory /inheritance:r /grant:r "$env:USERNAME`:(OI)(CI)F" | Out-Null
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(96, 64)
    try {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                $color = if (([math]::Floor($x / 8) + [math]::Floor($y / 8)) % 2 -eq 0) {
                    [System.Drawing.Color]::FromArgb(36, 99, 132)
                } else {
                    [System.Drawing.Color]::FromArgb(238, 240, 232)
                }
                $bitmap.SetPixel($x, $y, $color)
            }
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

$operationLock = Enter-WalkthroughOperationLock $RepositoryRoot
try {
Assert-SafeDatabaseName $DatabaseName
$databaseHost = '127.0.0.1'
$previousState = $null
$previousStateIdentity = $null
$freshStateConflict = Test-Path -LiteralPath $statePath
if ($RestartServicesOnly) {
    $previousSnapshot = Read-ValidatedWalkthroughState $statePath $operationLock $RepositoryRoot $DatabaseName $databaseHost $PostgresPort $PostgresContainerName $logsRoot $evidenceRoot $syntheticEvidencePath $networkName $allowedWalkthroughContainerNames
    $previousState = $previousSnapshot.State
    $previousStateIdentity = $previousSnapshot.Identity
}
elseif ($freshStateConflict -and -not $DryRun) {
    Assert-FreshStateAbsent $statePath
}

Assert-Tool docker
Assert-Tool dotnet
Assert-Tool npm
foreach ($path in @($canonicalSql, $canonicalValidator, $paymentRoutingPatch, $payMongoRailPatch, $ordinarySeed, $pilotSeed, $rbacSource, $statutorySeed, $statutoryVerify, $centralPmsProject, $paymentOrchestratorProject, $webPayRoot, $operatorConsoleRoot)) {
    Assert-PathExists $path "Required current walkthrough dependency"
}

Assert-CryptographicRuntimeCompatibility

if ($RestartServicesOnly) {
    Assert-RestartResourceOwnership $previousState
}

if ($DryRun) {
    Write-Host "DRY RUN: cryptographic runtime compatibility validation passed."
    Write-Host "DRY RUN: current paths, database guard, tools, ports, configuration names, and composition were validated."
    if ($RestartServicesOnly) {
        Write-Host "DRY_RUN_STATE=VALID_RESTARTABLE_STATE"
        Write-Host "DRY RUN: restart state schema, identity, database, resources, and owned paths validated without restart mutation."
    }
    elseif ($freshStateConflict) {
        Write-Host "DRY_RUN_STATE=BLOCKED_EXISTING_STATE"
        Write-Host "DRY RUN: fresh startup is blocked because state exists at $statePath. Use validated restart or separately governed cleanup."
    }
    else {
        Write-Host "DRY_RUN_STATE=ABSENT_READY_FOR_FRESH_START"
        Write-Host "DRY RUN: the state path is absent and fresh startup may proceed to the mutation boundary after operator approval."
    }
    Write-Host "DRY RUN: no container, database, service, credential, evidence, or state mutation was performed."
    return
}

$dbPassword = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD'
$reviewerPassword = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD'
$minioAccessKey = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY'
$minioSecretKey = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY'
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET')

foreach ($port in @($CentralPmsPort, $PaymentOrchestratorPort, $WebPayPort, $OperatorConsolePort)) {
    Assert-PortAvailable $port "walkthrough service"
}

New-Item -ItemType Directory -Force -Path $stateRoot, $logsRoot | Out-Null

$fixtureContext = $null
$activationReference = $null
$activationSecret = $null

if ($RestartServicesOnly) {
    $fixtureContext = $previousState.Fixture
}
else {
    foreach ($port in @($MinioApiPort, $MinioConsolePort, $ClamAvPort)) { Assert-PortAvailable $port "walkthrough dependency" }
    Ensure-SharedContainerRunning $PostgresContainerName
    Ensure-SharedContainerRunning 'exitpass-rabbitmq'
    Ensure-SharedContainerRunning 'exitpass-mock-payment-provider'
    Assert-WalkthroughContainerAbsent $minioContainerName
    Assert-WalkthroughContainerAbsent $clamAvContainerName

    Write-Host "Rebuilding guarded disposable database $DatabaseName..." -ForegroundColor Yellow
    Invoke-PostgresSql postgres "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DatabaseName';"
    Invoke-PostgresSql postgres "DROP DATABASE IF EXISTS $DatabaseName;"
    Invoke-PostgresSql postgres "CREATE DATABASE $DatabaseName;"
    Invoke-PostgresFile $DatabaseName $canonicalSql
    Invoke-PostgresFile $DatabaseName $canonicalValidator
    Invoke-PostgresFile $DatabaseName $paymentRoutingPatch
    Invoke-PostgresFile $DatabaseName $payMongoRailPatch
    Invoke-PostgresFile $DatabaseName $ordinarySeed
    Invoke-PostgresFile $DatabaseName $pilotSeed
    # The tracked Management Platform RBAC file is inspected as the authority
    # source, but its own database-name guard intentionally excludes this DB.
    # The bounded statutory seed carries only its exact reviewer permissions.

    $activationReference = [guid]::NewGuid()
    $activationBytes = New-CryptographicRandomBytes 32
    try {
        $activationSecret = [Convert]::ToBase64String($activationBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally {
        [System.Array]::Clear($activationBytes, 0, $activationBytes.Length)
        $activationBytes = $null
    }
    $activationHashBytes = Get-Sha256HashBytes ([Text.Encoding]::UTF8.GetBytes($activationSecret))
    try {
        $activationHash = ConvertTo-LowercaseHex $activationHashBytes
    }
    finally {
        [System.Array]::Clear($activationHashBytes, 0, $activationHashBytes.Length)
        $activationHashBytes = $null
    }
    $placeholderVerifier = New-CryptographicRandomLowercaseHex 32
    $placeholderSalt = New-CryptographicRandomLowercaseHex 16
    Invoke-StatutorySeed $activationReference $activationHash $placeholderVerifier $placeholderSalt

    $contextSql = @"
SELECT json_build_object(
 'ticketReference', ps.ticket_number_masked,
 'parkingSessionId', ps.parking_session_id,
 'siteId', ps.site_id,
 'siteGroupId', ps.site_group_id,
 'vendorSystemId', ps.vendor_system_id,
 'webPayServiceIdentityId', '78000000-0000-4000-8000-000000000003'::uuid,
 'reviewerUsername', u.username,
 'reviewerUserId', u.user_id,
 'operatorDeviceBindingId', (SELECT operator_device_binding_id FROM operator_console.operator_device_bindings WHERE device_binding_code='SANDBOX-OC-SD-235A-DEVICE'),
 'operatorShiftId', (SELECT operator_shift_id FROM operator_console.operator_shifts WHERE external_shift_id_masked='SHIFT-SANDBOX-REVIEWER'),
 'ordinaryTicketReference', 'WEBPAY-LOCAL-ORDINARY-001',
 'missingJurisdictionTicket', 'WEBPAY-STAT-MISSING-JURISDICTION',
 'ambiguousJurisdictionTicket', 'WEBPAY-STAT-AMBIGUOUS-JURISDICTION',
 'noPolicyTicket', 'WEBPAY-STAT-NO-POLICY')
FROM core.parking_sessions ps
JOIN identity.users u ON u.username_normalized='sandbox-oc-sd-pilot-reviewer'
WHERE ps.ticket_number_masked='E2E-231-SESSION-001';
"@
    $fixtureContext = (Invoke-PostgresQueryText $contextSql) | ConvertFrom-Json
    $fixtureContext | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixtureContextPath -Encoding UTF8

    docker network create --label $ownershipLabel $networkName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create walkthrough Docker network." }
    $env:MINIO_ROOT_USER = $minioAccessKey
    $env:MINIO_ROOT_PASSWORD = $minioSecretKey
    try {
        docker run -d --name $minioContainerName --network $networkName --label $ownershipLabel `
            -p "127.0.0.1:$MinioApiPort`:9000" -p "127.0.0.1:$MinioConsolePort`:9001" `
            -e MINIO_ROOT_USER -e MINIO_ROOT_PASSWORD $MinioImage server /data --console-address ':9001' | Out-Null
    }
    finally {
        Remove-Item Env:\MINIO_ROOT_USER -ErrorAction SilentlyContinue
        Remove-Item Env:\MINIO_ROOT_PASSWORD -ErrorAction SilentlyContinue
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not start private MinIO container." }

    docker run -d --name $clamAvContainerName --network $networkName --label $ownershipLabel `
        -p "127.0.0.1:$ClamAvPort`:3310" $ClamAvImage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not start ClamAV container." }
    Wait-HttpReady 'MinIO' "http://127.0.0.1:$MinioApiPort/minio/health/ready"
    Wait-TcpReady 'ClamAV' $ClamAvPort 300

    $env:MC_HOST_walkthrough = "http://$minioAccessKey`:$minioSecretKey@$minioContainerName`:9000"
    try {
        docker run --rm --network $networkName -e MC_HOST_walkthrough $MinioClientImage mb --ignore-existing "walkthrough/$bucketName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create the private walkthrough bucket." }
        docker run --rm --network $networkName -e MC_HOST_walkthrough $MinioClientImage anonymous set none "walkthrough/$bucketName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not enforce private bucket policy." }
    }
    finally { Remove-Item Env:\MC_HOST_walkthrough -ErrorAction SilentlyContinue }

    New-SyntheticEvidenceImage $syntheticEvidencePath
}

Write-Host "Building current Central PMS and Payment Orchestrator..." -ForegroundColor Yellow
dotnet build $centralPmsProject
if ($LASTEXITCODE -ne 0) { throw "Central PMS build failed." }
dotnet build $paymentOrchestratorProject
if ($LASTEXITCODE -ne 0) { throw "Payment Orchestrator build failed." }

$connectionExpression = "'Host=127.0.0.1;Port=$PostgresPort;Database=$DatabaseName;Username=$DatabaseUser;Password=' + `$env:EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD + ';Include Error Detail=false'"
$centralPmsUrl = "http://127.0.0.1:$CentralPmsPort"
$paymentOrchestratorUrl = "http://127.0.0.1:$PaymentOrchestratorPort"
$webPayUrl = "http://127.0.0.1:$WebPayPort"
$operatorConsoleUrl = "http://127.0.0.1:$OperatorConsolePort"
$mockProviderUrl = "http://127.0.0.1:$MockPaymentProviderPort"

$centralCommand = @"
`$env:ASPNETCORE_ENVIRONMENT='Production'
`$env:ASPNETCORE_URLS='$centralPmsUrl'
`$env:ConnectionStrings__MainDatabase=$connectionExpression
`$env:HumanAuthentication__AllowedWebOrigins__0='$operatorConsoleUrl'
`$env:HumanAuthentication__TotpProtectionKeyBase64=`$env:EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64
`$env:HumanAuthentication__TotpProtectionKeyReference='webpay-statutory-local'
`$env:HumanAuthentication__TotpProtectionKeyVersion='1'
`$env:CentralPms__StatutoryEvidence__Upload__Endpoint='http://127.0.0.1:$MinioApiPort'
`$env:CentralPms__StatutoryEvidence__Upload__PublicUploadEndpoint='http://127.0.0.1:$MinioApiPort'
`$env:CentralPms__StatutoryEvidence__Upload__Region='us-east-1'
`$env:CentralPms__StatutoryEvidence__Upload__BucketName='$bucketName'
`$env:CentralPms__StatutoryEvidence__Upload__BucketReference='webpay-statutory-private'
`$env:CentralPms__StatutoryEvidence__Upload__AccessKeyId=`$env:EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY
`$env:CentralPms__StatutoryEvidence__Upload__SecretAccessKey=`$env:EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY
`$env:CentralPms__StatutoryEvidence__Upload__EnvironmentPartition='local'
`$env:CentralPms__StatutoryEvidence__Upload__MaxContentLengthBytes='5242880'
`$env:CentralPms__StatutoryEvidence__Upload__RequireServerSideEncryptionMetadata='false'
`$env:CentralPms__StatutoryEvidence__Channel__EnvironmentScope='LOCAL_TEST'
`$env:CentralPms__StatutoryEvidence__Channel__SeniorCitizenDocumentProfileCode='SENIOR_CITIZEN_ID_FRONT_BACK_V1'
`$env:CentralPms__StatutoryEvidence__Channel__PwdDocumentProfileCode='PWD_ID_FRONT_BACK_V1'
`$env:CentralPms__StatutoryEvidence__Channel__RequiredDocumentProfileVersion='1'
`$env:CentralPms__StatutoryEvidence__Channel__SingleDocumentItemRole='SINGLE_DOCUMENT'
`$env:CentralPms__StatutoryEvidence__ScanWorker__Enabled='true'
`$env:CentralPms__StatutoryEvidence__ScanWorker__PollIntervalSeconds='2'
`$env:CentralPms__StatutoryEvidence__ScanWorker__MaxContentLengthBytes='5242880'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerProvider='CLAMAV_COMPATIBLE'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerEndpoint='127.0.0.1'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerPort='$ClamAvPort'
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
dotnet run --project '$centralPmsProject' --no-launch-profile --no-build
"@

$orchestratorCommand = @"
`$env:ASPNETCORE_ENVIRONMENT='Production'
`$env:ASPNETCORE_URLS='$paymentOrchestratorUrl'
`$env:ConnectionStrings__MainDatabase=$connectionExpression
`$env:Integrations__CentralPms__BaseUrl='$centralPmsUrl'
`$env:Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId='$($fixtureContext.webPayServiceIdentityId)'
`$env:WEBPAY_PUBLIC_BASE_URL='$webPayUrl'
`$env:WebPay__PublicBaseUrl='$webPayUrl'
`$env:Payments__Providers__PayMongo__BaseUrl='$mockProviderUrl'
`$env:Payments__Providers__PayMongo__SecretKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY
`$env:Payments__Providers__PayMongo__PublicKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY
`$env:Payments__Providers__PayMongo__WebhookSecretKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
dotnet run --project '$paymentOrchestratorProject' --no-launch-profile --no-build
"@

$webPayCommand = @"
`$env:VITE_WEBPAY_API_PROXY_TARGET='$paymentOrchestratorUrl'
`$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID='$($fixtureContext.siteGroupId)'
`$env:VITE_WEBPAY_DEFAULT_SITE_ID='$($fixtureContext.siteId)'
`$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID='$($fixtureContext.vendorSystemId)'
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
npm.cmd run dev -- --host 127.0.0.1 --port $WebPayPort
"@

$operatorCommand = @"
`$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET='$centralPmsUrl'
`$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID='$($fixtureContext.operatorDeviceBindingId)'
`$env:VITE_OPERATOR_CONSOLE_SHIFT_ID='$($fixtureContext.operatorShiftId)'
`$env:VITE_OPERATOR_CONSOLE_SITE_ID='$($fixtureContext.siteId)'
`$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID='$($fixtureContext.siteGroupId)'
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
npm.cmd run dev -- --host 127.0.0.1 --port $OperatorConsolePort
"@

$launcherProcesses = @(
    [pscustomobject]@{ Name = 'central-pms-launcher'; Marker = 'ExitPass.CentralPms.Api'; Process = (Start-WalkthroughProcess 'central-pms' $RepositoryRoot $centralCommand) },
    [pscustomobject]@{ Name = 'payment-orchestrator-launcher'; Marker = 'ExitPass.PaymentOrchestrator.Api'; Process = (Start-WalkthroughProcess 'payment-orchestrator' $RepositoryRoot $orchestratorCommand) },
    [pscustomobject]@{ Name = 'webpay-ui-launcher'; Marker = "--port $WebPayPort"; Process = (Start-WalkthroughProcess 'webpay-ui' $webPayRoot $webPayCommand) },
    [pscustomobject]@{ Name = 'operator-console-ui-launcher'; Marker = "--port $OperatorConsolePort"; Process = (Start-WalkthroughProcess 'operator-console-ui' $operatorConsoleRoot $operatorCommand) }
)

Wait-HttpReady 'Central PMS readiness' "$centralPmsUrl/health/ready" 180
Wait-HttpReady 'Payment Orchestrator readiness' "$paymentOrchestratorUrl/health/ready" 180
Wait-HttpReady 'WebPay UI' $webPayUrl 120
Wait-HttpReady 'Operator Console UI' $operatorConsoleUrl 120
Wait-HttpReady 'Mock payment provider' "$mockProviderUrl/__admin/mappings" 60

if (-not $RestartServicesOnly) {
    $activationBody = @{
        challengeReference = $activationReference
        challengeSecret = $activationSecret
        newPassword = $reviewerPassword
    } | ConvertTo-Json
    $activationResponse = Invoke-WebRequest -Uri "$centralPmsUrl/v1/human-authentication/activations" `
        -Method Post -ContentType 'application/json' -Headers @{ Origin = $operatorConsoleUrl } `
        -Body $activationBody -UseBasicParsing
    if ($activationResponse.StatusCode -ne 200) { throw "Synthetic reviewer activation failed safely with HTTP $($activationResponse.StatusCode)." }
    $activationSecret = $null
    $activationBody = $null
}

$fixtureHeaderStatus = $null
try {
    Invoke-WebRequest -Uri "$centralPmsUrl/v1/ops/operator-console/statutory-discounts/reviews/pending" `
        -Headers @{ 'X-ExitPass-User-Id' = $fixtureContext.reviewerUserId } -UseBasicParsing | Out-Null
    $fixtureHeaderStatus = 200
}
catch {
    $fixtureHeaderStatus = [int]$_.Exception.Response.StatusCode
}
if ($fixtureHeaderStatus -lt 400) { throw "Production fixture identity header was unexpectedly accepted." }

$listenerRecords = @(
    (Get-ListenerRecord 'central-pms' $CentralPmsPort @('ExitPass.CentralPms.Api')),
    (Get-ListenerRecord 'payment-orchestrator' $PaymentOrchestratorPort @('ExitPass.PaymentOrchestrator.Api')),
    (Get-ListenerRecord 'webpay-ui' $WebPayPort @('vite', "$WebPayPort")),
    (Get-ListenerRecord 'operator-console-ui' $OperatorConsolePort @('vite', "$OperatorConsolePort"))
)
$launcherRecords = @($launcherProcesses | ForEach-Object {
    Get-ProcessRecord $_.Name ([int]$_.Process.Id) @($_.Marker)
})

$containerRecords = foreach ($name in @($minioContainerName, $clamAvContainerName)) {
    [pscustomobject]@{ Name = $name; Id = (docker inspect --format '{{.Id}}' $name); OwnershipLabel = $ownershipLabelValue }
}

$now = (Get-Date).ToUniversalTime().ToString('o')
$runId = if ($RestartServicesOnly) { [string]$previousState.RunId } else { [guid]::NewGuid().ToString('D') }
$createdAt = if ($RestartServicesOnly) { [string]$previousState.CreatedAtUtc } else { $now }
$restartCount = if ($RestartServicesOnly) { [int]$previousState.RestartCount + 1 } else { 0 }
$state = [pscustomobject]@{
    StateSchemaVersion = $stateSchemaVersion
    WalkthroughIdentity = $walkthroughIdentity
    RepositoryRoot = $RepositoryRoot
    StatePath = $statePath
    RunId = $runId
    StartupMode = 'FRESH'
    LifecycleStatus = 'READY'
    CreatedAtUtc = $createdAt
    LastValidUpdateAtUtc = $now
    RestartCount = $restartCount
    DatabaseName = $DatabaseName
    DatabaseHost = $databaseHost
    DatabasePort = $PostgresPort
    PostgresContainerName = $PostgresContainerName
    PostgresContainerId = (docker inspect --format '{{.Id}}' $PostgresContainerName)
    ProductionHosted = $true
    FixtureHeaderProbeStatus = $fixtureHeaderStatus
    Processes = $listenerRecords
    Launchers = $launcherRecords
    Containers = $containerRecords
    Network = [pscustomobject]@{
        Name = $networkName
        Id = (docker network inspect --format '{{.Id}}' $networkName)
        OwnershipLabel = $ownershipLabelValue
    }
    LogsRoot = $logsRoot
    EvidenceRoot = $evidenceRoot
    SyntheticEvidencePath = $syntheticEvidencePath
    Fixture = $fixtureContext
    Urls = [pscustomobject]@{ CentralPms = $centralPmsUrl; PaymentOrchestrator = $paymentOrchestratorUrl; WebPay = $webPayUrl; OperatorConsole = $operatorConsoleUrl }
}
Assert-WalkthroughStateContract $state $RepositoryRoot $statePath $DatabaseName $databaseHost $PostgresPort $PostgresContainerName $logsRoot $evidenceRoot $syntheticEvidencePath $networkName $allowedWalkthroughContainerNames | Out-Null
Assert-StateRuntimeOwnershipCurrent $state
if ($RestartServicesOnly) {
    [void](Update-WalkthroughStateAtomically $state $statePath $previousStateIdentity $operationLock)
}
else {
    try {
        Write-WalkthroughStateAtomically $state $statePath $operationLock
    }
    catch {
        $ownedProcesses = @($listenerRecords + $launcherRecords | ForEach-Object { "$($_.Name):PID=$($_.Id)" }) -join ', '
        $ownedContainers = @($containerRecords | ForEach-Object { "$($_.Name):$($_.Id)" }) -join ', '
        throw "$($_.Exception.Message) RunId=$runId. Governed shutdown is required for startup-owned processes [$ownedProcesses], containers [$ownedContainers], and network '$networkName'; no name-only cleanup is authorized."
    }
}

Write-Host "WebPay statutory-discount walkthrough is ready for manual local execution." -ForegroundColor Green
Write-Host "WebPay:          $webPayUrl/?ticketReference=$($fixtureContext.ticketReference)"
Write-Host "Operator Console:$operatorConsoleUrl"
Write-Host "Reviewer username: $($fixtureContext.reviewerUsername)"
Write-Host "Synthetic evidence: $syntheticEvidencePath"
Write-Host "The reviewer password and all infrastructure secrets remain in the caller-owned environment and were not printed or stored."
Write-Host "This startup is local-development preparation only. It is not walkthrough execution, Controlled UAT, compliance evidence, or production authorization."
}
finally {
    Exit-WalkthroughOperationLock $operationLock
}
