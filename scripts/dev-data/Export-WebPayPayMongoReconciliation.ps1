<#
.SYNOPSIS
Exports read-only WebPay PayMongo reconciliation diagnostics to CSV or JSON.

.DESCRIPTION
Operator wrapper for scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql.
The script does not mutate payment, provider, exit authorization, gate, audit,
event, or reconciliation records.
#>

param(
    [string] $TicketReference,
    [datetime] $FromDate,
    [datetime] $ToDate,
    [string] $ProviderCode = "PAYMONGO",
    [string] $OutputPath,
    [ValidateSet("csv", "json")]
    [string] $Format = "csv",
    [string] $DockerComposePath = "infra/docker",
    [string] $DatabaseName = "exitpass_v12_dev",
    [string] $DatabaseUser = "exitpass",
    [switch] $ScriptSelfTest
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName Microsoft.VisualBasic

$StableColumns = @(
    "ticket_reference",
    "parking_session_id",
    "payment_attempt_id",
    "provider_session_id",
    "payment_confirmation_id",
    "provider_webhook_event_count",
    "provider_callback_count",
    "provider_outcome_count",
    "payment_confirmation_count",
    "provider_code",
    "rail_code",
    "provider_session_status",
    "payment_attempt_status",
    "payment_confirmation_status",
    "provider_reference",
    "provider_transaction_reference",
    "amount",
    "confirmed_amount",
    "provider_amount_minor_units",
    "provider_amount",
    "currency_code",
    "provider_currency",
    "exit_authorization_id",
    "exit_authorization_status",
    "gate_consume_status",
    "gate_consume_count",
    "correlation_id",
    "reconciliation_classification",
    "reconciliation_reason"
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

function Resolve-DefaultOutputPath {
    param(
        [string] $RepoRoot,
        [string] $Format,
        [string] $TicketReference,
        [Nullable[datetime]] $FromDate,
        [Nullable[datetime]] $ToDate
    )

    $outputDirectory = Join-Path $RepoRoot "scripts/dev-data/.reconciliation-exports"
    if (-not (Test-Path $outputDirectory)) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
        $name = "webpay-paymongo-reconciliation-{0}.{1}" -f (ConvertTo-SafeFileName $TicketReference), $Format
        return Join-Path $outputDirectory $name
    }

    $fromText = $FromDate.Value.ToString("yyyy-MM-dd")
    $toText = $ToDate.Value.ToString("yyyy-MM-dd")
    $rangeName = "webpay-paymongo-reconciliation-{0}-{1}.{2}" -f $fromText, $toText, $Format
    return Join-Path $outputDirectory $rangeName
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

function ConvertFrom-DiagnosticsCsv {
    param([string[]] $Lines)

    $header = ($StableColumns -join ",")
    $headerIndex = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -eq $header) {
            $headerIndex = $index
        }
    }

    if ($headerIndex -lt 0) {
        throw "RECONCILIATION_RESULTSET_NOT_FOUND: expected final diagnostics header was not found."
    }

    $rows = @()
    for ($index = $headerIndex + 1; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line.StartsWith("Requested ticket_reference:", [StringComparison]::Ordinal) -or
            $line.StartsWith("Selected ", [StringComparison]::Ordinal)) {
            continue
        }

        $fields = @(Split-CsvLine $line)
        if ($fields.Count -ne $StableColumns.Count) {
            break
        }

        $row = [ordered]@{}
        for ($columnIndex = 0; $columnIndex -lt $StableColumns.Count; $columnIndex++) {
            $row[$StableColumns[$columnIndex]] = $fields[$columnIndex]
        }

        $rows += [PSCustomObject]$row
    }

    return @($rows)
}

function Get-Count {
    param(
        [object[]] $Rows,
        [scriptblock] $Predicate
    )

    return @($Rows | Where-Object $Predicate).Count
}

function New-Summary {
    param([object[]] $Rows)

    $mismatchStatuses = @(
        "AMOUNT_MISMATCH",
        "CURRENCY_MISMATCH",
        "DUPLICATE_PROVIDER_EVENT",
        "DUPLICATE_PAYMENT_CONFIRMATION",
        "EXITPASS_CONFIRMED_PROVIDER_MISSING",
        "PROVIDER_PAID_EXITPASS_MISSING",
        "STALE_PENDING_ATTEMPT",
        "CONFIRMED_WITHOUT_EXIT_AUTHORIZATION",
        "EXIT_AUTHORIZATION_WITHOUT_CONFIRMATION",
        "GATE_CONSUMED_WITHOUT_CONFIRMATION",
        "REQUESTED_TICKET_NOT_FOUND",
        "INCONCLUSIVE"
    )

    return [ordered]@{
        totalRows = @($Rows).Count
        matched = Get-Count $Rows { $_.reconciliation_classification -eq "MATCHED" }
        mismatch = Get-Count $Rows { $_.reconciliation_classification -in $mismatchStatuses }
        missingProviderEvidence = Get-Count $Rows { $_.reconciliation_classification -eq "EXITPASS_CONFIRMED_PROVIDER_MISSING" }
        missingExitPassConfirmation = Get-Count $Rows { $_.reconciliation_classification -eq "PROVIDER_PAID_EXITPASS_MISSING" }
        amountMismatch = Get-Count $Rows { $_.reconciliation_classification -eq "AMOUNT_MISMATCH" }
        currencyMismatch = Get-Count $Rows { $_.reconciliation_classification -eq "CURRENCY_MISMATCH" }
        duplicateEvidence = Get-Count $Rows { $_.reconciliation_classification -in @("DUPLICATE_PROVIDER_EVENT", "DUPLICATE_PAYMENT_CONFIRMATION") }
        stalePending = Get-Count $Rows { $_.reconciliation_classification -eq "STALE_PENDING_ATTEMPT" }
        missingExitAuthorization = Get-Count $Rows { $_.reconciliation_classification -eq "CONFIRMED_WITHOUT_EXIT_AUTHORIZATION" }
        gateWithoutConfirmation = Get-Count $Rows { $_.reconciliation_classification -eq "GATE_CONSUMED_WITHOUT_CONFIRMATION" }
    }
}

function Invoke-DiagnosticsExport {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $TicketReference,
        [Nullable[datetime]] $FromDate,
        [Nullable[datetime]] $ToDate,
        [string] $ProviderCode
    )

    $sqlPath = Join-Path $RepoRoot "scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql"
    if (-not (Test-Path $sqlPath)) {
        throw "RECONCILIATION_DIAGNOSTICS_SQL_NOT_FOUND: $sqlPath"
    }

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    $arguments = @(
        "compose",
        "exec",
        "-T",
        "postgres",
        "psql",
        "-U",
        $DatabaseUser,
        "-d",
        $DatabaseName,
        "-v",
        "ON_ERROR_STOP=1",
        "-v",
        "provider_code=$ProviderCode",
        "-P",
        "format=csv",
        "-P",
        "footer=off"
    )

    if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
        $arguments += @("-v", "ticket_reference=$TicketReference")
    }
    else {
        $arguments += @("-v", ("from_date={0}" -f $FromDate.Value.ToString("yyyy-MM-dd")))
        $arguments += @("-v", ("to_date={0}" -f $ToDate.Value.ToString("yyyy-MM-dd")))
    }

    $arguments += @("-f", "-")

    Push-Location $composePathResolved
    try {
        $output = Get-Content $sqlPath | & docker @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "RECONCILIATION_DIAGNOSTICS_FAILED: $message"
    }

    return @($output | ForEach-Object { $_.ToString() })
}

function Invoke-SelfTest {
    $repoRoot = Resolve-RepoRoot
    $sqlPath = Join-Path $repoRoot "scripts/dev-data/webpay-paymongo-reconciliation-diagnostics.sql"
    if (-not (Test-Path $sqlPath)) {
        throw "SELFTEST_FAILED: diagnostics SQL was not found."
    }

    if ($ProviderCode -ne "PAYMONGO") {
        throw "SELFTEST_FAILED: ProviderCode default changed from PAYMONGO."
    }

    $testPath = Resolve-DefaultOutputPath `
        -RepoRoot $repoRoot `
        -Format "csv" `
        -TicketReference "SELFTEST" `
        -FromDate $null `
        -ToDate $null

    $directory = Split-Path -Parent $testPath
    if (-not (Test-Path $directory)) {
        throw "SELFTEST_FAILED: export directory was not created."
    }

    $scriptText = Get-Content $PSCommandPath -Raw
    $outOfScopeProviderToken = -join ([char[]](65, 85, 66))
    if ($scriptText -match $outOfScopeProviderToken) {
        throw "SELFTEST_FAILED: export script contains an out-of-scope provider reference."
    }

    Write-Host "SELFTEST PASS"
}

if ($ScriptSelfTest) {
    Invoke-SelfTest
    exit 0
}

if ($ProviderCode -ne "PAYMONGO") {
    throw "UNSUPPORTED_PROVIDER_CODE: this WebPay export wrapper supports PAYMONGO only."
}

if ([string]::IsNullOrWhiteSpace($TicketReference)) {
    if (-not $PSBoundParameters.ContainsKey("FromDate") -and -not $PSBoundParameters.ContainsKey("ToDate")) {
        throw "MISSING_RECONCILIATION_SCOPE: supply -TicketReference or both -FromDate and -ToDate."
    }

    if (-not $PSBoundParameters.ContainsKey("FromDate") -or -not $PSBoundParameters.ContainsKey("ToDate")) {
        throw "MISSING_RECONCILIATION_SCOPE: date range exports require both -FromDate and -ToDate."
    }
}

Assert-CommandExists "docker"

$repoRoot = Resolve-RepoRoot
$resolvedOutputPath = $OutputPath
if ([string]::IsNullOrWhiteSpace($resolvedOutputPath)) {
    $resolvedOutputPath = Resolve-DefaultOutputPath `
        -RepoRoot $repoRoot `
        -Format $Format `
        -TicketReference $TicketReference `
        -FromDate $FromDate `
        -ToDate $ToDate
}
else {
    $parent = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
}

if (-not [string]::IsNullOrWhiteSpace($TicketReference)) {
    Write-Host "Scope: ticketReference=$TicketReference"
}
else {
    Write-Host ("Scope: fromDate={0}; toDate={1}" -f $FromDate.ToString("yyyy-MM-dd"), $ToDate.ToString("yyyy-MM-dd"))
}

$diagnosticLines = Invoke-DiagnosticsExport `
    -RepoRoot $repoRoot `
    -DockerComposePath $DockerComposePath `
    -DatabaseName $DatabaseName `
    -DatabaseUser $DatabaseUser `
    -TicketReference $TicketReference `
    -FromDate $FromDate `
    -ToDate $ToDate `
    -ProviderCode $ProviderCode

$rows = @(ConvertFrom-DiagnosticsCsv $diagnosticLines)
$summary = New-Summary $rows

if ($Format -eq "csv") {
    $rows | Select-Object $StableColumns | Export-Csv -Path $resolvedOutputPath -NoTypeInformation -Encoding UTF8
}
else {
    $document = [ordered]@{
        metadata = [ordered]@{
            generatedAt = (Get-Date).ToUniversalTime().ToString("o")
            providerCode = $ProviderCode
            ticketReference = $(if ([string]::IsNullOrWhiteSpace($TicketReference)) { $null } else { $TicketReference })
            fromDate = $(if ([string]::IsNullOrWhiteSpace($TicketReference)) { $FromDate.ToString("yyyy-MM-dd") } else { $null })
            toDate = $(if ([string]::IsNullOrWhiteSpace($TicketReference)) { $ToDate.ToString("yyyy-MM-dd") } else { $null })
            totalRows = $summary.totalRows
            summary = $summary
        }
        rows = $rows
    }

    $document | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedOutputPath -Encoding UTF8
}

$file = Get-Item $resolvedOutputPath
Write-Host ""
Write-Host "Reconciliation export complete"
Write-Host "Output path: $($file.FullName)"
Write-Host "Format: $Format"
Write-Host "Provider: $ProviderCode"
Write-Host "Total rows: $($summary.totalRows)"
Write-Host "MATCHED count: $($summary.matched)"
Write-Host "Mismatch count: $($summary.mismatch)"
Write-Host "Missing provider evidence count: $($summary.missingProviderEvidence)"
Write-Host "Missing ExitPass confirmation count: $($summary.missingExitPassConfirmation)"
Write-Host "Amount mismatch count: $($summary.amountMismatch)"
Write-Host "Currency mismatch count: $($summary.currencyMismatch)"
Write-Host "Duplicate evidence count: $($summary.duplicateEvidence)"
Write-Host "Stale pending count: $($summary.stalePending)"
Write-Host "Missing exit authorization count: $($summary.missingExitAuthorization)"
Write-Host "Gate-without-confirmation count: $($summary.gateWithoutConfirmation)"
