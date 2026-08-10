param(
    [string]$RepositoryRoot,
    [string]$CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2",
    [string]$DatabaseName = "exitpass_webpay_local_walkthrough_statutory",
    [string]$PostgresContainerName = "exitpass-postgres",
    [string]$DatabaseUser = "exitpass",
    [int]$PostgresPort = 5433,
    [int]$CentralPmsPort = 8080,
    [int]$PaymentOrchestratorPort = 8082,
    [int]$MockPaymentProviderPort = 8084,
    [int]$WebPayPort = 5174,
    [int]$OperatorConsolePort = 5175,
    [int]$MinioApiPort = 19000,
    [int]$MinioConsolePort = 19001,
    [int]$ClamAvPort = 13310,
    [string]$MinioImage = "minio/minio:latest",
    [string]$MinioClientImage = "minio/mc:latest",
    [string]$ClamAvImage = "clamav/clamav:stable",
    [switch]$RestartServicesOnly,
    [switch]$AllowExistingPorts,
    [switch]$VisibleServiceWindows,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$CanonicalDatabaseRepository = [System.IO.Path]::GetFullPath($CanonicalDatabaseRepository)
$stateRoot = Join-Path $RepositoryRoot ".local\webpay-statutory-discount-walkthrough"
$statePath = Join-Path $stateRoot "state.json"
$fixtureContextPath = Join-Path $stateRoot "fixture-context.json"
$logsRoot = Join-Path $stateRoot "logs"
$evidenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ExitPass\webpay-statutory-discount-walkthrough"
$syntheticEvidencePath = Join-Path $evidenceRoot "synthetic-senior-citizen-id.png"
$canonicalSql = Join-Path $CanonicalDatabaseRepository "build\generated\exitpass-full-object.generated.sql"
$canonicalValidator = Join-Path $CanonicalDatabaseRepository "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$paymentRoutingPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql"
$payMongoRailPatch = Join-Path $RepositoryRoot "infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql"
$ordinarySeed = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql"
$pilotSeed = Join-Path $RepositoryRoot "scripts\operator-console\Seed-StatutoryDiscountPilotFixture.sql"
$rbacSource = Join-Path $RepositoryRoot "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql"
$statutorySeed = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql"
$statutoryVerify = Join-Path $RepositoryRoot "scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"
$centralPmsProject = Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"
$paymentOrchestratorProject = Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj"
$webPayRoot = Join-Path $RepositoryRoot "src\Services\WebPayUi"
$operatorConsoleRoot = Join-Path $RepositoryRoot "src\Services\OperatorConsoleUi"
$minioContainerName = "exitpass-webpay-statutory-minio"
$clamAvContainerName = "exitpass-webpay-statutory-clamav"
$networkName = "exitpass-webpay-statutory-walkthrough"
$bucketName = "exitpass-webpay-statutory-evidence"
$ownershipLabel = "exitpass.walkthrough=webpay-statutory-discount"

function Assert-SafeDatabaseName([string]$Name) {
    if ($Name -notmatch '^exitpass_webpay_local_walkthrough_statutory(_[a-z0-9_]+)?$') {
        throw "Refusing database '$Name'. Use exitpass_webpay_local_walkthrough_statutory or a suffixed disposable variant."
    }
}

function Assert-PathExists([string]$Path, [string]$Description) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description not found: $Path"
    }
}

function Assert-Tool([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required tool '$Name' was not found in PATH."
    }
}

function Get-RequiredEnvironmentValue([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Set $Name in the current shell. Its value is not printed or persisted by this walkthrough."
    }
    return $value
}

function New-CryptographicRandomBytes([int]$Length) {
    if ($Length -le 0) {
        throw "Cryptographic random byte length must be greater than zero."
    }

    $bytes = New-Object byte[] $Length
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return ,$bytes
    }
    finally {
        $generator.Dispose()
    }
}

function Get-Sha256HashBytes([byte[]]$Bytes) {
    if ($null -eq $Bytes) {
        throw "SHA-256 input is required."
    }

    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $algorithm.ComputeHash($Bytes)
        return ,$hash
    }
    finally {
        $algorithm.Dispose()
    }
}

function ConvertTo-LowercaseHex([byte[]]$Bytes) {
    if ($null -eq $Bytes) {
        throw "Hexadecimal input is required."
    }

    return ([System.BitConverter]::ToString($Bytes) -replace '-', '').ToLowerInvariant()
}

function New-CryptographicRandomLowercaseHex([int]$Length) {
    $bytes = New-CryptographicRandomBytes $Length
    try {
        return ConvertTo-LowercaseHex $bytes
    }
    finally {
        [System.Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Assert-CryptographicRuntimeCompatibility {
    $randomBytes = $null
    $hashBytes = $null
    try {
        $randomBytes = New-CryptographicRandomBytes 32
        if ($randomBytes.Length -ne 32) {
            throw "Cryptographic random generation returned an unexpected byte length."
        }

        $hexProbe = ConvertTo-LowercaseHex ([byte[]](0, 15, 16, 171, 255))
        if ($hexProbe -cne '000f10abff') {
            throw "Lowercase hexadecimal conversion did not match the required format."
        }

        $hashBytes = Get-Sha256HashBytes ([System.Text.Encoding]::ASCII.GetBytes('abc'))
        $hashHex = ConvertTo-LowercaseHex $hashBytes
        if ($hashHex -cne 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad') {
            throw "SHA-256 runtime validation did not match the expected test vector."
        }
    }
    catch {
        throw "Cryptographic runtime compatibility validation failed safely: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $randomBytes) {
            [System.Array]::Clear($randomBytes, 0, $randomBytes.Length)
        }
        if ($null -ne $hashBytes) {
            [System.Array]::Clear($hashBytes, 0, $hashBytes.Length)
        }
    }
}

function Test-PortOpen([int]$Port) {
    return $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
}

function Assert-PortAvailable([int]$Port, [string]$Purpose) {
    if (-not $AllowExistingPorts -and (Test-PortOpen $Port)) {
        throw "Port $Port is already listening for $Purpose. Stop the existing listener or select another port."
    }
}

function Invoke-PostgresSql([string]$Database, [string]$Sql) {
    docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL command failed for database $Database." }
}

function Invoke-PostgresFile([string]$Database, [string]$Path) {
    Get-Content -LiteralPath $Path -Raw | docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $Database
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL file failed for database $Database`: $Path" }
}

function Invoke-StatutorySeed(
    [guid]$ChallengeReference,
    [string]$ChallengeHash,
    [string]$PlaceholderVerifierHex,
    [string]$PlaceholderSaltHex
) {
    $preamble = @(
        "\set reviewer_challenge_reference '$($ChallengeReference.ToString('D'))'",
        "\set reviewer_challenge_hash '$ChallengeHash'",
        "\set placeholder_verifier_hex '$PlaceholderVerifierHex'",
        "\set placeholder_salt_hex '$PlaceholderSaltHex'"
    )
    @($preamble; Get-Content -LiteralPath $statutorySeed) |
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d $DatabaseName
    if ($LASTEXITCODE -ne 0) { throw "Statutory walkthrough seed failed." }
}

function Invoke-PostgresQueryText([string]$Sql) {
    $value = docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -t -A -U $DatabaseUser -d $DatabaseName -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL query failed for database $DatabaseName." }
    return (($value | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
}

function Ensure-SharedContainerRunning([string]$Name) {
    $exists = docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $Name }
    if (-not $exists) { throw "Required shared local container '$Name' does not exist. Use the current ordinary WebPay local-integration prerequisites first." }
    $running = docker inspect --format '{{.State.Running}}' $Name
    if ($running -ne 'true') {
        docker start $Name | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not start required shared container '$Name'." }
    }
}

function Assert-WalkthroughContainerAbsent([string]$Name) {
    if (docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $Name }) {
        $label = docker inspect --format '{{index .Config.Labels "exitpass.walkthrough"}}' $Name
        if ($label -ne 'webpay-statutory-discount') {
            throw "Container '$Name' exists without the walkthrough ownership label. It will not be changed."
        }
        throw "Walkthrough container '$Name' already exists. Run the stop script with -StopWalkthroughContainers before a fresh start."
    }
}

function Wait-HttpReady([string]$Name, [string]$Url, [int]$TimeoutSeconds = 120) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec 5 -UseBasicParsing
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) { return }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch { $lastError = $_.Exception.Message }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "$Name was not ready at $Url. Last error: $lastError"
}

function Wait-TcpReady([string]$Name, [int]$Port, [int]$TimeoutSeconds = 180) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $client = [System.Net.Sockets.TcpClient]::new()
        try {
            $task = $client.ConnectAsync('127.0.0.1', $Port)
            if ($task.Wait(1000) -and $client.Connected) { return }
        }
        catch { }
        finally { $client.Dispose() }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    throw "$Name did not listen on 127.0.0.1:$Port within $TimeoutSeconds seconds."
}

function Start-WalkthroughProcess([string]$Name, [string]$WorkingDirectory, [string]$Command) {
    $stdout = Join-Path $logsRoot "$Name.stdout.log"
    $stderr = Join-Path $logsRoot "$Name.stderr.log"
    $windowStyle = if ($VisibleServiceWindows) { 'Normal' } else { 'Hidden' }
    return Start-Process powershell -ArgumentList @('-NoProfile', '-Command', $Command) `
        -WorkingDirectory $WorkingDirectory -WindowStyle $windowStyle -PassThru `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr
}

function Get-ProcessRecord([string]$Name, [int]$Id, [string[]]$Markers, [int]$Port = 0) {
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop
    foreach ($marker in $Markers) {
        if ($process.CommandLine -notlike "*$marker*") {
            throw "$Name PID $($process.ProcessId) does not contain expected command marker '$marker'."
        }
    }
    $runtime = Get-Process -Id $process.ProcessId -ErrorAction Stop
    return [pscustomobject]@{
        Name = $Name
        Id = [int]$process.ProcessId
        Port = $Port
        ExecutablePath = $process.ExecutablePath
        CommandLineMarkers = $Markers
        StartTimeUtc = $runtime.StartTime.ToUniversalTime().ToString('o')
    }
}

function Get-ListenerRecord([string]$Name, [int]$Port, [string[]]$Markers) {
    $connection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop | Select-Object -First 1
    return Get-ProcessRecord $Name ([int]$connection.OwningProcess) $Markers $Port
}

function New-SyntheticEvidenceImage([string]$Path) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    & icacls.exe $directory /inheritance:r /grant:r "$env:USERNAME`:(OI)(CI)F" | Out-Null
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new(96, 64)
    try {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                $color = if (([math]::Floor($x / 8) + [math]::Floor($y / 8)) % 2 -eq 0) {
                    [System.Drawing.Color]::FromArgb(36, 99, 132)
                } else {
                    [System.Drawing.Color]::FromArgb(238, 240, 232)
                }
                $bitmap.SetPixel($x, $y, $color)
            }
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}

Assert-SafeDatabaseName $DatabaseName
Assert-Tool docker
Assert-Tool dotnet
Assert-Tool npm
foreach ($path in @($canonicalSql, $canonicalValidator, $paymentRoutingPatch, $payMongoRailPatch, $ordinarySeed, $pilotSeed, $rbacSource, $statutorySeed, $statutoryVerify, $centralPmsProject, $paymentOrchestratorProject, $webPayRoot, $operatorConsoleRoot)) {
    Assert-PathExists $path "Required current walkthrough dependency"
}

Assert-CryptographicRuntimeCompatibility

if ($DryRun) {
    Write-Host "DRY RUN: cryptographic runtime compatibility validation passed."
    Write-Host "DRY RUN: current paths, database guard, tools, ports, configuration names, and composition were validated."
    Write-Host "DRY RUN: no container, database, service, credential, evidence, or state mutation was performed."
    return
}

$dbPassword = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD'
$reviewerPassword = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD'
$minioAccessKey = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY'
$minioSecretKey = Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY'
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY')
[void](Get-RequiredEnvironmentValue 'EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET')

foreach ($port in @($CentralPmsPort, $PaymentOrchestratorPort, $WebPayPort, $OperatorConsolePort)) {
    Assert-PortAvailable $port "walkthrough service"
}

New-Item -ItemType Directory -Force -Path $stateRoot, $logsRoot | Out-Null

$fixtureContext = $null
$activationReference = $null
$activationSecret = $null

if ($RestartServicesOnly) {
    if (-not (Test-Path -LiteralPath $statePath)) { throw "Restart requires existing state at $statePath." }
    $previousState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($previousState.DatabaseName -ne $DatabaseName) { throw "Restart database does not match recorded walkthrough state." }
    $fixtureContext = $previousState.Fixture
    foreach ($container in @($minioContainerName, $clamAvContainerName)) {
        if ((docker inspect --format '{{.State.Running}}' $container) -ne 'true') { throw "Restart requires running walkthrough container '$container'." }
    }
}
else {
    foreach ($port in @($MinioApiPort, $MinioConsolePort, $ClamAvPort)) { Assert-PortAvailable $port "walkthrough dependency" }
    Ensure-SharedContainerRunning $PostgresContainerName
    Ensure-SharedContainerRunning 'exitpass-rabbitmq'
    Ensure-SharedContainerRunning 'exitpass-mock-payment-provider'
    Assert-WalkthroughContainerAbsent $minioContainerName
    Assert-WalkthroughContainerAbsent $clamAvContainerName

    Write-Host "Rebuilding guarded disposable database $DatabaseName..." -ForegroundColor Yellow
    Invoke-PostgresSql postgres "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DatabaseName';"
    Invoke-PostgresSql postgres "DROP DATABASE IF EXISTS $DatabaseName;"
    Invoke-PostgresSql postgres "CREATE DATABASE $DatabaseName;"
    Invoke-PostgresFile $DatabaseName $canonicalSql
    Invoke-PostgresFile $DatabaseName $canonicalValidator
    Invoke-PostgresFile $DatabaseName $paymentRoutingPatch
    Invoke-PostgresFile $DatabaseName $payMongoRailPatch
    Invoke-PostgresFile $DatabaseName $ordinarySeed
    Invoke-PostgresFile $DatabaseName $pilotSeed
    # The tracked Management Platform RBAC file is inspected as the authority
    # source, but its own database-name guard intentionally excludes this DB.
    # The bounded statutory seed carries only its exact reviewer permissions.

    $activationReference = [guid]::NewGuid()
    $activationBytes = New-CryptographicRandomBytes 32
    try {
        $activationSecret = [Convert]::ToBase64String($activationBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally {
        [System.Array]::Clear($activationBytes, 0, $activationBytes.Length)
        $activationBytes = $null
    }
    $activationHashBytes = Get-Sha256HashBytes ([Text.Encoding]::UTF8.GetBytes($activationSecret))
    try {
        $activationHash = ConvertTo-LowercaseHex $activationHashBytes
    }
    finally {
        [System.Array]::Clear($activationHashBytes, 0, $activationHashBytes.Length)
        $activationHashBytes = $null
    }
    $placeholderVerifier = New-CryptographicRandomLowercaseHex 32
    $placeholderSalt = New-CryptographicRandomLowercaseHex 16
    Invoke-StatutorySeed $activationReference $activationHash $placeholderVerifier $placeholderSalt

    $contextSql = @"
SELECT json_build_object(
 'ticketReference', ps.ticket_number_masked,
 'parkingSessionId', ps.parking_session_id,
 'siteId', ps.site_id,
 'siteGroupId', ps.site_group_id,
 'vendorSystemId', ps.vendor_system_id,
 'webPayServiceIdentityId', '78000000-0000-4000-8000-000000000003'::uuid,
 'reviewerUsername', u.username,
 'reviewerUserId', u.user_id,
 'operatorDeviceBindingId', (SELECT operator_device_binding_id FROM operator_console.operator_device_bindings WHERE device_binding_code='SANDBOX-OC-SD-235A-DEVICE'),
 'operatorShiftId', (SELECT operator_shift_id FROM operator_console.operator_shifts WHERE external_shift_id_masked='SHIFT-SANDBOX-REVIEWER'),
 'ordinaryTicketReference', 'WEBPAY-LOCAL-ORDINARY-001',
 'missingJurisdictionTicket', 'WEBPAY-STAT-MISSING-JURISDICTION',
 'ambiguousJurisdictionTicket', 'WEBPAY-STAT-AMBIGUOUS-JURISDICTION',
 'noPolicyTicket', 'WEBPAY-STAT-NO-POLICY')
FROM core.parking_sessions ps
JOIN identity.users u ON u.username_normalized='sandbox-oc-sd-pilot-reviewer'
WHERE ps.ticket_number_masked='E2E-231-SESSION-001';
"@
    $fixtureContext = (Invoke-PostgresQueryText $contextSql) | ConvertFrom-Json
    $fixtureContext | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fixtureContextPath -Encoding UTF8

    docker network create --label $ownershipLabel $networkName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create walkthrough Docker network." }
    $env:MINIO_ROOT_USER = $minioAccessKey
    $env:MINIO_ROOT_PASSWORD = $minioSecretKey
    try {
        docker run -d --name $minioContainerName --network $networkName --label $ownershipLabel `
            -p "127.0.0.1:$MinioApiPort`:9000" -p "127.0.0.1:$MinioConsolePort`:9001" `
            -e MINIO_ROOT_USER -e MINIO_ROOT_PASSWORD $MinioImage server /data --console-address ':9001' | Out-Null
    }
    finally {
        Remove-Item Env:\MINIO_ROOT_USER -ErrorAction SilentlyContinue
        Remove-Item Env:\MINIO_ROOT_PASSWORD -ErrorAction SilentlyContinue
    }
    if ($LASTEXITCODE -ne 0) { throw "Could not start private MinIO container." }

    docker run -d --name $clamAvContainerName --network $networkName --label $ownershipLabel `
        -p "127.0.0.1:$ClamAvPort`:3310" $ClamAvImage | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not start ClamAV container." }
    Wait-HttpReady 'MinIO' "http://127.0.0.1:$MinioApiPort/minio/health/ready"
    Wait-TcpReady 'ClamAV' $ClamAvPort 300

    $env:MC_HOST_walkthrough = "http://$minioAccessKey`:$minioSecretKey@$minioContainerName`:9000"
    try {
        docker run --rm --network $networkName -e MC_HOST_walkthrough $MinioClientImage mb --ignore-existing "walkthrough/$bucketName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not create the private walkthrough bucket." }
        docker run --rm --network $networkName -e MC_HOST_walkthrough $MinioClientImage anonymous set none "walkthrough/$bucketName" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not enforce private bucket policy." }
    }
    finally { Remove-Item Env:\MC_HOST_walkthrough -ErrorAction SilentlyContinue }

    New-SyntheticEvidenceImage $syntheticEvidencePath
}

Write-Host "Building current Central PMS and Payment Orchestrator..." -ForegroundColor Yellow
dotnet build $centralPmsProject
if ($LASTEXITCODE -ne 0) { throw "Central PMS build failed." }
dotnet build $paymentOrchestratorProject
if ($LASTEXITCODE -ne 0) { throw "Payment Orchestrator build failed." }

$connectionExpression = "'Host=127.0.0.1;Port=$PostgresPort;Database=$DatabaseName;Username=$DatabaseUser;Password=' + `$env:EXITPASS_WEBPAY_STATUTORY_DB_PASSWORD + ';Include Error Detail=false'"
$centralPmsUrl = "http://127.0.0.1:$CentralPmsPort"
$paymentOrchestratorUrl = "http://127.0.0.1:$PaymentOrchestratorPort"
$webPayUrl = "http://127.0.0.1:$WebPayPort"
$operatorConsoleUrl = "http://127.0.0.1:$OperatorConsolePort"
$mockProviderUrl = "http://127.0.0.1:$MockPaymentProviderPort"

$centralCommand = @"
`$env:ASPNETCORE_ENVIRONMENT='Production'
`$env:ASPNETCORE_URLS='$centralPmsUrl'
`$env:ConnectionStrings__MainDatabase=$connectionExpression
`$env:HumanAuthentication__AllowedWebOrigins__0='$operatorConsoleUrl'
`$env:HumanAuthentication__TotpProtectionKeyBase64=`$env:EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY_BASE64
`$env:HumanAuthentication__TotpProtectionKeyReference='webpay-statutory-local'
`$env:HumanAuthentication__TotpProtectionKeyVersion='1'
`$env:CentralPms__StatutoryEvidence__Upload__Endpoint='http://127.0.0.1:$MinioApiPort'
`$env:CentralPms__StatutoryEvidence__Upload__PublicUploadEndpoint='http://127.0.0.1:$MinioApiPort'
`$env:CentralPms__StatutoryEvidence__Upload__Region='us-east-1'
`$env:CentralPms__StatutoryEvidence__Upload__BucketName='$bucketName'
`$env:CentralPms__StatutoryEvidence__Upload__BucketReference='webpay-statutory-private'
`$env:CentralPms__StatutoryEvidence__Upload__AccessKeyId=`$env:EXITPASS_WEBPAY_STATUTORY_MINIO_ACCESS_KEY
`$env:CentralPms__StatutoryEvidence__Upload__SecretAccessKey=`$env:EXITPASS_WEBPAY_STATUTORY_MINIO_SECRET_KEY
`$env:CentralPms__StatutoryEvidence__Upload__EnvironmentPartition='local'
`$env:CentralPms__StatutoryEvidence__Upload__MaxContentLengthBytes='5242880'
`$env:CentralPms__StatutoryEvidence__Upload__RequireServerSideEncryptionMetadata='false'
`$env:CentralPms__StatutoryEvidence__Channel__EnvironmentScope='LOCAL_TEST'
`$env:CentralPms__StatutoryEvidence__Channel__SeniorCitizenDocumentProfileCode='SENIOR_CITIZEN_ID_FRONT_BACK_V1'
`$env:CentralPms__StatutoryEvidence__Channel__PwdDocumentProfileCode='PWD_ID_FRONT_BACK_V1'
`$env:CentralPms__StatutoryEvidence__Channel__RequiredDocumentProfileVersion='1'
`$env:CentralPms__StatutoryEvidence__Channel__SingleDocumentItemRole='SINGLE_DOCUMENT'
`$env:CentralPms__StatutoryEvidence__ScanWorker__Enabled='true'
`$env:CentralPms__StatutoryEvidence__ScanWorker__PollIntervalSeconds='2'
`$env:CentralPms__StatutoryEvidence__ScanWorker__MaxContentLengthBytes='5242880'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerProvider='CLAMAV_COMPATIBLE'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerEndpoint='127.0.0.1'
`$env:CentralPms__StatutoryEvidence__ScanWorker__ScannerPort='$ClamAvPort'
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
dotnet run --project '$centralPmsProject' --no-launch-profile --no-build
"@

$orchestratorCommand = @"
`$env:ASPNETCORE_ENVIRONMENT='Production'
`$env:ASPNETCORE_URLS='$paymentOrchestratorUrl'
`$env:ConnectionStrings__MainDatabase=$connectionExpression
`$env:Integrations__CentralPms__BaseUrl='$centralPmsUrl'
`$env:Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId='$($fixtureContext.webPayServiceIdentityId)'
`$env:WEBPAY_PUBLIC_BASE_URL='$webPayUrl'
`$env:WebPay__PublicBaseUrl='$webPayUrl'
`$env:Payments__Providers__PayMongo__BaseUrl='$mockProviderUrl'
`$env:Payments__Providers__PayMongo__SecretKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_SECRET_KEY
`$env:Payments__Providers__PayMongo__PublicKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_PUBLIC_KEY
`$env:Payments__Providers__PayMongo__WebhookSecretKey=`$env:EXITPASS_WEBPAY_STATUTORY_PAYMONGO_WEBHOOK_SECRET
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
dotnet run --project '$paymentOrchestratorProject' --no-launch-profile --no-build
"@

$webPayCommand = @"
`$env:VITE_WEBPAY_API_PROXY_TARGET='$paymentOrchestratorUrl'
`$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID='$($fixtureContext.siteGroupId)'
`$env:VITE_WEBPAY_DEFAULT_SITE_ID='$($fixtureContext.siteId)'
`$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID='$($fixtureContext.vendorSystemId)'
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
npm.cmd run dev -- --host 127.0.0.1 --port $WebPayPort
"@

$operatorCommand = @"
`$env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET='$centralPmsUrl'
`$env:VITE_OPERATOR_CONSOLE_DEVICE_BINDING_ID='$($fixtureContext.operatorDeviceBindingId)'
`$env:VITE_OPERATOR_CONSOLE_SHIFT_ID='$($fixtureContext.operatorShiftId)'
`$env:VITE_OPERATOR_CONSOLE_SITE_ID='$($fixtureContext.siteId)'
`$env:VITE_OPERATOR_CONSOLE_SITE_GROUP_ID='$($fixtureContext.siteGroupId)'
Remove-Item Env:\EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD -ErrorAction SilentlyContinue
npm.cmd run dev -- --host 127.0.0.1 --port $OperatorConsolePort
"@

$launcherProcesses = @(
    [pscustomobject]@{ Name = 'central-pms-launcher'; Marker = 'ExitPass.CentralPms.Api'; Process = (Start-WalkthroughProcess 'central-pms' $RepositoryRoot $centralCommand) },
    [pscustomobject]@{ Name = 'payment-orchestrator-launcher'; Marker = 'ExitPass.PaymentOrchestrator.Api'; Process = (Start-WalkthroughProcess 'payment-orchestrator' $RepositoryRoot $orchestratorCommand) },
    [pscustomobject]@{ Name = 'webpay-ui-launcher'; Marker = "--port $WebPayPort"; Process = (Start-WalkthroughProcess 'webpay-ui' $webPayRoot $webPayCommand) },
    [pscustomobject]@{ Name = 'operator-console-ui-launcher'; Marker = "--port $OperatorConsolePort"; Process = (Start-WalkthroughProcess 'operator-console-ui' $operatorConsoleRoot $operatorCommand) }
)

Wait-HttpReady 'Central PMS readiness' "$centralPmsUrl/health/ready" 180
Wait-HttpReady 'Payment Orchestrator readiness' "$paymentOrchestratorUrl/health/ready" 180
Wait-HttpReady 'WebPay UI' $webPayUrl 120
Wait-HttpReady 'Operator Console UI' $operatorConsoleUrl 120
Wait-HttpReady 'Mock payment provider' "$mockProviderUrl/__admin/mappings" 60

if (-not $RestartServicesOnly) {
    $activationBody = @{
        challengeReference = $activationReference
        challengeSecret = $activationSecret
        newPassword = $reviewerPassword
    } | ConvertTo-Json
    $activationResponse = Invoke-WebRequest -Uri "$centralPmsUrl/v1/human-authentication/activations" `
        -Method Post -ContentType 'application/json' -Headers @{ Origin = $operatorConsoleUrl } `
        -Body $activationBody -UseBasicParsing
    if ($activationResponse.StatusCode -ne 200) { throw "Synthetic reviewer activation failed safely with HTTP $($activationResponse.StatusCode)." }
    $activationSecret = $null
    $activationBody = $null
}

$fixtureHeaderStatus = $null
try {
    Invoke-WebRequest -Uri "$centralPmsUrl/v1/ops/operator-console/statutory-discounts/reviews/pending" `
        -Headers @{ 'X-ExitPass-User-Id' = $fixtureContext.reviewerUserId } -UseBasicParsing | Out-Null
    $fixtureHeaderStatus = 200
}
catch {
    $fixtureHeaderStatus = [int]$_.Exception.Response.StatusCode
}
if ($fixtureHeaderStatus -lt 400) { throw "Production fixture identity header was unexpectedly accepted." }

$listenerRecords = @(
    (Get-ListenerRecord 'central-pms' $CentralPmsPort @('ExitPass.CentralPms.Api')),
    (Get-ListenerRecord 'payment-orchestrator' $PaymentOrchestratorPort @('ExitPass.PaymentOrchestrator.Api')),
    (Get-ListenerRecord 'webpay-ui' $WebPayPort @('vite', "$WebPayPort")),
    (Get-ListenerRecord 'operator-console-ui' $OperatorConsolePort @('vite', "$OperatorConsolePort"))
)
$launcherRecords = @($launcherProcesses | ForEach-Object {
    Get-ProcessRecord $_.Name ([int]$_.Process.Id) @($_.Marker)
})

$containerRecords = foreach ($name in @($minioContainerName, $clamAvContainerName)) {
    [pscustomobject]@{ Name = $name; Id = (docker inspect --format '{{.Id}}' $name); OwnershipLabel = 'webpay-statutory-discount' }
}

$state = [pscustomobject]@{
    DatabaseName = $DatabaseName
    PostgresContainerName = $PostgresContainerName
    StartedAt = (Get-Date).ToUniversalTime().ToString('o')
    ProductionHosted = $true
    FixtureHeaderProbeStatus = $fixtureHeaderStatus
    Processes = $listenerRecords
    Launchers = $launcherRecords
    Containers = $containerRecords
    Network = $networkName
    EvidenceRoot = $evidenceRoot
    SyntheticEvidencePath = $syntheticEvidencePath
    Fixture = $fixtureContext
    Urls = [pscustomobject]@{ CentralPms = $centralPmsUrl; PaymentOrchestrator = $paymentOrchestratorUrl; WebPay = $webPayUrl; OperatorConsole = $operatorConsoleUrl }
}
$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding UTF8

Write-Host "WebPay statutory-discount walkthrough is ready for manual local execution." -ForegroundColor Green
Write-Host "WebPay:          $webPayUrl/?ticketReference=$($fixtureContext.ticketReference)"
Write-Host "Operator Console:$operatorConsoleUrl"
Write-Host "Reviewer username: $($fixtureContext.reviewerUsername)"
Write-Host "Synthetic evidence: $syntheticEvidencePath"
Write-Host "The reviewer password and all infrastructure secrets remain in the caller-owned environment and were not printed or stored."
Write-Host "This startup is local-development preparation only. It is not walkthrough execution, Controlled UAT, compliance evidence, or production authorization."
