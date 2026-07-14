<#
.SYNOPSIS
Preflights the statutory discount Operator Console UAT smoke against canonical aligned DB output.

.DESCRIPTION
This local/UAT-only helper rebuilds centralpms_operator_uat_aligned_local from
exitpassdb_v1.2 generated SQL, runs canonical DB validation, seeds/verifies the
v1.3 Management Platform identity/RBAC role model, seeds/verifies the statutory
discount pilot fixture, and prints the exact runtime/browser steps.

Use -VerifyLiveApi after starting Central PMS against the aligned database to
prove the live session lookup endpoint can see E2E-231-SESSION-001.
#>

[CmdletBinding()]
param(
    [string] $DbRepoRoot = "D:\SourceCodes\exitpassdb_v1.2",
    [string] $PostgresContainer = "exitpass-postgres",
    [string] $AdminDatabase = "postgres",
    [string] $DbName = "centralpms_operator_uat_aligned_local",
    [string] $DbUser = "exitpass",
    [string] $ApiBaseUrl = "http://localhost:56065",
    [string] $TicketReference = "E2E-231-SESSION-001",
    [switch] $SkipRebuild,
    [switch] $SkipSeed,
    [switch] $VerifyLiveApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$expectedSiteGroupId = "77000000-0000-0000-0000-000000000001"
$expectedSiteId = "77000000-0000-0000-0000-000000000002"
$expectedRequesterUserId = "77000000-0000-0000-0000-000000000010"
$expectedReviewerUserId = "77000000-0000-0000-0000-000000000012"
$expectedDeviceBindingId = "77000000-0000-0000-0000-000000000030"
$expectedRequesterShiftId = "77000000-0000-0000-0000-000000000050"
$expectedReviewerShiftId = "77000000-0000-0000-0000-000000000052"
$expectedParkingSessionId = "23100000-0000-0000-0000-000000000003"
$expectedTariffSnapshotId = "23100000-0000-0000-0000-000000000004"
$expectedAmountMinorUnits = 12500
$expectedCurrency = "PHP"
$requesterPermissions = "statutory-discounts.session.lookup,statutory-discounts.draft.view,statutory-discounts.draft.create,statutory-discounts.evidence.view,statutory-discounts.evidence.capture,statutory-discounts.policy.resolve,fiscal-issuance.status.read,ticket.lookup,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
$reviewerPermissions = "statutory-discounts.draft.view,statutory-discounts.evidence.view,statutory-discounts.decision.review,statutory-discounts.decision.approve,statutory-discounts.decision.reject,statutory-discounts.payable-basis.apply,statutory-discounts.policy.resolve,fiscal-issuance.status.read,fiscal-issuance.void.command,operator-workflow-audit.view,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"

$canonicalSqlPath = Join-Path $DbRepoRoot "build\generated\exitpass-full-object.generated.sql"
$canonicalValidationSqlPath = Join-Path $DbRepoRoot "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$managementPlatformScriptRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "management-platform"
$managementPlatformSeedSqlPath = Join-Path $managementPlatformScriptRoot "Seed-ManagementPlatformUatIdentityRbac.sql"
$managementPlatformVerifySqlPath = Join-Path $managementPlatformScriptRoot "Verify-ManagementPlatformUatIdentityRbac.sql"
$statutorySeedSqlPath = Join-Path $PSScriptRoot "Seed-StatutoryDiscountPilotFixture.sql"
$statutoryVerifySqlPath = Join-Path $PSScriptRoot "Verify-StatutoryDiscountPilotFixture.sql"

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required file not found: $Path"
    }
}

function Invoke-PsqlText {
    param(
        [Parameter(Mandatory = $true)][string] $Database,
        [Parameter(Mandatory = $true)][string] $Sql
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = $Sql | & docker exec -i $PostgresContainer psql -v ON_ERROR_STOP=1 -U $DbUser -d $Database 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        throw "psql failed for database '$Database': $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory = $true)][string] $Database,
        [Parameter(Mandatory = $true)][string] $Path
    )

    Assert-FileExists -Path $Path
    Write-Host "Running SQL against ${Database}: $Path"
    $sql = Get-Content -LiteralPath $Path -Raw
    [void](Invoke-PsqlText -Database $Database -Sql $sql)
}

function Write-StartupCommands {
    Write-Host ""
    Write-Host "Central PMS startup:"
    Write-Host "cd D:\SourceCodes\ExitPass"
    Write-Host '$env:ASPNETCORE_ENVIRONMENT="Development"'
    Write-Host '$env:ASPNETCORE_URLS="http://localhost:56065"'
    Write-Host "`$env:ConnectionStrings__MainDatabase=`"Host=localhost;Port=5433;Database=$DbName;Username=exitpass;Password=change_me;Include Error Detail=true`""
    Write-Host '$env:ConnectionStrings__PosServer="Host=localhost;Port=5433;Database=posserver_api_smoke_validation_local;Username=exitpass;Password=change_me"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow="false"'
    Write-Host '$env:FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow="false"'
    Write-Host 'dotnet run --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-launch-profile'
    Write-Host ""
    Write-Host "Operator Console UI startup:"
    Write-Host "cd D:\SourceCodes\ExitPass"
    Write-Host '$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET="http://localhost:56065"'
    Write-Host 'npm.cmd --prefix src\Services\OperatorConsoleUi run dev -- --host localhost --port 5175'
}

Assert-FileExists -Path $canonicalSqlPath
Assert-FileExists -Path $canonicalValidationSqlPath
Assert-FileExists -Path $managementPlatformSeedSqlPath
Assert-FileExists -Path $managementPlatformVerifySqlPath
Assert-FileExists -Path $statutorySeedSqlPath
Assert-FileExists -Path $statutoryVerifySqlPath

Write-Host "ExitPass statutory discount Operator UAT aligned-DB preflight"
Write-Host "Canonical SQL: $canonicalSqlPath"
Write-Host "Canonical validation: $canonicalValidationSqlPath"
Write-Host "Disposable DB: $DbName"

if (-not $SkipRebuild) {
    Write-Host "Recreating disposable database $DbName from canonical generated SQL."
    [void](Invoke-PsqlText -Database $AdminDatabase -Sql "DROP DATABASE IF EXISTS $DbName WITH (FORCE);")
    [void](Invoke-PsqlText -Database $AdminDatabase -Sql "CREATE DATABASE $DbName OWNER $DbUser;")
    Invoke-PsqlFile -Database $DbName -Path $canonicalSqlPath
}

Invoke-PsqlFile -Database $DbName -Path $canonicalValidationSqlPath

if (-not $SkipSeed) {
    Invoke-PsqlFile -Database $DbName -Path $statutorySeedSqlPath
    Invoke-PsqlFile -Database $DbName -Path $managementPlatformSeedSqlPath
}

Invoke-PsqlFile -Database $DbName -Path $managementPlatformVerifySqlPath
Invoke-PsqlFile -Database $DbName -Path $statutoryVerifySqlPath

$summarySql = @"
COPY (
    WITH fixture AS (
        SELECT
            '$expectedParkingSessionId'::uuid AS parking_session_id,
            '$expectedTariffSnapshotId'::uuid AS tariff_snapshot_id,
            '$expectedSiteGroupId'::uuid AS site_group_id,
            '$expectedSiteId'::uuid AS site_id,
            '$TicketReference'::text AS ticket_reference
    ),
    uat_roles AS (
        SELECT COUNT(*) AS role_count
        FROM identity.roles
        WHERE role_code IN (
            'SYSTEM_RBAC_ADMINISTRATOR',
            'PLATFORM_ADMINISTRATOR',
            'OPERATIONS_SUPERVISOR',
            'OPERATOR_SUPPORT_STAFF',
            'FINANCE_RECONCILIATION_ANALYST',
            'COMPLIANCE_POLICY_ADMINISTRATOR',
            'EXECUTIVE_MANAGEMENT'
        )
          AND role_status = 'ACTIVE'
    ),
    uat_users AS (
        SELECT COUNT(*) AS user_count
        FROM identity.users
        WHERE username IN (
            'uat-system-rbac-admin',
            'uat-platform-admin',
            'uat-operations-supervisor',
            'uat-operator-support',
            'uat-finance-reconciliation',
            'uat-compliance-policy-admin',
            'uat-executive-management'
        )
          AND user_status = 'ACTIVE'
    ),
    unsafe_counts AS (
        SELECT
            (SELECT COUNT(*) FROM core.payment_attempts pa, fixture f WHERE pa.parking_session_id = f.parking_session_id) AS payment_attempt_count,
            (SELECT COUNT(*) FROM core.exit_authorizations ea, fixture f WHERE ea.parking_session_id = f.parking_session_id) AS exit_authorization_count,
            (SELECT COUNT(*) FROM coupons.coupon_applications ca, fixture f WHERE ca.parking_session_id = f.parking_session_id) AS coupon_application_count,
            (SELECT COUNT(*) FROM reconciliation.mops_transaction_records mr, fixture f WHERE mr.parking_session_id = f.parking_session_id) AS reconciliation_count
    )
    SELECT
        current_database() AS database_name,
        (SELECT user_count FROM uat_users) AS uat_user_count,
        (SELECT role_count FROM uat_roles) AS uat_role_bundle_count,
        ps.parking_session_id::text,
        ps.ticket_number_masked AS ticket_reference,
        ps.session_status::text AS session_status,
        ps.site_group_id::text,
        ps.site_id::text,
        ts.tariff_snapshot_id::text,
        ROUND((ts.net_amount * 100)::numeric, 0)::bigint AS current_payable_amount_minor_units,
        ts.currency_code,
        ts.snapshot_status::text AS tariff_snapshot_status,
        uc.payment_attempt_count,
        uc.exit_authorization_count,
        uc.coupon_application_count,
        uc.reconciliation_count
    FROM fixture f
    LEFT JOIN core.parking_sessions ps ON ps.parking_session_id = f.parking_session_id
    LEFT JOIN core.tariff_snapshots ts ON ts.tariff_snapshot_id = f.tariff_snapshot_id
    CROSS JOIN unsafe_counts uc
) TO STDOUT WITH CSV HEADER
"@

$summaryRows = @(Invoke-PsqlText -Database $DbName -Sql $summarySql | ConvertFrom-Csv)
if ($summaryRows.Count -ne 1) {
    throw "Expected one aligned UAT summary row but got $($summaryRows.Count)."
}

$summary = $summaryRows[0]
if ($summary.database_name -ne $DbName) {
    throw "Verified unexpected database '$($summary.database_name)'. Expected '$DbName'."
}
if ([int]$summary.uat_user_count -ne 7 -or [int]$summary.uat_role_bundle_count -ne 7) {
    throw "Expected seven UAT users and seven role bundles. users=$($summary.uat_user_count), roles=$($summary.uat_role_bundle_count)"
}
if ($summary.ticket_reference -ne $TicketReference -or $summary.session_status -ne "ACTIVE") {
    throw "Fixture session is not active for ticket $TicketReference."
}
if ($summary.site_group_id -ne $expectedSiteGroupId -or $summary.site_id -ne $expectedSiteId) {
    throw "Fixture site scope mismatch."
}
if ([int64]$summary.current_payable_amount_minor_units -ne $expectedAmountMinorUnits -or $summary.currency_code.Trim() -ne $expectedCurrency) {
    throw "Fixture amount/currency mismatch. Expected $expectedAmountMinorUnits $expectedCurrency."
}
if ($summary.tariff_snapshot_status -ne "ACTIVE") {
    throw "Original tariff snapshot is not active."
}
if ([int]$summary.payment_attempt_count -ne 0 -or [int]$summary.exit_authorization_count -ne 0 -or [int]$summary.coupon_application_count -ne 0 -or [int]$summary.reconciliation_count -ne 0) {
    throw "Fixture has unsafe side-effect rows before UAT."
}

if ($VerifyLiveApi) {
    $correlationId = [Guid]::NewGuid().ToString()
    $lookupBody = @{
        userId = $expectedRequesterUserId
        operatorDeviceBindingId = $expectedDeviceBindingId
        siteId = $expectedSiteId
        siteGroupId = $expectedSiteGroupId
        operatorShiftId = $expectedRequesterShiftId
        parkingSessionId = $null
        ticketReference = $TicketReference
        plateNumber = $null
        lookupMode = "TICKET_REFERENCE"
        idempotencyKey = "operator-console-statutory-discount-aligned-uat-preflight:${TicketReference}:$correlationId"
        correlationId = $correlationId
    } | ConvertTo-Json -Depth 8

    $headers = @{
        "Content-Type" = "application/json"
        "X-Correlation-Id" = $correlationId
        "X-Operator-User-Id" = $expectedRequesterUserId
        "X-ExitPass-User-Id" = $expectedRequesterUserId
        "X-ExitPass-Permissions" = $requesterPermissions
        "X-Operator-Device-Binding-Id" = $expectedDeviceBindingId
        "X-Operator-Shift-Id" = $expectedRequesterShiftId
        "X-Site-Id" = $expectedSiteId
        "X-Site-Group-Id" = $expectedSiteGroupId
    }

    $lookupUri = "$($ApiBaseUrl.TrimEnd('/'))/v1/ops/operator-console/sessions/lookup"
    Write-Host "Calling live Central PMS endpoint: POST $lookupUri"
    try {
        $lookup = Invoke-RestMethod -Uri $lookupUri -Method Post -Headers $headers -Body $lookupBody -TimeoutSec 20
    }
    catch {
        Write-StartupCommands
        throw "Central PMS session lookup endpoint could not be called. Start/restart Central PMS with the command above. $($_.Exception.Message)"
    }

    if ($lookup.sessionFound -ne $true -or $lookup.sessionEligible -ne $true -or [int64]$lookup.currentPayableAmountMinorUnits -ne $expectedAmountMinorUnits -or $lookup.ticketReference -ne $TicketReference) {
        $lookup | ConvertTo-Json -Depth 8 | Write-Host
        throw "Live API did not return the aligned UAT fixture."
    }
}

Write-Host ""
Write-Host "Preflight PASSED."
Write-Host "DB source: $canonicalSqlPath"
Write-Host "Disposable DB: $DbName"
Write-Host "Ticket: $TicketReference"
Write-Host "Requester/evidence actor: uat-operator-support / $expectedRequesterUserId"
Write-Host "Reviewer/apply actor: uat-operations-supervisor / $expectedReviewerUserId"
Write-Host "Expected amounts: gross=12500 vatExclusive=11161 vat=1339 discount=2232 final=8929"
Write-Host ""
Write-StartupCommands
Write-Host ""
Write-Host "Browser URL: http://localhost:5175/operator-console/ticket-lookup"
Write-Host "Ticket number: $TicketReference"
Write-Host ""
Write-Host "Requester local/dev header profile:"
Write-Host "  X-ExitPass-User-Id=$expectedRequesterUserId"
Write-Host "  X-Operator-User-Id=$expectedRequesterUserId"
Write-Host "  X-Operator-Shift-Id=$expectedRequesterShiftId"
Write-Host "  X-Operator-Device-Binding-Id=$expectedDeviceBindingId"
Write-Host "  X-Site-Group-Id=$expectedSiteGroupId"
Write-Host "  X-Site-Id=$expectedSiteId"
Write-Host "  X-ExitPass-Permissions=$requesterPermissions"
Write-Host "Requester Operator Console UI env profile:"
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_USER_ID=`"$expectedRequesterUserId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SHIFT_ID=`"$expectedRequesterShiftId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID=`"$expectedDeviceBindingId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID=`"$expectedSiteGroupId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SITE_ID=`"$expectedSiteId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_PERMISSIONS=`"$requesterPermissions`""
Write-Host ""
Write-Host "Reviewer/apply local/dev header profile:"
Write-Host "  X-ExitPass-User-Id=$expectedReviewerUserId"
Write-Host "  X-Operator-User-Id=$expectedReviewerUserId"
Write-Host "  X-Operator-Shift-Id=$expectedReviewerShiftId"
Write-Host "  X-Operator-Device-Binding-Id=$expectedDeviceBindingId"
Write-Host "  X-Site-Group-Id=$expectedSiteGroupId"
Write-Host "  X-Site-Id=$expectedSiteId"
Write-Host "  X-ExitPass-Permissions=$reviewerPermissions"
Write-Host "Reviewer/apply Operator Console UI env profile:"
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_USER_ID=`"$expectedReviewerUserId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SHIFT_ID=`"$expectedReviewerShiftId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID=`"$expectedDeviceBindingId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID=`"$expectedSiteGroupId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_SITE_ID=`"$expectedSiteId`""
Write-Host "  `$env:VITE_OPERATOR_CONSOLE_PERMISSIONS=`"$reviewerPermissions`""
