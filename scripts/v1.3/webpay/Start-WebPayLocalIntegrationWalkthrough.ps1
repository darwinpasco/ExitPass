param(
    [string]$RepositoryRoot,
    [string]$CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2",
    [string]$DatabaseName = "exitpass_webpay_local_walkthrough",
    [string]$PostgresContainerName = "exitpass-postgres",
    [string]$DatabaseUser = "exitpass",
    [string]$DatabasePassword = $env:EXITPASS_WEBPAY_LOCAL_DB_PASSWORD,
    [int]$PostgresPort = 5433,
    [int]$CentralPmsPort = 8080,
    [int]$PaymentOrchestratorPort = 8082,
    [int]$MockPaymentProviderPort = 8084,
    [int]$WebPayPort = 5173,
    [switch]$DryRun,
    [switch]$StartServices,
    [switch]$SkipInfrastructure,
    [switch]$SkipDatabaseRebuild,
    [switch]$SkipSeed,
    [switch]$AllowExistingPorts,
    [switch]$VisibleServiceWindows
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$CanonicalDatabaseRepository = [System.IO.Path]::GetFullPath($CanonicalDatabaseRepository)
$stateRoot = Join-Path $RepositoryRoot ".local\webpay-local-integration-walkthrough"
$statePath = Join-Path $stateRoot "state.json"
$fixtureContextPath = Join-Path $stateRoot "fixture-context.json"
$logsRoot = Join-Path $stateRoot "logs"
$canonicalSql = Join-Path $CanonicalDatabaseRepository "build\generated\exitpass-full-object.generated.sql"
$canonicalValidator = Join-Path $CanonicalDatabaseRepository "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$paymentRoutingPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql"
$payMongoRailPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql"
$seedSql = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql"
$verifySql = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Verify-WebPayLocalIntegrationWalkthrough.sql"
$centralPmsProject = Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"
$paymentOrchestratorProject = Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj"
$webPayRoot = Join-Path $RepositoryRoot "src\Services\WebPayUi"

function Assert-SafeDatabaseName {
    param([string]$Name)

    if ($Name -notmatch '^exitpass_webpay_local_walkthrough(_[a-z0-9_]+)?$') {
        throw "Refusing to use database '$Name'. Use exitpass_webpay_local_walkthrough or a suffixed disposable variant."
    }
}

function Assert-PathExists {
    param(
        [string]$Path,
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Assert-Tool {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$Name' was not found in PATH."
    }
}

function Test-PortOpen {
    param([int]$Port)

    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    return $null -ne $connection
}

function Assert-PortAvailable {
    param(
        [int]$Port,
        [string]$Purpose
    )

    if ($AllowExistingPorts) {
        return
    }

    if (Test-PortOpen -Port $Port) {
        throw "Port $Port is already listening for $Purpose. Stop the existing process or rerun with -AllowExistingPorts."
    }
}

function Invoke-Checked {
    param(
        [string]$Description,
        [scriptblock]$Command
    )

    Write-Host $Description -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "DRY RUN: skipped." -ForegroundColor DarkYellow
        return
    }

    & $Command
}

function Invoke-PostgresSql {
    param(
        [string]$Database,
        [string]$Sql
    )

    docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql command failed for database $Database."
    }
}

function Invoke-PostgresFile {
    param(
        [string]$Database,
        [string]$Path
    )

    Get-Content -LiteralPath $Path -Raw | docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database
    if ($LASTEXITCODE -ne 0) {
        throw "psql file failed for database $Database`: $Path"
    }
}

function Invoke-PostgresQueryText {
    param([string]$Sql)

    $result = docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -t -A -U $DatabaseUser -d $DatabaseName -c $Sql
    if ($LASTEXITCODE -ne 0) {
        throw "psql query failed for database $DatabaseName."
    }

    return (($result | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
}

function Get-WalkthroughFixtureContext {
    $sql = @"
WITH
site_group_matches AS (
    SELECT *
    FROM sites.site_groups
    WHERE site_group_code = 'WEBPAY_LOCAL_GROUP'
),
site_matches AS (
    SELECT s.*
    FROM sites.sites s
    INNER JOIN site_group_matches sg ON sg.site_group_id = s.site_group_id
    WHERE s.site_code = 'WEBPAY_LOCAL_SITE'
),
vendor_matches AS (
    SELECT *
    FROM integration.vendor_systems
    WHERE vendor_code = 'WEBPAY_LOCAL_MOCK_PMS'
      AND environment_code = 'LOCAL'
),
session_matches AS (
    SELECT ps.*
    FROM core.parking_sessions ps
    WHERE ps.ticket_number_masked = 'WEBPAY-LOCAL-ORDINARY-001'
      AND ps.plate_number_masked = 'LOCALPAY001'
),
resolved_session AS (
    SELECT ps.*
    FROM session_matches ps
    INNER JOIN site_group_matches sg ON sg.site_group_id = ps.site_group_id
    INNER JOIN site_matches s ON s.site_id = ps.site_id
    INNER JOIN vendor_matches vs ON vs.vendor_system_id = ps.vendor_system_id
),
tariff_matches AS (
    SELECT ts.*
    FROM core.tariff_snapshots ts
    INNER JOIN resolved_session ps ON ps.parking_session_id = ts.parking_session_id
    WHERE ts.snapshot_status = 'ACTIVE'
      AND ts.currency_code = 'PHP'
      AND (ts.net_amount * 100)::bigint = 13750
)
SELECT json_build_object(
    'siteGroupCount', (SELECT COUNT(*) FROM site_group_matches),
    'siteCount', (SELECT COUNT(*) FROM site_matches),
    'vendorSystemCount', (SELECT COUNT(*) FROM vendor_matches),
    'parkingSessionCount', (SELECT COUNT(*) FROM resolved_session),
    'tariffSnapshotCount', (SELECT COUNT(*) FROM tariff_matches),
    'siteGroupId', (SELECT site_group_id FROM site_group_matches LIMIT 1),
    'siteGroupCode', 'WEBPAY_LOCAL_GROUP',
    'siteGroupPublicLookupEnabled', COALESCE((SELECT public_lookup_enabled FROM site_group_matches LIMIT 1), false),
    'siteGroupPaymentEnabled', COALESCE((SELECT default_payment_enabled FROM site_group_matches LIMIT 1), false),
    'siteId', (SELECT site_id FROM site_matches LIMIT 1),
    'siteCode', 'WEBPAY_LOCAL_SITE',
    'siteBelongsToSiteGroup', COALESCE((
        SELECT s.site_group_id = sg.site_group_id
        FROM site_matches s
        CROSS JOIN site_group_matches sg
        LIMIT 1
    ), false),
    'sitePublicLookupEnabled', COALESCE((SELECT public_lookup_enabled FROM site_matches LIMIT 1), false),
    'sitePaymentEnabled', COALESCE((SELECT payment_enabled FROM site_matches LIMIT 1), false),
    'vendorSystemId', (SELECT vendor_system_id FROM vendor_matches LIMIT 1),
    'vendorCode', 'WEBPAY_LOCAL_MOCK_PMS',
    'environmentCode', 'LOCAL',
    'vendorSystemActive', COALESCE((SELECT vendor_system_status::text = 'ACTIVE' FROM vendor_matches LIMIT 1), false),
    'parkingSessionId', (SELECT parking_session_id FROM resolved_session LIMIT 1),
    'parkingSessionStatus', (SELECT session_status::text FROM resolved_session LIMIT 1),
    'ticketReference', 'WEBPAY-LOCAL-ORDINARY-001',
    'plateNumber', 'LOCALPAY001',
    'tariffSnapshotId', (SELECT tariff_snapshot_id FROM tariff_matches ORDER BY calculated_at DESC, created_at DESC LIMIT 1),
    'amountMinorUnits', COALESCE((SELECT (net_amount * 100)::bigint FROM tariff_matches ORDER BY calculated_at DESC, created_at DESC LIMIT 1), 0),
    'currency', COALESCE((SELECT currency_code FROM tariff_matches ORDER BY calculated_at DESC, created_at DESC LIMIT 1), ''),
    'statutoryDecisionCount', COALESCE((
        SELECT COUNT(*)
        FROM discounts.statutory_discount_decision_commands d
        INNER JOIN resolved_session ps ON ps.parking_session_id = d.parking_session_id
    ), 0),
    'statutoryApplicationCount', COALESCE((
        SELECT COUNT(*)
        FROM discounts.statutory_discount_payable_basis_application_commands a
        INNER JOIN resolved_session ps ON ps.parking_session_id = a.parking_session_id
    ), 0)
)::text;
"@

    $json = Invoke-PostgresQueryText -Sql $sql
    if ([string]::IsNullOrWhiteSpace($json)) {
        throw "Fixture discovery returned no rows."
    }

    $context = $json | ConvertFrom-Json
    $failures = New-Object System.Collections.Generic.List[string]

    if ([int]$context.siteGroupCount -ne 1) { $failures.Add("Expected one WEBPAY_LOCAL_GROUP site group, found $($context.siteGroupCount).") }
    if ([int]$context.siteCount -ne 1) { $failures.Add("Expected one WEBPAY_LOCAL_SITE site under WEBPAY_LOCAL_GROUP, found $($context.siteCount).") }
    if ([int]$context.vendorSystemCount -ne 1) { $failures.Add("Expected one WEBPAY_LOCAL_MOCK_PMS/LOCAL vendor system, found $($context.vendorSystemCount).") }
    if ([int]$context.parkingSessionCount -ne 1) { $failures.Add("Expected one ordinary walkthrough parking session, found $($context.parkingSessionCount).") }
    if ([int]$context.tariffSnapshotCount -ne 1) { $failures.Add("Expected one active PHP 137.50 tariff snapshot, found $($context.tariffSnapshotCount).") }
    if (-not [bool]$context.siteBelongsToSiteGroup) { $failures.Add("WEBPAY_LOCAL_SITE does not belong to WEBPAY_LOCAL_GROUP.") }
    if (-not [bool]$context.vendorSystemActive) { $failures.Add("WEBPAY_LOCAL_MOCK_PMS/LOCAL vendor system is not active.") }
    if (-not [bool]$context.siteGroupPublicLookupEnabled) { $failures.Add("WEBPAY_LOCAL_GROUP public lookup is disabled.") }
    if (-not [bool]$context.siteGroupPaymentEnabled) { $failures.Add("WEBPAY_LOCAL_GROUP payment is disabled.") }
    if (-not [bool]$context.sitePublicLookupEnabled) { $failures.Add("WEBPAY_LOCAL_SITE public lookup is disabled.") }
    if (-not [bool]$context.sitePaymentEnabled) { $failures.Add("WEBPAY_LOCAL_SITE payment is disabled.") }
    if ([string]$context.parkingSessionStatus -ne "ACTIVE") { $failures.Add("Walkthrough parking session status is $($context.parkingSessionStatus), expected ACTIVE.") }
    if ([int64]$context.amountMinorUnits -ne 13750) { $failures.Add("Walkthrough amount is $($context.amountMinorUnits), expected 13750.") }
    if ([string]$context.currency -ne "PHP") { $failures.Add("Walkthrough currency is $($context.currency), expected PHP.") }
    if ([int]$context.statutoryDecisionCount -ne 0) { $failures.Add("Ordinary fixture has statutory decision rows: $($context.statutoryDecisionCount).") }
    if ([int]$context.statutoryApplicationCount -ne 0) { $failures.Add("Ordinary fixture has statutory application rows: $($context.statutoryApplicationCount).") }

    foreach ($propertyName in @("siteGroupId", "siteId", "vendorSystemId", "parkingSessionId", "tariffSnapshotId")) {
        if ([string]::IsNullOrWhiteSpace([string]$context.$propertyName)) {
            $failures.Add("Fixture discovery did not resolve $propertyName.")
        }
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "Walkthrough fixture discovery failed with $($failures.Count) error(s)."
    }

    return $context
}

function Ensure-ContainerRunning {
    param([string]$Name)

    $exists = docker ps -a --filter "name=^/$Name$" --format "{{.Names}}"
    if ($exists -ne $Name) {
        throw "Required container '$Name' does not exist. Start the standard ExitPass infrastructure first or restore the repository compose file."
    }

    $running = docker ps --filter "name=^/$Name$" --filter "status=running" --format "{{.Names}}"
    if ($running -ne $Name) {
        Write-Host "Starting existing container $Name..." -ForegroundColor Yellow
        if (-not $DryRun) {
            docker start $Name | Out-Null
        }
    }
}

function Wait-HttpReady {
    param(
        [string]$Name,
        [string]$Url,
        [string]$Method = "GET",
        [string]$Body,
        [string]$ExpectedContent,
        [int]$TimeoutSeconds = 90
    )

    if ($DryRun) {
        Write-Host "DRY RUN: would verify $Name at $Url" -ForegroundColor DarkYellow
        return
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $parameters = @{
                Uri = $Url
                Method = $Method
                UseBasicParsing = $true
                TimeoutSec = 5
            }

            if ($Body) {
                $parameters.Body = $Body
                $parameters.ContentType = "application/json"
                $parameters.Headers = @{ Authorization = "Basic local-placeholder" }
            }

            $response = Invoke-WebRequest @parameters
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
                if ($ExpectedContent -and $response.Content -notlike "*$ExpectedContent*") {
                    $lastError = "HTTP $($response.StatusCode) without expected response marker '$ExpectedContent'"
                    Start-Sleep -Seconds 2
                    continue
                }

                Write-Host "$Name reachable: $Url" -ForegroundColor Green
                return
            }

            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    throw "$Name was not ready at $Url. Last error: $lastError"
}

function Start-WalkthroughProcess {
    param(
        [string]$Name,
        [string]$WorkingDirectory,
        [string]$Command
    )

    $stdoutLogPath = Join-Path $logsRoot "$Name.out.log"
    $stderrLogPath = Join-Path $logsRoot "$Name.err.log"
    $windowStyle = if ($VisibleServiceWindows) { "Normal" } else { "Hidden" }
    $arguments = @(
        "-NoLogo",
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        $Command
    )

    if ($DryRun) {
        Write-Host "DRY RUN: would start $Name in $WorkingDirectory" -ForegroundColor DarkYellow
        Write-Host $Command
        return $null
    }

    $process = Start-Process powershell `
        -ArgumentList $arguments `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $stdoutLogPath `
        -RedirectStandardError $stderrLogPath `
        -PassThru `
        -WindowStyle $windowStyle

    return [pscustomobject]@{
        Name = $Name
        Id = $process.Id
        StdoutLog = $stdoutLogPath
        StderrLog = $stderrLogPath
    }
}

Assert-SafeDatabaseName -Name $DatabaseName
Assert-PathExists -Path $RepositoryRoot -Description "Repository root"
Assert-PathExists -Path $CanonicalDatabaseRepository -Description "Canonical DB repository"
Assert-PathExists -Path $canonicalSql -Description "Canonical generated SQL baseline"
Assert-PathExists -Path $canonicalValidator -Description "Canonical Central PMS alignment validator"
Assert-PathExists -Path $paymentRoutingPatch -Description "Repository payment routing compatibility patch"
Assert-PathExists -Path $payMongoRailPatch -Description "Repository PayMongo checkout-session rail compatibility patch"
Assert-PathExists -Path $seedSql -Description "Walkthrough seed SQL"
Assert-PathExists -Path $verifySql -Description "Walkthrough verification SQL"
Assert-PathExists -Path $centralPmsProject -Description "Central PMS project"
Assert-PathExists -Path $paymentOrchestratorProject -Description "Payment Orchestrator project"
Assert-PathExists -Path $webPayRoot -Description "WebPay UI root"

Assert-Tool -Name "docker"
Assert-Tool -Name "dotnet"
Assert-Tool -Name "npm"

if ($StartServices) {
    Assert-PortAvailable -Port $CentralPmsPort -Purpose "Central PMS"
    Assert-PortAvailable -Port $PaymentOrchestratorPort -Purpose "Payment Orchestrator"
    Assert-PortAvailable -Port $WebPayPort -Purpose "WebPay UI"
}

if (-not $DryRun -and $StartServices -and -not $DatabasePassword) {
    throw "Set EXITPASS_WEBPAY_LOCAL_DB_PASSWORD before starting services so host processes can connect to PostgreSQL."
}

New-Item -ItemType Directory -Force -Path $stateRoot, $logsRoot | Out-Null

Write-Host "ExitPass WebPay ordinary-payment local walkthrough" -ForegroundColor Cyan
Write-Host "Repository: $RepositoryRoot"
Write-Host "Disposable database: $DatabaseName"
Write-Host "Canonical baseline: $canonicalSql"

if (-not $SkipInfrastructure) {
    Invoke-Checked "Verifying local infrastructure containers..." {
        Ensure-ContainerRunning -Name $PostgresContainerName
        Ensure-ContainerRunning -Name "exitpass-rabbitmq"
        Ensure-ContainerRunning -Name "exitpass-mock-payment-provider"
    }
}

if (-not $SkipDatabaseRebuild) {
    Invoke-Checked "Rebuilding disposable database $DatabaseName from canonical baseline..." {
        Invoke-PostgresSql -Database "postgres" -Sql "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DatabaseName';"
        Invoke-PostgresSql -Database "postgres" -Sql "DROP DATABASE IF EXISTS $DatabaseName;"
        Invoke-PostgresSql -Database "postgres" -Sql "CREATE DATABASE $DatabaseName;"
        Invoke-PostgresFile -Database $DatabaseName -Path $canonicalSql
        Invoke-PostgresFile -Database $DatabaseName -Path $canonicalValidator
        Invoke-PostgresFile -Database $DatabaseName -Path $paymentRoutingPatch
        Invoke-PostgresFile -Database $DatabaseName -Path $payMongoRailPatch
    }
}

if (-not $SkipSeed) {
    Invoke-Checked "Applying ordinary WebPay walkthrough fixture..." {
        Invoke-PostgresFile -Database $DatabaseName -Path $seedSql
        Invoke-PostgresFile -Database $DatabaseName -Path $verifySql
    }
}

$fixtureContext = $null
if ($DryRun) {
    Write-Host "DRY RUN: would discover fixture IDs by site_group_code, site_code, vendor_code/environment, ticket, and plate." -ForegroundColor DarkYellow
}
else {
    Write-Host "Discovering authoritative ordinary WebPay fixture context..." -ForegroundColor Yellow
    $fixtureContext = Get-WalkthroughFixtureContext
    $fixtureContext | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fixtureContextPath -Encoding UTF8
    Write-Host "Fixture context discovered:" -ForegroundColor Green
    Write-Host "  Site Group ID:     $($fixtureContext.siteGroupId)"
    Write-Host "  Site ID:           $($fixtureContext.siteId)"
    Write-Host "  Vendor System ID:  $($fixtureContext.vendorSystemId)"
    Write-Host "  Parking Session:   $($fixtureContext.parkingSessionId)"
    Write-Host "  Tariff Snapshot:   $($fixtureContext.tariffSnapshotId)"
    Write-Host "  Ticket reference:  $($fixtureContext.ticketReference)"
    Write-Host "  Plate number:      $($fixtureContext.plateNumber)"
    Write-Host "  Amount:            PHP 137.50 ($($fixtureContext.amountMinorUnits) minor units)"
}

$connectionStringExpression = "'Host=127.0.0.1;Port=$PostgresPort;Database=$DatabaseName;Username=$DatabaseUser;Password=' + `$env:EXITPASS_WEBPAY_LOCAL_DB_PASSWORD + ';Include Error Detail=true'"
$centralPmsUrl = "http://localhost:$CentralPmsPort"
$paymentOrchestratorUrl = "http://localhost:$PaymentOrchestratorPort"
$webPayUrl = "http://localhost:$WebPayPort"
$webPayBrowserUrl = "http://127.0.0.1:$WebPayPort/?ticketReference=WEBPAY-LOCAL-ORDINARY-001"
$mockProviderUrl = "http://localhost:$MockPaymentProviderPort"
$mockProviderCheckoutProbeBody = '{"data":{"attributes":{"line_items":[{"currency":"PHP","amount":13750,"name":"ExitPass Local Walkthrough","quantity":1}],"success_url":"http://localhost:5174/webpay/payment-return","cancel_url":"http://localhost:5174/webpay/payment-return"}}}'
$serviceProcesses = @()

if ($StartServices) {
    Invoke-Checked "Building .NET services once before launch..." {
        dotnet build $centralPmsProject
        if ($LASTEXITCODE -ne 0) {
            throw "Central PMS build failed."
        }

        dotnet build $paymentOrchestratorProject
        if ($LASTEXITCODE -ne 0) {
            throw "Payment Orchestrator build failed."
        }
    }

    $centralCommand = @"
`$connectionString = $connectionStringExpression
`$env:ASPNETCORE_ENVIRONMENT = 'Development'
`$env:ASPNETCORE_URLS = '$centralPmsUrl'
`$env:ConnectionStrings__MainDatabase = `$connectionString
`$env:EXITPASS_TEST_MAIN_DB = `$connectionString
`$env:EXITPASS_INTEGRATION_DB = `$connectionString
dotnet run --project '$centralPmsProject' --no-launch-profile --no-build
"@

    $orchestratorCommand = @"
`$connectionString = $connectionStringExpression
`$env:ASPNETCORE_ENVIRONMENT = 'Development'
`$env:ASPNETCORE_URLS = '$paymentOrchestratorUrl'
`$env:ConnectionStrings__MainDatabase = `$connectionString
`$env:Integrations__CentralPms__BaseUrl = '$centralPmsUrl'
`$env:WEBPAY_PUBLIC_BASE_URL = '$webPayUrl'
`$env:WebPay__PublicBaseUrl = '$webPayUrl'
`$env:Payments__Providers__PayMongo__BaseUrl = '$mockProviderUrl'
`$env:Payments__Providers__PayMongo__SecretKey = 'local-walkthrough-secret-placeholder'
`$env:Payments__Providers__PayMongo__PublicKey = 'local-walkthrough-public-placeholder'
`$env:Payments__Providers__PayMongo__WebhookSecretKey = 'local-walkthrough-webhook-placeholder'
dotnet run --project '$paymentOrchestratorProject' --no-launch-profile --no-build
"@

    $webPayCommand = @"
`$env:VITE_WEBPAY_API_PROXY_TARGET = '$paymentOrchestratorUrl'
`$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = '$($fixtureContext.siteGroupId)'
`$env:VITE_WEBPAY_DEFAULT_SITE_ID = '$($fixtureContext.siteId)'
`$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = '$($fixtureContext.vendorSystemId)'
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue
npm.cmd run dev -- --host 0.0.0.0 --port $WebPayPort
"@

    $serviceProcesses += Start-WalkthroughProcess -Name "central-pms" -WorkingDirectory $RepositoryRoot -Command $centralCommand
    $serviceProcesses += Start-WalkthroughProcess -Name "payment-orchestrator" -WorkingDirectory $RepositoryRoot -Command $orchestratorCommand
    $serviceProcesses += Start-WalkthroughProcess -Name "webpay-ui" -WorkingDirectory $webPayRoot -Command $webPayCommand

    Write-Host "Waiting for local walkthrough endpoints..." -ForegroundColor Yellow
    Wait-HttpReady -Name "Central PMS readiness" -Url "$centralPmsUrl/health/ready"
    Wait-HttpReady -Name "Payment Orchestrator readiness" -Url "$paymentOrchestratorUrl/health/ready"
    Wait-HttpReady -Name "WebPay UI" -Url $webPayUrl
    Wait-HttpReady -Name "Mock payment provider admin" -Url "$mockProviderUrl/__admin/mappings"
    Wait-HttpReady -Name "Mock PayMongo checkout-session endpoint" -Url "$mockProviderUrl/v1/checkout_sessions" -Method "POST" -Body $mockProviderCheckoutProbeBody -ExpectedContent "cs_test_exitpass_local"
}
else {
    Write-Host ""
    Write-Host "Service commands were not started. Rerun with -StartServices or use these local URLs after starting services manually:" -ForegroundColor Yellow
}

if (-not $DryRun) {
    $state = [pscustomobject]@{
        DatabaseName = $DatabaseName
        PostgresContainerName = $PostgresContainerName
        StartedAt = (Get-Date).ToString("o")
        Processes = @($serviceProcesses | Where-Object { $null -ne $_ })
        Fixture = [pscustomobject]@{
            TicketReference = "WEBPAY-LOCAL-ORDINARY-001"
            PlateNumber = "LOCALPAY001"
            ParkingSessionId = $fixtureContext.parkingSessionId
            TariffSnapshotId = $fixtureContext.tariffSnapshotId
            SiteGroupId = $fixtureContext.siteGroupId
            SiteId = $fixtureContext.siteId
            VendorSystemId = $fixtureContext.vendorSystemId
            AmountMinorUnits = 13750
            Currency = "PHP"
        }
    }

    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

Write-Host ""
Write-Host "Local URLs" -ForegroundColor Cyan
Write-Host "Central PMS:          $centralPmsUrl"
Write-Host "Payment Orchestrator: $paymentOrchestratorUrl"
Write-Host "WebPay UI:            $webPayUrl"
Write-Host "Mock payment provider:$mockProviderUrl"
Write-Host "WebPay public base URL:$webPayUrl"
Write-Host "Browser walkthrough:  $webPayBrowserUrl"
Write-Host ""
Write-Host "Fixture" -ForegroundColor Cyan
Write-Host "Ticket reference: WEBPAY-LOCAL-ORDINARY-001"
Write-Host "Plate number:     LOCALPAY001"
Write-Host "Amount:           PHP 137.50"
Write-Host "Payment method:   QRPh through local mock provider"
if ($fixtureContext) {
    Write-Host "Site Group ID:    $($fixtureContext.siteGroupId)"
    Write-Host "Site ID:          $($fixtureContext.siteId)"
    Write-Host "Vendor System ID: $($fixtureContext.vendorSystemId)"
    Write-Host "Parking Session:  $($fixtureContext.parkingSessionId)"
    Write-Host "Tariff Snapshot:  $($fixtureContext.tariffSnapshotId)"
}
Write-Host ""
Write-Host "Verify fixture/payment state:" -ForegroundColor Cyan
Write-Host "docker exec -i $PostgresContainerName psql -U $DatabaseUser -d $DatabaseName -f /dev/stdin < scripts\v1.3\webpay\Verify-WebPayLocalIntegrationWalkthrough.sql"
Write-Host "Prove local provider handoff:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Test-WebPayLocalIntegrationPaymentHandoff.ps1"
Write-Host "Probe real browser-facing parking-session route only:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Test-WebPayLocalIntegrationPaymentHandoff.ps1 -ParkingSessionProbeOnly -WebPayUrl http://127.0.0.1:$WebPayPort"
Write-Host ""
Write-Host "Stop:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1"
Write-Host "Drop disposable database only when intended:" -ForegroundColor Cyan
Write-Host "powershell -ExecutionPolicy Bypass -File scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1 -RemoveDisposableDatabase"
