<#
.SYNOPSIS
Creates or reads persisted WebPay PayMongo reconciliation runs.

.DESCRIPTION
This operator helper uses the existing read-only WebPay PayMongo reconciliation
diagnostics/export flow for classification, then persists run/item/exception
evidence into the existing reconciliation schema only.

It does not mutate payment attempts, provider sessions, payment confirmations,
exit authorizations, gate consumptions, audit events, domain events, outbox
events, settlement records, payout records, or MOPS records.
#>

param(
    [string] $TicketReference,
    [datetime] $FromDate,
    [datetime] $ToDate,
    [string] $ProviderCode = "PAYMONGO",
    [switch] $DryRun,
    [string] $OutputPath,
    [string] $ReadRun,
    [string] $DockerComposePath = "infra/docker",
    [string] $DatabaseName = "exitpass_v12_dev",
    [string] $DatabaseUser = "exitpass",
    [switch] $ScriptSelfTest
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $MyInvocation.MyCommand.Path
    }

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        return (Get-Location).Path
    }

    return (Resolve-Path (Join-Path (Split-Path -Parent $scriptPath) "..\..")).Path
}

function Assert-CommandExists {
    param([string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "REQUIRED_COMMAND_NOT_FOUND: $Name"
    }
}

function ConvertTo-SafeFileName {
    param([string] $Value)

    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    $safe = $Value
    foreach ($character in $invalid) {
        $safe = $safe.Replace($character, "-")
    }

    return $safe
}

function Quote-SqlString {
    param([AllowNull()][string] $Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace($Value)) {
        return "NULL"
    }

    return "'" + $Value.Replace("'", "''") + "'"
}

function Quote-SqlUuid {
    param([AllowNull()][string] $Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace($Value)) {
        return "NULL"
    }

    return "'" + $Value.Replace("'", "''") + "'::uuid"
}

function Quote-SqlNumeric {
    param([AllowNull()][string] $Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace($Value)) {
        return "NULL"
    }

    $number = [decimal]::Parse($Value, [Globalization.CultureInfo]::InvariantCulture)
    return $number.ToString([Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-ShortCode {
    param(
        [AllowNull()][string] $Value,
        [int] $MaxLength = 64
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $trimmed = $Value.Trim()
    if ($trimmed.Length -le $MaxLength) {
        return $trimmed
    }

    return $trimmed.Substring(0, $MaxLength)
}

function Get-ItemStatus {
    param([string] $Classification)

    if ($Classification -eq "MATCHED") {
        return "MATCHED"
    }

    return "EXCEPTION"
}

function Get-MatchStatus {
    param([string] $Classification)

    switch ($Classification) {
        "MATCHED" { "MATCH"; break }
        "AMOUNT_MISMATCH" { "AMOUNT_MISMATCH"; break }
        "CURRENCY_MISMATCH" { "AMOUNT_MISMATCH"; break }
        "EXITPASS_CONFIRMED_PROVIDER_MISSING" { "MISSING_TARGET"; break }
        "PROVIDER_PAID_EXITPASS_MISSING" { "MISSING_SOURCE"; break }
        "DUPLICATE_PROVIDER_EVENT" { "DUPLICATE"; break }
        "DUPLICATE_PAYMENT_CONFIRMATION" { "DUPLICATE"; break }
        default { "INCONCLUSIVE"; break }
    }
}

function Get-ExceptionType {
    param([string] $Classification)

    switch ($Classification) {
        "AMOUNT_MISMATCH" { "AMOUNT_MISMATCH"; break }
        "CURRENCY_MISMATCH" { "AMOUNT_MISMATCH"; break }
        "PROVIDER_PAID_EXITPASS_MISSING" { "MISSING_PAYMENT_CONFIRMATION"; break }
        "EXITPASS_CONFIRMED_PROVIDER_MISSING" { "MISSING_PROVIDER_OUTCOME"; break }
        "DUPLICATE_PROVIDER_EVENT" { "DUPLICATE_RECORD"; break }
        "DUPLICATE_PAYMENT_CONFIRMATION" { "DUPLICATE_RECORD"; break }
        "GATE_CONSUMED_WITHOUT_CONFIRMATION" { "MANUAL_GATE_WITHOUT_PAYMENT"; break }
        default { "POLICY_EXCEPTION"; break }
    }
}

function Get-ExceptionSeverity {
    param([string] $Classification)

    switch ($Classification) {
        "GATE_CONSUMED_WITHOUT_CONFIRMATION" { "CRITICAL"; break }
        "EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION" { "HIGH"; break }
        "PROVIDER_PAID_EXITPASS_MISSING" { "HIGH"; break }
        "AMOUNT_MISMATCH" { "MEDIUM"; break }
        "CURRENCY_MISMATCH" { "MEDIUM"; break }
        default { "LOW"; break }
    }
}

function New-Summary {
    param([object[]] $Rows)

    return [PSCustomObject][ordered]@{
        totalRows = @($Rows).Count
        matched = @($Rows | Where-Object { $_.reconciliation_classification -eq "MATCHED" }).Count
        exceptions = @($Rows | Where-Object { $_.reconciliation_classification -ne "MATCHED" }).Count
    }
}

function Invoke-ReadRun {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $RunId
    )

    $readSql = Join-Path $RepoRoot "scripts/dev-data/read-webpay-paymongo-reconciliation-run.sql"
    if (-not (Test-Path $readSql)) {
        throw "READBACK_SQL_NOT_FOUND: $readSql"
    }

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    Push-Location $composePathResolved
    try {
        Get-Content $readSql | docker compose exec -T postgres psql `
            -U $DatabaseUser `
            -d $DatabaseName `
            -v ON_ERROR_STOP=1 `
            -v "run_id=$RunId" `
            -P format=aligned `
            -f -

        if ($LASTEXITCODE -ne 0) {
            throw "READBACK_FAILED: $RunId"
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-PsqlSql {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $Sql
    )

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    Push-Location $composePathResolved
    try {
        $output = $Sql | docker compose exec -T postgres psql `
            -U $DatabaseUser `
            -d $DatabaseName `
            -v ON_ERROR_STOP=1 `
            -P format=aligned

        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "PERSISTENCE_SQL_FAILED: $($output | Out-String)"
    }

    return $output
}

function Invoke-ClassificationExport {
    param(
        [string] $RepoRoot,
        [string] $TicketReference,
        [Nullable[datetime]] $FromDate,
        [Nullable[datetime]] $ToDate,
        [string] $ProviderCode,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $OutputPath
    )

    $exportScript = Join-Path $RepoRoot "scripts/dev-data/Export-WebPayPayMongoReconciliation.ps1"
    if (-not (Test-Path $exportScript)) {
        throw "EXPORT_WRAPPER_NOT_FOUND: $exportScript"
    }

    $arguments = @{
        Format = "csv"
        ProviderCode = $ProviderCode
        DockerComposePath = $DockerComposePath
        DatabaseName = $DatabaseName
        DatabaseUser = $DatabaseUser
        OutputPath = $OutputPath
    }

    if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
        $arguments.TicketReference = $TicketReference
    }
    else {
        $arguments.FromDate = $FromDate.Value
        $arguments.ToDate = $ToDate.Value
    }

    & $exportScript @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "CLASSIFICATION_EXPORT_FAILED"
    }

    if (-not (Test-Path $OutputPath)) {
        throw "CLASSIFICATION_EXPORT_NOT_FOUND: $OutputPath"
    }

    return @(Import-Csv $OutputPath)
}

function Build-PersistenceSql {
    param(
        [object[]] $Rows,
        [string] $ProviderCode,
        [string] $RunId,
        [string] $RunCode,
        [string] $SourceBatchRef,
        [string] $ScopeType,
        [Nullable[datetime]] $FromDate,
        [Nullable[datetime]] $ToDate,
        [hashtable] $ItemIds,
        [object] $Summary
    )

    $correlationId = ($Rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.correlation_id) } | Select-Object -First 1).correlation_id
    $windowStart = "NULL"
    $windowEnd = "NULL"
    if ($FromDate.HasValue) {
        $windowStart = Quote-SqlString ($FromDate.Value.ToString("yyyy-MM-ddT00:00:00zzz"))
    }
    if ($ToDate.HasValue) {
        $windowEnd = Quote-SqlString ($ToDate.Value.ToString("yyyy-MM-ddT23:59:59zzz"))
    }

    $sql = New-Object System.Text.StringBuilder
    [void]$sql.AppendLine("BEGIN;")
    [void]$sql.AppendLine("INSERT INTO reconciliation.reconciliation_runs (")
    [void]$sql.AppendLine("    reconciliation_run_id, run_code, run_type, run_status, scope_type, source_batch_ref,")
    [void]$sql.AppendLine("    window_start_at, window_end_at, started_at, completed_at, item_count, matched_count,")
    [void]$sql.AppendLine("    exception_count, rejected_count, disputed_count, correlation_id")
    [void]$sql.AppendLine(") VALUES (")
    [void]$sql.AppendLine(("    {0}, {1}, 'PAYMENT_PROVIDER_RECONCILIATION', 'COMPLETED', '{2}', {3}," -f (Quote-SqlUuid $RunId), (Quote-SqlString $RunCode), $ScopeType, (Quote-SqlString $SourceBatchRef)))
    [void]$sql.AppendLine(("    {0}::timestamptz, {1}::timestamptz, now(), now(), {2}, {3}," -f $windowStart, $windowEnd, $Summary.totalRows, $Summary.matched))
    [void]$sql.AppendLine(("    {0}, 0, 0, {1}" -f $Summary.exceptions, (Quote-SqlUuid $correlationId)))
    [void]$sql.AppendLine(");")

    foreach ($row in $Rows) {
        $classification = ConvertTo-ShortCode $row.reconciliation_classification
        $itemId = $ItemIds[$row.ticket_reference]
        $itemStatus = Get-ItemStatus $classification
        $matchStatus = Get-MatchStatus $classification
        $expectedAmount = if (-not [string]::IsNullOrWhiteSpace($row.confirmed_amount)) { $row.confirmed_amount } else { $row.amount }
        $actualAmount = $row.provider_amount
        $variance = "NULL"
        if (-not [string]::IsNullOrWhiteSpace($expectedAmount) -and -not [string]::IsNullOrWhiteSpace($actualAmount)) {
            $expected = [decimal]::Parse($expectedAmount, [Globalization.CultureInfo]::InvariantCulture)
            $actual = [decimal]::Parse($actualAmount, [Globalization.CultureInfo]::InvariantCulture)
            $variance = [Math]::Abs($expected - $actual).ToString([Globalization.CultureInfo]::InvariantCulture)
        }

        [void]$sql.AppendLine("INSERT INTO reconciliation.reconciliation_items (")
        [void]$sql.AppendLine("    reconciliation_item_id, reconciliation_run_id, payment_attempt_id, payment_confirmation_id,")
        [void]$sql.AppendLine("    target_entity_type, target_entity_id, comparison_basis, item_status, match_status,")
        [void]$sql.AppendLine("    expected_amount, actual_amount, currency_code, variance_amount, exception_reason_code, correlation_id")
        [void]$sql.AppendLine(") VALUES (")
        [void]$sql.AppendLine(("    {0}, {1}, {2}, {3}," -f (Quote-SqlUuid $itemId), (Quote-SqlUuid $RunId), (Quote-SqlUuid $row.payment_attempt_id), (Quote-SqlUuid $row.payment_confirmation_id)))
        [void]$sql.AppendLine(("    'ParkingSession', {0}, 'PROVIDER_TO_CORE', '{1}', '{2}'," -f (Quote-SqlUuid $row.parking_session_id), $itemStatus, $matchStatus))
        [void]$sql.AppendLine(("    {0}, {1}, {2}, {3}, {4}, {5}" -f (Quote-SqlNumeric $expectedAmount), (Quote-SqlNumeric $actualAmount), (Quote-SqlString $row.currency_code), $variance, (Quote-SqlString $classification), (Quote-SqlUuid $row.correlation_id)))
        [void]$sql.AppendLine(");")

        if ($classification -ne "MATCHED") {
            $exceptionType = Get-ExceptionType $classification
            $severity = Get-ExceptionSeverity $classification
            $summary = ConvertTo-ShortCode ("WebPay PayMongo reconciliation " + $classification) 256
            $detail = $row.reconciliation_reason

            [void]$sql.AppendLine("INSERT INTO reconciliation.reconciliation_exceptions (")
            [void]$sql.AppendLine("    reconciliation_run_id, reconciliation_item_id, exception_type, exception_severity,")
            [void]$sql.AppendLine("    exception_status, exception_reason_code, exception_summary, exception_detail,")
            [void]$sql.AppendLine("    created_from_status, detected_at, correlation_id")
            [void]$sql.AppendLine(") VALUES (")
            [void]$sql.AppendLine(("    {0}, {1}, '{2}', '{3}'," -f (Quote-SqlUuid $RunId), (Quote-SqlUuid $itemId), $exceptionType, $severity))
            [void]$sql.AppendLine(("    'OPEN', {0}, {1}, {2}," -f (Quote-SqlString $classification), (Quote-SqlString $summary), (Quote-SqlString $detail)))
            [void]$sql.AppendLine(("    {0}, now(), {1}" -f (Quote-SqlString $classification), (Quote-SqlUuid $row.correlation_id)))
            [void]$sql.AppendLine(");")
        }
    }

    [void]$sql.AppendLine("COMMIT;")
    [void]$sql.AppendLine(("SELECT {0} AS reconciliation_run_id, {1} AS run_code, {2}::integer AS item_count, {3}::integer AS matched_count, {4}::integer AS exception_count;" -f (Quote-SqlString $RunId), (Quote-SqlString $RunCode), $Summary.totalRows, $Summary.matched, $Summary.exceptions))

    return $sql.ToString()
}

function Invoke-SelfTest {
    $repoRoot = Resolve-RepoRoot
    foreach ($relativePath in @(
        "scripts/dev-data/Export-WebPayPayMongoReconciliation.ps1",
        "scripts/dev-data/read-webpay-paymongo-reconciliation-run.sql",
        "scripts/dev-data/persist-webpay-paymongo-reconciliation-run.sql")) {
        if (-not (Test-Path (Join-Path $repoRoot $relativePath))) {
            throw "SELFTEST_FAILED: missing $relativePath"
        }
    }

    if ($ProviderCode -ne "PAYMONGO") {
        throw "SELFTEST_FAILED: ProviderCode default changed from PAYMONGO."
    }

    $scriptText = Get-Content $PSCommandPath -Raw
    $outOfScopeProviderToken = -join ([char[]](65, 85, 66))
    if ($scriptText -match $outOfScopeProviderToken) {
        throw "SELFTEST_FAILED: out-of-scope provider reference found."
    }

    Write-Host "SELFTEST PASS"
}

if ($ScriptSelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($ProviderCode -ne "PAYMONGO") {
    throw "UNSUPPORTED_PROVIDER_CODE: this reconciliation run wrapper supports PAYMONGO only."
}

Assert-CommandExists "docker"
$repoRoot = Resolve-RepoRoot

if (-not [string]::IsNullOrWhiteSpace($ReadRun)) {
    Invoke-ReadRun `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -RunId $ReadRun
    exit 0
}

if ([string]::IsNullOrWhiteSpace($TicketReference)) {
    if (-not $PSBoundParameters.ContainsKey("FromDate") -and -not $PSBoundParameters.ContainsKey("ToDate")) {
        throw "MISSING_RECONCILIATION_SCOPE: supply -TicketReference or both -FromDate and -ToDate."
    }

    if (-not $PSBoundParameters.ContainsKey("FromDate") -or -not $PSBoundParameters.ContainsKey("ToDate")) {
        throw "MISSING_RECONCILIATION_SCOPE: date range runs require both -FromDate and -ToDate."
    }
}

$exportDirectory = Join-Path $repoRoot "scripts/dev-data/.reconciliation-exports"
if (-not (Test-Path $exportDirectory)) {
    New-Item -ItemType Directory -Force -Path $exportDirectory | Out-Null
}

$scopeToken = if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
    ConvertTo-SafeFileName $TicketReference
}
else {
    "{0}-{1}" -f $FromDate.ToString("yyyy-MM-dd"), $ToDate.ToString("yyyy-MM-dd")
}

$classificationPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $exportDirectory ("webpay-paymongo-reconciliation-run-source-{0}.csv" -f $scopeToken)
}
else {
    $OutputPath
}

$rows = @(Invoke-ClassificationExport `
    -RepoRoot $repoRoot `
    -TicketReference $TicketReference `
    -FromDate $FromDate `
    -ToDate $ToDate `
    -ProviderCode $ProviderCode `
    -DockerComposePath $DockerComposePath `
    -DatabaseName $DatabaseName `
    -DatabaseUser $DatabaseUser `
    -OutputPath $classificationPath)

$summary = New-Summary $rows

if ($summary.totalRows -eq 0) {
    throw "NO_RECONCILIATION_ROWS"
}

if ($rows.Count -eq 1 -and $rows[0].reconciliation_classification -eq "REQUESTED_TICKET_NOT_FOUND") {
    Write-Host "REQUESTED_TICKET_NOT_FOUND"
    Write-Host "No reconciliation run was persisted."
    exit 0
}

Write-Host ""
Write-Host "Reconciliation run source classified"
Write-Host "Provider: $ProviderCode"
Write-Host "Total rows: $($summary.totalRows)"
Write-Host "Matched count: $($summary.matched)"
Write-Host "Exception count: $($summary.exceptions)"

if ($DryRun) {
    Write-Host "DryRun: no reconciliation rows were persisted."
    exit 0
}

$runId = [guid]::NewGuid().ToString()
$runCode = "PMWPR-{0}-{1}" -f (Get-Date -Format "yyyyMMddHHmmss"), $runId.Substring(0, 8)
$sourceBatchRef = if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
    ConvertTo-ShortCode ("PAYMONGO;TICKET=" + $TicketReference) 128
}
else {
    ConvertTo-ShortCode ("PAYMONGO;RANGE={0}:{1}" -f $FromDate.ToString("yyyy-MM-dd"), $ToDate.ToString("yyyy-MM-dd")) 128
}
$scopeType = if (-not [string]::IsNullOrWhiteSpace($TicketReference)) { "SOURCE_BATCH" } else { "TIME_WINDOW" }
$itemIds = @{}
foreach ($row in $rows) {
    $key = if ([string]::IsNullOrWhiteSpace($row.ticket_reference)) { [guid]::NewGuid().ToString() } else { $row.ticket_reference }
    $itemIds[$key] = [guid]::NewGuid().ToString()
}

$sql = Build-PersistenceSql `
    -Rows $rows `
    -ProviderCode $ProviderCode `
    -RunId $runId `
    -RunCode $runCode `
    -SourceBatchRef $sourceBatchRef `
    -ScopeType $scopeType `
    -FromDate $(if ([string]::IsNullOrWhiteSpace($TicketReference)) { $FromDate } else { $null }) `
    -ToDate $(if ([string]::IsNullOrWhiteSpace($TicketReference)) { $ToDate } else { $null }) `
    -ItemIds $itemIds `
    -Summary $summary

Invoke-PsqlSql `
    -RepoRoot $repoRoot `
    -DockerComposePath $DockerComposePath `
    -DatabaseName $DatabaseName `
    -DatabaseUser $DatabaseUser `
    -Sql $sql | Out-Host

Write-Host ""
Write-Host "Persisted reconciliation run"
Write-Host "Run id: $runId"
Write-Host "Run code: $runCode"
Write-Host "Duplicate run behavior: new explicit run version per execution via unique run_code."
Write-Host "Item count: $($summary.totalRows)"
Write-Host "Exception count: $($summary.exceptions)"
