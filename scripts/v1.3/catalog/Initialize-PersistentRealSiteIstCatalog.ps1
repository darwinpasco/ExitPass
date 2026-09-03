[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$DatabaseObjectRepositoryRoot = 'D:\SourceCodes\exitpassdb_v1.2',
    [string]$PosServerRepositoryRoot = 'D:\SourceCodes\ExitPass-PoSServer',
    [string]$ExitPassContainer = 'exitpass-ist-persistent-db',
    [string]$ExitPassVolume = 'exitpass-ist-persistent-data',
    [string]$ExitPassDatabase = 'exitpass_ist',
    [string]$PosContainer = 'exitpass-pos-ist-persistent-db',
    [string]$PosVolume = 'exitpass-pos-ist-persistent-data',
    [string]$PosDatabase = 'exitpass_pos_ist',
    [string]$DatabaseUser = 'exitpass_ist',
    [string]$Network = 'exitpass-ist-persistent',
    [string]$PostgresImage = 'postgres:16-alpine',
    [string]$EvidenceDirectory,
    [switch]$SkipProvision
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
$password = [Environment]::GetEnvironmentVariable('EXITPASS_IST_DB_PASSWORD')
if ([string]::IsNullOrWhiteSpace($password)) {
    throw 'EXITPASS_IST_DB_PASSWORD must be set for the persistent IST databases.'
}

function Invoke-Docker {
    param([string[]]$Arguments, [switch]$IgnoreExitCode)
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = @(& docker @Arguments 2>&1) }
    finally { $ErrorActionPreference = $previousErrorPreference }
    $script:LastDockerExitCode = $LASTEXITCODE
    if (-not $IgnoreExitCode -and $script:LastDockerExitCode -ne 0) {
        throw "docker $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Test-DockerObject {
    param([string]$Kind, [string]$Name)
    [void](Invoke-Docker @($Kind, 'inspect', $Name) -IgnoreExitCode)
    return $script:LastDockerExitCode -eq 0
}

function Wait-Postgres {
    param([string]$Container)
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        [void](Invoke-Docker @('exec', $Container, 'pg_isready', '-U', $DatabaseUser) -IgnoreExitCode)
        if ($script:LastDockerExitCode -eq 0) { return }
        Start-Sleep -Seconds 1
    }
    throw "PostgreSQL container '$Container' did not become ready."
}

function Invoke-PsqlText {
    param([string]$Container, [string]$Database, [string]$Sql)
    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = 'docker.exe'
    $startInfo.Arguments = "exec -i $Container psql -X -qAt -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($Sql)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "psql failed for '$Container/$Database':`n$stdout`n$stderr"
        }
        return @(($stdout + $stderr) -split "`r?`n" | Where-Object { $_ -ne '' })
    }
    finally { $process.Dispose() }
}

function Invoke-PsqlFile {
    param([string]$Container, [string]$Database, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required SQL source is missing: $Path" }
    return Invoke-PsqlText $Container $Database ([IO.File]::ReadAllText($Path))
}

function Ensure-PostgresContainer {
    param([string]$Container, [string]$Volume, [string]$Database, [string]$Role)
    if (-not (Test-DockerObject 'volume' $Volume)) {
        [void](Invoke-Docker @('volume', 'create', '--label', 'com.exitpass.ist.persistence=true', '--label', "com.exitpass.ist.role=$Role", $Volume))
    }
    if (-not (Test-DockerObject 'container' $Container)) {
        [void](Invoke-Docker @(
            'run', '-d', '--name', $Container, '--network', $Network, '--restart', 'unless-stopped',
            '--label', 'com.exitpass.ist.persistence=true', '--label', "com.exitpass.ist.role=$Role",
            '-e', "POSTGRES_USER=$DatabaseUser", '-e', "POSTGRES_PASSWORD=$password", '-e', "POSTGRES_DB=$Database",
            '-v', "${Volume}:/var/lib/postgresql/data", $PostgresImage))
    }
    else {
        [void](Invoke-Docker @('start', $Container))
    }
    Wait-Postgres $Container
}

function ConvertTo-PostgresCopyBlock {
    param([string]$Table, [object[]]$Rows, [string[]]$Properties)
    $selected = @($Rows | Select-Object -Property $Properties)
    $csv = $selected | ConvertTo-Csv -NoTypeInformation
    return "COPY $Table ($($Properties -join ', ')) FROM STDIN WITH (FORMAT csv, HEADER true);`n$($csv -join "`n")`n\.`n"
}

if (-not $SkipProvision) {
    if (-not (Test-DockerObject 'network' $Network)) {
        [void](Invoke-Docker @('network', 'create', '--label', 'com.exitpass.ist.persistence=true', $Network))
    }
    Ensure-PostgresContainer $ExitPassContainer $ExitPassVolume $ExitPassDatabase 'exitpass-db'
    Ensure-PostgresContainer $PosContainer $PosVolume $PosDatabase 'pos-db'
}
else {
    Wait-Postgres $ExitPassContainer
    Wait-Postgres $PosContainer
}

$schemaExists = (Invoke-PsqlText $ExitPassContainer $ExitPassDatabase "SELECT to_regclass('sites.sites') IS NOT NULL;") -match '^t$'
if (-not $schemaExists) {
    Invoke-PsqlFile $ExitPassContainer $ExitPassDatabase (Join-Path $DatabaseObjectRepositoryRoot 'build\generated\exitpass-full-object.generated.sql') | Out-Null
}

Invoke-PsqlFile $ExitPassContainer $ExitPassDatabase (Join-Path $RepositoryRoot 'infra\db\patches\ExitPass_Core_PaymentAttemptPaymentMethod_v1.3.sql') | Out-Null
$catalogCount = [int]((Invoke-PsqlText $ExitPassContainer $ExitPassDatabase "SELECT count(*) FROM sites.sites WHERE site_id IN (SELECT site_id FROM sites.sites WHERE site_code IN ('PITX-LEVEL-3','PITX-OPEN-LOT'));") | Where-Object { $_ -match '^\d+$' } | Select-Object -Last 1)
if ($catalogCount -eq 0) {
    Invoke-PsqlFile $ExitPassContainer $ExitPassDatabase (Join-Path $DatabaseObjectRepositoryRoot 'objects\reference-data\sites.realistic-carpark-catalog.seed.sql') | Out-Null
}
elseif ($catalogCount -ne 2) {
    throw "The canonical real-Site seed is partially present ($catalogCount of 2 PITX sentinels); refusing mutation."
}

$posSchemaExists = (Invoke-PsqlText $PosContainer $PosDatabase "SELECT to_regclass('pos.fiscal_documents') IS NOT NULL;") -match '^t$'
if (-not $posSchemaExists) {
    $orderPath = Join-Path $PosServerRepositoryRoot 'db\rebuild\pos_sql_apply_order.txt'
    foreach ($relativePath in [IO.File]::ReadAllLines($orderPath)) {
        $trimmed = $relativePath.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        Invoke-PsqlFile $PosContainer $PosDatabase (Join-Path $PosServerRepositoryRoot $trimmed) | Out-Null
    }
}

$dataRoot = Join-Path $RepositoryRoot 'docs\v1.3\central-pms\seed-manifests\data'
$groupsPath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Groups_v1.0.csv'
$sitesPath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Sites_v1.0.csv'
$assignmentsPath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Jurisdiction_Assignments_v1.0.csv'
$coveragePath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Statutory_Discount_Coverage_v1.0.csv'
$groups = @(Import-Csv -LiteralPath $groupsPath -Encoding UTF8)
$sites = @(Import-Csv -LiteralPath $sitesPath -Encoding UTF8)
$assignments = @(Import-Csv -LiteralPath $assignmentsPath -Encoding UTF8)
$coverage = @(Import-Csv -LiteralPath $coveragePath -Encoding UTF8)
$groupsHash = (Get-FileHash -LiteralPath $groupsPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sitesHash = (Get-FileHash -LiteralPath $sitesPath -Algorithm SHA256).Hash.ToLowerInvariant()
$coverageHash = (Get-FileHash -LiteralPath $coveragePath -Algorithm SHA256).Hash.ToLowerInvariant()

$groupRows = @($groups | ForEach-Object { [pscustomobject]@{
    site_group_id=$_.site_group_id; site_group_code=$_.site_group_code; source_manifest_sha256=$groupsHash
} })
$siteRows = @($sites | ForEach-Object { [pscustomobject]@{
    site_id=$_.site_id; site_group_id=$_.site_group_id; site_code=$_.site_code; source_workbook=$_.source_workbook;
    source_sheet=$_.source_sheet; source_row=[int]$_.source_row; source_manifest_sha256=$sitesHash
} })
$assignmentRows = @($assignments | ForEach-Object { [pscustomobject]@{
    assignment_id=$_.site_jurisdiction_assignment_id; site_id=$_.site_id; jurisdiction_id=$_.jurisdiction_id
} })
$coverageRows = @($coverage | ForEach-Object { [pscustomobject]@{
    jurisdiction_id=$_.jurisdiction_id; jurisdiction_code=$_.jurisdiction_code;
    jurisdiction_display_name=$_.jurisdiction_display_name; entitlement_type=$_.entitlement_type;
    parking_policy_identified=$_.parking_policy_identified; benefit_type=$_.benefit_type;
    free_period_minutes=$_.free_period_minutes; discount_percent=$_.discount_percent; residency_scope=$_.residency_scope;
    ordinance_or_authority_reference=$_.ordinance_or_authority_reference; ordinance_number_status=$_.ordinance_number_status;
    source_quality_classification=$_.source_quality_classification;
    operational_verification_status=$_.operational_verification_status; legal_review_status=$_.legal_review_status;
    runtime_publication_eligibility=$_.proposed_runtime_publication_eligibility;
    manual_review_required=$_.manual_review_required; source_reference=$_.repository_source_reference;
    notes=$_.notes; source_manifest_sha256=$coverageHash
} })

$preamble = @"
CREATE TEMP TABLE ep_ist_groups (site_group_id uuid, site_group_code text, source_manifest_sha256 text);
CREATE TEMP TABLE ep_ist_sites (site_id uuid, site_group_id uuid, site_code text, source_workbook text, source_sheet text, source_row integer, source_manifest_sha256 text);
CREATE TEMP TABLE ep_ist_assignments (assignment_id uuid, site_id uuid, jurisdiction_id uuid);
CREATE TEMP TABLE ep_ist_coverage (
  jurisdiction_id uuid, jurisdiction_code text, jurisdiction_display_name text, entitlement_type text,
  parking_policy_identified boolean, benefit_type text, free_period_minutes text, discount_percent text,
  residency_scope text, ordinance_or_authority_reference text, ordinance_number_status text,
  source_quality_classification text, operational_verification_status text, legal_review_status text,
  runtime_publication_eligibility text, manual_review_required boolean, source_reference text, notes text,
  source_manifest_sha256 text);
"@
$preamble += ConvertTo-PostgresCopyBlock 'ep_ist_groups' $groupRows @('site_group_id','site_group_code','source_manifest_sha256')
$preamble += ConvertTo-PostgresCopyBlock 'ep_ist_sites' $siteRows @('site_id','site_group_id','site_code','source_workbook','source_sheet','source_row','source_manifest_sha256')
$preamble += ConvertTo-PostgresCopyBlock 'ep_ist_assignments' $assignmentRows @('assignment_id','site_id','jurisdiction_id')
$preamble += ConvertTo-PostgresCopyBlock 'ep_ist_coverage' $coverageRows @(
    'jurisdiction_id','jurisdiction_code','jurisdiction_display_name','entitlement_type','parking_policy_identified',
    'benefit_type','free_period_minutes','discount_percent','residency_scope','ordinance_or_authority_reference',
    'ordinance_number_status','source_quality_classification','operational_verification_status','legal_review_status',
    'runtime_publication_eligibility','manual_review_required','source_reference','notes','source_manifest_sha256')
$activationSql = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Initialize-PersistentRealSiteIstCatalog.sql'))
$result = Invoke-PsqlText $ExitPassContainer $ExitPassDatabase ($preamble + $activationSql)

if (-not [string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null
    @{
        initialized_at=(Get-Date).ToUniversalTime().ToString('o')
        exitpass_container=$ExitPassContainer; exitpass_volume=$ExitPassVolume; exitpass_database=$ExitPassDatabase
        pos_container=$PosContainer; pos_volume=$PosVolume; pos_database=$PosDatabase; network=$Network
        site_groups=$groups.Count; sites=$sites.Count; assignments=$assignments.Count; statutory_coverage=$coverage.Count
        source_hashes=@{site_groups=$groupsHash; sites=$sitesHash; statutory_coverage=$coverageHash}
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'initialization-result.json') -Encoding UTF8
    $result | Set-Content -LiteralPath (Join-Path $EvidenceDirectory 'initializer-psql-output.txt') -Encoding UTF8
}

Write-Output "Persistent real-Site IST catalog initialized: 39 groups, 46 Sites, 46 active assignments, 26 coverage rows."
