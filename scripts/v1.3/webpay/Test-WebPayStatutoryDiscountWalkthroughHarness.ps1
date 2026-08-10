[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")).Path
}

$authorizedRelativePaths = @(
    "docs\v1.3\webpay\runbooks\ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md",
    "scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql",
    "scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1",
    "scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1",
    "scripts\v1.3\webpay\Test-WebPayStatutoryDiscountWalkthroughHarness.ps1",
    "scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"
)

$assets = @{}
foreach ($relativePath in $authorizedRelativePaths) {
    $path = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required walkthrough asset is missing: $relativePath"
    }

    $assets[$relativePath] = Get-Content -LiteralPath $path -Raw
}

function Assert-Contains {
    param([string]$Text, [string]$Literal, [string]$Context)
    if ($Text.IndexOf($Literal, [StringComparison]::Ordinal) -lt 0) {
        throw "$Context is missing required literal: $Literal"
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Literal, [string]$Context)
    if ($Text.IndexOf($Literal, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "$Context contains forbidden literal: $Literal"
    }
}

function Assert-NoPowerShell51IncompatibleCryptographicApi {
    param([string]$Text)
    foreach ($unsupportedPattern in @(
        '(?i)\[System\.Security\.Cryptography\.RandomNumberGenerator\]\s*::\s*GetBytes\s*\(',
        '(?i)\[System\.Convert\]\s*::\s*ToHexString\s*\(',
        '(?i)\[Convert\]\s*::\s*ToHexString\s*\(',
        '(?i)\[System\.Security\.Cryptography\.SHA256\]\s*::\s*HashData\s*\('
    )) {
        if ($Text -match $unsupportedPattern) {
            throw "PowerShell 5.1-incompatible static cryptographic API detected."
        }
    }
}

function Assert-PowerShellParses {
    param([string]$Path)
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        $detail = ($errors | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Message)" }) -join "; "
        throw "PowerShell syntax failed for ${Path}: $detail"
    }
}

function Assert-ThrowsClassification {
    param([scriptblock]$Action, [string]$Classification, [string]$Context)
    $caught = $null
    try { & $Action }
    catch { $caught = $_ }
    if ($null -eq $caught -or $caught.Exception.Message -notlike "*$Classification*") {
        $actual = if ($null -eq $caught) { 'no exception' } else { $caught.Exception.Message }
        throw "$Context did not fail with $Classification. Actual: $actual"
    }
}

$startPath = Join-Path $PSScriptRoot "Start-WebPayStatutoryDiscountWalkthrough.ps1"
$powerShellPaths = @(
    $startPath,
    (Join-Path $PSScriptRoot "Stop-WebPayStatutoryDiscountWalkthrough.ps1"),
    (Join-Path $PSScriptRoot "Test-WebPayStatutoryDiscountWalkthroughHarness.ps1")
)
foreach ($path in $powerShellPaths) {
    Assert-PowerShellParses -Path $path
}

$startTokens = $null
$startErrors = $null
$startAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $startPath,
    [ref]$startTokens,
    [ref]$startErrors)
if ($startErrors.Count -gt 0) {
    throw "Startup script cannot be inspected for runtime compatibility because it does not parse."
}

$requiredCompatibilityHelpers = @(
    "New-CryptographicRandomBytes",
    "Get-Sha256HashBytes",
    "ConvertTo-LowercaseHex",
    "New-CryptographicRandomLowercaseHex",
    "Assert-CryptographicRuntimeCompatibility"
)
$functionDefinitions = @($startAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true))
foreach ($helperName in $requiredCompatibilityHelpers) {
    $definition = @($functionDefinitions | Where-Object { $_.Name -ceq $helperName })
    if ($definition.Count -ne 1) {
        throw "Startup script must define exactly one $helperName compatibility helper."
    }
    Invoke-Expression $definition[0].Extent.Text
}

$randomProbe = $null
$hashProbe = $null
try {
    $randomProbe = New-CryptographicRandomBytes 32
    if ($randomProbe.Length -ne 32) {
        throw "Compatibility helper returned an unexpected random-byte length."
    }

    $lowercaseHexProbe = ConvertTo-LowercaseHex ([byte[]](0, 15, 16, 171, 255))
    if ($lowercaseHexProbe -cne "000f10abff") {
        throw "Compatibility helper did not produce deterministic lowercase hexadecimal output."
    }

    $hashProbe = Get-Sha256HashBytes ([System.Text.Encoding]::ASCII.GetBytes("abc"))
    $hashHexProbe = ConvertTo-LowercaseHex $hashProbe
    if ($hashHexProbe -cne "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") {
        throw "Compatibility helper failed the SHA-256 abc test vector."
    }

    $randomHexProbe = New-CryptographicRandomLowercaseHex 16
    if ($randomHexProbe -cnotmatch '^[0-9a-f]{32}$') {
        throw "Random hexadecimal helper did not preserve the required lowercase format and length."
    }

    Assert-CryptographicRuntimeCompatibility
}
finally {
    if ($null -ne $randomProbe) {
        [System.Array]::Clear($randomProbe, 0, $randomProbe.Length)
    }
    if ($null -ne $hashProbe) {
        [System.Array]::Clear($hashProbe, 0, $hashProbe.Length)
    }
}

$stateSchemaVersion = 1
$walkthroughIdentity = "ExitPass.WebPay.StatutoryDiscount.LocalWalkthrough"
$ownershipLabelValue = "webpay-statutory-discount"
$requiredStateHelpers = @(
    "Assert-SafeDatabaseName",
    "Get-CanonicalPath",
    "Test-CanonicalPathEqual",
    "Assert-OwnedPath",
    "Get-RequiredStateProperty",
    "Assert-StateTimestamp",
    "Assert-ProcessRecordStructure",
    "Assert-WalkthroughStateContract",
    "Read-ValidatedWalkthroughState",
    "Assert-FreshStateAbsent",
    "Write-WalkthroughStateAtomically",
    "Get-StateFileHash",
    "Update-WalkthroughStateAtomically"
)
foreach ($helperName in $requiredStateHelpers) {
    $definition = @($functionDefinitions | Where-Object { $_.Name -ceq $helperName })
    if ($definition.Count -ne 1) { throw "Startup script must define exactly one $helperName state-ownership helper." }
    Invoke-Expression $definition[0].Extent.Text
}

function New-SyntheticStateFixture {
    param(
        [string]$SyntheticRepositoryRoot,
        [string]$SyntheticStatePath,
        [string]$SyntheticEvidenceRoot,
        [string]$SyntheticEvidencePath
    )
    $executablePath = (Get-Command powershell.exe -ErrorAction Stop).Source
    $processNames = @('central-pms', 'payment-orchestrator', 'webpay-ui', 'operator-console-ui')
    $launcherNames = @('central-pms-launcher', 'payment-orchestrator-launcher', 'webpay-ui-launcher', 'operator-console-ui-launcher')
    $nextId = 41000
    $processes = @($processNames | ForEach-Object {
        $nextId++
        [pscustomobject]@{ Name = $_; Id = $nextId; Port = 0; ExecutablePath = $executablePath; CommandLineMarkers = @("synthetic-$_"); StartTimeUtc = '2026-08-11T00:00:00.0000000Z' }
    })
    $launchers = @($launcherNames | ForEach-Object {
        $nextId++
        [pscustomobject]@{ Name = $_; Id = $nextId; Port = 0; ExecutablePath = $executablePath; CommandLineMarkers = @("synthetic-$_"); StartTimeUtc = '2026-08-11T00:00:00.0000000Z' }
    })
    return [pscustomobject]@{
        StateSchemaVersion = 1
        WalkthroughIdentity = $walkthroughIdentity
        RepositoryRoot = $SyntheticRepositoryRoot
        StatePath = $SyntheticStatePath
        RunId = '11111111-2222-4333-8444-555555555555'
        StartupMode = 'FRESH'
        LifecycleStatus = 'READY'
        CreatedAtUtc = '2026-08-11T00:00:00.0000000Z'
        LastValidUpdateAtUtc = '2026-08-11T00:01:00.0000000Z'
        RestartCount = 0
        DatabaseName = 'exitpass_webpay_local_walkthrough_statutory_harness'
        DatabaseHost = '127.0.0.1'
        DatabasePort = 5433
        PostgresContainerName = 'exitpass-postgres'
        PostgresContainerId = 'synthetic-postgres-container-id'
        ProductionHosted = $true
        FixtureHeaderProbeStatus = 403
        Processes = $processes
        Launchers = $launchers
        Containers = @(
            [pscustomobject]@{ Name = 'exitpass-webpay-statutory-minio'; Id = 'synthetic-minio-id'; OwnershipLabel = $ownershipLabelValue },
            [pscustomobject]@{ Name = 'exitpass-webpay-statutory-clamav'; Id = 'synthetic-clamav-id'; OwnershipLabel = $ownershipLabelValue }
        )
        Network = [pscustomobject]@{ Name = 'exitpass-webpay-statutory-walkthrough'; Id = 'synthetic-network-id'; OwnershipLabel = $ownershipLabelValue }
        EvidenceRoot = $SyntheticEvidenceRoot
        SyntheticEvidencePath = $SyntheticEvidencePath
        Fixture = [pscustomobject]@{ ticketReference = 'SYNTHETIC-HARNESS' }
        Urls = [pscustomobject]@{ CentralPms = 'http://127.0.0.1:8080' }
    }
}

function Copy-SyntheticState($State) {
    return ($State | ConvertTo-Json -Depth 10 | ConvertFrom-Json)
}

function Assert-StateFileRejectedUnchanged {
    param(
        $BaseState,
        [string]$CaseName,
        [scriptblock]$Mutator,
        [string]$Classification,
        [string]$SyntheticStateRoot,
        [string]$SyntheticRepositoryRoot,
        [string]$ExpectedDatabaseName,
        [string]$SyntheticEvidenceRoot,
        [string]$SyntheticEvidencePath,
        [string[]]$ExpectedContainers
    )
    $casePath = Join-Path $SyntheticStateRoot "$CaseName.json"
    $caseState = Copy-SyntheticState $BaseState
    $caseState.StatePath = $casePath
    & $Mutator $caseState
    [System.IO.File]::WriteAllText($casePath, ($caseState | ConvertTo-Json -Depth 10), (New-Object System.Text.UTF8Encoding($false)))
    $fixedTimestamp = [DateTime]::SpecifyKind([DateTime]'2026-08-11T02:03:04', [DateTimeKind]::Utc)
    [System.IO.File]::SetLastWriteTimeUtc($casePath, $fixedTimestamp)
    $before = Get-Item -LiteralPath $casePath
    $beforeHash = (Get-FileHash -LiteralPath $casePath -Algorithm SHA256).Hash
    Assert-ThrowsClassification {
        Read-ValidatedWalkthroughState $casePath $SyntheticRepositoryRoot $ExpectedDatabaseName '127.0.0.1' 5433 'exitpass-postgres' $SyntheticEvidenceRoot $SyntheticEvidencePath 'exitpass-webpay-statutory-walkthrough' $ExpectedContainers
    } $Classification "$CaseName state guard"
    $after = Get-Item -LiteralPath $casePath
    if ((Get-FileHash -LiteralPath $casePath -Algorithm SHA256).Hash -cne $beforeHash -or
        $after.Length -ne $before.Length -or $after.LastWriteTimeUtc -ne $before.LastWriteTimeUtc) {
        throw "$CaseName state guard changed state bytes, length, or timestamp."
    }
}

$stateHarnessRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ExitPass\webpay-statutory-state-harness-{0}" -f [guid]::NewGuid().ToString('N'))
$syntheticRepositoryRoot = Join-Path $stateHarnessRoot 'repository'
$syntheticStateRoot = Join-Path $syntheticRepositoryRoot '.local\webpay-statutory-discount-walkthrough'
$syntheticStatePath = Join-Path $syntheticStateRoot 'state.json'
$syntheticEvidenceRoot = Join-Path $stateHarnessRoot 'evidence'
$syntheticEvidencePath = Join-Path $syntheticEvidenceRoot 'synthetic-senior-citizen-id.png'
$expectedContainers = @('exitpass-webpay-statutory-minio', 'exitpass-webpay-statutory-clamav')
[void][System.IO.Directory]::CreateDirectory($syntheticStateRoot)
try {
    $validState = New-SyntheticStateFixture $syntheticRepositoryRoot $syntheticStatePath $syntheticEvidenceRoot $syntheticEvidencePath
    Assert-FreshStateAbsent $syntheticStatePath
    Assert-WalkthroughStateContract $validState $syntheticRepositoryRoot $syntheticStatePath $validState.DatabaseName '127.0.0.1' 5433 'exitpass-postgres' $syntheticEvidenceRoot $syntheticEvidencePath 'exitpass-webpay-statutory-walkthrough' $expectedContainers | Out-Null

    Write-WalkthroughStateAtomically $validState $syntheticStatePath
    $firstBytes = [System.IO.File]::ReadAllBytes($syntheticStatePath)
    if ($firstBytes.Length -ge 3 -and $firstBytes[0] -eq 0xEF -and $firstBytes[1] -eq 0xBB -and $firstBytes[2] -eq 0xBF) {
        throw 'Atomic state creation emitted an unexpected UTF-8 BOM.'
    }
    $restartState = Copy-SyntheticState $validState
    $restartState.RestartCount = 1
    $restartState.LastValidUpdateAtUtc = '2026-08-11T00:02:00.0000000Z'
    Update-WalkthroughStateAtomically $restartState $syntheticStatePath (Get-StateFileHash $syntheticStatePath)
    $restartReadback = Read-ValidatedWalkthroughState $syntheticStatePath $syntheticRepositoryRoot $validState.DatabaseName '127.0.0.1' 5433 'exitpass-postgres' $syntheticEvidenceRoot $syntheticEvidencePath 'exitpass-webpay-statutory-walkthrough' $expectedContainers
    if ([int]$restartReadback.RestartCount -ne 1) { throw 'Atomic restart-state update did not persist validated restart metadata.' }
    $fixedTimestamp = [DateTime]::SpecifyKind([DateTime]'2026-08-11T01:02:03', [DateTimeKind]::Utc)
    [System.IO.File]::SetLastWriteTimeUtc($syntheticStatePath, $fixedTimestamp)
    $existingHash = (Get-FileHash -LiteralPath $syntheticStatePath -Algorithm SHA256).Hash
    $existingTimestamp = (Get-Item -LiteralPath $syntheticStatePath).LastWriteTimeUtc
    Assert-ThrowsClassification { Assert-FreshStateAbsent $syntheticStatePath } 'EXISTING_STATE_CONFLICT' 'existing valid state fresh-start guard'
    if ((Get-FileHash -LiteralPath $syntheticStatePath -Algorithm SHA256).Hash -cne $existingHash -or
        (Get-Item -LiteralPath $syntheticStatePath).LastWriteTimeUtc -ne $existingTimestamp) {
        throw 'Existing-state guard changed state bytes or timestamp.'
    }
    Read-ValidatedWalkthroughState $syntheticStatePath $syntheticRepositoryRoot $validState.DatabaseName '127.0.0.1' 5433 'exitpass-postgres' $syntheticEvidenceRoot $syntheticEvidencePath 'exitpass-webpay-statutory-walkthrough' $expectedContainers | Out-Null

    $malformedPath = Join-Path $syntheticStateRoot 'malformed.json'
    [System.IO.File]::WriteAllText($malformedPath, '{not-json', (New-Object System.Text.UTF8Encoding($false)))
    $malformedHash = (Get-FileHash -LiteralPath $malformedPath -Algorithm SHA256).Hash
    Assert-ThrowsClassification { Read-ValidatedWalkthroughState $malformedPath $syntheticRepositoryRoot $validState.DatabaseName '127.0.0.1' 5433 'exitpass-postgres' $syntheticEvidenceRoot $syntheticEvidencePath 'exitpass-webpay-statutory-walkthrough' $expectedContainers } 'STATE_MALFORMED' 'malformed state restart guard'
    if ((Get-FileHash -LiteralPath $malformedPath -Algorithm SHA256).Hash -cne $malformedHash) { throw 'Malformed-state guard changed the file.' }

    $fileCaseArguments = @($syntheticStateRoot, $syntheticRepositoryRoot, $validState.DatabaseName, $syntheticEvidenceRoot, $syntheticEvidencePath, $expectedContainers)
    Assert-StateFileRejectedUnchanged $validState 'unsupported' { param($s) $s.StateSchemaVersion = 99 } 'STATE_UNSUPPORTED_SCHEMA' @fileCaseArguments
    Assert-StateFileRejectedUnchanged $validState 'identity-mismatch' { param($s) $s.WalkthroughIdentity = 'another-walkthrough' } 'STATE_OWNERSHIP_MISMATCH' @fileCaseArguments
    Assert-StateFileRejectedUnchanged $validState 'repository-mismatch' { param($s) $s.RepositoryRoot = (Join-Path $stateHarnessRoot 'other-repository') } 'STATE_OWNERSHIP_MISMATCH' @fileCaseArguments
    Assert-StateFileRejectedUnchanged $validState 'database-mismatch' { param($s) $s.DatabaseName = 'exitpass_webpay_local_walkthrough_statutory_other' } 'STATE_DATABASE_MISMATCH' @fileCaseArguments
    Assert-StateFileRejectedUnchanged $validState 'unsafe-path' { param($s) $s.SyntheticEvidencePath = (Join-Path $stateHarnessRoot 'escaped\evidence.png') } 'STATE_PATH_ESCAPE' @fileCaseArguments
    Assert-StateFileRejectedUnchanged $validState 'stale' { param($s) $s.LifecycleStatus = 'UNKNOWN' } 'STATE_NOT_RESTARTABLE' @fileCaseArguments

    $preexistingTemporaryPath = Join-Path $syntheticStateRoot ('.state.{0}.tmp' -f $validState.RunId)
    [System.IO.File]::WriteAllText($preexistingTemporaryPath, 'preexisting-temporary-file', (New-Object System.Text.UTF8Encoding($false)))
    $preexistingTargetPath = Join-Path $syntheticStateRoot 'preexisting-temp-target.json'
    $preexistingTargetState = Copy-SyntheticState $validState
    $preexistingTargetState.StatePath = $preexistingTargetPath
    Assert-ThrowsClassification { Write-WalkthroughStateAtomically $preexistingTargetState $preexistingTargetPath } 'ATOMIC_STATE_CREATE_FAILED' 'pre-existing atomic temporary path'
    if ([System.IO.File]::ReadAllText($preexistingTemporaryPath) -cne 'preexisting-temporary-file') {
        throw 'Atomic state failure removed or changed a temporary file it did not create.'
    }
    Remove-Item -LiteralPath $preexistingTemporaryPath -Force

    $racePath = Join-Path $syntheticStateRoot 'race-state.json'
    $raceState = Copy-SyntheticState $validState
    $raceState.StatePath = $racePath
    $competingBytes = [System.Text.Encoding]::UTF8.GetBytes('competing-state-must-survive')
    Assert-ThrowsClassification {
        Write-WalkthroughStateAtomically $raceState $racePath { param($target) [System.IO.File]::WriteAllBytes($target, $competingBytes) }
    } 'ATOMIC_STATE_CREATE_FAILED' 'atomic state create race'
    if ([System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($racePath)) -cne 'competing-state-must-survive') {
        throw 'Atomic state creation overwrote the competing state file.'
    }
}
finally {
    if (Test-Path -LiteralPath $stateHarnessRoot) { Remove-Item -LiteralPath $stateHarnessRoot -Recurse -Force }
}

$start = $assets["scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1"]
$stop = $assets["scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1"]
$seed = $assets["scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql"]
$verify = $assets["scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"]
$runbook = $assets["docs\v1.3\webpay\runbooks\ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md"]
$operationalText = @($start, $stop, $seed, $verify, $runbook) -join "`n"
$executableText = @($start, $stop, $seed, $verify) -join "`n"

Assert-NoPowerShell51IncompatibleCryptographicApi -Text $start
foreach ($unsupportedExample in @(
    '[System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)',
    '[System.Convert]::ToHexString($bytes)',
    '[Convert]::ToHexString($bytes)',
    '[System.Security.Cryptography.SHA256]::HashData($bytes)'
)) {
    $rejected = $false
    try {
        Assert-NoPowerShell51IncompatibleCryptographicApi -Text $unsupportedExample
    }
    catch {
        $rejected = $true
    }
    if (-not $rejected) {
        throw "Harness failed to reject a synthetic PowerShell 5.1-incompatible cryptographic API."
    }
}

$compatibilityInvocation = [regex]::Match(
    $start,
    '(?m)^\s*Assert-CryptographicRuntimeCompatibility\s*$')
$dryRunBranch = [regex]::Match($start, '(?m)^\s*if\s*\(\$DryRun\)\s*\{')
if (-not $compatibilityInvocation.Success -or -not $dryRunBranch.Success -or
    $compatibilityInvocation.Index -ge $dryRunBranch.Index) {
    throw "Cryptographic runtime compatibility must be validated before the DryRun return path."
}

$actualStatePath = Join-Path $RepositoryRoot '.local\webpay-statutory-discount-walkthrough\state.json'
$actualStateBefore = if (Test-Path -LiteralPath $actualStatePath -PathType Leaf) {
    $item = Get-Item -LiteralPath $actualStatePath
    [pscustomobject]@{ Hash = (Get-FileHash -LiteralPath $actualStatePath -Algorithm SHA256).Hash; Length = $item.Length; Timestamp = $item.LastWriteTimeUtc }
} else { $null }
$dryRunOutput = @(& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $startPath `
    -RepositoryRoot $RepositoryRoot `
    -CanonicalDatabaseRepository $CanonicalDatabaseRepository `
    -DryRun 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "Startup DryRun failed during executable compatibility validation: $($dryRunOutput -join '; ')"
}
$dryRunText = $dryRunOutput -join "`n"
Assert-Contains -Text $dryRunText `
    -Literal "DRY RUN: cryptographic runtime compatibility validation passed." `
    -Context "startup DryRun runtime validation"
if ($null -eq $actualStateBefore) {
    Assert-Contains -Text $dryRunText -Literal 'DRY_RUN_STATE=ABSENT_READY_FOR_FRESH_START' -Context 'startup DryRun absent-state classification'
}
else {
    Assert-Contains -Text $dryRunText -Literal 'DRY_RUN_STATE=BLOCKED_EXISTING_STATE' -Context 'startup DryRun existing-state classification'
    $actualStateAfter = Get-Item -LiteralPath $actualStatePath
    if ((Get-FileHash -LiteralPath $actualStatePath -Algorithm SHA256).Hash -cne $actualStateBefore.Hash -or
        $actualStateAfter.Length -ne $actualStateBefore.Length -or $actualStateAfter.LastWriteTimeUtc -ne $actualStateBefore.Timestamp) {
        throw 'Startup DryRun changed the pre-existing walkthrough state file.'
    }
}
Assert-Contains -Text $dryRunText -Literal 'no container, database, service, credential, evidence, or state mutation was performed' -Context 'startup DryRun non-mutation result'

# Current tracked composition and source references.
$trackedReferences = @(
    "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql",
    "scripts\operator-console\Seed-StatutoryDiscountPilotFixture.sql",
    "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql",
    "infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql",
    "infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql",
    "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj",
    "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj",
    "src\Services\WebPayUi\package.json",
    "src\Services\OperatorConsoleUi\package.json"
)
foreach ($relativePath in $trackedReferences) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relativePath) -PathType Leaf)) {
        throw "Current tracked dependency reference is missing: $relativePath"
    }
}

$paymentEndpointSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Endpoints\WebPayPaymentIntentEndpoints.cs") -Raw
$evidenceEndpointSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Endpoints\WebPayStatutoryEvidenceEndpoints.cs") -Raw
$humanAuthenticationSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\HumanAuthenticationEndpoints.cs") -Raw
$operatorReviewSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\OperatorConsoleStatutoryDiscountDraftEndpoints.cs") -Raw
$operatorEvidenceSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\OperatorConsoleStatutoryEvidenceReviewEndpoints.cs") -Raw
$fixtureGuardSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Security\ProductionFixtureIdentityHeaderGuardMiddleware.cs") -Raw
$rbacCatalogSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Application\Security\CentralPmsRbacPolicyCatalog.cs") -Raw
$rbacSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql") -Raw

$canonicalDdl = Join-Path $CanonicalDatabaseRepository "build\generated\exitpass-full-object.generated.sql"
$canonicalValidator = Join-Path $CanonicalDatabaseRepository "scripts\validation\Validate-V13CentralPmsAlignment.sql"
foreach ($path in @($canonicalDdl, $canonicalValidator)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Canonical database dependency is missing: $path"
    }
}

# Current configuration and authentication boundaries.
foreach ($literal in @(
    "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID",
    "VITE_WEBPAY_DEFAULT_SITE_ID",
    "VITE_WEBPAY_DEFAULT_SITE_GROUP_ID",
    "Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId",
    "CentralPms__StatutoryEvidence__Upload",
    "CentralPms__StatutoryEvidence__Channel",
    "CentralPms__StatutoryEvidence__ScanWorker",
    "ASPNETCORE_ENVIRONMENT",
    "Production",
    "EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD",
    "EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY"
)) {
    Assert-Contains -Text $start -Literal $literal -Context "startup script"
}

foreach ($literal in @(
    "/v1/human-authentication/login",
    "/v1/human-authentication/session",
    "/v1/human-authentication/logout",
    "X-ExitPass-User-Id",
    "X-Operator-User-Id",
    "fixture identity",
    "Site Group",
    "GLOBAL"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook authentication boundary"
}

foreach ($permission in @(
    "statutory-discounts.review.queue.read",
    "statutory-discounts.review.detail.read",
    "statutory-discounts.decision.review",
    "statutory-discounts.decision.approve",
    "statutory-discounts.decision.reject",
    "statutory-discounts.evidence.review.view"
)) {
    Assert-Contains -Text $seed -Literal $permission -Context "seed reviewer authority"
    Assert-Contains -Text $runbook -Literal $permission -Context "runbook reviewer authority"
    Assert-Contains -Text $rbacSource -Literal $permission -Context "tracked RBAC authority source"
}

# Current endpoint paths.
foreach ($path in @(
    "/v1/webpay/statutory-discounts/availability",
    "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover",
    "/v1/webpay/statutory-discounts/decisions",
    "/apply-payable-basis",
    "/v1/webpay/statutory-discounts/evidence/bootstrap",
    "/v1/webpay/statutory-discounts/evidence/status",
    "/v1/webpay/statutory-discounts/evidence/upload-sessions",
    "/finalize",
    "/v1/ops/operator-console/statutory-discounts/reviews/pending",
    "/v1/ops/operator-console/statutory-discounts/reviews/",
    "/evidence/",
    "/preview",
    "/v1/webpay/payment-intents"
)) {
    Assert-Contains -Text $runbook -Literal $path -Context "runbook endpoint map"
}
foreach ($path in @(
    "/v1/webpay/statutory-discounts/availability",
    "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover",
    "/v1/webpay/statutory-discounts/decisions",
    "/apply-payable-basis",
    "/v1/webpay/payment-intents"
)) {
    Assert-Contains -Text $paymentEndpointSource -Literal $path -Context "current Payment Orchestrator endpoint source"
}
foreach ($path in @(
    "/v1/webpay/statutory-discounts/evidence/bootstrap",
    "/v1/webpay/statutory-discounts/evidence/status",
    "/v1/webpay/statutory-discounts/evidence/upload-sessions",
    "/finalize"
)) {
    Assert-Contains -Text $evidenceEndpointSource -Literal $path -Context "current evidence endpoint source"
}
foreach ($path in @("/v1/human-authentication", "/login", "/session", "/logout")) {
    Assert-Contains -Text $humanAuthenticationSource -Literal $path -Context "current human-authentication source"
}
foreach ($path in @("/statutory-discounts/reviews/pending", "/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}", "/decision")) {
    Assert-Contains -Text $operatorReviewSource -Literal $path -Context "current Operator Console review source"
}
foreach ($path in @("/v1/ops/operator-console/statutory-discounts/reviews", "/evidence", "/preview")) {
    Assert-Contains -Text $operatorEvidenceSource -Literal $path -Context "current Operator Console evidence source"
}
foreach ($header in @("X-ExitPass-User-Id", "X-ExitPass-Permissions")) {
    Assert-Contains -Text $rbacCatalogSource -Literal $header -Context "fixture identity header catalog"
}
foreach ($literal in @("X-Operator-User-Id", "AllowFixtureIdentityHeaders", "FIXTURE_IDENTITY_HEADER_PROHIBITED")) {
    Assert-Contains -Text $fixtureGuardSource -Literal $literal -Context "Production fixture identity guard"
}

# Availability, lifecycle, decision, application, and handoff coverage.
foreach ($literal in @(
    "Supported jurisdiction",
    "Unsupported entitlement",
    "Missing jurisdiction",
    "Ambiguous jurisdiction",
    "No applicable ordinance",
    "Manipulated direct submission",
    "ordinary-payment fallback",
    "opaque upload",
    "streaming",
    "finalization",
    "validation",
    "malware",
    "reviewable",
    "pending review",
    "second tab",
    "idempotent replay",
    "restart",
    "applied payable basis",
    "provider session",
    "payment handoff"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook scenario coverage"
}

# Seed and verifier SQL structure.
Assert-Contains -Text $seed -Literal "BEGIN;" -Context "seed SQL"
Assert-Contains -Text $seed -Literal "COMMIT;" -Context "seed SQL"
Assert-Contains -Text $seed -Literal "current_database() !~ '^exitpass_webpay_local_walkthrough_statutory" -Context "seed database guard"
Assert-Contains -Text $seed -Literal "username_normalized" -Context "seed business-key discovery"
Assert-Contains -Text $seed -Literal "statutory_evidence_principal_scope_grants" -Context "seed evidence scope"
Assert-Contains -Text $seed -Literal "SENIOR_CITIZEN_ID" -Context "seed current evidence type"
Assert-Contains -Text $verify -Literal "BEGIN TRANSACTION READ ONLY;" -Context "verification SQL"
Assert-Contains -Text $verify -Literal "ROLLBACK;" -Context "verification SQL"
foreach ($staleColumn in @("provider_status_code", "recovery_action")) {
    Assert-NotContains -Text ($seed + $verify) -Literal $staleColumn -Context "SQL package"
}
foreach ($currentColumn in @("command_status", "result_classification", "recovery_classification", "safe_error_code", "session_status")) {
    Assert-Contains -Text $verify -Literal $currentColumn -Context "verification SQL current columns"
}
if ($verify -match '(?im)^\s*(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP|CALL|DO)\b') {
    throw "Verification SQL contains a mutating statement."
}
if (($seed.ToCharArray() | Where-Object { $_ -eq '(' }).Count -ne ($seed.ToCharArray() | Where-Object { $_ -eq ')' }).Count) {
    throw "Seed SQL has unbalanced parentheses."
}
if (($verify.ToCharArray() | Where-Object { $_ -eq '(' }).Count -ne ($verify.ToCharArray() | Where-Object { $_ -eq ')' }).Count) {
    throw "Verification SQL has unbalanced parentheses."
}

$canonicalTableSources = @(
    "objects\schemas\discounts\tables\discounts.statutory_discount_decision_commands.sql",
    "objects\schemas\discounts\tables\discounts.statutory_discount_payable_basis_application_commands.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_sets.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_items.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_scan_attempts.sql",
    "objects\schemas\core\tables\core.payment_attempts.sql",
    "objects\schemas\payments\tables\payments.provider_sessions.sql"
)
$canonicalTableText = @($canonicalTableSources | ForEach-Object {
    $path = Join-Path $CanonicalDatabaseRepository $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Canonical table source missing: $path" }
    Get-Content -LiteralPath $path -Raw
}) -join "`n"
foreach ($column in @("command_status", "result_classification", "recovery_classification", "safe_error_code", "session_status")) {
    Assert-Contains -Text $canonicalTableText -Literal $column -Context "canonical current database columns"
}
foreach ($staleColumn in @("provider_status_code", "recovery_action")) {
    Assert-NotContains -Text $canonicalTableText -Literal $staleColumn -Context "canonical current database columns"
}

# Startup, shutdown, and cleanup safety.
foreach ($literal in @(
    "exitpass_webpay_local_walkthrough_statutory",
    "exitpass.walkthrough=webpay-statutory-discount",
    "StateSchemaVersion",
    "WalkthroughIdentity",
    "EXISTING_STATE_CONFLICT",
    "STATE_UNSUPPORTED_SCHEMA",
    "STATE_OWNERSHIP_MISMATCH",
    "STATE_PATH_ESCAPE",
    "STATE_NOT_RESTARTABLE",
    "Write-WalkthroughStateAtomically",
    "FileMode]::CreateNew",
    "File]::Move",
    "Get-NetTCPConnection",
    "ExecutablePath",
    "CommandLineMarkers",
    "StartTimeUtc",
    "RestartServicesOnly"
)) {
    Assert-Contains -Text $start -Literal $literal -Context "startup ownership/recovery"
}
foreach ($literal in @(
    "StateSchemaVersion",
    "WalkthroughIdentity",
    "Read-ValidatedWalkthroughState",
    "STATE_OWNERSHIP_MISMATCH",
    "STATE_PATH_ESCAPE",
    "ExecutablePath",
    "CommandLineMarkers",
    "StartTimeUtc",
    "refusing to stop",
    "RemoveDisposableDatabase",
    "RemoveGeneratedState",
    "StopWalkthroughContainers"
)) {
    Assert-Contains -Text $stop -Literal $literal -Context "shutdown ownership guard"
}
Assert-NotContains -Text $operationalText -Literal "infra\docker\docker-compose.yml" -Context "walkthrough package"
Assert-NotContains -Text $executableText -Literal "git clean" -Context "walkthrough executable assets"
Assert-NotContains -Text $executableText -Literal "DROP DATABASE IF EXISTS exitpass_v12_dev" -Context "walkthrough executable assets"
Assert-NotContains -Text $start -Literal 'Invoke-PostgresFile $DatabaseName $rbacSource' -Context "startup fixture-guard handling"
Assert-NotContains -Text $start -Literal 'Set-Content -LiteralPath $statePath' -Context "final state persistence"
Assert-NotContains -Text $start -Literal 'Out-File -FilePath $statePath' -Context "final state persistence"

$lastFunctionEnd = ($functionDefinitions | ForEach-Object { $_.Extent.EndOffset } | Measure-Object -Maximum).Maximum
$startupMain = $start.Substring([int]$lastFunctionEnd)
$guardIndex = $startupMain.IndexOf('$freshStateConflict = Test-Path -LiteralPath $statePath', [StringComparison]::Ordinal)
if ($guardIndex -lt 0) { throw 'Fresh-start state inspection is missing from startup main flow.' }
foreach ($mutationBoundary in @(
    "Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD'",
    'New-Item -ItemType Directory -Force -Path $stateRoot, $logsRoot',
    'Ensure-SharedContainerRunning $PostgresContainerName',
    'Invoke-PostgresSql postgres',
    'docker network create',
    'New-SyntheticEvidenceImage $syntheticEvidencePath',
    'Start-WalkthroughProcess'
)) {
    $mutationIndex = $startupMain.IndexOf($mutationBoundary, [StringComparison]::Ordinal)
    if ($mutationIndex -lt 0 -or $guardIndex -ge $mutationIndex) {
        throw "Fresh-start state inspection must precede mutation boundary: $mutationBoundary"
    }
}

foreach ($prohibitedStateProperty in @('Password', 'Secret', 'Token', 'ConnectionString', 'ProvisioningUri', 'UploadUrl')) {
    if ($start -match ("(?m)^\s+{0}\s*=" -f [regex]::Escape($prohibitedStateProperty))) {
        throw "State schema contains prohibited secret-shaped property: $prohibitedStateProperty"
    }
}

# Secret-shaped values and unsafe credential transport. Environment-variable names
# and documentation about prohibited secrets are allowed; literal values are not.
$secretPatterns = @(
    '(?i)sk_live_[A-Za-z0-9]+',
    '(?i)pk_live_[A-Za-z0-9]+',
    '(?i)-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)otpauth://',
    '(?i)Authorization\s*:\s*Bearer\s+[A-Za-z0-9._-]+'
)
foreach ($pattern in $secretPatterns) {
    if ($operationalText -match $pattern) {
        throw "Secret-shaped literal found by pattern: $pattern"
    }
}
$assignmentPattern = '(?i)(password|secret|token)\s*=\s*["''][^$%{][^"'']{7,}["'']'
foreach ($match in [regex]::Matches($operationalText, $assignmentPattern)) {
    if ($match.Value -notmatch '\$env:') {
        throw "Secret-shaped assignment found in walkthrough assets."
    }
}

foreach ($claimPattern in @(
    '(?im)^\s*Controlled UAT\s*:\s*(passed|complete|ready|authorized)',
    '(?im)^\s*Production (validation|rollout)\s*:\s*(passed|complete|ready|authorized)',
    '(?i)\b(is|was)\s+(BIR|compliance) certified\b'
)) {
    if ($operationalText -match $claimPattern) {
        throw "Walkthrough assets contain an unauthorized validation claim."
    }
}

# Confirm the package itself states the intended limitations.
foreach ($literal in @(
    "local-development validation",
    "not Controlled UAT",
    "not compliance certification",
    "not production validation",
    "not production rollout authorization",
    "static harness does not execute the walkthrough"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook exclusions"
}

Write-Host "WebPay statutory-discount walkthrough static validation passed." -ForegroundColor Green
