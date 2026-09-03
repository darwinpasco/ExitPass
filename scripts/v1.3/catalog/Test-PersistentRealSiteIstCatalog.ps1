[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ExitPassContainer = 'exitpass-ist-persistent-db',
    [string]$ExitPassDatabase = 'exitpass_ist',
    [string]$PosContainer = 'exitpass-pos-ist-persistent-db',
    [string]$PosDatabase = 'exitpass_pos_ist',
    [string]$DatabaseUser = 'exitpass_ist',
    [switch]$StaticOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$script:Checks = 0
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    $script:Checks++
    if ([string]$Actual -cne [string]$Expected) {
        throw "$Message (expected '$Expected', observed '$Actual')"
    }
}

function Invoke-Scalar {
    param([string]$Container, [string]$Database, [string]$Sql)
    $output = @(& docker exec $Container psql -X -qAt -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database -c $Sql 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Database validation failed:`n$($output -join [Environment]::NewLine)" }
    return [string]($output | Select-Object -Last 1)
}

$dataRoot = Join-Path $RepositoryRoot 'docs\v1.3\central-pms\seed-manifests\data'
$groups = @(Import-Csv -LiteralPath (Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Groups_v1.0.csv') -Encoding UTF8)
$sites = @(Import-Csv -LiteralPath (Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Sites_v1.0.csv') -Encoding UTF8)
$assignments = @(Import-Csv -LiteralPath (Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Jurisdiction_Assignments_v1.0.csv') -Encoding UTF8)
$coverage = @(Import-Csv -LiteralPath (Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Statutory_Discount_Coverage_v1.0.csv') -Encoding UTF8)
Assert-Equal $groups.Count 39 'Reviewed Site Group count changed'
Assert-Equal $sites.Count 46 'Reviewed Site count changed'
Assert-Equal $assignments.Count 46 'Reviewed assignment count changed'
Assert-Equal $coverage.Count 26 'Reviewed statutory coverage count changed'
Assert-Equal @($assignments.jurisdiction_id | Sort-Object -Unique).Count 13 'Reviewed jurisdiction count changed'
Assert-Equal @($sites.site_id | Sort-Object -Unique).Count 46 'Duplicate Site IDs exist'
Assert-Equal @($sites.site_code | Sort-Object -Unique).Count 46 'Duplicate Site codes exist'
Assert-Equal @($sites | Where-Object { $_.site_code -match '^(TEST_SITE|SITE-A|RESTART-)' }).Count 0 'Synthetic Site exists in reviewed catalog'

if (-not $StaticOnly) {
    $validationSql = @'
SELECT concat_ws('|',
  (SELECT count(*) FROM sites.site_groups g JOIN (SELECT DISTINCT s.site_group_id FROM ist_configuration.real_site_catalog_members m JOIN sites.sites s USING(site_id)) real USING(site_group_id) WHERE g.site_group_status='ACTIVE'),
  (SELECT count(*) FROM ist_configuration.real_site_catalog_members),
  (SELECT count(*) FROM ist_configuration.real_site_readiness WHERE jurisdiction_active),
  (SELECT count(DISTINCT jurisdiction_code) FROM ist_configuration.real_site_readiness),
  (SELECT count(*) FROM ist_configuration.statutory_coverage_register),
  (SELECT count(*) FROM ist_configuration.real_site_readiness),
  (SELECT count(*) FROM ist_configuration.real_site_readiness WHERE final_test_readiness='PARTIALLY_CONFIGURED'),
  (SELECT count(*) FROM ist_configuration.real_site_readiness WHERE site_code ~ '^(TEST_SITE|SITE-A|RESTART-)'),
  (SELECT count(*) FROM sites.sites s JOIN ist_configuration.real_site_catalog_members m USING(site_id) WHERE s.timezone_name <> 'Asia/Manila'),
  (SELECT count(*) FROM sites.site_groups g JOIN sites.sites s USING(site_group_id) JOIN ist_configuration.real_site_catalog_members m USING(site_id) WHERE g.default_currency_code <> 'PHP'),
  (SELECT count(*) FROM ist_configuration.resolve_real_site('PITX-LEVEL-3') WHERE site_id='2d1dcdf8-f563-537c-8542-0bde7cc9da97'::uuid),
  (SELECT count(*) FROM ist_configuration.resolve_real_site('PITX-OPEN-LOT') WHERE site_id='b336964f-3b84-5404-8690-97ead0929b1f'::uuid),
  (SELECT count(*) FROM core.payment_attempts),
  (SELECT count(*) FROM core.payment_confirmations),
  (SELECT count(*) FROM core.exit_authorizations)
);
'@
    Assert-Equal (Invoke-Scalar $ExitPassContainer $ExitPassDatabase $validationSql) '39|46|46|13|26|46|46|0|0|0|1|1|0|0|0' 'Persistent ExitPass IST database validation failed'
    Assert-Equal (Invoke-Scalar $PosContainer $PosDatabase "SELECT concat_ws('|',(SELECT count(*) FROM pos.fiscal_documents),(SELECT count(*) FROM pos.electronic_journal_records));") '0|0' 'Persistent POS IST database contains business transactions'
}

Write-Output "Persistent real-Site IST catalog validation passed ($script:Checks checks)."
