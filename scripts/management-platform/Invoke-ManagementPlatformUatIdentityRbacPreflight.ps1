<#
.SYNOPSIS
Preflights the local/UAT Management Platform identity and RBAC seed.

.DESCRIPTION
This local-only helper seeds and verifies the approved ExitPass v1.3 seven-role
UAT identity/RBAC posture in exitpass_v12_dev. It also composes with the
Operator Console statutory discount pilot fixture so requester/reviewer site,
device, and shift context are deterministic.
#>

[CmdletBinding()]
param(
    [string] $ApiBaseUrl = "http://localhost:56065",
    [string] $PostgresContainer = "exitpass-postgres",
    [string] $DbName = "exitpass_v12_dev",
    [string] $DbUser = "exitpass",
    [switch] $SkipSeed,
    [switch] $SkipApi
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$systemRbacAdminUserId = "79000000-0000-0000-0000-000000000001"
$platformAdminUserId = "79000000-0000-0000-0000-000000000002"
$operationsSupervisorUserId = "77000000-0000-0000-0000-000000000012"
$operatorSupportUserId = "77000000-0000-0000-0000-000000000010"
$financeUserId = "79000000-0000-0000-0000-000000000005"
$complianceUserId = "79000000-0000-0000-0000-000000000006"
$executiveUserId = "79000000-0000-0000-0000-000000000007"

$siteGroupId = "77000000-0000-0000-0000-000000000001"
$siteId = "77000000-0000-0000-0000-000000000002"
$deviceBindingId = "77000000-0000-0000-0000-000000000030"
$requesterShiftId = "77000000-0000-0000-0000-000000000050"
$reviewerShiftId = "77000000-0000-0000-0000-000000000052"

$systemRbacAdminPermissions = "management-platform.identity-rbac.inventory.read,user.view,user.manage,rbac.view,rbac.manage,role.view,role.manage,permission.view,permission.manage,assignment.view,assignment.manage,access-audit.view"
$operatorSupportPermissions = "statutory-discounts.session.lookup,statutory-discounts.draft.view,statutory-discounts.draft.create,statutory-discounts.evidence.view,statutory-discounts.evidence.capture,statutory-discounts.policy.resolve,fiscal-issuance.status.read,ticket.lookup,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
$operationsSupervisorPermissions = "statutory-discounts.draft.view,statutory-discounts.evidence.view,statutory-discounts.review.queue.read,statutory-discounts.review.detail.read,statutory-discounts.decision.review,statutory-discounts.decision.approve,statutory-discounts.decision.reject,statutory-discounts.policy.resolve,fiscal-issuance.status.read,fiscal-issuance.void.command,operator-workflow-audit.view,projection-health.view,ops.vendor-session-projection-health.view,operator-console.vendor-projection-health.view,vendor-acknowledgments.view"
$executivePermissions = "dashboard.view,reports.view,executive-summary.view,site-performance.view,site-group-performance.view,revenue-summary.view,payment-summary.view,fiscal-summary.view,statutory-discount-summary.view,exception-trend.view,operational-monitoring.view"

$seedSqlPath = Join-Path $PSScriptRoot "Seed-ManagementPlatformUatIdentityRbac.sql"
$verifySqlPath = Join-Path $PSScriptRoot "Verify-ManagementPlatformUatIdentityRbac.sql"
$operatorConsoleScriptRoot = Join-Path (Split-Path $PSScriptRoot -Parent) "operator-console"
$statutoryDiscountSeedPath = Join-Path $operatorConsoleScriptRoot "Seed-StatutoryDiscountPilotFixture.sql"
$statutoryDiscountVerifyPath = Join-Path $operatorConsoleScriptRoot "Verify-StatutoryDiscountPilotFixture.sql"

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

function Write-CentralPmsStartupCommand {
    Write-Host ""
    Write-Host "Start Central PMS with this local UAT database:"
    Write-Host "cd D:\SourceCodes\ExitPass"
    Write-Host '$env:ASPNETCORE_ENVIRONMENT="Development"'
    Write-Host '$env:ASPNETCORE_URLS="http://localhost:56065"'
    Write-Host '$env:ConnectionStrings__MainDatabase="Host=localhost;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me"'
    Write-Host 'dotnet run --project src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj --no-build'
    Write-Host ""
}

if ($DbName -ne "exitpass_v12_dev") {
    throw "This preflight is pinned to local DB exitpass_v12_dev. Received: $DbName"
}

Write-Host "ExitPass Management Platform UAT identity/RBAC preflight"
Write-Host "Expected Central PMS DB: $DbName"

if (-not $SkipSeed) {
    Invoke-PsqlFile -Path $statutoryDiscountSeedPath
    Invoke-PsqlFile -Path $seedSqlPath
}

Invoke-PsqlFile -Path $statutoryDiscountVerifyPath
Invoke-PsqlFile -Path $verifySqlPath

$summarySql = @"
COPY (
    WITH role_permissions AS (
        SELECT
            r.role_code,
            string_agg(p.permission_code, ',' ORDER BY p.permission_code) AS permissions
        FROM identity.role_permissions rp
        JOIN identity.roles r ON r.role_id = rp.role_id
        JOIN identity.permissions p ON p.permission_id = rp.permission_id
        WHERE rp.binding_status = 'ACTIVE'
          AND r.role_code IN (
              'SYSTEM_RBAC_ADMINISTRATOR',
              'PLATFORM_ADMINISTRATOR',
              'OPERATIONS_SUPERVISOR',
              'OPERATOR_SUPPORT_STAFF',
              'FINANCE_RECONCILIATION_ANALYST',
              'COMPLIANCE_POLICY_ADMINISTRATOR',
              'EXECUTIVE_MANAGEMENT'
          )
        GROUP BY r.role_code
    )
    SELECT
        u.user_id::text,
        u.username,
        u.display_name,
        r.role_code,
        rp.permissions
    FROM identity.users u
    JOIN identity.user_roles ur ON ur.user_id = u.user_id AND ur.assignment_status = 'ACTIVE'
    JOIN identity.roles r ON r.role_id = ur.role_id
    JOIN role_permissions rp ON rp.role_code = r.role_code
    WHERE u.user_id IN (
        '$systemRbacAdminUserId'::uuid,
        '$platformAdminUserId'::uuid,
        '$operationsSupervisorUserId'::uuid,
        '$operatorSupportUserId'::uuid,
        '$financeUserId'::uuid,
        '$complianceUserId'::uuid,
        '$executiveUserId'::uuid
    )
    ORDER BY r.role_code
) TO STDOUT WITH CSV HEADER
"@

$summaryRows = @(Invoke-PsqlText -Sql $summarySql | ConvertFrom-Csv)
if ($summaryRows.Count -ne 7) {
    throw "Expected seven UAT identity/RBAC rows but got $($summaryRows.Count)."
}

if (($summaryRows | Where-Object { $_.user_id -eq $systemRbacAdminUserId }).permissions -match "statutory-discounts\.decision\.approve|statutory-discounts\.payable-basis\.apply|fiscal-issuance\.void\.command|reconciliation\.manage") {
    throw "System / RBAC Administrator unexpectedly has business workflow mutation permissions."
}

if (($summaryRows | Where-Object { $_.user_id -eq $executiveUserId }).permissions -match "\.manage|\.command|\.apply|\.approve|\.reject|statutory-discounts\.draft\.create|statutory-discounts\.evidence\.capture|reports\.export") {
    throw "Executive / Management unexpectedly has mutation/export permissions."
}

if (($summaryRows | Where-Object { $_.user_id -eq $operatorSupportUserId }).permissions -match "statutory-discounts\.decision\.approve|statutory-discounts\.decision\.reject|statutory-discounts\.payable-basis\.apply") {
    throw "Operator / Support Staff unexpectedly has statutory discount approve/reject/apply permission."
}

if (($summaryRows | Where-Object { $_.user_id -eq $operationsSupervisorUserId }).permissions -notmatch "statutory-discounts\.decision\.approve" -or
    ($summaryRows | Where-Object { $_.user_id -eq $operationsSupervisorUserId }).permissions -notmatch "statutory-discounts\.payable-basis\.apply") {
    throw "Operations Supervisor does not have statutory discount approve/apply permissions."
}

if (-not $SkipApi) {
    $inventoryUri = "$($ApiBaseUrl.TrimEnd('/'))/v1/ops/management-platform/identity-rbac/inventory"
    Write-Host "Calling live Central PMS endpoint: GET $inventoryUri"
    try {
        $inventory = Invoke-RestMethod -Uri $inventoryUri -Method Get -Headers @{
            "X-ExitPass-User-Id" = $systemRbacAdminUserId
            "X-ExitPass-Permissions" = "management-platform.identity-rbac.inventory.read"
        } -TimeoutSec 20
    }
    catch {
        Write-CentralPmsStartupCommand
        throw "Central PMS identity/RBAC inventory endpoint could not be called. Start/restart Central PMS with the command above. $($_.Exception.Message)"
    }

    $inventoryUserIds = @($inventory.users | ForEach-Object { $_.userId })
    foreach ($requiredUserId in @($systemRbacAdminUserId, $platformAdminUserId, $operationsSupervisorUserId, $operatorSupportUserId, $financeUserId, $complianceUserId, $executiveUserId)) {
        if ($inventoryUserIds -notcontains $requiredUserId) {
            throw "Inventory API did not return seeded UAT user $requiredUserId."
        }
    }

    $inventoryRoleAssignmentUserIds = @($inventory.userRoleAssignments | ForEach-Object { $_.userId })
    foreach ($requiredUserId in @($systemRbacAdminUserId, $platformAdminUserId, $operationsSupervisorUserId, $operatorSupportUserId, $financeUserId, $complianceUserId, $executiveUserId)) {
        if ($inventoryRoleAssignmentUserIds -notcontains $requiredUserId) {
            throw "Inventory API did not return role assignment for UAT user $requiredUserId."
        }
    }

    try {
        [void](Invoke-RestMethod -Uri $inventoryUri -Method Get -Headers @{
            "X-ExitPass-User-Id" = $operatorSupportUserId
            "X-ExitPass-Permissions" = "statutory-discounts.session.lookup"
        } -TimeoutSec 20)
        throw "Inventory API unexpectedly allowed Operator / Support Staff permissions."
    }
    catch {
        $response = $_.Exception.Response
        if ($null -eq $response -or [int]$response.StatusCode -ne 403) {
            throw
        }
    }
}

Write-Host ""
Write-Host "Preflight PASSED."
Write-Host "Seven UAT role bundles are seeded and verified in $DbName."
Write-Host ""
Write-Host "UAT users:"
Write-Host "  System / RBAC Administrator: $systemRbacAdminUserId / uat-system-rbac-admin"
Write-Host "  Platform Administrator:      $platformAdminUserId / uat-platform-admin"
Write-Host "  Operations Supervisor:       $operationsSupervisorUserId / uat-operations-supervisor"
Write-Host "  Operator / Support Staff:    $operatorSupportUserId / uat-operator-support"
Write-Host "  Finance / Reconciliation:    $financeUserId / uat-finance-reconciliation"
Write-Host "  Compliance / Policy Admin:   $complianceUserId / uat-compliance-policy-admin"
Write-Host "  Executive / Management:      $executiveUserId / uat-executive-management"
Write-Host ""
Write-Host "Operator Console statutory discount two-user context:"
Write-Host "  Site group: $siteGroupId"
Write-Host "  Site:       $siteId"
Write-Host "  Device:     $deviceBindingId"
Write-Host "  Requester:  $operatorSupportUserId, shift $requesterShiftId"
Write-Host "  Reviewer:   $operationsSupervisorUserId, shift $reviewerShiftId"
Write-Host ""
Write-Host "Local/dev header profiles:"
Write-Host "  System/RBAC Admin permissions: $systemRbacAdminPermissions"
Write-Host "  Requester permissions:         $operatorSupportPermissions"
Write-Host "  Reviewer/apply permissions:    $operationsSupervisorPermissions"
Write-Host "  Executive permissions:         $executivePermissions"
