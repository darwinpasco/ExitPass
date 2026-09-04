[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string]$InputPath,
    [string]$ExitPassContainer = 'exitpass-ist-persistent-db',
    [string]$ExitPassDatabase = 'exitpass_ist',
    [string]$DatabaseUser = 'exitpass_ist'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredColumns = @(
    'site_id', 'site_code', 'operator_entity_code', 'hikcentral_instance_code',
    'hikcentral_parking_lot_index_code', 'hikcentral_parking_lot_name',
    'hikcentral_target_configured', 'hikcentral_connectivity_verified',
    'site_adapter_base_url', 'site_adapter_environment_code',
    'site_adapter_secret_reference', 'central_pms_service_identity_id',
    'webpay_public_lookup_enabled', 'webpay_payment_enabled',
    'fiscal_merchant_configured', 'fiscal_supplier_configured', 'fiscal_profile_approved',
    'pos_site_server_id', 'fiscal_identity_id', 'sales_invoice_profile_id',
    'paymongo_enabled', 'last_verified_at', 'verification_reference'
)

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    throw "Operational configuration input not found: $InputPath"
}
$rows = @(Import-Csv -LiteralPath $InputPath -Encoding UTF8)
if ($rows.Count -ne 46) {
    throw "Operational configuration requires exactly 46 rows; found $($rows.Count)."
}
$headers = @($rows[0].PSObject.Properties.Name)
$missingColumns = @($requiredColumns | Where-Object { $_ -notin $headers })
if ($missingColumns.Count -gt 0) {
    throw "Operational configuration is missing columns: $($missingColumns -join ', ')"
}

function Convert-Nullable([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '\N' }
    return $Value.Replace("`t", ' ').Replace("`r", ' ').Replace("`n", ' ')
}
function Convert-Boolean([string]$Value, [string]$Column, [string]$SiteCode) {
    switch ($Value.Trim().ToUpperInvariant()) {
        'YES' { return 'true' }
        'NO' { return 'false' }
        default { throw "$Column for $SiteCode must be YES or NO." }
    }
}
function Convert-Uuid([string]$Value, [string]$Column, [string]$SiteCode) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '\N' }
    $parsed = [guid]::Empty
    if (-not [guid]::TryParse($Value, [ref]$parsed) -or $parsed -eq [guid]::Empty) {
        throw "$Column for $SiteCode must be a non-empty UUID."
    }
    return $parsed.ToString('D')
}
function Test-SiteAdapterRoute($Row) {
    $configured = (Convert-Boolean $Row.hikcentral_target_configured 'hikcentral_target_configured' $Row.site_code) -eq 'true'
    $routeValues = @(
        $Row.site_adapter_base_url,
        $Row.site_adapter_environment_code,
        $Row.site_adapter_secret_reference,
        $Row.central_pms_service_identity_id
    )
    if (-not $configured) {
        if (@($routeValues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
            throw "Site Adapter routing fields for $($Row.site_code) require hikcentral_target_configured = YES."
        }
        return
    }
    if (@($routeValues | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "Configured HikCentral Site $($Row.site_code) requires a complete Site Adapter route."
    }
    $uri = $null
    if (-not [Uri]::TryCreate($Row.site_adapter_base_url, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https') -or [string]::IsNullOrWhiteSpace($uri.Host)) {
        throw "site_adapter_base_url for $($Row.site_code) must be an absolute HTTP(S) URL."
    }
    $environment = $Row.site_adapter_environment_code.Trim().ToUpperInvariant()
    if ($uri.Scheme -eq 'http' -and $environment -ne 'IST') {
        throw "HTTP Site Adapter routes are allowed only for the explicit IST environment: $($Row.site_code)."
    }
    if ($headers -contains 'hikcentral_base_url_ref' -and
        -not [string]::IsNullOrWhiteSpace($Row.hikcentral_base_url_ref) -and
        $uri.AbsoluteUri.TrimEnd('/') -eq $Row.hikcentral_base_url_ref.Trim().TrimEnd('/')) {
        throw "site_adapter_base_url for $($Row.site_code) must identify the Site Adapter, not its HikCentral upstream."
    }
    if (-not $Row.site_adapter_secret_reference.Trim().StartsWith('file:', [StringComparison]::OrdinalIgnoreCase)) {
        throw "site_adapter_secret_reference for $($Row.site_code) must use the mounted file: contract."
    }
    [void](Convert-Uuid $Row.central_pms_service_identity_id 'central_pms_service_identity_id' $Row.site_code)
}

foreach ($row in $rows) { Test-SiteAdapterRoute $row }

$copy = [Text.StringBuilder]::new()
[void]$copy.AppendLine(@'
\set ON_ERROR_STOP on
CREATE TEMP TABLE ep_ist_operational (
    site_id uuid, site_code text, operator_entity_code text, hikcentral_instance_code text,
    hikcentral_parking_lot_index_code text, hikcentral_parking_lot_name text,
    hikcentral_target_configured boolean, hikcentral_connectivity_verified boolean,
    site_adapter_base_url text, site_adapter_environment_code text,
    site_adapter_secret_reference text, central_pms_service_identity_id uuid,
    webpay_public_lookup_enabled boolean, webpay_payment_enabled boolean,
    fiscal_merchant_configured boolean, fiscal_supplier_configured boolean, fiscal_profile_approved boolean,
    pos_site_server_id uuid, fiscal_identity_id uuid, sales_invoice_profile_id uuid,
    paymongo_enabled boolean, last_verified_at timestamptz, verification_reference text
);
COPY ep_ist_operational FROM STDIN;
'@)
foreach ($row in $rows) {
    $siteId = Convert-Uuid $row.site_id 'site_id' $row.site_code
    $values = @(
        $siteId, (Convert-Nullable $row.site_code), (Convert-Nullable $row.operator_entity_code),
        (Convert-Nullable $row.hikcentral_instance_code), (Convert-Nullable $row.hikcentral_parking_lot_index_code),
        (Convert-Nullable $row.hikcentral_parking_lot_name),
        (Convert-Boolean $row.hikcentral_target_configured 'hikcentral_target_configured' $row.site_code),
        (Convert-Boolean $row.hikcentral_connectivity_verified 'hikcentral_connectivity_verified' $row.site_code),
        (Convert-Nullable $row.site_adapter_base_url), (Convert-Nullable $row.site_adapter_environment_code),
        (Convert-Nullable $row.site_adapter_secret_reference),
        (Convert-Uuid $row.central_pms_service_identity_id 'central_pms_service_identity_id' $row.site_code),
        (Convert-Boolean $row.webpay_public_lookup_enabled 'webpay_public_lookup_enabled' $row.site_code),
        (Convert-Boolean $row.webpay_payment_enabled 'webpay_payment_enabled' $row.site_code),
        (Convert-Boolean $row.fiscal_merchant_configured 'fiscal_merchant_configured' $row.site_code),
        (Convert-Boolean $row.fiscal_supplier_configured 'fiscal_supplier_configured' $row.site_code),
        (Convert-Boolean $row.fiscal_profile_approved 'fiscal_profile_approved' $row.site_code),
        (Convert-Uuid $row.pos_site_server_id 'pos_site_server_id' $row.site_code),
        (Convert-Uuid $row.fiscal_identity_id 'fiscal_identity_id' $row.site_code),
        (Convert-Uuid $row.sales_invoice_profile_id 'sales_invoice_profile_id' $row.site_code),
        (Convert-Boolean $row.paymongo_enabled 'paymongo_enabled' $row.site_code),
        (Convert-Nullable $row.last_verified_at), (Convert-Nullable $row.verification_reference)
    )
    [void]$copy.AppendLine($values -join "`t")
}
[void]$copy.AppendLine('\.')
$sqlPath = Join-Path $PSScriptRoot 'Update-PersistentRealSiteOperationalConfiguration.sql'
[void]$copy.AppendLine([IO.File]::ReadAllText($sqlPath))

if (-not $PSCmdlet.ShouldProcess("$ExitPassContainer/$ExitPassDatabase", 'Apply persistent real-Site operational configuration')) {
    Write-Output "Validated 46 operational configuration rows; no database changes applied."
    return
}

$process = [Diagnostics.Process]::new()
$process.StartInfo = [Diagnostics.ProcessStartInfo]@{
    FileName = 'docker.exe'
    Arguments = "exec -i $ExitPassContainer psql -X -q -v ON_ERROR_STOP=1 -U $DatabaseUser -d $ExitPassDatabase"
    UseShellExecute = $false
    RedirectStandardInput = $true
    RedirectStandardOutput = $true
    RedirectStandardError = $true
}
try {
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.StandardInput.Write($copy.ToString())
    $process.StandardInput.Close()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw "Operational configuration failed:`n$stdout`n$stderr" }
}
finally {
    $process.Dispose()
}

Write-Output "Applied persistent operational configuration for 46 real Sites."
