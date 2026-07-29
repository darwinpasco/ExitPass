<#
.SYNOPSIS
Preflights the local Operator Console statutory discount UAT smoke fixture.

.DESCRIPTION
This sandbox-only helper seeds and verifies the E2E-231-SESSION-001 fixture in
the expected local Central PMS database, then calls the live Central PMS
Operator Console session lookup endpoint. It fails loudly unless the running API
can see the fixture with the expected payable amount.
#>

[CmdletBinding()]
param(
    [string] $ApiBaseUrl = "http://localhost:56065",
    [string] $PostgresContainer = "exitpass-postgres",
    [string] $DbName = "exitpass_v12_dev",
    [string] $DbUser = "exitpass",
    [string] $TicketReference = "E2E-231-SESSION-001",
    [switch] $SkipSeed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$expectedSiteGroupId = "77000000-0000-0000-0000-000000000001"
$expectedSiteId = "77000000-0000-0000-0000-000000000002"
$expectedOperatorUserId = "77000000-0000-0000-0000-000000000010"
$expectedReviewerUserId = "77000000-0000-0000-0000-000000000012"
$expectedDeviceBindingId = "77000000-0000-0000-0000-000000000030"
$expectedShiftId = "77000000-0000-0000-0000-000000000050"
$expectedReviewerShiftId = "77000000-0000-0000-0000-000000000052"
$expectedParkingSessionId = "23100000-0000-0000-0000-000000000003"
$expectedTariffSnapshotId = "23100000-0000-0000-0000-000000000004"
$expectedAmountMinorUnits = 12500
$expectedCurrency = "PHP"
$requesterPermissions = "statutory-discounts.session.lookup,statutory-discounts.draft.view,statutory-discounts.draft.create,statutory-discounts.evidence.view,statutory-discounts.evidence.capture,statutory-discounts.policy.resolve,fiscal-issuance.status.read,ticket.lookup,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
$reviewerPermissions = "statutory-discounts.draft.view,statutory-discounts.evidence.view,statutory-discounts.review.queue.read,statutory-discounts.review.detail.read,statutory-discounts.decision.review,statutory-discounts.decision.approve,statutory-discounts.decision.reject,statutory-discounts.policy.resolve,fiscal-issuance.status.read,fiscal-issuance.void.command,operator-workflow-audit.view,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"

$seedSqlPath = Join-Path $PSScriptRoot "Seed-StatutoryDiscountPilotFixture.sql"
$verifySqlPath = Join-Path $PSScriptRoot "Verify-StatutoryDiscountPilotFixture.sql"
$managementPlatformScriptRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "management-platform"
$managementPlatformSeedSqlPath = Join-Path $managementPlatformScriptRoot "Seed-ManagementPlatformUatIdentityRbac.sql"
$managementPlatformVerifySqlPath = Join-Path $managementPlatformScriptRoot "Verify-ManagementPlatformUatIdentityRbac.sql"

function Invoke-PsqlText {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Sql
    )

    $output = $Sql | & docker exec -i $PostgresContainer psql -v ON_ERROR_STOP=1 -U $DbUser -d $DbName 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed for database '$DbName': $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "SQL file not found: $Path"
    }

    Write-Host "Running SQL: $Path"
    $sql = Get-Content -LiteralPath $Path -Raw
    [void](Invoke-PsqlText -Sql $sql)
}

function Write-StartupCommand {
    Write-Host ""
    Write-Host "Start Central PMS with this local UAT smoke database:"
    Write-Host "cd D:\SourceCodes\ExitPass"
    Write-Host '$env:ASPNETCORE_ENVIRONMENT="Development"'
    Write-Host '$env:ASPNETCORE_URLS="http://localhost:56065"'
    Write-Host '$env:ConnectionStrings__MainDatabase="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"'
    Write-Host '$env:ConnectionStrings__PosServer="Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__PosServerBaseUrl="http://localhost:5000"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__TimeoutSeconds="10"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall="true"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnableControlledUatDiagnosticPath="true"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow="false"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow="false"'
    Write-Host 'dotnet run --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-build'
    Write-Host ""
}

if ($DbName -ne "exitpass_v12_dev") {
    throw "This preflight is pinned to local DB exitpass_v12_dev. Received: $DbName"
}

Write-Host "ExitPass statutory discount pilot preflight"
Write-Host "Expected Central PMS DB: $DbName"
Write-Host "Ticket/reference: $TicketReference"

if (-not $SkipSeed) {
    Invoke-PsqlFile -Path $seedSqlPath
    Invoke-PsqlFile -Path $managementPlatformSeedSqlPath
}

Invoke-PsqlFile -Path $verifySqlPath
Invoke-PsqlFile -Path $managementPlatformVerifySqlPath

$fixtureSql = @"
COPY (
    WITH fixture AS (
        SELECT
            '$expectedParkingSessionId'::uuid AS parking_session_id,
            '$expectedTariffSnapshotId'::uuid AS tariff_snapshot_id,
            '$expectedSiteGroupId'::uuid AS site_group_id,
            '$expectedSiteId'::uuid AS site_id,
            '$TicketReference'::text AS ticket_reference
    ),
    unsafe_counts AS (
        SELECT
            (SELECT COUNT(*) FROM core.payment_attempts pa, fixture f WHERE pa.parking_session_id = f.parking_session_id) AS payment_attempt_count,
            (SELECT COUNT(*)
               FROM core.payment_confirmations pc
               JOIN core.payment_attempts pa ON pa.payment_attempt_id = pc.payment_attempt_id
               CROSS JOIN fixture f
              WHERE pa.parking_session_id = f.parking_session_id) AS payment_confirmation_count,
            (SELECT COUNT(*)
               FROM payments.provider_sessions ps
               JOIN core.payment_attempts pa ON pa.payment_attempt_id = ps.payment_attempt_id
               CROSS JOIN fixture f
              WHERE pa.parking_session_id = f.parking_session_id) AS provider_session_count,
            (SELECT COUNT(*)
               FROM payments.provider_outcomes po
               JOIN core.payment_attempts pa ON pa.payment_attempt_id = po.payment_attempt_id
               CROSS JOIN fixture f
              WHERE pa.parking_session_id = f.parking_session_id) AS provider_outcome_count,
            (SELECT COUNT(*) FROM core.exit_authorizations ea, fixture f WHERE ea.parking_session_id = f.parking_session_id) AS exit_authorization_count,
            (SELECT COUNT(*)
               FROM gates.gate_authorization_consumptions gac
               JOIN core.exit_authorizations ea ON ea.exit_authorization_id = gac.exit_authorization_id
               CROSS JOIN fixture f
              WHERE ea.parking_session_id = f.parking_session_id) AS gate_consumption_count,
            (SELECT COUNT(*)
               FROM gates.gate_events ge
               LEFT JOIN core.exit_authorizations ea ON ea.exit_authorization_id = ge.exit_authorization_id
               LEFT JOIN gates.gate_authorization_consumptions gac ON gac.gate_authorization_consumption_id = ge.gate_authorization_consumption_id
               LEFT JOIN core.exit_authorizations gcea ON gcea.exit_authorization_id = gac.exit_authorization_id
               CROSS JOIN fixture f
              WHERE ea.parking_session_id = f.parking_session_id
                 OR gcea.parking_session_id = f.parking_session_id) AS gate_event_count,
            (SELECT COUNT(*) FROM coupons.coupon_applications ca, fixture f WHERE ca.parking_session_id = f.parking_session_id) AS coupon_application_count
    )
    SELECT
        current_database() AS database_name,
        ps.parking_session_id::text,
        ps.vendor_session_ref,
        ps.ticket_number_masked,
        ps.site_group_id::text,
        ps.site_id::text,
        ps.session_status::text AS session_status,
        ts.tariff_snapshot_id::text,
        ROUND((ts.net_amount * 100)::numeric, 0)::bigint AS current_payable_amount_minor_units,
        ts.currency_code,
        ts.snapshot_status::text AS tariff_snapshot_status,
        uc.payment_attempt_count,
        uc.payment_confirmation_count,
        (
            uc.provider_session_count
          + uc.provider_outcome_count
          + uc.exit_authorization_count
          + uc.gate_consumption_count
          + uc.gate_event_count
          + uc.coupon_application_count
        ) AS unsafe_side_effect_count
    FROM fixture f
    LEFT JOIN core.parking_sessions ps ON ps.parking_session_id = f.parking_session_id
    LEFT JOIN core.tariff_snapshots ts ON ts.tariff_snapshot_id = f.tariff_snapshot_id
    CROSS JOIN unsafe_counts uc
) TO STDOUT WITH CSV HEADER
"@

$fixtureRows = @(Invoke-PsqlText -Sql $fixtureSql | ConvertFrom-Csv)
if ($fixtureRows.Count -ne 1) {
    throw "Expected one fixture verification row but got $($fixtureRows.Count)."
}

$fixture = $fixtureRows[0]
if ($fixture.database_name -ne $DbName) {
    throw "Verified unexpected database '$($fixture.database_name)'. Expected '$DbName'."
}
if ($fixture.vendor_session_ref -ne $TicketReference -or $fixture.ticket_number_masked -ne $TicketReference) {
    throw "Fixture ticket reference mismatch. Expected '$TicketReference'."
}
if ($fixture.site_group_id -ne $expectedSiteGroupId -or $fixture.site_id -ne $expectedSiteId) {
    throw "Fixture site scope mismatch. Expected siteGroupId=$expectedSiteGroupId siteId=$expectedSiteId."
}
if ($fixture.session_status -ne "ACTIVE" -or $fixture.tariff_snapshot_status -ne "ACTIVE") {
    throw "Fixture session/tariff is not active. session=$($fixture.session_status), tariff=$($fixture.tariff_snapshot_status)"
}
if ([int64]$fixture.current_payable_amount_minor_units -ne $expectedAmountMinorUnits) {
    throw "Fixture payable amount mismatch. Expected $expectedAmountMinorUnits but got $($fixture.current_payable_amount_minor_units)."
}
if ($fixture.currency_code.Trim() -ne $expectedCurrency) {
    throw "Fixture currency mismatch. Expected $expectedCurrency but got $($fixture.currency_code)."
}
if ([int]$fixture.payment_attempt_count -ne 0 -or [int]$fixture.payment_confirmation_count -ne 0 -or [int]$fixture.unsafe_side_effect_count -ne 0) {
    throw "Fixture is not clean. paymentAttempts=$($fixture.payment_attempt_count), confirmations=$($fixture.payment_confirmation_count), unsafeSideEffects=$($fixture.unsafe_side_effect_count)"
}

Write-Host "DB fixture verified in $DbName."
Write-Host "Parking session: $expectedParkingSessionId"
Write-Host "Payable amount: $expectedAmountMinorUnits $expectedCurrency minor units"

$correlationId = [Guid]::NewGuid().ToString()
$lookupBody = @{
    userId = $expectedOperatorUserId
    operatorDeviceBindingId = $expectedDeviceBindingId
    siteId = $expectedSiteId
    siteGroupId = $expectedSiteGroupId
    operatorShiftId = $expectedShiftId
    parkingSessionId = $null
    ticketReference = $TicketReference
    plateNumber = $null
    lookupMode = "TICKET_REFERENCE"
    idempotencyKey = "operator-console-statutory-discount-pilot-preflight:${TicketReference}:$correlationId"
    correlationId = $correlationId
} | ConvertTo-Json -Depth 8

$headers = @{
    "Content-Type" = "application/json"
    "X-Correlation-Id" = $correlationId
    "X-Operator-User-Id" = $expectedOperatorUserId
    "X-ExitPass-User-Id" = $expectedOperatorUserId
    "X-ExitPass-Permissions" = $requesterPermissions
    "X-Operator-Device-Binding-Id" = $expectedDeviceBindingId
    "X-Operator-Shift-Id" = $expectedShiftId
    "X-Site-Id" = $expectedSiteId
    "X-Site-Group-Id" = $expectedSiteGroupId
}

$lookupUri = "$($ApiBaseUrl.TrimEnd('/'))/v1/ops/operator-console/sessions/lookup"
Write-Host "Calling live Central PMS endpoint: POST $lookupUri"

try {
    $lookup = Invoke-RestMethod -Uri $lookupUri -Method Post -Headers $headers -Body $lookupBody -TimeoutSec 20
}
catch {
    Write-StartupCommand
    throw "Central PMS session lookup endpoint could not be called. Start/restart Central PMS with the command above. $($_.Exception.Message)"
}

if ($lookup.sessionFound -ne $true -or
    $lookup.sessionEligible -ne $true -or
    [int64]$lookup.currentPayableAmountMinorUnits -ne $expectedAmountMinorUnits -or
    $lookup.ticketReference -ne $TicketReference) {
    Write-Host "Live endpoint response:"
    $lookup | ConvertTo-Json -Depth 8 | Write-Host
    Write-StartupCommand
    throw "Preflight failed: live API did not return the seeded fixture. The running Central PMS API is likely connected to the wrong DB or using stale site context."
}

Write-Host "Live endpoint verified: sessionFound=true, sessionEligible=true, currentPayableAmountMinorUnits=$expectedAmountMinorUnits."
Write-Host ""
Write-Host "Preflight PASSED."
Write-Host "Browser URL: http://localhost:5175/operator-console/ticket-lookup"
Write-Host "Ticket number: $TicketReference"
Write-Host ""
Write-Host "Requester context for lookup/draft/evidence/apply:"
Write-Host "  VITE_OPERATOR_CONSOLE_USER_ID=$expectedOperatorUserId"
Write-Host "  VITE_OPERATOR_CONSOLE_SHIFT_ID=$expectedShiftId"
Write-Host "  VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID=$expectedDeviceBindingId"
Write-Host "  X-ExitPass-Permissions=$requesterPermissions"
Write-Host ""
Write-Host "Reviewer context for approve/reject under requester-vs-approver segregation:"
Write-Host "  VITE_OPERATOR_CONSOLE_USER_ID=$expectedReviewerUserId"
Write-Host "  VITE_OPERATOR_CONSOLE_SHIFT_ID=$expectedReviewerShiftId"
Write-Host "  VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID=$expectedDeviceBindingId"
Write-Host "  X-ExitPass-Permissions=$reviewerPermissions"
