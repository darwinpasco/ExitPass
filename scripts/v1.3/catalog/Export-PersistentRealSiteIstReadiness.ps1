[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$ExitPassContainer = 'exitpass-ist-persistent-db',
    [string]$ExitPassDatabase = 'exitpass_ist',
    [string]$DatabaseUser = 'exitpass_ist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    throw 'OutputPath is required.'
}

$query = @'
SELECT readiness.site_id, readiness.site_code, readiness.site_name, readiness.site_group_id, readiness.site_group_name, readiness.jurisdiction,
       CASE WHEN readiness.site_exists_active AND readiness.site_group_exists_active AND readiness.jurisdiction_active THEN 'ACTIVE' ELSE 'INCOMPLETE' END AS catalog,
       readiness.senior_policy_status AS senior_policy,
       readiness.pwd_policy_status AS pwd_policy,
       capability.operator_entity_code,
       capability.hikcentral_instance_code,
       CASE WHEN capability.hikcentral_target_configured THEN 'YES' ELSE 'NO' END AS hikcentral_target_configured,
       CASE WHEN capability.hikcentral_connectivity_verified THEN 'YES' ELSE 'NO' END AS hikcentral_connectivity_verified,
       readiness.site_adapter_route_count,
       CASE WHEN readiness.site_adapter_route_ready THEN 'YES' ELSE 'NO' END AS site_adapter_route_ready,
       CASE WHEN readiness.projection_target_required THEN 'YES' ELSE 'NO' END AS projection_target_required,
       readiness.enabled_projection_target_count,
       CASE WHEN readiness.projection_target_route_aligned THEN 'YES' ELSE 'NO' END AS projection_target_route_aligned,
       readiness.projection_sync_target_id,
       readiness.projection_target_vendor_system_id,
       readiness.projection_target_parking_lot_index_code,
       readiness.projection_target_health_status,
       readiness.projection_target_last_success_at,
       CASE WHEN readiness.projection_target_runtime_healthy IS NULL THEN 'NOT_APPLICABLE'
            WHEN readiness.projection_target_runtime_healthy THEN 'YES' ELSE 'NO' END AS projection_target_runtime_healthy,
       capability.site_adapter_base_url,
       capability.site_adapter_environment_code,
       capability.central_pms_service_identity_id,
       capability.site_adapter_service_identity_id,
       capability.site_adapter_vendor_system_id,
       capability.site_adapter_credential_reference_id,
       capability.site_adapter_endpoint_id,
       capability.site_adapter_mapping_id,
       capability.hikcentral_parking_lot_index_code,
       capability.hikcentral_parking_lot_name,
       CASE WHEN readiness.webpay_public_lookup_enabled THEN 'YES' ELSE 'NO' END AS webpay_public_lookup_enabled,
       CASE WHEN readiness.webpay_payment_enabled THEN 'YES' ELSE 'NO' END AS webpay_payment_enabled,
       CASE WHEN capability.fiscal_merchant_configured THEN 'YES' ELSE 'NO' END AS fiscal_merchant_configured,
       CASE WHEN capability.fiscal_supplier_configured THEN 'YES' ELSE 'NO' END AS fiscal_supplier_configured,
       CASE WHEN capability.fiscal_profile_approved THEN 'YES' ELSE 'NO' END AS fiscal_profile_approved,
       capability.pos_site_server_id,
       capability.fiscal_identity_id,
       capability.sales_invoice_profile_id,
       CASE WHEN capability.paymongo_enabled THEN 'YES' ELSE 'NO' END AS paymongo_enabled,
       readiness.final_test_readiness,
       concat_ws('; ',
         CASE WHEN NOT capability.hikcentral_target_configured THEN 'HikCentral target' END,
         CASE WHEN NOT capability.hikcentral_connectivity_verified THEN 'HikCentral connectivity' END,
          CASE WHEN capability.hikcentral_target_configured AND NOT readiness.site_adapter_route_ready THEN 'Central PMS Site Adapter route (exactly one required)' END,
          CASE WHEN readiness.projection_target_required AND NOT readiness.projection_target_route_aligned THEN 'projection target route alignment' END,
          CASE WHEN readiness.projection_target_required AND NOT coalesce(readiness.projection_target_runtime_healthy, false) THEN 'current projection runtime health' END,
         CASE WHEN NOT readiness.webpay_public_lookup_enabled THEN 'WebPay publication' END,
         CASE WHEN NOT readiness.webpay_payment_enabled THEN 'WebPay payment enablement' END,
         CASE WHEN NOT capability.fiscal_merchant_configured THEN 'merchant fiscal identity' END,
         CASE WHEN NOT capability.fiscal_supplier_configured THEN 'supplier fiscal identity' END,
         CASE WHEN NOT capability.fiscal_profile_approved THEN 'approved Sales Invoice profile' END,
         CASE WHEN NOT capability.paymongo_enabled THEN 'Site PayMongo enablement' END
       ) AS missing_facts
FROM ist_configuration.real_site_readiness readiness
JOIN ist_configuration.site_operational_capabilities capability USING (site_id)
ORDER BY site_code;
'@

$csv = @(& docker exec $ExitPassContainer psql -X -v ON_ERROR_STOP=1 --csv -U $DatabaseUser -d $ExitPassDatabase -c $query 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Readiness export failed:`n$($csv -join [Environment]::NewLine)" }
$directory = Split-Path -Parent $OutputPath
if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
$csv | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$rows = @(Import-Csv -LiteralPath $OutputPath -Encoding UTF8)
if ($rows.Count -ne 46) { throw "Readiness export expected 46 Sites but produced $($rows.Count)." }
Write-Output "Exported 46 real-Site readiness rows to $OutputPath"
