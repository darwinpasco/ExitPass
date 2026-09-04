[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ExitPassContainer = 'exitpass-operational-validation-db',
    [string]$ExitPassDatabase = 'exitpass_ist',
    [string]$DatabaseUser = 'exitpass_ist',
    [string]$InputPath,
    [switch]$DatabaseValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

$sql = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Update-PersistentRealSiteOperationalConfiguration.sql'))
$script = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Set-PersistentRealSiteOperationalConfiguration.ps1'))

$requiredSql = @(
    'BEGIN;', 'COMMIT;', 'count(*) FROM ep_ist_operational) <> 46',
    'unknown or conflicting Site identity', 'invalid capability relationship',
    'CREATE OR REPLACE VIEW ist_configuration.real_site_operational_readiness',
    'CREATE OR REPLACE VIEW ist_configuration.effective_site_adapter_routes',
    'CREATE OR REPLACE VIEW ist_configuration.site_adapter_route_readiness',
    'effective_route_count <> 1',
    "endpoint.endpoint_code = 'SITE_ADAPTER_API'",
    "mapping.vendor_object_type = 'SITE_ADAPTER'",
    'credential.service_identity_id = capability.central_pms_service_identity_id'
)
foreach ($expected in $requiredSql) {
    if ($sql.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Operational SQL is missing required guard: $expected"
    }
}
foreach ($prohibited in @('core.payment_attempts', 'core.payment_confirmations', 'pos.fiscal_documents', 'exit_authorizations')) {
    if ($sql.IndexOf($prohibited, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Operational SQL must not touch business transaction state: $prohibited"
    }
}
foreach ($expected in @(
    'SupportsShouldProcess', 'exactly 46 rows', 'Convert-Uuid', 'Convert-Boolean',
    'Test-SiteAdapterRoute', 'site_adapter_base_url', 'site_adapter_secret_reference',
    'must identify the Site Adapter, not its HikCentral upstream')) {
    if ($script.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Operational importer is missing required behavior: $expected"
    }
}
Write-Output 'Persistent real-Site operational configuration tooling checks passed.'

if (-not $DatabaseValidation) { return }
if ($ExitPassContainer -eq 'exitpass-ist-persistent-db') {
    throw 'Database routing regression tests must not run against the authoritative persistent IST database.'
}
if ([string]::IsNullOrWhiteSpace($InputPath)) {
    throw 'InputPath is required for database idempotency validation.'
}

function Invoke-DbScalar([string]$DatabaseSql) {
    $output = @(& docker exec $ExitPassContainer psql -X -qAt -v ON_ERROR_STOP=1 `
        -U $DatabaseUser -d $ExitPassDatabase -c $DatabaseSql 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Database routing validation failed:`n$($output -join [Environment]::NewLine)"
    }
    return [string]($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Last 1)
}
function Assert-DbEqual([string]$Actual, [string]$Expected, [string]$Scenario) {
    if ($Actual -cne $Expected) {
        throw "$Scenario expected '$Expected' but observed '$Actual'."
    }
}

$pitxId = '2d1dcdf8-f563-537c-8542-0bde7cc9da97'
$routeStateSql = "SELECT concat_ws('|',site_adapter_route_count,site_adapter_route_ready,final_test_readiness) FROM ist_configuration.real_site_readiness WHERE site_id='$pitxId'::uuid;"
Assert-DbEqual (Invoke-DbScalar $routeStateSql) '1|t|READY' 'one valid PITX route'

$mappingId = "(SELECT site_adapter_mapping_id FROM ist_configuration.site_operational_capabilities WHERE site_id='$pitxId'::uuid)"
$endpointId = "(SELECT site_adapter_endpoint_id FROM ist_configuration.site_operational_capabilities WHERE site_id='$pitxId'::uuid)"
$credentialId = "(SELECT site_adapter_credential_reference_id FROM ist_configuration.site_operational_capabilities WHERE site_id='$pitxId'::uuid)"
$adapterId = "(SELECT site_adapter_service_identity_id FROM ist_configuration.site_operational_capabilities WHERE site_id='$pitxId'::uuid)"
$vendorId = "(SELECT site_adapter_vendor_system_id FROM ist_configuration.site_operational_capabilities WHERE site_id='$pitxId'::uuid)"
$notReady = '0|f|PARTIALLY_CONFIGURED'
$cases = @(
    @{ Name = 'configured boolean with zero canonical routes'; Sql = "DELETE FROM integration.adapter_mappings WHERE adapter_mapping_id=$mappingId;" },
    @{ Name = 'inactive adapter mapping'; Sql = "UPDATE integration.adapter_mappings SET mapping_status='SUSPENDED' WHERE adapter_mapping_id=$mappingId;" },
    @{ Name = 'missing SITE_ADAPTER_API endpoint'; Sql = "UPDATE integration.vendor_endpoints SET endpoint_status='SUSPENDED' WHERE vendor_endpoint_id=$endpointId;" },
    @{ Name = 'inactive credential'; Sql = "UPDATE integration.integration_credential_references SET credential_status='REVOKED' WHERE integration_credential_reference_id=$credentialId;" },
    @{ Name = 'invalid credential'; Sql = "UPDATE integration.integration_credential_references SET secret_reference='inline-secret-is-forbidden' WHERE integration_credential_reference_id=$credentialId;" },
    @{ Name = 'credential owned by wrong identity'; Sql = "UPDATE integration.integration_credential_references SET service_identity_id=$adapterId WHERE integration_credential_reference_id=$credentialId;" },
    @{ Name = 'inactive adapter identity'; Sql = "UPDATE identity.service_identities SET identity_status='SUSPENDED' WHERE service_identity_id=$adapterId;" },
    @{ Name = 'environment mismatch'; Sql = "UPDATE integration.vendor_systems SET environment_code='IST-MISMATCH' WHERE vendor_system_id=$vendorId;" },
    @{ Name = 'invalid Site Adapter URL'; Sql = "UPDATE integration.vendor_systems SET base_url_ref='not-an-absolute-url' WHERE vendor_system_id=$vendorId;" }
)
foreach ($case in $cases) {
    $actual = Invoke-DbScalar "BEGIN; $($case.Sql) $routeStateSql ROLLBACK;"
    Assert-DbEqual $actual $notReady $case.Name
}

$duplicateRouteSql = @"
INSERT INTO identity.service_identities (service_identity_id,service_identity_code,service_identity_name,identity_type,identity_status,owning_service_name,effective_from)
SELECT 'f4100000-0000-4000-8000-000000000001','ist-route-regression-duplicate-adapter','IST route regression duplicate adapter',identity_type,identity_status,owning_service_name,effective_from FROM identity.service_identities WHERE service_identity_id=$adapterId;
INSERT INTO integration.vendor_systems (vendor_system_id,vendor_code,vendor_name,vendor_system_type,vendor_system_status,environment_code,base_url_ref,api_version,owner_team,effective_from)
SELECT 'f4100000-0000-4000-8000-000000000002','IST_ROUTE_REGRESSION_DUPLICATE','IST route regression duplicate',vendor_system_type,vendor_system_status,environment_code,base_url_ref,api_version,owner_team,effective_from FROM integration.vendor_systems WHERE vendor_system_id=$vendorId;
INSERT INTO integration.integration_credential_references (integration_credential_reference_id,vendor_system_id,service_identity_id,credential_code,credential_name,credential_type,secret_store_type,secret_reference,credential_status)
SELECT 'f4100000-0000-4000-8000-000000000003','f4100000-0000-4000-8000-000000000002',service_identity_id,'IST_ROUTE_REGRESSION_DUPLICATE','IST route regression duplicate',credential_type,secret_store_type,secret_reference,credential_status FROM integration.integration_credential_references WHERE integration_credential_reference_id=$credentialId;
INSERT INTO integration.vendor_endpoints (vendor_endpoint_id,vendor_system_id,endpoint_code,endpoint_name,endpoint_type,path_template,operation_ref,credential_reference_id,endpoint_status,effective_from)
SELECT 'f4100000-0000-4000-8000-000000000004','f4100000-0000-4000-8000-000000000002',endpoint_code,'IST route regression duplicate',endpoint_type,path_template,operation_ref,'f4100000-0000-4000-8000-000000000003',endpoint_status,effective_from FROM integration.vendor_endpoints WHERE vendor_endpoint_id=$endpointId;
INSERT INTO integration.adapter_mappings (adapter_mapping_id,vendor_system_id,mapping_type,site_group_id,site_id,vendor_object_type,vendor_object_ref,vendor_object_name,mapping_status,mapping_confidence,effective_from)
SELECT 'f4100000-0000-4000-8000-000000000005','f4100000-0000-4000-8000-000000000002',mapping_type,site_group_id,site_id,vendor_object_type,'f4100000-0000-4000-8000-000000000001','IST route regression duplicate',mapping_status,mapping_confidence,effective_from FROM integration.adapter_mappings WHERE adapter_mapping_id=$mappingId;
"@
Assert-DbEqual (Invoke-DbScalar "BEGIN; $duplicateRouteSql $routeStateSql ROLLBACK;") `
    '2|f|PARTIALLY_CONFIGURED' 'duplicate effective route'

$identitySql = @"
SELECT concat_ws('|',site_adapter_service_identity_id,site_adapter_vendor_system_id,
site_adapter_credential_reference_id,site_adapter_endpoint_id,site_adapter_mapping_id,
adapter.row_version,vendor.row_version,credential.row_version,endpoint.row_version,mapping.row_version,
route.effective_route_count)
FROM ist_configuration.site_operational_capabilities capability
JOIN identity.service_identities adapter ON adapter.service_identity_id=capability.site_adapter_service_identity_id
JOIN integration.vendor_systems vendor ON vendor.vendor_system_id=capability.site_adapter_vendor_system_id
JOIN integration.integration_credential_references credential ON credential.integration_credential_reference_id=capability.site_adapter_credential_reference_id
JOIN integration.vendor_endpoints endpoint ON endpoint.vendor_endpoint_id=capability.site_adapter_endpoint_id
JOIN integration.adapter_mappings mapping ON mapping.adapter_mapping_id=capability.site_adapter_mapping_id
JOIN ist_configuration.site_adapter_route_readiness route ON route.site_id=capability.site_id
WHERE capability.site_id='$pitxId'::uuid;
"@
$before = Invoke-DbScalar $identitySql
& (Join-Path $PSScriptRoot 'Set-PersistentRealSiteOperationalConfiguration.ps1') `
    -InputPath $InputPath -ExitPassContainer $ExitPassContainer `
    -ExitPassDatabase $ExitPassDatabase -DatabaseUser $DatabaseUser
$after = Invoke-DbScalar $identitySql
Assert-DbEqual $after $before 'importer rerun stable route identity'
Assert-DbEqual $after '3986bf89-8373-da14-fc3f-f3818dc6b500|afdefaab-6be4-6b25-8f3f-3ad8309662e8|c01e2f80-4596-3862-ce3f-8bd1883b6384|c22a55cb-6509-afdd-ec47-27e13d1fdab7|b18bb030-1d5e-eb33-f5eb-4ed379ec6804|1|1|1|1|1|1' 'deterministic PITX route IDs and cardinality'

$invariantsSql = @"
SELECT concat_ws('|',
 (SELECT count(DISTINCT site.site_group_id) FROM ist_configuration.real_site_catalog_members member JOIN sites.sites site USING(site_id)),
 (SELECT count(*) FROM ist_configuration.real_site_catalog_members),
 (SELECT count(*) FROM sites.site_jurisdiction_assignments assignment JOIN ist_configuration.real_site_catalog_members member USING(site_id) WHERE assignment.assignment_status='ACTIVE' AND assignment.effective_to IS NULL),
 (SELECT count(*) FROM ist_configuration.effective_site_adapter_routes route JOIN sites.sites site USING(site_id) WHERE site.site_code='TEST_SITE'),
 (SELECT count(*) FROM ist_configuration.site_operational_capabilities capability JOIN integration.adapter_mappings mapping USING(site_id) JOIN integration.vendor_systems vendor USING(vendor_system_id) WHERE NOT capability.hikcentral_target_configured AND vendor.vendor_code LIKE 'IST_SITE_ADAPTER_%'),
 (SELECT count(*) FROM ist_configuration.effective_site_adapter_routes WHERE site_id='$pitxId'::uuid AND lower(base_url_ref) IN ('https://sys-service.exitpass.test:443','https://host.docker.internal:443')),
 (SELECT count(*) FROM core.payment_attempts),
 (SELECT count(*) FROM core.payment_confirmations),
 (SELECT count(*) FROM core.exit_authorizations));
"@
Assert-DbEqual (Invoke-DbScalar $invariantsSql) '39|46|46|0|0|0|0|0|0' `
    'catalog, TEST_SITE exclusion, architecture boundary, and transaction preservation'

Write-Output 'Persistent Site Adapter database routing regression checks passed.'
