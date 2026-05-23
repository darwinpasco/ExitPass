<#
.SYNOPSIS
    Runs the validated WebPay PayMongo QRPh gate-exit runtime chain.

.DESCRIPTION
    Developer/operator helper for the ExitPass v1.2 WebPay PayMongo QRPh slice.
    The script resolves a current-day WebPay test ticket, creates a PayMongo
    checkout, waits for webhook finality, consumes the issued exit authorization,
    verifies duplicate consume behavior, and prints a PASS/FAIL summary.

    Standing rules:
      - QRPH/PHP must route to PAYMONGO only.
      - Do not select, configure, route to, or invoke AUB for this flow.
      - Inspect live database schema before issuing data queries.
      - Do not hardcode PayMongo secrets or make schema changes.
#>

[CmdletBinding()]
param(
    [string] $TicketReference,

    [string] $PaymentMethod = "QRPH",

    [string] $DockerComposePath = "D:\SourceCodes\ExitPass\infra\docker",

    [string] $DatabaseName = "exitpass_v12_dev",

    [string] $DatabaseUser = "exitpass",

    [string] $NgrokBaseUrl,

    [string] $PublicBaseUrl,

    [switch] $SkipSeed,

    [switch] $SkipBrowserPayment,

    [switch] $DiagnosticsOnly,

    [switch] $ScriptSelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$paymentOrchestratorBaseUrl = "http://localhost:8082"
$centralPmsBaseUrl = "http://localhost:8080"
$correlationId = [guid]::NewGuid().ToString()
$summary = [ordered]@{
    TicketReference = $TicketReference
    PaymentMethod = $PaymentMethod
    Provider = $null
    PaymentAttemptStatus = $null
    ProviderSessionStatus = $null
    PaymentConfirmationCount = $null
    ExitAuthorizationId = $null
    ExitAuthorizationStatus = $null
    GateConsumeStatus = $null
    DuplicateConsumeBehavior = $null
    CorrelationId = $correlationId
}

function Write-Step {
    param([string] $Message)
    Write-Host ""
    Write-Host "== $Message" -ForegroundColor Cyan
}

function Fail {
    param([string] $Message)
    Write-Host ""
    Write-Host "FULL E2E FAIL" -ForegroundColor Red
    Write-Host $Message -ForegroundColor Red
    Print-Summary
    exit 1
}

function Print-Summary {
    Write-Host ""
    Write-Host "Summary" -ForegroundColor Cyan
    foreach ($entry in $summary.GetEnumerator()) {
        $value = if ($null -eq $entry.Value -or ($entry.Value -is [string] -and $entry.Value -eq "")) { "<none>" } else { $entry.Value }
        Write-Host ("  {0}: {1}" -f $entry.Key, $value)
    }
}

function Normalize-PaymentStatus {
    param([string] $PaymentStatus)

    if ([string]::IsNullOrWhiteSpace($PaymentStatus)) {
        return ""
    }

    return ($PaymentStatus -replace '[\s_\-]', '').Trim().ToUpperInvariant()
}

function Test-IsPaidFinalPaymentStatus {
    param([string] $PaymentStatus)

    $normalized = Normalize-PaymentStatus -PaymentStatus $PaymentStatus
    return $normalized -in @("PAID", "PAYMENTCOMPLETED", "CONFIRMED")
}

function Get-ResolvedPaymentAction {
    param([string] $PaymentStatus)

    if (Test-IsPaidFinalPaymentStatus -PaymentStatus $PaymentStatus) {
        return "SkipPaymentIntent"
    }

    return "CreatePaymentIntent"
}

function Get-GateValidationAction {
    param([object] $Snapshot)

    if ($Snapshot.latest_consume_status -eq "CONSUMED" -or $Snapshot.exit_authorization_status -eq "CONSUMED") {
        return "VerifyDuplicateConsume"
    }

    return "ConsumeThenVerifyDuplicate"
}

function Invoke-ScriptSelfTest {
    Write-Step "Running script self-tests"

    $paidStatuses = @("Paid", "PAID", "PaymentCompleted", "CONFIRMED", "Confirmed")
    foreach ($status in $paidStatuses) {
        if (-not (Test-IsPaidFinalPaymentStatus -PaymentStatus $status)) {
            throw "Expected paid/final status '$status' to be recognized."
        }

        if ((Get-ResolvedPaymentAction -PaymentStatus $status) -ne "SkipPaymentIntent") {
            throw "Expected paid/final status '$status' to skip payment intent creation."
        }
    }

    if ((Get-ResolvedPaymentAction -PaymentStatus "Not Started") -ne "CreatePaymentIntent") {
        throw "Expected unpaid status to create payment intent."
    }

    $consumedSnapshot = [pscustomobject]@{
        latest_consume_status = "CONSUMED"
        exit_authorization_status = "CONSUMED"
    }
    if ((Get-GateValidationAction -Snapshot $consumedSnapshot) -ne "VerifyDuplicateConsume") {
        throw "Expected already consumed authorization to validate duplicate consume behavior."
    }

    $issuedSnapshot = [pscustomobject]@{
        latest_consume_status = $null
        exit_authorization_status = "ISSUED"
    }
    if ((Get-GateValidationAction -Snapshot $issuedSnapshot) -ne "ConsumeThenVerifyDuplicate") {
        throw "Expected issued unconsumed authorization to consume before duplicate validation."
    }

    Write-Host "Script self-tests passed." -ForegroundColor Green
}

function Assert-PathExists {
    param(
        [string] $Path,
        [string] $Description
    )

    if (-not (Test-Path $Path)) {
        throw "$Description not found: $Path"
    }
}

function Invoke-DockerCompose {
    param([string[]] $Arguments)

    Push-Location $DockerComposePath
    try {
        & docker compose @Arguments
    }
    finally {
        Pop-Location
    }
}

function Invoke-PsqlText {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Sql,

        [switch] $Json
    )

    $args = @(
        "exec", "-T", "postgres",
        "psql", "-X", "-v", "ON_ERROR_STOP=1",
        "-U", $DatabaseUser,
        "-d", $DatabaseName
    )

    if ($Json) {
        $args += @("-q", "-A", "-t")
    }

    Push-Location $DockerComposePath
    try {
        $output = $Sql | & docker compose @args
        if ($LASTEXITCODE -ne 0) {
            throw "psql failed with exit code $LASTEXITCODE."
        }

        return ($output -join "`n").Trim()
    }
    finally {
        Pop-Location
    }
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,

        [string] $Ticket
    )

    $args = @(
        "exec", "-T", "postgres",
        "psql", "-X", "-v", "ON_ERROR_STOP=1",
        "-U", $DatabaseUser,
        "-d", $DatabaseName
    )

    if (-not [string]::IsNullOrWhiteSpace($Ticket)) {
        $args += @("-v", "ticket_ref='$Ticket'")
    }

    Push-Location $DockerComposePath
    try {
        Get-Content $Path | & docker compose @args
        if ($LASTEXITCODE -ne 0) {
            throw "psql diagnostics failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-SchemaInspection {
    Write-Step "Inspecting live database schema"

    $meta = @"
\d core.parking_sessions
\d core.tariff_snapshots
\d core.payment_attempts
\d payments.payment_rails
\d payments.payment_provider_routing_policies
\d payments.provider_sessions
\d core.payment_confirmations
\d core.exit_authorizations
\d gates.gate_authorization_consumptions
\d identity.service_identities
"@
    [void](Invoke-PsqlText -Sql $meta)

    $required = @"
WITH expected(schema_name, table_name, column_name) AS (
    VALUES
        ('core','parking_sessions','parking_session_id'),
        ('core','parking_sessions','vendor_session_ref'),
        ('core','parking_sessions','session_status'),
        ('core','parking_sessions','site_group_id'),
        ('core','parking_sessions','site_id'),
        ('core','parking_sessions','vendor_system_id'),
        ('core','tariff_snapshots','parking_session_id'),
        ('core','tariff_snapshots','snapshot_status'),
        ('core','payment_attempts','payment_attempt_id'),
        ('core','payment_attempts','parking_session_id'),
        ('core','payment_attempts','attempt_status'),
        ('payments','payment_rails','provider_code'),
        ('payments','payment_provider_routing_policies','primary_provider_code'),
        ('payments','payment_provider_routing_policies','fallback_provider_code'),
        ('payments','provider_sessions','session_status'),
        ('core','payment_confirmations','payment_confirmation_id'),
        ('core','exit_authorizations','exit_authorization_id'),
        ('core','exit_authorizations','authorization_status'),
        ('gates','gate_authorization_consumptions','consume_status'),
        ('identity','service_identities','service_identity_id'),
        ('identity','service_identities','service_identity_code')
),
missing AS (
    SELECT e.*
    FROM expected e
    LEFT JOIN information_schema.columns c
      ON c.table_schema = e.schema_name
     AND c.table_name = e.table_name
     AND c.column_name = e.column_name
    WHERE c.column_name IS NULL
)
SELECT COALESCE(json_agg(row_to_json(missing)), '[]'::json)
FROM missing;
"@

    $missingJson = Invoke-PsqlText -Sql $required -Json
    $missing = $missingJson | ConvertFrom-Json
    if ($missing.Count -gt 0) {
        throw "Live schema is missing expected columns: $missingJson"
    }
}

function Get-PhilippineToday {
    $tz = [System.TimeZoneInfo]::FindSystemTimeZoneById("Singapore Standard Time")
    return [System.TimeZoneInfo]::ConvertTime([datetimeoffset]::UtcNow, $tz).Date
}

function Get-CurrentTicketStamp {
    return (Get-PhilippineToday).ToString("yyyyMMdd")
}

function Invoke-SeedIfPresent {
    if ($SkipSeed) {
        Write-Host "Skipping seed because -SkipSeed was supplied."
        return
    }

    $stamp = Get-CurrentTicketStamp
    $seed = Join-Path $PSScriptRoot "webpay-$stamp-seed.sql"
    if (-not (Test-Path $seed)) {
        Write-Host "No current-day WebPay seed found at $seed. Continuing without seeding."
        return
    }

    $existingSql = @"
SELECT COUNT(*)
FROM core.parking_sessions
WHERE vendor_session_ref LIKE 'WEBPAY-$stamp%';
"@
    $existingCount = [int](Invoke-PsqlText -Sql $existingSql -Json)
    if ($existingCount -gt 0) {
        Write-Host "Current-day WebPay seed data already exists ($existingCount parking sessions). Reusing it instead of replaying seed cleanup."
        return
    }

    Write-Step "Running current-day WebPay seed"
    Write-Host "Seed: $seed"
    Invoke-PsqlFile -Path $seed
}

function Find-NextFreshTicket {
    $stamp = Get-CurrentTicketStamp
    $pattern = "WEBPAY-$stamp-FRESH-%"

    Write-Step "Finding next unpaid/unconsumed current-day FRESH ticket"
    Write-Host "Pattern: $pattern"

    $sql = @"
WITH candidates AS (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref,
        ps.site_group_id,
        ps.site_id,
        ps.vendor_system_id,
        COUNT(pa.payment_attempt_id) FILTER (WHERE pa.attempt_status::text IN ('REQUESTED','PENDING','PENDING_PROVIDER','ACTIVE','CONFIRMED')) AS active_or_confirmed_attempt_count,
        COUNT(ea.exit_authorization_id) FILTER (WHERE ea.authorization_status::text IN ('ISSUED','CONSUMED')) AS issued_or_consumed_authorization_count,
        COUNT(gac.gate_authorization_consumption_id) FILTER (WHERE gac.consume_status::text = 'CONSUMED') AS consumed_count
    FROM core.parking_sessions ps
    JOIN core.tariff_snapshots ts
      ON ts.parking_session_id = ps.parking_session_id
     AND ts.snapshot_status = 'ACTIVE'
    LEFT JOIN core.payment_attempts pa
      ON pa.parking_session_id = ps.parking_session_id
    LEFT JOIN core.exit_authorizations ea
      ON ea.parking_session_id = ps.parking_session_id
    LEFT JOIN gates.gate_authorization_consumptions gac
      ON gac.exit_authorization_id = ea.exit_authorization_id
    WHERE ps.vendor_session_ref LIKE '$pattern'
    GROUP BY ps.parking_session_id, ps.vendor_session_ref, ps.site_group_id, ps.site_id, ps.vendor_system_id
)
SELECT COALESCE(row_to_json(candidates), '{}'::json)
FROM candidates
WHERE active_or_confirmed_attempt_count = 0
  AND issued_or_consumed_authorization_count = 0
  AND consumed_count = 0
ORDER BY vendor_session_ref
LIMIT 1;
"@

    $json = Invoke-PsqlText -Sql $sql -Json
    if ([string]::IsNullOrWhiteSpace($json) -or $json -eq "{}") {
        throw "No unpaid/unconsumed current-day FRESH ticket found for pattern $pattern."
    }

    return $json | ConvertFrom-Json
}

function Get-TicketContext {
    param([string] $Ticket)

    $safeTicket = $Ticket.Replace("'", "''")
    $sql = @"
SELECT COALESCE(row_to_json(ctx), '{}'::json)
FROM (
    SELECT
        ps.parking_session_id,
        ps.vendor_session_ref,
        ps.site_group_id,
        ps.site_id,
        ps.vendor_system_id,
        vs.vendor_code AS vendor_system_code
    FROM core.parking_sessions ps
    LEFT JOIN integration.vendor_systems vs
      ON vs.vendor_system_id = ps.vendor_system_id
    WHERE ps.vendor_session_ref = '$safeTicket'
    LIMIT 1
) ctx;
"@

    $json = Invoke-PsqlText -Sql $sql -Json
    if ([string]::IsNullOrWhiteSpace($json) -or $json -eq "{}") {
        throw "Ticket not found in core.parking_sessions: $Ticket"
    }

    return $json | ConvertFrom-Json
}

function Get-ServiceIdentityId {
    $stamp = Get-CurrentTicketStamp
    $code = "WEBPAY_${stamp}_TEST_SEEDER"
    $sql = @"
SELECT COALESCE((
    SELECT service_identity_id::text
    FROM identity.service_identities
    WHERE service_identity_code = '$code'
    LIMIT 1
), (
    SELECT service_identity_id::text
    FROM identity.service_identities
    WHERE identity_status = 'ACTIVE'
    ORDER BY created_at
    LIMIT 1
), '');
"@

    $id = Invoke-PsqlText -Sql $sql -Json
    if ([string]::IsNullOrWhiteSpace($id)) {
        throw "No active service identity found for gate consume requestedByUserId."
    }

    return $id
}

function Invoke-JsonPost {
    param(
        [string] $Url,
        [object] $Body,
        [hashtable] $Headers = @{}
    )

    $json = $Body | ConvertTo-Json -Depth 10
    try {
        return Invoke-RestMethod -Method Post -Uri $Url -Body $json -ContentType "application/json" -Headers $Headers -TimeoutSec 60
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) {
            throw
        }

        $statusCode = [int]$response.StatusCode
        $stream = $response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        $bodyText = $reader.ReadToEnd()
        throw "POST $Url failed with HTTP $statusCode. Body: $bodyText"
    }
}

function Resolve-WebPaySession {
    param([object] $Context)

    Write-Step "Resolving WebPay parking session"
    $body = @{
        ticketReference = $Context.vendor_session_ref
        vendorSystemId = $Context.vendor_system_code
        correlationId = $correlationId
    }

    if ($null -ne $Context.site_group_id) { $body.siteGroupId = $Context.site_group_id }
    if ($null -ne $Context.site_id) { $body.siteId = $Context.site_id }

    return Invoke-JsonPost -Url "$paymentOrchestratorBaseUrl/v1/webpay/parking-session" -Body $body -Headers @{
        "X-Correlation-Id" = $correlationId
    }
}

function New-WebPayPaymentIntent {
    param([object] $Context)

    Write-Step "Creating WebPay PayMongo payment intent"
    $body = @{
        ticketReference = $Context.vendor_session_ref
        paymentMethod = $PaymentMethod
        vendorSystemId = $Context.vendor_system_code
        correlationId = $correlationId
    }

    if ($null -ne $Context.site_group_id) { $body.siteGroupId = $Context.site_group_id }
    if ($null -ne $Context.site_id) { $body.siteId = $Context.site_id }

    return Invoke-JsonPost -Url "$paymentOrchestratorBaseUrl/v1/webpay/payment-intents" -Body $body -Headers @{
        "X-Correlation-Id" = $correlationId
    }
}

function Get-FinalitySnapshot {
    param([string] $Ticket)

    $safeTicket = $Ticket.Replace("'", "''")
    $sql = @"
WITH target_session AS (
    SELECT parking_session_id, vendor_session_ref
    FROM core.parking_sessions
    WHERE vendor_session_ref = '$safeTicket'
),
attempts AS (
    SELECT pa.payment_attempt_id, pa.parking_session_id, pa.attempt_status, pr.provider_code
    FROM core.payment_attempts pa
    JOIN target_session s ON s.parking_session_id = pa.parking_session_id
    LEFT JOIN payments.payment_rails pr ON pr.payment_rail_id = pa.payment_rail_id
),
provider_sessions AS (
    SELECT ps.provider_session_id, ps.payment_attempt_id, ps.session_status, ps.provider_session_ref, ps.checkout_url
    FROM payments.provider_sessions ps
    JOIN attempts a ON a.payment_attempt_id = ps.payment_attempt_id
),
confirmations AS (
    SELECT pc.payment_confirmation_id, pc.payment_attempt_id, pc.correlation_id
    FROM core.payment_confirmations pc
    JOIN attempts a ON a.payment_attempt_id = pc.payment_attempt_id
),
exit_authorizations AS (
    SELECT ea.exit_authorization_id, ea.payment_attempt_id, ea.authorization_status
    FROM core.exit_authorizations ea
    JOIN attempts a ON a.payment_attempt_id = ea.payment_attempt_id
),
consumptions AS (
    SELECT gac.gate_authorization_consumption_id, gac.exit_authorization_id, gac.consume_status, gac.correlation_id
    FROM gates.gate_authorization_consumptions gac
    JOIN exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
)
SELECT COALESCE(row_to_json(x), '{}'::json)
FROM (
    SELECT
        s.vendor_session_ref AS ticket_reference,
        (SELECT payment_attempt_id::text FROM attempts ORDER BY payment_attempt_id LIMIT 1) AS payment_attempt_id,
        (SELECT attempt_status::text FROM attempts ORDER BY payment_attempt_id LIMIT 1) AS payment_attempt_status,
        (SELECT provider_code FROM attempts ORDER BY payment_attempt_id LIMIT 1) AS provider_code,
        (SELECT session_status::text FROM provider_sessions ORDER BY provider_session_id DESC LIMIT 1) AS provider_session_status,
        (SELECT provider_session_ref FROM provider_sessions ORDER BY provider_session_id DESC LIMIT 1) AS provider_session_ref,
        (SELECT checkout_url FROM provider_sessions ORDER BY provider_session_id DESC LIMIT 1) AS checkout_url,
        (SELECT COUNT(*) FROM confirmations) AS payment_confirmation_count,
        (SELECT correlation_id::text FROM confirmations ORDER BY payment_confirmation_id DESC LIMIT 1) AS payment_confirmation_correlation_id,
        (SELECT COUNT(*) FROM exit_authorizations) AS exit_authorization_count,
        (SELECT exit_authorization_id::text FROM exit_authorizations ORDER BY exit_authorization_id DESC LIMIT 1) AS exit_authorization_id,
        (SELECT authorization_status::text FROM exit_authorizations ORDER BY exit_authorization_id DESC LIMIT 1) AS exit_authorization_status,
        (SELECT COUNT(*) FROM consumptions WHERE consume_status::text = 'CONSUMED') AS successful_consume_count,
        (SELECT consume_status::text FROM consumptions ORDER BY gate_authorization_consumption_id DESC LIMIT 1) AS latest_consume_status,
        (SELECT COUNT(*)
           FROM exit_authorizations ea
           JOIN consumptions gac ON gac.exit_authorization_id = ea.exit_authorization_id
          WHERE gac.consume_status::text = 'CONSUMED'
          GROUP BY ea.exit_authorization_id
         HAVING COUNT(*) > 1
          LIMIT 1) AS duplicate_success_rows
    FROM target_session s
    LIMIT 1
) x;
"@

    $json = Invoke-PsqlText -Sql $sql -Json
    if ([string]::IsNullOrWhiteSpace($json) -or $json -eq "{}") {
        throw "No finality snapshot found for ticket $Ticket."
    }

    return $json | ConvertFrom-Json
}

function Update-SummaryFromSnapshot {
    param([object] $Snapshot)

    $summary.TicketReference = $Snapshot.ticket_reference
    $summary.Provider = $Snapshot.provider_code
    $summary.PaymentAttemptStatus = $Snapshot.payment_attempt_status
    $summary.ProviderSessionStatus = $Snapshot.provider_session_status
    $summary.PaymentConfirmationCount = $Snapshot.payment_confirmation_count
    $summary.ExitAuthorizationId = $Snapshot.exit_authorization_id
    $summary.ExitAuthorizationStatus = $Snapshot.exit_authorization_status
    $summary.GateConsumeStatus = $Snapshot.latest_consume_status
    if ($Snapshot.payment_confirmation_correlation_id) {
        $summary.CorrelationId = $Snapshot.payment_confirmation_correlation_id
    }
}

function Assert-PaymentFinalitySnapshot {
    param([object] $Snapshot)

    Update-SummaryFromSnapshot -Snapshot $Snapshot

    if ($Snapshot.provider_code -ne "PAYMONGO") {
        Fail "Expected provider PAYMONGO, got $($Snapshot.provider_code)."
    }

    if ($Snapshot.payment_attempt_status -ne "CONFIRMED") {
        Fail "Expected payment attempt CONFIRMED for final paid session, got $($Snapshot.payment_attempt_status)."
    }

    if ($Snapshot.provider_session_status -ne "PAID") {
        Fail "Expected provider session PAID for final paid session, got $($Snapshot.provider_session_status)."
    }

    if ([int]$Snapshot.payment_confirmation_count -ne 1) {
        Fail "Expected payment confirmation count 1 for final paid session, got $($Snapshot.payment_confirmation_count)."
    }

    if ([int]$Snapshot.exit_authorization_count -ne 1 -or [string]::IsNullOrWhiteSpace($Snapshot.exit_authorization_id)) {
        Fail "Expected exactly one ExitAuthorization for final paid session."
    }

    if ($Snapshot.exit_authorization_status -notin @("ISSUED", "CONSUMED")) {
        Fail "Expected ExitAuthorization status ISSUED or CONSUMED, got $($Snapshot.exit_authorization_status)."
    }
}

function Wait-ForPaymentFinality {
    param([string] $Ticket)

    Write-Step "Polling diagnostics for PayMongo webhook finality"
    $deadline = (Get-Date).AddMinutes(10)
    do {
        $snapshot = Get-FinalitySnapshot -Ticket $Ticket
        Update-SummaryFromSnapshot -Snapshot $snapshot

        $ready =
            $snapshot.payment_attempt_status -eq "CONFIRMED" -and
            $snapshot.provider_session_status -eq "PAID" -and
            [int]$snapshot.payment_confirmation_count -eq 1 -and
            $snapshot.provider_code -eq "PAYMONGO" -and
            [int]$snapshot.exit_authorization_count -eq 1 -and
            ($snapshot.exit_authorization_status -eq "ISSUED" -or $snapshot.exit_authorization_status -eq "CONSUMED")

        if ($ready) {
            return $snapshot
        }

        Write-Host ("Waiting: attempt={0}; providerSession={1}; confirmations={2}; exitAuth={3}/{4}" -f `
            $snapshot.payment_attempt_status,
            $snapshot.provider_session_status,
            $snapshot.payment_confirmation_count,
            $snapshot.exit_authorization_count,
            $snapshot.exit_authorization_status)
        Start-Sleep -Seconds 10
    } while ((Get-Date) -lt $deadline)

    Fail "Timed out waiting for PayMongo webhook finality. Complete the checkout and make sure the public webhook reaches Payment Orchestrator."
}

function Invoke-GateConsume {
    param(
        [string] $ExitAuthorizationId,
        [string] $RequestedByUserId
    )

    $headers = @{
        "X-Correlation-Id" = $correlationId
    }

    $body = @{
        requestedByUserId = $RequestedByUserId
    }

    return Invoke-JsonPost -Url "$centralPmsBaseUrl/v1/gate/authorizations/$ExitAuthorizationId/consume" -Body $body -Headers $headers
}

function Invoke-GateDuplicateConsume {
    param(
        [string] $ExitAuthorizationId,
        [string] $RequestedByUserId
    )

    $headers = @{
        "X-Correlation-Id" = $correlationId
    }
    $json = @{ requestedByUserId = $RequestedByUserId } | ConvertTo-Json
    $url = "$centralPmsBaseUrl/v1/gate/authorizations/$ExitAuthorizationId/consume"

    try {
        $response = Invoke-WebRequest -Method Post -Uri $url -Body $json -ContentType "application/json" -Headers $headers -TimeoutSec 60
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $response.Content
        }
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response) {
            throw
        }

        $stream = $response.GetResponseStream()
        $reader = New-Object System.IO.StreamReader($stream)
        return [pscustomobject]@{
            StatusCode = [int]$response.StatusCode
            Body = $reader.ReadToEnd()
        }
    }
}

function Complete-GateValidation {
    param(
        [string] $Ticket,
        [object] $Snapshot
    )

    $requestedByUserId = Get-ServiceIdentityId
    $action = Get-GateValidationAction -Snapshot $Snapshot

    if ($action -eq "ConsumeThenVerifyDuplicate") {
        Write-Step "Consuming exit authorization at gate boundary"
        $consume = Invoke-GateConsume -ExitAuthorizationId $Snapshot.exit_authorization_id -RequestedByUserId $requestedByUserId
        $summary.GateConsumeStatus = $consume.authorizationStatus
        $summary.ExitAuthorizationStatus = $consume.authorizationStatus
        Write-Host ("Gate consume result: {0}" -f ($consume | ConvertTo-Json -Compress))
    }
    else {
        Write-Step "Exit authorization already consumed; verifying duplicate gate consume conflict"
        $summary.GateConsumeStatus = "CONSUMED"
        $summary.ExitAuthorizationStatus = "CONSUMED"
    }

    Write-Step "Verifying duplicate gate consume conflict"
    $duplicate = Invoke-GateDuplicateConsume -ExitAuthorizationId $Snapshot.exit_authorization_id -RequestedByUserId $requestedByUserId
    $summary.DuplicateConsumeBehavior = "HTTP $($duplicate.StatusCode)"
    Write-Host ("Duplicate consume result: HTTP {0} {1}" -f $duplicate.StatusCode, $duplicate.Body)

    if ([int]$duplicate.StatusCode -ne 409) {
        Fail "Expected duplicate consume to return HTTP 409 clean conflict, got $($duplicate.StatusCode)."
    }

    $finalSnapshot = Get-FinalitySnapshot -Ticket $Ticket
    Update-SummaryFromSnapshot -Snapshot $finalSnapshot
    Assert-FinalState -Snapshot $finalSnapshot -DuplicateResult $duplicate

    return $finalSnapshot
}

function Assert-FinalState {
    param(
        [object] $Snapshot,
        [object] $DuplicateResult
    )

    $duplicateSuccessRows = if ($null -eq $Snapshot.duplicate_success_rows) { 0 } else { [int]$Snapshot.duplicate_success_rows }

    if ($summary.Provider -ne "PAYMONGO") { Fail "Expected provider PAYMONGO, got $($summary.Provider)." }
    if ($summary.PaymentAttemptStatus -ne "CONFIRMED") { Fail "Expected payment attempt CONFIRMED, got $($summary.PaymentAttemptStatus)." }
    if ($summary.ProviderSessionStatus -ne "PAID") { Fail "Expected provider session PAID, got $($summary.ProviderSessionStatus)." }
    if ([int]$summary.PaymentConfirmationCount -ne 1) { Fail "Expected payment confirmation count 1, got $($summary.PaymentConfirmationCount)." }
    if ([string]::IsNullOrWhiteSpace($summary.ExitAuthorizationId)) { Fail "Expected ExitAuthorization id." }
    if ($summary.ExitAuthorizationStatus -notin @("ISSUED", "CONSUMED")) { Fail "Expected final ExitAuthorization status ISSUED or CONSUMED, got $($summary.ExitAuthorizationStatus)." }
    if ($summary.GateConsumeStatus -ne "CONSUMED") { Fail "Expected gate consume status CONSUMED, got $($summary.GateConsumeStatus)." }
    if ([int]$DuplicateResult.StatusCode -ne 409) { Fail "Expected duplicate consume HTTP 409, got $($DuplicateResult.StatusCode)." }
    if ($duplicateSuccessRows -ne 0) { Fail "Duplicate-success diagnostics returned $duplicateSuccessRows rows." }
}

if ($ScriptSelfTest) {
    try {
        Invoke-ScriptSelfTest
        exit 0
    }
    catch {
        Write-Host "Script self-tests failed: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

try {
    Write-Step "Validating local paths and parameters"
    if ($PaymentMethod.Trim().ToUpperInvariant() -ne "QRPH") {
        throw "This script is only for WebPay QRPH/PHP PayMongo validation. PaymentMethod must be QRPH."
    }

    Assert-PathExists -Path $DockerComposePath -Description "Docker Compose path"
    Assert-PathExists -Path (Join-Path $DockerComposePath "docker-compose.yml") -Description "Docker Compose file"
    Assert-PathExists -Path (Join-Path $repoRoot "scripts\dev-data") -Description "dev-data directory"

    if (-not [string]::IsNullOrWhiteSpace($NgrokBaseUrl) -and [string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
        $PublicBaseUrl = $NgrokBaseUrl
    }

    if (-not [string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
        Write-Host "Public base URL supplied: $PublicBaseUrl"
        Write-Host "Note: current Payment Orchestrator code uses configured provider/webhook settings; this script does not write configuration."
    }

    Write-Step "Checking Docker services"
    [void](Invoke-DockerCompose -Arguments @("ps", "postgres"))

    Invoke-SchemaInspection

    if (-not $DiagnosticsOnly) {
        Invoke-SeedIfPresent
    }

    if ([string]::IsNullOrWhiteSpace($TicketReference)) {
        $candidate = Find-NextFreshTicket
        $TicketReference = $candidate.vendor_session_ref
    }

    $summary.TicketReference = $TicketReference
    $context = Get-TicketContext -Ticket $TicketReference

    if ($DiagnosticsOnly) {
        Write-Step "Diagnostics only"
        $snapshot = Get-FinalitySnapshot -Ticket $TicketReference
        Update-SummaryFromSnapshot -Snapshot $snapshot
        Print-Summary
        exit 0
    }

    $resolved = Resolve-WebPaySession -Context $context
    Write-Host ("Resolved parkingSessionId={0}; paymentStatus={1}; parkingStatus={2}" -f `
        $resolved.parkingSessionId,
        $resolved.paymentStatus,
        $resolved.parkingStatus)

    if ((Get-ResolvedPaymentAction -PaymentStatus $resolved.paymentStatus) -eq "SkipPaymentIntent") {
        Write-Host "Payment is already final; skipping WebPay payment intent creation." -ForegroundColor Yellow

        Write-Step "Loading diagnostics for already-final paid session"
        $finality = Get-FinalitySnapshot -Ticket $TicketReference
        Assert-PaymentFinalitySnapshot -Snapshot $finality

        [void](Complete-GateValidation -Ticket $TicketReference -Snapshot $finality)

        $diag = Join-Path $PSScriptRoot ("webpay-{0}-gate-consume-diagnostics.sql" -f (Get-CurrentTicketStamp))
        if (Test-Path $diag) {
            Write-Step "Running gate consume diagnostics"
            Invoke-PsqlFile -Path $diag -Ticket $TicketReference
        }

        Write-Host ""
        Write-Host "FULL E2E PASS" -ForegroundColor Green
        Print-Summary
        exit 0
    }

    $intent = New-WebPayPaymentIntent -Context $context
    $summary.Provider = $intent.selectedProviderCode

    if ($intent.selectedProviderCode -ne "PAYMONGO") {
        Fail "QRPH/PHP routing regression: selectedProviderCode was $($intent.selectedProviderCode)."
    }

    if (-not [string]::IsNullOrWhiteSpace($intent.fallbackProviderCode)) {
        Fail "QRPH/PHP fallback provider must be null/disabled, got $($intent.fallbackProviderCode)."
    }

    $checkoutUrl = $intent.handoff.handoffUrl
    if ([string]::IsNullOrWhiteSpace($checkoutUrl)) {
        Fail "PayMongo checkout URL was not returned."
    }

    Write-Host "PayMongo checkout URL:"
    Write-Host $checkoutUrl -ForegroundColor Yellow

    if ($SkipBrowserPayment) {
        Write-Host ""
        Write-Host "Manual action required: open the checkout URL, complete PayMongo payment, then rerun with -DiagnosticsOnly or rerun without -SkipBrowserPayment to continue polling." -ForegroundColor Yellow
        Print-Summary
        exit 2
    }

    Start-Process $checkoutUrl

    $finality = Wait-ForPaymentFinality -Ticket $TicketReference
    Update-SummaryFromSnapshot -Snapshot $finality

    [void](Complete-GateValidation -Ticket $TicketReference -Snapshot $finality)

    $diag = Join-Path $PSScriptRoot ("webpay-{0}-gate-consume-diagnostics.sql" -f (Get-CurrentTicketStamp))
    if (Test-Path $diag) {
        Write-Step "Running gate consume diagnostics"
        Invoke-PsqlFile -Path $diag -Ticket $TicketReference
    }

    Write-Host ""
    Write-Host "FULL E2E PASS" -ForegroundColor Green
    Print-Summary
    exit 0
}
catch {
    Fail $_.Exception.Message
}
