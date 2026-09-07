Set-StrictMode -Version Latest

$script:DeveloperRuntimeIds = [pscustomobject]@{
    SiteGroupId = '12000000-0000-0000-0000-000000000301'
    SiteId = '12000000-0000-0000-0000-000000000302'
    VendorSystemId = 'de100000-0000-0000-0000-000000000003'
    AdapterIdentityId = 'de100000-0000-0000-0000-000000000004'
    CentralPmsIdentityId = '12000000-0000-0000-0000-000000000002'
    ProjectionTargetId = 'de100000-0000-0000-0000-000000000008'
}

function Get-ExitPassDeveloperRuntimeState {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    $stateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_DEVELOPER_RUNTIME_ROOT)) {
        Join-Path $env:LOCALAPPDATA 'ExitPass\DeveloperRuntime'
    }
    else {
        $env:EXITPASS_DEVELOPER_RUNTIME_ROOT
    }

    return [pscustomobject]@{
        Root = [IO.Path]::GetFullPath($stateRoot)
        ComposeFile = Join-Path $RepositoryRoot 'infra\docker\developer-runtime\compose.yml'
        EnvironmentFile = Join-Path $stateRoot '.env'
        DatabasePasswordFile = Join-Path $stateRoot 'secrets\database-password'
        AdapterSecretRoot = Join-Path $stateRoot 'secrets\site-adapter'
        CentralAdapterKeyFile = Join-Path $stateRoot 'secrets\site-adapter\central-key'
        WireMockRoot = Join-Path $stateRoot 'wiremock'
        DatabaseConnectionFile = Join-Path $stateRoot 'database-connection'
        TotpKeyFile = Join-Path $stateRoot 'secrets\central-pms-totp-key'
    }
}

function New-ExitPassDeveloperSecret {
    $bytes = [byte[]]::new(32)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($bytes)
}

function Set-ExitPassDeveloperPrivateFile {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Value)
    $parent = Split-Path -Parent $Path
    [void](New-Item -ItemType Directory -Path $parent -Force)
    [IO.File]::WriteAllText($Path, $Value, [Text.UTF8Encoding]::new($false))
}

function Get-OrCreateExitPassDeveloperPrivateValue {
    param([Parameter(Mandatory)][string] $Path)
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $value = (Get-Content -LiteralPath $Path -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    }
    $value = New-ExitPassDeveloperSecret
    Set-ExitPassDeveloperPrivateFile -Path $Path -Value $value
    return $value
}

function Get-DeveloperWireMockCommonRequest {
    param([Parameter(Mandatory)][string] $Path)
    return [ordered]@{
        method = 'POST'
        urlPath = $Path
        headers = [ordered]@{
            'Content-Type' = @{ contains = 'application/json' }
            'X-Ca-Key' = @{ equalTo = 'exitpass-developer-app-key' }
            'X-Ca-Signature' = @{ matches = '.+' }
            'X-Ca-Timestamp' = @{ matches = '^[0-9]{13}$' }
            'X-Ca-Signature-Headers' = @{ equalTo = 'x-ca-key,x-ca-timestamp' }
            'userId' = @{ equalTo = 'exitpass-developer-adapter' }
        }
    }
}

function New-ExitPassDeveloperWireMockMappings {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $DestinationRoot
    )

    $fixturePath = Join-Path $RepositoryRoot 'infra\docker\developer-runtime\fixtures.json'
    $fixtures = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
    if ($fixtures.Count -ne 5) { throw 'Developer HikCentral fixture inventory must contain exactly five sessions.' }

    $mappingRoot = Join-Path $DestinationRoot 'mappings'
    $filesRoot = Join-Path $DestinationRoot '__files'
    [void](New-Item -ItemType Directory -Path $mappingRoot -Force)
    [void](New-Item -ItemType Directory -Path $filesRoot -Force)
    Get-ChildItem -LiteralPath $mappingRoot -File -ErrorAction SilentlyContinue | Remove-Item -Force

    $entryTime = '2026-09-07T08:00:00+08:00'
    $passageRequest = Get-DeveloperWireMockCommonRequest '/artemis/api/vehicle/v1/parkinglot/passageway/record'
    $passageRequest['bodyPatterns'] = @(
        @{ matchesJsonPath = '$[?(@.pageIndex >= 1 && @.pageSize >= 1)]' },
        @{ matchesJsonPath = '$[?(@.queryInfo.parkingLotIndexCode == ''DEV-LOT-1'')]' },
        @{ matchesJsonPath = '$[?(@.queryInfo.beginTime && @.queryInfo.endTime)]' },
        @{ matchesJsonPath = '$[?(@.queryInfo.directionType == -1 && @.queryInfo.allowResult == -1)]' },
        @{ matchesJsonPath = '$[?(@.queryInfo.sortField == ''EnterTime'' && @.queryInfo.orderType == 1)]' }
    )
    $passageRows = @($fixtures | ForEach-Object {
        [ordered]@{
            guid = $_.vendorRecordGuid
            parkingLotInfo = @{ parkingLotIndexCode = 'DEV-LOT-1'; parkingLotName = 'ExitPass Developer Parking' }
            passagewayInfo = @{ passagewayIndexCode = 'DEV-ENTRY-1'; passagewayName = 'Developer Entry' }
            laneInfo = @{ laneIndexCode = 'DEV-LANE-1'; laneName = 'Developer Entry Lane'; direction = 'ENTRY' }
            personInfo = @{ cardNum = $_.ticketReference }
            carInfo = @{ plateLicense = $_.plateLicense; EnterTime = $entryTime; ExitTime = $null }
            allowType = '1'
            allowResult = '1'
        }
    })
    $passageMapping = [ordered]@{
        name = 'developer-passageway-records'
        priority = 1
        request = $passageRequest
        response = @{ status = 200; headers = @{ 'Content-Type' = 'application/json' }; jsonBody = @{
            code = '0'; msg = 'Success'; data = @{ total = $fixtures.Count; pageIndex = 1; pageSize = 100; list = $passageRows }
        }}
    }
    Set-ExitPassDeveloperPrivateFile (Join-Path $mappingRoot '01-passageway.json') ($passageMapping | ConvertTo-Json -Depth 30)

    $position = 10
    foreach ($fixture in $fixtures) {
        $calculateRequest = Get-DeveloperWireMockCommonRequest '/artemis/api/vehicle/v1/parkingfee/calculate'
        $calculateRequest['bodyPatterns'] = @(
            @{ matchesJsonPath = ('$[?(@.cardNum == ''' + $fixture.ticketReference + ''')]' ) }
        )
        $calculate = [ordered]@{
            name = "developer-calculate-$($fixture.ticketReference)"
            priority = 1
            request = $calculateRequest
            response = @{ status = 200; headers = @{ 'Content-Type' = 'application/json' }; jsonBody = @{
                code = '0'; msg = 'Success'; data = @{
                    plateLicense = $fixture.plateLicense
                    cardNum = $fixture.ticketReference
                    parkingInTime = $entryTime
                    parkingDuration = 60
                    feeRuleType = 0
                    feeRuleIndexCode = "DEV-RULE-$($fixture.paymentMethod)"
                    feeRuleName = "Developer $($fixture.paymentMethod) Rule"
                    fee = $fixture.fee
                }
            }}
        }
        Set-ExitPassDeveloperPrivateFile (Join-Path $mappingRoot ("{0:D2}-calculate-{1}.json" -f $position, $fixture.paymentMethod.ToLowerInvariant())) ($calculate | ConvertTo-Json -Depth 30)

        $confirmRequest = Get-DeveloperWireMockCommonRequest '/artemis/api/vehicle/v1/parkingfee/confirm'
        $confirmRequest['bodyPatterns'] = @(
            @{ matchesJsonPath = ('$[?(@.cardNum == ''' + $fixture.ticketReference + ''')]' ) },
            @{ matchesJsonPath = ('$[?(@.fee == ''' + $fixture.fee + ''')]' ) },
            @{ matchesJsonPath = '$[?(@.immediatelyLeave == 0 || @.immediatelyLeave == 1)]' }
        )
        $confirm = [ordered]@{
            name = "developer-confirm-$($fixture.ticketReference)"
            priority = 1
            request = $confirmRequest
            response = @{ status = 200; headers = @{ 'Content-Type' = 'application/json' }; jsonBody = @{
                code = '0'; msg = 'Success'; data = @{ fee = $fixture.fee; feeTime = '2026-09-07T09:00:00+08:00' }
            }}
        }
        Set-ExitPassDeveloperPrivateFile (Join-Path $mappingRoot ("{0:D2}-confirm-{1}.json" -f ($position + 1), $fixture.paymentMethod.ToLowerInvariant())) ($confirm | ConvertTo-Json -Depth 30)
        $position += 2
    }

    foreach ($endpoint in @('parkingfee/calculate', 'parkingfee/confirm', 'parkinglot/passageway/record')) {
        $fallback = [ordered]@{
            name = "developer-reject-malformed-$($endpoint.Replace('/', '-'))"
            priority = 10
            request = @{ method = 'POST'; urlPath = "/artemis/api/vehicle/v1/$endpoint" }
            response = @{ status = 400; headers = @{ 'Content-Type' = 'application/json' }; jsonBody = @{
                code = '400'; msg = 'Developer fixture request did not match the HikCentral contract.'; data = $null
            }}
        }
        Set-ExitPassDeveloperPrivateFile (Join-Path $mappingRoot ("90-reject-{0}.json" -f $endpoint.Replace('/', '-'))) ($fallback | ConvertTo-Json -Depth 20)
    }
}

function Invoke-ExitPassDeveloperSqlFile {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $DatabasePassword
    )
    $sql = Get-Content -LiteralPath $Path -Raw
    $sql | & docker.exe exec -i -e "PGPASSWORD=$DatabasePassword" exitpass-dev-synthetic-db `
        psql -v ON_ERROR_STOP=1 -U exitpass_dev -d exitpass_dev | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Developer database script failed: $Path" }
}

function Assert-ExitPassDeveloperOwnedResource {
    param([Parameter(Mandatory)][string] $Type, [Parameter(Mandatory)][string] $Name)
    $exists = if ($Type -eq 'container') {
        & docker.exe ps -a --filter "name=^/$Name$" --format '{{.Names}}'
    } elseif ($Type -eq 'network') {
        & docker.exe network ls --filter "name=^$Name$" --format '{{.Name}}'
    } else {
        & docker.exe volume ls --filter "name=^$Name$" --format '{{.Name}}'
    }
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($exists -join ''))) { return }
    $inspection = if ($Type -eq 'container') {
        (& docker.exe inspect $Name | ConvertFrom-Json)[0]
    } elseif ($Type -eq 'network') {
        (& docker.exe network inspect $Name | ConvertFrom-Json)[0]
    } else {
        (& docker.exe volume inspect $Name | ConvertFrom-Json)[0]
    }
    $label = if ($Type -eq 'container') {
        $inspection.Config.Labels.'com.exitpass.resource-owner'
    } else {
        $inspection.Labels.'com.exitpass.resource-owner'
    }
    if ($label -ne 'developer-runtime') {
        throw "Refusing to use unexpected $Type '$Name'; the Developer ownership label is absent."
    }
}

function Initialize-ExitPassDeveloperRuntime {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) { throw 'Docker Desktop is required for the Developer runtime.' }
    & docker.exe version --format '{{.Server.Version}}' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop is not available.' }

    $state = Get-ExitPassDeveloperRuntimeState -RepositoryRoot $RepositoryRoot
    $canonicalDatabaseRepository = [Environment]::GetEnvironmentVariable('EXITPASS_CANONICAL_DB_REPOSITORY')
    if ([string]::IsNullOrWhiteSpace($canonicalDatabaseRepository)) {
        $cursor = [System.IO.DirectoryInfo]::new($RepositoryRoot)
        while ($null -ne $cursor) {
            $candidate = Join-Path $cursor.FullName 'exitpassdb_v1.2'
            if (Test-Path -LiteralPath $candidate -PathType Container) {
                $canonicalDatabaseRepository = $candidate
                break
            }
            $cursor = $cursor.Parent
        }
    }
    if ([string]::IsNullOrWhiteSpace($canonicalDatabaseRepository)) {
        throw 'Canonical database repository was not found. Set EXITPASS_CANONICAL_DB_REPOSITORY to the exitpassdb_v1.2 checkout.'
    }
    $canonicalDatabaseRepository = [System.IO.Path]::GetFullPath($canonicalDatabaseRepository)
    $canonicalSql = Join-Path $canonicalDatabaseRepository 'build\generated\exitpass-full-object.generated.sql'
    $canonicalValidator = Join-Path $canonicalDatabaseRepository 'scripts\validation\Validate-V13CentralPmsAlignment.sql'
    foreach ($requiredPath in @($canonicalSql, $canonicalValidator)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Canonical database artifact was not found: $requiredPath"
        }
    }
    [void](New-Item -ItemType Directory -Path $state.Root -Force)
    $databasePassword = Get-OrCreateExitPassDeveloperPrivateValue $state.DatabasePasswordFile
    $appSecret = Get-OrCreateExitPassDeveloperPrivateValue (Join-Path $state.AdapterSecretRoot 'app-secret')
    $centralKey = Get-OrCreateExitPassDeveloperPrivateValue $state.CentralAdapterKeyFile
    [void]$appSecret
    Set-ExitPassDeveloperPrivateFile (Join-Path $state.AdapterSecretRoot 'app-key') 'exitpass-developer-app-key'
    $totpKey = Get-OrCreateExitPassDeveloperPrivateValue $state.TotpKeyFile
    New-ExitPassDeveloperWireMockMappings -RepositoryRoot $RepositoryRoot -DestinationRoot $state.WireMockRoot

    $sourceRevision = (& git.exe -C $RepositoryRoot rev-parse --short=12 HEAD).Trim()
    $sourceRootDocker = $RepositoryRoot.Replace('\', '/')
    $stateRootDocker = $state.Root.Replace('\', '/')
    Set-ExitPassDeveloperPrivateFile $state.EnvironmentFile (@(
        "DEVELOPER_DATABASE_PASSWORD=$databasePassword"
        "DEVELOPER_STATE_ROOT=$stateRootDocker"
        "SOURCE_ROOT=$sourceRootDocker"
        "SOURCE_REVISION=$sourceRevision"
    ) -join "`n")
    Set-ExitPassDeveloperPrivateFile $state.DatabaseConnectionFile "Host=exitpass-dev-synthetic-db;Port=5432;Database=exitpass_dev;Username=exitpass_dev;Password=$databasePassword"

    foreach ($resource in @(
        @('container', 'exitpass-dev-synthetic-db'), @('container', 'exitpass-dev-hikcentral'),
        @('container', 'exitpass-dev-site-adapter'), @('network', 'exitpass-dev-synthetic'),
        @('volume', 'exitpass-dev-synthetic-data'))) {
        Assert-ExitPassDeveloperOwnedResource -Type $resource[0] -Name $resource[1]
    }

    & docker.exe compose --env-file $state.EnvironmentFile -f $state.ComposeFile up --build --detach postgres hikcentral site-adapter | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Developer database, WireMock, or Site Adapter startup failed.' }

    for ($attempt = 1; $attempt -le 90; $attempt++) {
        & docker.exe exec -e "PGPASSWORD=$databasePassword" exitpass-dev-synthetic-db pg_isready -U exitpass_dev -d exitpass_dev | Out-Null
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq 90) { throw 'Developer database did not become ready.' }
        Start-Sleep -Seconds 1
    }

    $schemaState = (& docker.exe exec -e "PGPASSWORD=$databasePassword" exitpass-dev-synthetic-db `
        psql -U exitpass_dev -d exitpass_dev -At -c @"
SELECT concat_ws('|',
    to_regclass('core.parking_sessions') IS NOT NULL,
    to_regclass('identity.human_sessions') IS NOT NULL,
    to_regclass('operator_console.operator_shifts') IS NOT NULL,
    to_regclass('core.terminal_cash_payment_commands') IS NOT NULL);
"@).Trim()
    if ($schemaState -ne 'f|f|f|f' -and $schemaState -ne 't|t|t|t') {
        throw 'Developer database initialization is incomplete. Reset only the Developer database with Start-DeveloperRuntime.ps1 -Action Reset -ConfirmDeveloperReset.'
    }
    if ($schemaState -eq 'f|f|f|f') {
        Invoke-ExitPassDeveloperSqlFile -Path $canonicalSql -DatabasePassword $databasePassword
        Invoke-ExitPassDeveloperSqlFile -Path $canonicalValidator -DatabasePassword $databasePassword
        $scripts = @(
            'infra\db\seed\ExitPass_Reference_Data_v1.2.sql',
            'infra\db\patches\ExitPass_OperatorConsoleSchema_v1.2.sql',
            'infra\db\patches\ExitPass_HumanAuthentication_v1.3.sql',
            'infra\db\patches\validation\Validate_HumanAuthentication_v1.3.sql',
            'infra\db\patches\ExitPass_OperatorConsoleOperatingContext_v1.3.sql',
            'infra\db\patches\validation\Validate_OperatorConsoleOperatingContext_v1.3.sql',
            'infra\db\patches\ExitPass_ShiftManagementMvp_v1.3.sql',
            'docs\sql\HikCentralProjectionSchemaPatch.sql',
            'infra\db\patches\ExitPass_HikCentralProjectionSafety_v1.3.sql',
            'infra\db\patches\ExitPass_MultiSiteVendorAdapterRouting_v1.3.sql',
            'infra\db\patches\ExitPass_Core_PaymentAttemptPaymentMethod_v1.3.sql',
            'infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql',
            'infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql',
            'infra\db\patches\ExitPass_QrphPayMongoRoutingOverride_v1.2.sql'
        )
        foreach ($relativePath in $scripts) {
            Invoke-ExitPassDeveloperSqlFile -Path (Join-Path $RepositoryRoot $relativePath) -DatabasePassword $databasePassword
        }
        Invoke-ExitPassDeveloperSqlFile -Path (Join-Path $RepositoryRoot 'infra\docker\developer-runtime\Configure-DeveloperRuntime.sql') -DatabasePassword $databasePassword
        Invoke-ExitPassDeveloperSqlFile -Path (Join-Path $RepositoryRoot 'infra\db\patches\ExitPass_TerminalCashPaymentCommandReadback_v1.3.sql') -DatabasePassword $databasePassword
    }
    Invoke-ExitPassDeveloperSqlFile -Path (Join-Path $RepositoryRoot 'infra\db\patches\validation\Validate_HumanAuthentication_v1.3.sql') -DatabasePassword $databasePassword
    Write-Host 'DEVELOPER_DATABASE_BOOTSTRAP_PASS'
    Invoke-ExitPassDeveloperSqlFile -Path (Join-Path $RepositoryRoot 'infra\docker\developer-runtime\Configure-DeveloperRuntime.sql') -DatabasePassword $databasePassword

    foreach ($uri in @('http://127.0.0.1:58080/__admin/health', 'http://127.0.0.1:58081/health/ready')) {
        for ($attempt = 1; $attempt -le 90; $attempt++) {
            try {
                $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 2
                if ($response.StatusCode -eq 200) { break }
            } catch { }
            if ($attempt -eq 90) { throw "Developer dependency did not become ready: $uri" }
            Start-Sleep -Seconds 1
        }
    }

    return [pscustomobject]@{
        State = $state
        DatabasePassword = $databasePassword
        DatabaseConnectionString = (Get-Content -LiteralPath $state.DatabaseConnectionFile -Raw).Trim()
        TotpKey = $totpKey
        Ids = $script:DeveloperRuntimeIds
    }
}

function Stop-ExitPassDeveloperRuntime {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string] $RepositoryRoot)
    $state = Get-ExitPassDeveloperRuntimeState -RepositoryRoot $RepositoryRoot
    if (-not (Test-Path -LiteralPath $state.EnvironmentFile)) { return }
    foreach ($resource in @(
        @('container', 'exitpass-dev-synthetic-db'), @('container', 'exitpass-dev-hikcentral'),
        @('container', 'exitpass-dev-site-adapter'), @('network', 'exitpass-dev-synthetic'),
        @('volume', 'exitpass-dev-synthetic-data'))) {
        Assert-ExitPassDeveloperOwnedResource -Type $resource[0] -Name $resource[1]
    }
    & docker.exe compose --env-file $state.EnvironmentFile -f $state.ComposeFile down
    if ($LASTEXITCODE -ne 0) { throw 'Developer runtime shutdown failed.' }
}
