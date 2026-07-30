param(
    [string]$DatabaseName = "exitpass_webpay_local_walkthrough",
    [string]$PostgresContainerName = "exitpass-postgres",
    [string]$DatabaseUser = "exitpass",
    [string]$PaymentOrchestratorUrl = "http://localhost:8082",
    [string]$WebPayUrl = "http://127.0.0.1:5173",
    [string]$MockPaymentProviderUrl = "http://localhost:8084",
    [switch]$ParkingSessionProbeOnly,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Assert-SafeDatabaseName {
    param([string]$Name)

    if ($Name -notmatch '^exitpass_webpay_local_walkthrough(_[a-z0-9_]+)?$') {
        throw "Refusing to use database '$Name'. Use exitpass_webpay_local_walkthrough or a suffixed disposable variant."
    }
}

function Invoke-JsonPost {
    param(
        [string]$Url,
        [object]$Body,
        [hashtable]$Headers = @{}
    )

    $json = $Body | ConvertTo-Json -Depth 8
    try {
        Invoke-WebRequest -Uri $Url -Method POST -Body $json -ContentType "application/json" -Headers $Headers -UseBasicParsing -TimeoutSec 30
    }
    catch {
        $status = $null
        if ($_.Exception.Response) {
            $status = $_.Exception.Response.StatusCode.value__
        }

        $safeBody = $_.ErrorDetails.Message
        if ($safeBody -and $safeBody.Length -gt 300) {
            $safeBody = $safeBody.Substring(0, 300)
        }

        throw "POST $Url failed with HTTP $status. Safe response: $safeBody"
    }
}

function Invoke-ScalarSql {
    param([string]$Sql)

    $result = docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -t -A -U $DatabaseUser -d $DatabaseName -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql command failed for database $DatabaseName."
    }

    return ($result | Where-Object { $_ -and $_.Trim() } | Select-Object -First 1).Trim()
}

function Invoke-QueryText {
    param([string]$Sql)

    $result = docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -t -A -U $DatabaseUser -d $DatabaseName -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql command failed for database $DatabaseName."
    }

    return (($result | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
}

function Get-FixtureContext {
    $sql = @"
WITH
sg AS (
    SELECT *
    FROM sites.site_groups
    WHERE site_group_code = 'WEBPAY_LOCAL_GROUP'
),
s AS (
    SELECT s.*
    FROM sites.sites s
    INNER JOIN sg ON sg.site_group_id = s.site_group_id
    WHERE s.site_code = 'WEBPAY_LOCAL_SITE'
),
vs AS (
    SELECT *
    FROM integration.vendor_systems
    WHERE vendor_code = 'WEBPAY_LOCAL_MOCK_PMS'
      AND environment_code = 'LOCAL'
),
ps AS (
    SELECT ps.*
    FROM core.parking_sessions ps
    INNER JOIN sg ON sg.site_group_id = ps.site_group_id
    INNER JOIN s ON s.site_id = ps.site_id
    INNER JOIN vs ON vs.vendor_system_id = ps.vendor_system_id
    WHERE ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND ps.plate_number_masked = 'LOCALPAY001'
),
ts AS (
    SELECT ts.*
    FROM core.tariff_snapshots ts
    INNER JOIN ps ON ps.parking_session_id = ts.parking_session_id
    WHERE ts.snapshot_status = 'ACTIVE'
      AND ts.currency_code = 'PHP'
      AND (ts.net_amount * 100)::bigint = 13750
)
SELECT json_build_object(
    'siteGroupCount', (SELECT COUNT(*) FROM sg),
    'siteCount', (SELECT COUNT(*) FROM s),
    'vendorSystemCount', (SELECT COUNT(*) FROM vs),
    'parkingSessionCount', (SELECT COUNT(*) FROM ps),
    'tariffSnapshotCount', (SELECT COUNT(*) FROM ts),
    'siteGroupId', (SELECT site_group_id FROM sg LIMIT 1),
    'siteId', (SELECT site_id FROM s LIMIT 1),
    'vendorSystemId', (SELECT vendor_system_id FROM vs LIMIT 1),
    'parkingSessionId', (SELECT parking_session_id FROM ps LIMIT 1),
    'tariffSnapshotId', (SELECT tariff_snapshot_id FROM ts ORDER BY calculated_at DESC, created_at DESC LIMIT 1),
    'ticketReference', 'WEBPAY-LOCAL-ORDINARY-001',
    'plateNumber', 'LOCALPAY001',
    'amountMinorUnits', COALESCE((SELECT (net_amount * 100)::bigint FROM ts ORDER BY calculated_at DESC, created_at DESC LIMIT 1), 0),
    'currency', COALESCE((SELECT currency_code FROM ts ORDER BY calculated_at DESC, created_at DESC LIMIT 1), ''),
    'vendorSystemActive', COALESCE((SELECT vendor_system_status::text = 'ACTIVE' FROM vs LIMIT 1), false),
    'siteRelationshipValid', COALESCE((
        SELECT ps.site_group_id = sg.site_group_id AND ps.site_id = s.site_id AND ps.vendor_system_id = vs.vendor_system_id
        FROM ps
        CROSS JOIN sg
        CROSS JOIN s
        CROSS JOIN vs
        LIMIT 1
    ), false),
    'statutoryDecisionCount', COALESCE((SELECT COUNT(*) FROM discounts.statutory_discount_decision_commands d INNER JOIN ps ON ps.parking_session_id = d.parking_session_id), 0),
    'statutoryApplicationCount', COALESCE((SELECT COUNT(*) FROM discounts.statutory_discount_payable_basis_application_commands a INNER JOIN ps ON ps.parking_session_id = a.parking_session_id), 0)
)::text;
"@

    $context = (Invoke-QueryText -Sql $sql) | ConvertFrom-Json
    $failures = New-Object System.Collections.Generic.List[string]
    if ([int]$context.siteGroupCount -ne 1) { $failures.Add("site group count $($context.siteGroupCount)") }
    if ([int]$context.siteCount -ne 1) { $failures.Add("site count $($context.siteCount)") }
    if ([int]$context.vendorSystemCount -ne 1) { $failures.Add("vendor system count $($context.vendorSystemCount)") }
    if ([int]$context.parkingSessionCount -ne 1) { $failures.Add("parking session count $($context.parkingSessionCount)") }
    if ([int]$context.tariffSnapshotCount -ne 1) { $failures.Add("tariff snapshot count $($context.tariffSnapshotCount)") }
    if (-not [bool]$context.vendorSystemActive) { $failures.Add("vendor system inactive") }
    if (-not [bool]$context.siteRelationshipValid) { $failures.Add("fixture relationships invalid") }
    if ([int64]$context.amountMinorUnits -ne 13750) { $failures.Add("amount $($context.amountMinorUnits)") }
    if ([string]$context.currency -ne "PHP") { $failures.Add("currency $($context.currency)") }
    if ([int]$context.statutoryDecisionCount -ne 0) { $failures.Add("statutory decision rows $($context.statutoryDecisionCount)") }
    if ([int]$context.statutoryApplicationCount -ne 0) { $failures.Add("statutory application rows $($context.statutoryApplicationCount)") }
    foreach ($propertyName in @("siteGroupId", "siteId", "vendorSystemId", "parkingSessionId", "tariffSnapshotId")) {
        if ([string]::IsNullOrWhiteSpace([string]$context.$propertyName)) {
            $failures.Add("missing $propertyName")
        }
    }

    if ($failures.Count -gt 0) {
        throw "Fixture discovery failed: $($failures -join '; ')."
    }

    return $context
}

function Assert-Equal {
    param(
        [object]$Actual,
        [object]$Expected,
        [string]$Message
    )

    if ("$Actual" -ne "$Expected") {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Get-MockCheckoutSessionRequestCount {
    $journal = Invoke-WebRequest -Uri $mockRequestJournalUrl -UseBasicParsing -TimeoutSec 10
    $payload = $journal.Content | ConvertFrom-Json
    $matches = @($payload.requests | Where-Object { $_.request.url -eq "/v1/checkout_sessions" -and $_.request.method -eq "POST" })
    return $matches.Count
}

function Assert-ResolvedParkingSession {
    param(
        [object]$Resolved,
        [object]$Context,
        [string]$LookupKind
    )

    Assert-Equal -Actual $Resolved.parkingSessionId -Expected $Context.parkingSessionId -Message "$LookupKind resolved parking session mismatch."
    Assert-Equal -Actual $Resolved.ticketReference -Expected $Context.ticketReference -Message "$LookupKind resolved ticket reference mismatch."
    Assert-Equal -Actual $Resolved.plateNumber -Expected $Context.plateNumber -Message "$LookupKind resolved plate mismatch."
    Assert-Equal -Actual $Resolved.amountMinorUnits -Expected 13750 -Message "$LookupKind resolved amount mismatch."
    Assert-Equal -Actual $Resolved.currency -Expected "PHP" -Message "$LookupKind resolved currency mismatch."
    Assert-Equal -Actual $Resolved.tariffSnapshotId -Expected $Context.tariffSnapshotId -Message "$LookupKind resolved tariff snapshot mismatch."
    if (-not $Resolved.correlationId) {
        throw "$LookupKind response did not include a correlation ID."
    }
}

Assert-SafeDatabaseName -Name $DatabaseName

$routeBaseUrl = $WebPayUrl.TrimEnd("/")
$resolveUrl = "$routeBaseUrl/v1/webpay/parking-session"
$paymentIntentUrl = "$routeBaseUrl/v1/webpay/payment-intents"
$mockRequestJournalUrl = "$MockPaymentProviderUrl/__admin/requests"
$correlationId = "24100000-0000-0000-0000-000000000099"

Write-Host "Testing WebPay ordinary-payment local handoff through real local routes..." -ForegroundColor Cyan
Write-Host "Payment Orchestrator: $PaymentOrchestratorUrl"
Write-Host "Browser-facing route base: $routeBaseUrl"
Write-Host "Mock payment provider: $MockPaymentProviderUrl"
Write-Host "Disposable database: $DatabaseName"

if ($DryRun) {
    Write-Host "DRY RUN: would POST $resolveUrl"
    Write-Host "DRY RUN: would discover Site Group/Site/Vendor IDs from WEBPAY_LOCAL_GROUP, WEBPAY_LOCAL_SITE, WEBPAY_LOCAL_MOCK_PMS/LOCAL"
    Write-Host "DRY RUN: would assert ticket WEBPAY-LOCAL-ORDINARY-001 and plate LOCALPAY001 resolve to PHP 137.50"
    Write-Host "DRY RUN: would POST $paymentIntentUrl"
    Write-Host "DRY RUN: would inspect $mockRequestJournalUrl"
    Write-Host "DRY RUN: would verify payment_attempts and provider_sessions counts in $DatabaseName"
    return
}

$context = Get-FixtureContext
Write-Host "Discovered Site Group ID: $($context.siteGroupId)" -ForegroundColor Green
Write-Host "Discovered Site ID: $($context.siteId)" -ForegroundColor Green
Write-Host "Discovered Vendor System ID: $($context.vendorSystemId)" -ForegroundColor Green
Write-Host "Discovered Parking Session ID: $($context.parkingSessionId)" -ForegroundColor Green
Write-Host "Discovered Tariff Snapshot ID: $($context.tariffSnapshotId)" -ForegroundColor Green

$headers = @{ "X-Correlation-Id" = $correlationId }
$resolveBody = @{
    siteGroupId = $context.siteGroupId
    siteId = $context.siteId
    vendorSystemId = $context.vendorSystemId
    ticketReference = $context.ticketReference
    correlationId = $correlationId
}

$resolveResponse = Invoke-JsonPost -Url $resolveUrl -Body $resolveBody -Headers $headers
Assert-Equal -Actual $resolveResponse.StatusCode -Expected 200 -Message "Parking-session resolve failed."
$resolved = $resolveResponse.Content | ConvertFrom-Json
Assert-ResolvedParkingSession -Resolved $resolved -Context $context -LookupKind "Ticket lookup"

$plateResolveBody = @{
    siteGroupId = $context.siteGroupId
    siteId = $context.siteId
    vendorSystemId = $context.vendorSystemId
    plateNumber = $context.plateNumber
    correlationId = "24100000-0000-0000-0000-000000000098"
}
$plateResolveResponse = Invoke-JsonPost -Url $resolveUrl -Body $plateResolveBody -Headers @{ "X-Correlation-Id" = "24100000-0000-0000-0000-000000000098" }
Assert-Equal -Actual $plateResolveResponse.StatusCode -Expected 200 -Message "Plate parking-session resolve failed."
$plateResolved = $plateResolveResponse.Content | ConvertFrom-Json
Assert-ResolvedParkingSession -Resolved $plateResolved -Context $context -LookupKind "Plate lookup"

if ($ParkingSessionProbeOnly) {
    Write-Host "Ticket parking-session probe: HTTP 200, PHP 137.50, correlation $($resolved.correlationId)" -ForegroundColor Green
    Write-Host "Plate parking-session probe: HTTP 200, PHP 137.50, correlation $($plateResolved.correlationId)" -ForegroundColor Green
    Write-Host "No statutory decision/application rows are linked to the ordinary fixture." -ForegroundColor Green
    return
}

$paymentBody = @{
    siteGroupId = $context.siteGroupId
    siteId = $context.siteId
    vendorSystemId = $context.vendorSystemId
    ticketReference = $context.ticketReference
    plateNumber = $context.plateNumber
    paymentMethod = "QRPH"
    tariffSnapshotId = $context.tariffSnapshotId
    expectedAmountMinorUnits = 13750
    expectedCurrency = "PHP"
    correlationId = $correlationId
}

$attemptCountBefore = [int](Invoke-ScalarSql -Sql "SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = '$($context.parkingSessionId)'::uuid;")
$providerSessionCountBefore = [int](Invoke-ScalarSql -Sql "SELECT COUNT(*) FROM payments.provider_sessions ps JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id WHERE pa.parking_session_id = '$($context.parkingSessionId)'::uuid;")
$mockCheckoutRequestCountBefore = Get-MockCheckoutSessionRequestCount

$paymentResponse = Invoke-JsonPost -Url $paymentIntentUrl -Body $paymentBody -Headers $headers
Assert-Equal -Actual $paymentResponse.StatusCode -Expected 200 -Message "Payment intent handoff failed."
$payment = $paymentResponse.Content | ConvertFrom-Json
if (-not $payment.paymentAttemptId) {
    throw "Payment response did not include paymentAttemptId."
}
if (-not $payment.handoff -or -not $payment.handoff.handoffUrl) {
    throw "Payment response did not include a provider handoff URL."
}

$replaySucceeded = $false
try {
    $replayRequestCountBefore = Get-MockCheckoutSessionRequestCount
    $replayResponse = Invoke-JsonPost -Url $paymentIntentUrl -Body $paymentBody -Headers $headers
    Assert-Equal -Actual $replayResponse.StatusCode -Expected 200 -Message "Payment intent replay failed."
    $replay = $replayResponse.Content | ConvertFrom-Json
    Assert-Equal -Actual $replay.paymentAttemptId -Expected $payment.paymentAttemptId -Message "Replay did not reuse the same payment attempt."
    $replayRequestCountAfter = Get-MockCheckoutSessionRequestCount
    Assert-Equal -Actual $replayRequestCountAfter -Expected $replayRequestCountBefore -Message "Replay called the mock provider again."
    $replaySucceeded = $true
}
catch {
    $replayError = $_.Exception.Message
}

$attemptCountAfter = [int](Invoke-ScalarSql -Sql "SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = '$($context.parkingSessionId)'::uuid;")
$providerSessionCountAfter = [int](Invoke-ScalarSql -Sql "SELECT COUNT(*) FROM payments.provider_sessions ps JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id WHERE pa.parking_session_id = '$($context.parkingSessionId)'::uuid;")
$attemptAmount = Invoke-ScalarSql -Sql "SELECT (amount * 100)::bigint::text FROM core.payment_attempts WHERE parking_session_id = '$($context.parkingSessionId)'::uuid ORDER BY created_at DESC LIMIT 1;"
$attemptCurrency = Invoke-ScalarSql -Sql "SELECT currency_code FROM core.payment_attempts WHERE parking_session_id = '$($context.parkingSessionId)'::uuid ORDER BY created_at DESC LIMIT 1;"
$providerSessionRef = Invoke-ScalarSql -Sql "SELECT ps.provider_session_ref FROM payments.provider_sessions ps JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id WHERE pa.parking_session_id = '$($context.parkingSessionId)'::uuid ORDER BY ps.created_at DESC LIMIT 1;"
$mockCheckoutRequestCountAfter = Get-MockCheckoutSessionRequestCount

Assert-Equal -Actual $attemptCountAfter -Expected 1 -Message "Expected exactly one durable payment attempt after submit/replay."
Assert-Equal -Actual $providerSessionCountAfter -Expected 1 -Message "Expected exactly one provider handoff after submit/replay."
Assert-Equal -Actual $attemptAmount -Expected 13750 -Message "Payment attempt amount mismatch."
Assert-Equal -Actual $attemptCurrency -Expected "PHP" -Message "Payment attempt currency mismatch."
Assert-Equal -Actual $providerSessionRef -Expected "cs_test_exitpass_local" -Message "Unexpected mock provider session reference."

if ($attemptCountBefore -eq 0 -and $mockCheckoutRequestCountAfter -le $mockCheckoutRequestCountBefore) {
    throw "Mock provider request journal did not record a new /v1/checkout_sessions request for a fresh payment handoff."
}

if ($mockCheckoutRequestCountAfter -lt 1) {
    throw "Mock provider request journal does not show /v1/checkout_sessions."
}

if (-not $replaySucceeded) {
    throw "Payment handoff was created, but payment-intent replay failed after an existing provider session was present. Attempts=$attemptCountAfter ProviderSessions=$providerSessionCountAfter Error=$replayError"
}

Write-Host "Parking session resolved: $($context.ticketReference) / $($context.plateNumber) / PHP 137.50" -ForegroundColor Green
Write-Host "Payment attempt count before/after: $attemptCountBefore -> $attemptCountAfter" -ForegroundColor Green
Write-Host "Provider handoff count before/after: $providerSessionCountBefore -> $providerSessionCountAfter" -ForegroundColor Green
Write-Host "Mock checkout-session requests before/after: $mockCheckoutRequestCountBefore -> $mockCheckoutRequestCountAfter" -ForegroundColor Green
Write-Host "Replay provider calls: 0" -ForegroundColor Green
Write-Host "PaymentAttemptId: $($payment.paymentAttemptId)" -ForegroundColor Green
Write-Host "ProviderSessionRef: $providerSessionRef" -ForegroundColor Green
Write-Host "Provider handoff URL: $($payment.handoff.handoffUrl)" -ForegroundColor Green
Write-Host "WebPay local integration payment handoff proof passed." -ForegroundColor Green
