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
SELECT site_id, site_code, site_name, site_group_id, site_group_name, jurisdiction,
       CASE WHEN site_exists_active AND site_group_exists_active AND jurisdiction_active THEN 'ACTIVE' ELSE 'INCOMPLETE' END AS catalog,
       senior_policy_status AS senior_policy,
       pwd_policy_status AS pwd_policy,
       CASE WHEN hikcentral_target_configured THEN 'YES' ELSE 'NO' END AS hikcentral_target_configured,
       CASE WHEN hikcentral_connectivity_verified THEN 'YES' ELSE 'NO' END AS hikcentral_connectivity_verified,
       CASE WHEN webpay_public_lookup_enabled THEN 'YES' ELSE 'NO' END AS webpay_public_lookup_enabled,
       CASE WHEN webpay_payment_enabled THEN 'YES' ELSE 'NO' END AS webpay_payment_enabled,
       CASE WHEN fiscal_merchant_configured THEN 'YES' ELSE 'NO' END AS fiscal_merchant_configured,
       CASE WHEN fiscal_supplier_configured THEN 'YES' ELSE 'NO' END AS fiscal_supplier_configured,
       CASE WHEN fiscal_profile_approved THEN 'YES' ELSE 'NO' END AS fiscal_profile_approved,
       CASE WHEN paymongo_enabled THEN 'YES' ELSE 'NO' END AS paymongo_enabled,
       final_test_readiness,
       concat_ws('; ',
         CASE WHEN NOT hikcentral_target_configured THEN 'HikCentral target' END,
         CASE WHEN NOT hikcentral_connectivity_verified THEN 'HikCentral connectivity' END,
         CASE WHEN NOT webpay_public_lookup_enabled THEN 'WebPay publication' END,
         CASE WHEN NOT webpay_payment_enabled THEN 'WebPay payment enablement' END,
         CASE WHEN NOT fiscal_merchant_configured THEN 'merchant fiscal identity' END,
         CASE WHEN NOT fiscal_supplier_configured THEN 'supplier fiscal identity' END,
         CASE WHEN NOT fiscal_profile_approved THEN 'approved Sales Invoice profile' END,
         CASE WHEN NOT paymongo_enabled THEN 'Site PayMongo enablement' END
       ) AS missing_facts
FROM ist_configuration.real_site_readiness
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
