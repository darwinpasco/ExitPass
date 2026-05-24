<#
.SYNOPSIS
Lists persisted WebPay PayMongo reconciliation runs and reviews exceptions.

.DESCRIPTION
Read-only operator helper for persisted WebPay PayMongo reconciliation evidence.
It lists runs, reads exception details, and exports exception review data without
mutating payment, provider, exit authorization, gate, audit, event, settlement,
payout, or reconciliation records.
#>

param(
    [switch] $ListRuns,
    [string] $RunId,
    [string] $RunCode,
    [string] $ProviderCode = "PAYMONGO",
    [datetime] $FromDate,
    [datetime] $ToDate,
    [int] $Limit = 20,
    [string] $Classification,
    [string] $TicketReference,
    [string] $ExceptionStatus,
    [string] $Severity,
    [ValidateSet("table", "csv", "json")]
    [string] $Format = "table",
    [string] $OutputPath,
    [string] $DockerComposePath = "infra/docker",
    [string] $DatabaseName = "exitpass_v12_dev",
    [string] $DatabaseUser = "exitpass",
    [switch] $ScriptSelfTest
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName Microsoft.VisualBasic

$RunColumns = @(
    "reconciliation_run_id",
    "run_code",
    "provider_code",
    "run_type",
    "run_status",
    "scope_type",
    "source_batch_ref",
    "window_start_at",
    "window_end_at",
    "item_count",
    "matched_count",
    "exception_count",
    "started_at",
    "completed_at",
    "created_at"
)

$ExceptionColumns = @(
    "result_status",
    "reconciliation_run_id",
    "run_code",
    "provider_code",
    "run_status",
    "scope_type",
    "source_batch_ref",
    "item_count",
    "matched_count",
    "exception_count",
    "run_created_at",
    "started_at",
    "completed_at",
    "reconciliation_exception_id",
    "reconciliation_item_id",
    "ticket_reference",
    "classification",
    "exception_reason_code",
    "exception_summary",
    "exception_detail",
    "exception_type",
    "exception_severity",
    "exception_status",
    "payment_attempt_id",
    "provider_session_id",
    "provider_session_status",
    "payment_confirmation_id",
    "payment_confirmation_status",
    "expected_amount",
    "actual_amount",
    "currency_code",
    "variance_amount",
    "exit_authorization_id",
    "exit_authorization_status",
    "gate_consume_status",
    "gate_consume_count",
    "detected_at",
    "assigned_at",
    "resolved_at",
    "closed_at",
    "correlation_id",
    "exception_created_at"
)

function Resolve-RepoRoot {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $MyInvocation.MyCommand.Path
    }

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        return (Get-Location).Path
    }

    return (Resolve-Path (Join-Path (Split-Path -Parent $scriptPath) "..\..")).Path
}

function Assert-CommandExists {
    param([string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "REQUIRED_COMMAND_NOT_FOUND: $Name"
    }
}

function ConvertTo-SafeFileName {
    param([string] $Value)

    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    $safe = $Value
    foreach ($character in $invalid) {
        $safe = $safe.Replace($character, "-")
    }

    return $safe
}

function Split-CsvLine {
    param([string] $Line)

    $reader = [System.IO.StringReader]::new($Line)
    try {
        $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($reader)
        try {
            $parser.SetDelimiters(",")
            $parser.HasFieldsEnclosedInQuotes = $true
            return @($parser.ReadFields())
        }
        finally {
            $parser.Dispose()
        }
    }
    finally {
        $reader.Dispose()
    }
}

function ConvertFrom-StableCsv {
    param(
        [string[]] $Lines,
        [string[]] $Columns
    )

    $header = ($Columns -join ",")
    $headerIndex = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -eq $header) {
            $headerIndex = $index
        }
    }

    if ($headerIndex -lt 0) {
        throw "RESULTSET_NOT_FOUND: expected CSV header was not found."
    }

    $rows = @()
    for ($index = $headerIndex + 1; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $fields = @(Split-CsvLine $line)
        if ($fields.Count -ne $Columns.Count) {
            break
        }

        $row = [ordered]@{}
        for ($columnIndex = 0; $columnIndex -lt $Columns.Count; $columnIndex++) {
            $row[$Columns[$columnIndex]] = $fields[$columnIndex]
        }

        $rows += [PSCustomObject]$row
    }

    return @($rows)
}

function Resolve-ExportPath {
    param(
        [string] $RepoRoot,
        [string] $Format,
        [string] $RunId,
        [string] $RunCode
    )

    $outputDirectory = Join-Path $RepoRoot "scripts/dev-data/.reconciliation-exports"
    if (-not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    $token = if (-not [string]::IsNullOrWhiteSpace($RunCode)) {
        $RunCode
    }
    elseif (-not [string]::IsNullOrWhiteSpace($RunId)) {
        $RunId
    }
    else {
        "recent-runs"
    }

    $fileName = "webpay-paymongo-reconciliation-exceptions-{0}.{1}" -f (ConvertTo-SafeFileName $token), $Format
    return Join-Path $outputDirectory $fileName
}

function Invoke-DockerPsql {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string[]] $Arguments,
        [string] $InputSql
    )

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    Push-Location $composePathResolved
    try {
        $output = $InputSql | & docker @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "PSQL_QUERY_FAILED: $message"
    }

    return @($output | ForEach-Object { $_.ToString() })
}

function Invoke-ListRuns {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $ProviderCode,
        [Nullable[datetime]] $FromDate,
        [Nullable[datetime]] $ToDate,
        [int] $Limit
    )

    $sql = @"
WITH requested AS (
    SELECT
        NULLIF(:'provider_code', '') AS provider_code,
        NULLIF(:'from_date', '')::date AS from_date,
        NULLIF(:'to_date', '')::date AS to_date,
        GREATEST(NULLIF(:'limit', '')::integer, 1) AS row_limit
)
SELECT
    rr.reconciliation_run_id,
    rr.run_code,
    split_part(rr.source_batch_ref, ';', 1) AS provider_code,
    rr.run_type::text AS run_type,
    rr.run_status::text AS run_status,
    rr.scope_type::text AS scope_type,
    rr.source_batch_ref,
    rr.window_start_at,
    rr.window_end_at,
    rr.item_count,
    rr.matched_count,
    rr.exception_count,
    rr.started_at,
    rr.completed_at,
    rr.created_at
FROM reconciliation.reconciliation_runs rr
CROSS JOIN requested req
WHERE rr.source_batch_ref LIKE (req.provider_code || ';%')
  AND (req.from_date IS NULL OR rr.created_at >= req.from_date::timestamptz)
  AND (req.to_date IS NULL OR rr.created_at < (req.to_date + 1)::timestamptz)
ORDER BY rr.created_at DESC, rr.reconciliation_run_id DESC
LIMIT (SELECT row_limit FROM requested);
"@

    $arguments = @(
        "compose", "exec", "-T", "postgres", "psql",
        "-U", $DatabaseUser,
        "-d", $DatabaseName,
        "-v", "ON_ERROR_STOP=1",
        "-v", "provider_code=$ProviderCode",
        "-v", ("from_date={0}" -f $(if ($FromDate.HasValue) { $FromDate.Value.ToString("yyyy-MM-dd") } else { "" })),
        "-v", ("to_date={0}" -f $(if ($ToDate.HasValue) { $ToDate.Value.ToString("yyyy-MM-dd") } else { "" })),
        "-v", "limit=$Limit",
        "-P", "format=csv",
        "-P", "footer=off"
    )

    return Invoke-DockerPsql `
        -RepoRoot $RepoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -Arguments $arguments `
        -InputSql $sql
}

function Invoke-ReadExceptions {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $RunId,
        [string] $RunCode,
        [string] $ProviderCode,
        [string] $Classification,
        [string] $TicketReference,
        [string] $ExceptionStatus,
        [string] $Severity
    )

    $sqlPath = Join-Path $RepoRoot "scripts/dev-data/read-webpay-paymongo-reconciliation-exceptions.sql"
    if (-not (Test-Path $sqlPath)) {
        throw "EXCEPTION_READ_SQL_NOT_FOUND: $sqlPath"
    }

    $arguments = @(
        "compose", "exec", "-T", "postgres", "psql",
        "-U", $DatabaseUser,
        "-d", $DatabaseName,
        "-v", "ON_ERROR_STOP=1",
        "-v", "provider_code=$ProviderCode",
        "-v", "run_id=$RunId",
        "-v", "run_code=$RunCode",
        "-v", "classification=$Classification",
        "-v", "ticket_reference=$TicketReference",
        "-v", "exception_status=$ExceptionStatus",
        "-v", "severity=$Severity",
        "-P", "format=csv",
        "-P", "footer=off",
        "-f", "-"
    )

    $sql = Get-Content $sqlPath -Raw
    return Invoke-DockerPsql `
        -RepoRoot $RepoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -Arguments $arguments `
        -InputSql $sql
}

function Write-Export {
    param(
        [object[]] $Rows,
        [string[]] $Columns,
        [string] $OutputPath,
        [string] $Format,
        [string] $ProviderCode,
        [string] $RunId,
        [string] $RunCode,
        [hashtable] $Filters
    )

    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    if ($Format -eq "csv") {
        $Rows | Select-Object $Columns | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8
    }
    elseif ($Format -eq "json") {
        $document = [ordered]@{
            metadata = [ordered]@{
                generatedAt = (Get-Date).ToUniversalTime().ToString("o")
                providerCode = $ProviderCode
                runId = $(if ([string]::IsNullOrWhiteSpace($RunId)) { $null } else { $RunId })
                runCode = $(if ([string]::IsNullOrWhiteSpace($RunCode)) { $null } else { $RunCode })
                filters = $Filters
                totalExceptions = @($Rows | Where-Object { $_.result_status -eq "EXCEPTION" }).Count
            }
            rows = $Rows
        }

        $document | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding UTF8
    }
}

function Invoke-SelfTest {
    $repoRoot = Resolve-RepoRoot
    $sqlPath = Join-Path $repoRoot "scripts/dev-data/read-webpay-paymongo-reconciliation-exceptions.sql"
    if (-not (Test-Path $sqlPath)) {
        throw "SELFTEST_FAILED: exception read SQL was not found."
    }

    if ($ProviderCode -ne "PAYMONGO") {
        throw "SELFTEST_FAILED: ProviderCode default changed from PAYMONGO."
    }

    $exportPath = Resolve-ExportPath `
        -RepoRoot $repoRoot `
        -Format "csv" `
        -RunId "" `
        -RunCode "SELFTEST"

    $directory = Split-Path -Parent $exportPath
    if (-not (Test-Path $directory)) {
        throw "SELFTEST_FAILED: export directory was not created."
    }

    $scriptText = Get-Content $PSCommandPath -Raw
    $sqlText = Get-Content $sqlPath -Raw
    $outOfScopeProviderToken = -join ([char[]](65, 85, 66))
    if ($scriptText -match $outOfScopeProviderToken -or $sqlText -match $outOfScopeProviderToken) {
        throw "SELFTEST_FAILED: out-of-scope provider reference found."
    }

    Write-Host "SELFTEST PASS"
}

if ($ScriptSelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($ProviderCode -ne "PAYMONGO") {
    throw "UNSUPPORTED_PROVIDER_CODE: this exception review wrapper supports PAYMONGO only."
}

if ($Limit -lt 1) {
    throw "INVALID_LIMIT: Limit must be greater than zero."
}

Assert-CommandExists "docker"
$repoRoot = Resolve-RepoRoot

if ($ListRuns) {
    $lines = Invoke-ListRuns `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -ProviderCode $ProviderCode `
        -FromDate $(if ($PSBoundParameters.ContainsKey("FromDate")) { $FromDate } else { $null }) `
        -ToDate $(if ($PSBoundParameters.ContainsKey("ToDate")) { $ToDate } else { $null }) `
        -Limit $Limit

    $rows = @(ConvertFrom-StableCsv -Lines $lines -Columns $RunColumns)
    if ($Format -eq "table") {
        Write-Host "Recent WebPay PayMongo reconciliation runs"
        Write-Host "Provider: $ProviderCode"
        Write-Host "Rows: $($rows.Count)"
        $rows | Format-Table run_code, run_status, scope_type, source_batch_ref, item_count, matched_count, exception_count, created_at -AutoSize
    }
    else {
        $resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
            Resolve-ExportPath -RepoRoot $repoRoot -Format $Format -RunId "" -RunCode "recent-runs"
        }
        else {
            $OutputPath
        }

        Write-Export `
            -Rows $rows `
            -Columns $RunColumns `
            -OutputPath $resolvedOutputPath `
            -Format $Format `
            -ProviderCode $ProviderCode `
            -RunId "" `
            -RunCode "recent-runs" `
            -Filters @{ fromDate = $(if ($PSBoundParameters.ContainsKey("FromDate")) { $FromDate.ToString("yyyy-MM-dd") } else { $null }); toDate = $(if ($PSBoundParameters.ContainsKey("ToDate")) { $ToDate.ToString("yyyy-MM-dd") } else { $null }); limit = $Limit }

        Write-Host "Reconciliation run export complete"
        Write-Host "Output path: $((Get-Item $resolvedOutputPath).FullName)"
        Write-Host "Rows: $($rows.Count)"
    }

    exit 0
}

if ([string]::IsNullOrWhiteSpace($RunId) -and [string]::IsNullOrWhiteSpace($RunCode)) {
    throw "MISSING_RECONCILIATION_RUN_SCOPE: supply -RunId, -RunCode, or -ListRuns."
}

$exceptionLines = Invoke-ReadExceptions `
    -RepoRoot $repoRoot `
    -DockerComposePath $DockerComposePath `
    -DatabaseName $DatabaseName `
    -DatabaseUser $DatabaseUser `
    -RunId $RunId `
    -RunCode $RunCode `
    -ProviderCode $ProviderCode `
    -Classification $Classification `
    -TicketReference $TicketReference `
    -ExceptionStatus $ExceptionStatus `
    -Severity $Severity

$exceptionRows = @(ConvertFrom-StableCsv -Lines $exceptionLines -Columns $ExceptionColumns)
if ($exceptionRows.Count -eq 0) {
    throw "NO_RECONCILIATION_REVIEW_ROWS"
}

$firstStatus = $exceptionRows[0].result_status
if ($firstStatus -eq "RECONCILIATION_RUN_NOT_FOUND") {
    Write-Host "RECONCILIATION_RUN_NOT_FOUND"
    Write-Host "No fallback reconciliation run was selected."
    exit 0
}

$exceptionCount = @($exceptionRows | Where-Object { $_.result_status -eq "EXCEPTION" }).Count

if ($Format -eq "table") {
    Write-Host "WebPay PayMongo reconciliation exception review"
    Write-Host "Provider: $ProviderCode"
    Write-Host "Run id: $($exceptionRows[0].reconciliation_run_id)"
    Write-Host "Run code: $($exceptionRows[0].run_code)"
    Write-Host "Run status: $($exceptionRows[0].run_status)"
    Write-Host "Run exception count: $($exceptionRows[0].exception_count)"
    Write-Host "Filtered exception rows: $exceptionCount"

    if ($firstStatus -eq "NO_RECONCILIATION_EXCEPTIONS") {
        Write-Host "NO_RECONCILIATION_EXCEPTIONS"
        exit 0
    }

    $exceptionRows |
        Where-Object { $_.result_status -eq "EXCEPTION" } |
        Format-Table reconciliation_exception_id, ticket_reference, classification, exception_status, exception_severity, exception_reason_code, exception_created_at -AutoSize
}
else {
    $resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        Resolve-ExportPath -RepoRoot $repoRoot -Format $Format -RunId $RunId -RunCode $RunCode
    }
    else {
        $OutputPath
    }

    Write-Export `
        -Rows $exceptionRows `
        -Columns $ExceptionColumns `
        -OutputPath $resolvedOutputPath `
        -Format $Format `
        -ProviderCode $ProviderCode `
        -RunId $RunId `
        -RunCode $RunCode `
        -Filters @{ classification = $Classification; ticketReference = $TicketReference; exceptionStatus = $ExceptionStatus; severity = $Severity }

    Write-Host "Reconciliation exception export complete"
    Write-Host "Output path: $((Get-Item $resolvedOutputPath).FullName)"
    Write-Host "Provider: $ProviderCode"
    Write-Host "Run id: $($exceptionRows[0].reconciliation_run_id)"
    Write-Host "Run code: $($exceptionRows[0].run_code)"
    Write-Host "Total exceptions: $exceptionCount"
    if ($firstStatus -eq "NO_RECONCILIATION_EXCEPTIONS") {
        Write-Host "NO_RECONCILIATION_EXCEPTIONS"
    }
}
