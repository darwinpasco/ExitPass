[CmdletBinding()]
param([string]$RepositoryRoot)

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
    'CREATE OR REPLACE VIEW ist_configuration.real_site_operational_readiness'
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
foreach ($expected in @('SupportsShouldProcess', 'exactly 46 rows', 'Convert-Uuid', 'Convert-Boolean')) {
    if ($script.IndexOf($expected, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "Operational importer is missing required behavior: $expected"
    }
}

Write-Output 'Persistent real-Site operational configuration tooling checks passed.'
