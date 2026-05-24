<#
.SYNOPSIS
    Generates date-specific WebPay seed and diagnostics SQL files from an existing validated WebPay seed pair.

.DESCRIPTION
    This script is intended for ExitPass v1.2 WebPay runtime validation.

    It creates:
      - scripts/dev-data/webpay-YYYYMMDD-seed.sql
      - scripts/dev-data/webpay-YYYYMMDD-payment-finalization-diagnostics.sql

    It is deliberately simple:
      - It does NOT weaken tariff validity rules.
      - It creates a new day-specific seed by replacing the source date tokens.
      - It preserves historical seed files.
      - It does NOT touch AUB.
      - QRPH/PHP must remain PAYMONGO-only in the generated seed.

.PARAMETER SourceDate
    Existing seed date to use as the template source. Example: 2026-05-21

.PARAMETER TargetDate
    New runtime validation date. Example: 2026-05-23

.PARAMETER RepoRoot
    ExitPass repository root.

.PARAMETER Execute
    If set, runs the generated seed against the local Docker PostgreSQL database.

.PARAMETER Force
    If set, overwrites existing generated target files.

.EXAMPLE
    .\scripts\dev-data\New-WebPayDailySeed.ps1 -SourceDate 2026-05-21 -TargetDate 2026-05-23 -Execute

.NOTES
    Standing ExitPass rule:
    Do not select, route to, configure, or invoke AUB for WebPay QRPH work.
    AUB is out of scope until the AUB integration slice.
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string] $SourceDate,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{4}-\d{2}-\d{2}$')]
    [string] $TargetDate,

    [string] $RepoRoot = "D:\SourceCodes\ExitPass",

    [switch] $Execute,

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Convert-DateToStamp {
    param([string] $Date)
    return $Date.Replace("-", "")
}

function Get-DateTokenVariants {
    param([string] $Date)

    $parsed = [DateTime]::ParseExact(
        $Date,
        "yyyy-MM-dd",
        [System.Globalization.CultureInfo]::InvariantCulture)

    $monthName = $parsed.ToString("MMMM", [System.Globalization.CultureInfo]::InvariantCulture)
    $monthShort = $parsed.ToString("MMM", [System.Globalization.CultureInfo]::InvariantCulture)
    $day = [int]$parsed.Day
    $dayTwoDigits = $parsed.ToString("dd", [System.Globalization.CultureInfo]::InvariantCulture)

    return @(
        $Date,
        (Convert-DateToStamp $Date),
        "$monthName $day",
        "$monthName $dayTwoDigits",
        "$monthShort $day",
        "$monthShort $dayTwoDigits",
        "$($monthName.ToLowerInvariant())_$day",
        "$($monthName.ToLowerInvariant())_$dayTwoDigits",
        "$($monthShort.ToLowerInvariant())_$day",
        "$($monthShort.ToLowerInvariant())_$dayTwoDigits",
        "$($monthName.ToUpperInvariant())_$day",
        "$($monthName.ToUpperInvariant())_$dayTwoDigits",
        "$($monthShort.ToUpperInvariant())_$day",
        "$($monthShort.ToUpperInvariant())_$dayTwoDigits"
    ) | Select-Object -Unique
}

function Get-OperationalSqlText {
    param([string] $SqlText)

    $lines = $SqlText -split "`r?`n"
    $operationalLines = foreach ($line in $lines) {
        if ($line -match '^\s*--') {
            continue
        }

        $line
    }

    return ($operationalLines -join "`n")
}

$sourceStamp = Convert-DateToStamp $SourceDate
$targetStamp = Convert-DateToStamp $TargetDate
$sourceTokenVariants = Get-DateTokenVariants $SourceDate
$targetTokenVariants = Get-DateTokenVariants $TargetDate

$devDataDir = Join-Path $RepoRoot "scripts\dev-data"
$dockerDir = Join-Path $RepoRoot "infra\docker"

$sourceSeed = Join-Path $devDataDir "webpay-$sourceStamp-seed.sql"
$sourceDiagnostics1 = Join-Path $devDataDir "webpay-$sourceStamp-payment-finalization-diagnostics.sql"
$sourceDiagnostics2 = Join-Path $devDataDir "webpay-$sourceStamp-diagnostics.sql"

$targetSeed = Join-Path $devDataDir "webpay-$targetStamp-seed.sql"
$targetDiagnostics = Join-Path $devDataDir "webpay-$targetStamp-payment-finalization-diagnostics.sql"

if (-not (Test-Path $sourceSeed)) {
    throw "Source seed not found: $sourceSeed"
}

$sourceDiagnostics = $null
if (Test-Path $sourceDiagnostics1) {
    $sourceDiagnostics = $sourceDiagnostics1
}
elseif (Test-Path $sourceDiagnostics2) {
    $sourceDiagnostics = $sourceDiagnostics2
}
else {
    Write-Warning "No source diagnostics file found. Only the seed file will be generated."
}

if ((Test-Path $targetSeed) -and -not $Force) {
    throw "Target seed already exists: $targetSeed. Use -Force to overwrite."
}

if ((Test-Path $targetDiagnostics) -and -not $Force) {
    throw "Target diagnostics already exists: $targetDiagnostics. Use -Force to overwrite."
}

Write-Host "Generating WebPay daily seed..." -ForegroundColor Cyan
Write-Host "Source date : $SourceDate ($sourceStamp)"
Write-Host "Target date : $TargetDate ($targetStamp)"
Write-Host "Source seed : $sourceSeed"
Write-Host "Target seed : $targetSeed"

$seedText = Get-Content $sourceSeed -Raw

$seedText = $seedText.Replace($sourceStamp, $targetStamp)
$seedText = $seedText.Replace($SourceDate, $TargetDate)
for ($i = 0; $i -lt $sourceTokenVariants.Count; $i++) {
    $seedText = $seedText.Replace($sourceTokenVariants[$i], $targetTokenVariants[$i])
}

# Keep a visible generated header.
$header = @"
-- Generated by scripts/dev-data/New-WebPayDailySeed.ps1
-- Source seed date: $SourceDate
-- Target seed date: $TargetDate
-- ExitPass v1.2 WebPay daily runtime validation seed
-- Standing rule: QRPH/PHP must route to PAYMONGO only. Do not route this slice to AUB.

"@

Set-Content -Path $targetSeed -Value ($header + $seedText) -Encoding UTF8

if ($sourceDiagnostics -ne $null) {
    Write-Host "Source diagnostics : $sourceDiagnostics"
    Write-Host "Target diagnostics : $targetDiagnostics"

    $diagText = Get-Content $sourceDiagnostics -Raw
    $diagText = $diagText.Replace($sourceStamp, $targetStamp)
    $diagText = $diagText.Replace($SourceDate, $TargetDate)
    for ($i = 0; $i -lt $sourceTokenVariants.Count; $i++) {
        $diagText = $diagText.Replace($sourceTokenVariants[$i], $targetTokenVariants[$i])
    }

    $diagHeader = @"
-- Generated by scripts/dev-data/New-WebPayDailySeed.ps1
-- Source diagnostics date: $SourceDate
-- Target diagnostics date: $TargetDate
-- ExitPass v1.2 WebPay daily runtime validation diagnostics
-- Standing rule: inspect actual schema before changing queries.

"@

    Set-Content -Path $targetDiagnostics -Value ($diagHeader + $diagText) -Encoding UTF8
}

Write-Host ""
Write-Host "Running safety checks..." -ForegroundColor Cyan

$generatedSeed = Get-Content $targetSeed -Raw
$operationalSeed = Get-OperationalSqlText $generatedSeed

foreach ($sourceToken in $sourceTokenVariants) {
    if ($operationalSeed.Contains($sourceToken)) {
        throw "Generated seed executable SQL still contains source date token '$sourceToken'. Review $targetSeed."
    }
}

if ($operationalSeed -match "AUB_QRPH|AUB.*QRPH|QRPH.*AUB") {
    throw "Generated seed appears to contain AUB QRPH routing. This is forbidden for the current WebPay QRPH slice."
}

if ($operationalSeed -notmatch "PAYMONGO") {
    throw "Generated seed does not contain PAYMONGO. Review routing seed content."
}

if (-not $operationalSeed.Contains("WEBPAY-$targetStamp-")) {
    throw "Generated seed executable SQL does not contain expected WEBPAY-$targetStamp-* ticket references."
}

if (-not $operationalSeed.Contains("WebPay Test Site $TargetDate")) {
    throw "Generated seed executable SQL does not contain expected target site label."
}

if (-not $operationalSeed.Contains("PAYMONGO_QRPH_WEBPAY_$targetStamp")) {
    throw "Generated seed executable SQL does not contain expected PAYMONGO QRPH target rail code."
}

if (-not $operationalSeed.Contains($TargetDate)) {
    throw "Generated seed executable SQL does not contain expected target-date values."
}

if ($sourceDiagnostics -ne $null) {
    $generatedDiagnostics = Get-Content $targetDiagnostics -Raw
    $operationalDiagnostics = Get-OperationalSqlText $generatedDiagnostics

    foreach ($sourceToken in $sourceTokenVariants) {
        if ($operationalDiagnostics.Contains($sourceToken)) {
            throw "Generated diagnostics executable SQL still contains source date token '$sourceToken'. Review $targetDiagnostics."
        }
    }
}

Write-Host "Safety checks passed." -ForegroundColor Green

Write-Host ""
Write-Host "Generated files:" -ForegroundColor Green
Write-Host "  $targetSeed"
if ($sourceDiagnostics -ne $null) {
    Write-Host "  $targetDiagnostics"
}

if ($Execute) {
    if (-not (Test-Path $dockerDir)) {
        throw "Docker compose directory not found: $dockerDir"
    }

    Write-Host ""
    Write-Host "Executing generated seed against local Docker PostgreSQL..." -ForegroundColor Cyan

    Push-Location $dockerDir
    try {
        Get-Content $targetSeed | docker compose exec -T postgres psql -U exitpass -d exitpass_v12_dev
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host "Seed execution completed." -ForegroundColor Green

    if ($sourceDiagnostics -ne $null) {
        Write-Host ""
        Write-Host "You may now run diagnostics:" -ForegroundColor Cyan
        Write-Host "cd $dockerDir"
        Write-Host "Get-Content $targetDiagnostics | docker compose exec -T postgres psql -U exitpass -d exitpass_v12_dev"
    }
}

Write-Host ""
Write-Host "Manual test ticket examples:" -ForegroundColor Cyan
Write-Host "  WEBPAY-$targetStamp-FRESH-001"
Write-Host "  WEBPAY-$targetStamp-FRESH-002"
Write-Host "  WEBPAY-$targetStamp-FRESH-003"
