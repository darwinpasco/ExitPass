<#
.SYNOPSIS
Runs the aligned-DB statutory discount payment-to-Sales-Invoice runtime proof.

.DESCRIPTION
This local/UAT-only helper rebuilds a disposable Central PMS database from the
canonical exitpassdb_v1.2 generated SQL, validates the v1.3 Central PMS objects,
seeds/verifies UAT identity/RBAC and the statutory discount pilot fixture, checks
the local POS Server runtime, and runs the opt-in Central PMS integration proof
that issues a discounted Sales Invoice through local POS Server.
#>

[CmdletBinding()]
param(
    [string] $DbRepoRoot = "D:\SourceCodes\exitpassdb_v1.2",
    [string] $PostgresContainer = "exitpass-postgres",
    [string] $AdminDatabase = "postgres",
    [string] $DbName = "centralpms_aligned_discount_payment_si_runtime_local",
    [string] $DbUser = "exitpass",
    [string] $PosServerBaseUrl = "http://localhost:5000",
    [string] $RunId = (Get-Date -Format "yyyyMMddHHmmss"),
    [switch] $SkipRebuild,
    [switch] $SkipSeed,
    [switch] $SkipRuntimeProof
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$canonicalSqlPath = Join-Path $DbRepoRoot "build\generated\exitpass-full-object.generated.sql"
$canonicalValidationSqlPath = Join-Path $DbRepoRoot "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$managementPlatformSeedSqlPath = Join-Path $repoRoot "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql"
$managementPlatformVerifySqlPath = Join-Path $repoRoot "scripts\management-platform\Verify-ManagementPlatformUatIdentityRbac.sql"
$statutorySeedSqlPath = Join-Path $repoRoot "scripts\operator-console\Seed-StatutoryDiscountPilotFixture.sql"
$statutoryVerifySqlPath = Join-Path $repoRoot "scripts\operator-console\Verify-StatutoryDiscountPilotFixture.sql"
$testProjectPath = Join-Path $repoRoot "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"
$connectionString = "Host=localhost;Port=5433;Database=$DbName;Username=exitpass;Password=change_me;Include Error Detail=true"

function Assert-FileExists {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required file not found: $Path"
    }
}

function Invoke-PsqlText {
    param(
        [Parameter(Mandatory = $true)][string] $Database,
        [Parameter(Mandatory = $true)][string] $Sql
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = $Sql | & docker exec -i $PostgresContainer psql -v ON_ERROR_STOP=1 -U $DbUser -d $Database 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($exitCode -ne 0) {
        throw "psql failed for database '$Database': $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Invoke-PsqlFile {
    param(
        [Parameter(Mandatory = $true)][string] $Database,
        [Parameter(Mandatory = $true)][string] $Path
    )

    Assert-FileExists -Path $Path
    Write-Host "Running SQL against ${Database}: $Path"
    $sql = Get-Content -LiteralPath $Path -Raw
    [void](Invoke-PsqlText -Database $Database -Sql $sql)
}

function Assert-PosServerAvailable {
    Write-Host "Checking POS Server runtime: $PosServerBaseUrl"
    $statusCode = $null
    try {
        $response = Invoke-WebRequest `
            -Uri "$($PosServerBaseUrl.TrimEnd('/'))/v1/fiscal-documents/00000000-0000-0000-0000-000000000000" `
            -UseBasicParsing `
            -TimeoutSec 10
        $statusCode = [int]$response.StatusCode
    }
    catch {
        if ($_.Exception.Response -ne $null) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        else {
            throw "POS Server runtime is not reachable at $PosServerBaseUrl. Start local POS Server before running runtime proof. $($_.Exception.Message)"
        }
    }

    if ($statusCode -ne 404) {
        throw "Expected POS Server zero-GUID fiscal document probe to return 404, got $statusCode."
    }
}

Assert-FileExists -Path $canonicalSqlPath
Assert-FileExists -Path $canonicalValidationSqlPath
Assert-FileExists -Path $managementPlatformSeedSqlPath
Assert-FileExists -Path $managementPlatformVerifySqlPath
Assert-FileExists -Path $statutorySeedSqlPath
Assert-FileExists -Path $statutoryVerifySqlPath
Assert-FileExists -Path $testProjectPath

Write-Host "ExitPass aligned-DB statutory discount payment-to-Sales-Invoice runtime proof"
Write-Host "Canonical SQL: $canonicalSqlPath"
Write-Host "Canonical validation: $canonicalValidationSqlPath"
Write-Host "Disposable Central PMS DB: $DbName"
Write-Host "POS Server URL: $PosServerBaseUrl"
Write-Host "Run ID: $RunId"

if (-not $SkipRebuild) {
    Write-Host "Recreating disposable database $DbName from canonical generated SQL."
    [void](Invoke-PsqlText -Database $AdminDatabase -Sql "DROP DATABASE IF EXISTS $DbName WITH (FORCE);")
    [void](Invoke-PsqlText -Database $AdminDatabase -Sql "CREATE DATABASE $DbName OWNER $DbUser;")
    Invoke-PsqlFile -Database $DbName -Path $canonicalSqlPath
}

Invoke-PsqlFile -Database $DbName -Path $canonicalValidationSqlPath

if (-not $SkipSeed) {
    Invoke-PsqlFile -Database $DbName -Path $statutorySeedSqlPath
    Invoke-PsqlFile -Database $DbName -Path $managementPlatformSeedSqlPath
}

Invoke-PsqlFile -Database $DbName -Path $managementPlatformVerifySqlPath
Invoke-PsqlFile -Database $DbName -Path $statutoryVerifySqlPath
Assert-PosServerAvailable

if (-not $SkipRuntimeProof) {
    $env:EXITPASS_TEST_MAIN_DB = $connectionString
    $env:ConnectionStrings__MainDatabase = $connectionString
    $env:EXITPASS_RUN_STATUTORY_DISCOUNT_LIVE_POS_SMOKE = "true"
    $env:EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_SMOKE_RUN_ID = $RunId
    $env:EXITPASS_STATUTORY_DISCOUNT_LIVE_POS_BASE_URL = $PosServerBaseUrl
    $env:MSBUILDDISABLENODEREUSE = "1"

    Write-Host "Running Central PMS opt-in live POS Server integration proof."
    dotnet test $testProjectPath `
        --no-restore `
        --filter "FullyQualifiedName~LocalRuntime_WhenEnabled_IssuesDiscountedSalesInvoiceThroughCentralPmsLivePosServer" `
        -m:1 `
        /p:UseSharedCompilation=false

    if ($LASTEXITCODE -ne 0) {
        throw "Central PMS live POS Server runtime proof failed with exit code $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Aligned DB statutory discount payment-to-Sales-Invoice runtime proof PASSED."
Write-Host "Central PMS DB: $DbName"
Write-Host "Connection: $connectionString"
Write-Host "POS Server URL: $PosServerBaseUrl"
Write-Host "Run ID: $RunId"
