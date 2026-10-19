[CmdletBinding()]
param(
    [switch] $SmokeTest,
    [switch] $SkipEvidenceGovernanceInitialization,
    [string] $OperatorConsolePublicOrigin = 'https://operator-console-exitpass.ngrok.dev',
    [string] $AnnexE1GovernedCorrectionApiKeyFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$databaseContainer = 'exitpass-ist-persistent-db'
$databaseName = 'exitpass_ist'
$databaseUser = 'exitpass_ist'
$databaseVolume = 'exitpass-ist-persistent-data'
$networkName = 'exitpass-ist-persistent'
$containerName = 'exitpass-central-pms-pitx-local'
$httpUrl = 'http://127.0.0.1:56065'
$httpsUrl = 'https://localhost:56064'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$dockerfilePath = Join-Path $repoRoot 'src\Services\CentralPms\src\ExitPass.CentralPms.Api\Dockerfile'
$privateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_PERSISTENT_IST_ROOT)) {
    'D:\SourceCodes\ExitPass.local\persistent-ist'
}
else {
    $env:EXITPASS_PERSISTENT_IST_ROOT
}
$environmentFile = Join-Path $privateRoot 'runtime\restart-41-business\central.env'
$posApiKeyFile = Join-Path $privateRoot 'runtime\restart-41-business\pos-api-key'
$annexE1GovernedCorrectionApiKeyFile = if ([string]::IsNullOrWhiteSpace($AnnexE1GovernedCorrectionApiKeyFile)) {
    Join-Path $privateRoot 'runtime\restart-41-business\central-pms-annex-e1-governed-api-key'
}
else {
    [IO.Path]::GetFullPath($AnnexE1GovernedCorrectionApiKeyFile)
}
$annexE1GovernedCorrectionApiKeyContainerPath = '/run/exitpass/annex-e1-governed-api-key'
$adapterApiKeyFile = Join-Path $privateRoot 'site-adapters\pitx-level-3\central-pms-api-key'
$dataProtectionKeyDirectory = Join-Path $privateRoot 'data-protection\central-pms'
$evidenceServicesLauncherPath = Join-Path $PSScriptRoot 'Start-StatutoryEvidenceServices.ps1'
$evidenceGovernanceInitializerPath = Join-Path $PSScriptRoot 'Initialize-StatutoryEvidenceRuntime.ps1'
$mtlsProvisionerPath = Join-Path $PSScriptRoot 'Initialize-WebPayStatutoryMtls.ps1'
$containerStarted = $false
$normalizedOperatorConsolePublicOrigin = $OperatorConsolePublicOrigin.TrimEnd('/')
if (-not [Uri]::IsWellFormedUriString($normalizedOperatorConsolePublicOrigin, [UriKind]::Absolute)) {
    throw "Operator Console public origin must be an absolute URI."
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Get-PrivateEnvironmentValue {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name
    )

    $prefix = "$Name="
    $line = Get-Content -LiteralPath $Path |
        Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) } |
        Select-Object -Last 1
    if ($null -eq $line) {
        return $null
    }

    return $line.Substring($prefix.Length)
}

function Assert-PrivateEnvironmentValue {
    param([Parameter(Mandatory)][string] $Name)

    if ([string]::IsNullOrWhiteSpace((Get-PrivateEnvironmentValue $environmentFile $Name))) {
        throw "The private Central PMS environment is missing required setting '$Name'."
    }
}

function Assert-ReadablePrivateFile {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description was not found at $Path."
    }

    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $stream.Dispose()
    }
    catch {
        throw "$Description is not readable at $Path."
    }
}

function Assert-AnnexE1GovernedCredentialConfiguration {
    param(
        [Parameter(Mandatory)][string] $EnvironmentFile,
        [Parameter(Mandatory)][string] $CredentialFile,
        [Parameter(Mandatory)][string] $ContainerPath
    )

    Assert-ReadablePrivateFile $CredentialFile 'The Annex E-1 governed-correction credential'

    $settingName = 'FiscalIssuance__PosServerIntegration__Endpoints__0__AnnexE1GovernedCorrectionApiKeyFile'
    $configuredPath = Get-PrivateEnvironmentValue $EnvironmentFile $settingName
    if ($configuredPath -cne $ContainerPath) {
        throw "The private Central PMS environment setting '$settingName' must map to '$ContainerPath'."
    }
}

function Wait-ForHealth {
    param([int] $Attempts = 180)

    $uri = "$httpUrl/health/ready"
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $uri -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Central PMS did not become ready at $uri. Inspect with: docker logs $containerName"
            }
        }

        Start-Sleep -Seconds 1
    }
}

if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw 'Docker Desktop is required for the persistent PITX runtime network.'
}
if (-not (Test-Path -LiteralPath $dockerfilePath) -or
    -not (Test-Path -LiteralPath $mtlsProvisionerPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $evidenceGovernanceInitializerPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $evidenceServicesLauncherPath -PathType Leaf)) {
    throw 'Run this launcher from a complete ExitPass source checkout.'
}
foreach ($requiredFile in @($environmentFile, $posApiKeyFile, $adapterApiKeyFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required persistent PITX private configuration was not found at $requiredFile."
    }
}
Assert-AnnexE1GovernedCredentialConfiguration `
    -EnvironmentFile $environmentFile `
    -CredentialFile $annexE1GovernedCorrectionApiKeyFile `
    -ContainerPath $annexE1GovernedCorrectionApiKeyContainerPath
if (Test-Path -LiteralPath $dataProtectionKeyDirectory) {
    if (-not (Test-Path -LiteralPath $dataProtectionKeyDirectory -PathType Container)) {
        throw "Central PMS Data Protection key path is not a directory: $dataProtectionKeyDirectory"
    }
}
else {
    New-Item -ItemType Directory -Path $dataProtectionKeyDirectory -Force | Out-Null
}

$databaseConnection = Get-PrivateEnvironmentValue $environmentFile 'ConnectionStrings__MainDatabase'
if ([string]::IsNullOrWhiteSpace($databaseConnection) -or
    $databaseConnection -notmatch '(?i)(^|;)\s*Host=exitpass-ist-persistent-db\s*(;|$)' -or
    $databaseConnection -notmatch '(?i)(^|;)\s*Database=exitpass_ist\s*(;|$)') {
    throw 'The private Central PMS environment does not target the authoritative persistent PITX IST database.'
}

foreach ($requiredName in @(
        'HumanAuthentication__TotpProtectionKeyBase64',
        'CentralPms__VendorPms__CentralPmsServiceIdentityId',
        'CentralPms__VendorPms__AdapterSecretMountRoot',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__SiteId',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__SitePosServerId',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__BaseUrl',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__WebPayTerminalIdentifier',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__0__ChannelTerminalId',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__0__TerminalIdentifier',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__1__ChannelTerminalId',
        'FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__1__TerminalIdentifier',
        'CentralPms__VendorSessionProjections__SchedulerEnabled',
        'CentralPms__VendorSessionProjections__RequiredForEnvironment',
        'CentralPms__VendorSessionProjections__SchedulerScanIntervalSeconds'
    )) {
    Assert-PrivateEnvironmentValue $requiredName
}
$webPayTerminalIdentifier = Get-PrivateEnvironmentValue $environmentFile 'FiscalIssuance__PosServerIntegration__Endpoints__0__WebPayTerminalIdentifier'
$siteIdText = Get-PrivateEnvironmentValue $environmentFile 'FiscalIssuance__PosServerIntegration__Endpoints__0__SiteId'
$siteId = [guid]::Empty
if (-not [guid]::TryParse($siteIdText, [ref]$siteId) -or $siteId -eq [guid]::Empty) {
    throw "The private Central PMS Site ID '$siteIdText' is invalid."
}
$terminalMappings = foreach ($index in 0, 1) {
    $terminalIdName = "FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__${index}__ChannelTerminalId"
    $terminalIdentifierName = "FiscalIssuance__PosServerIntegration__Endpoints__0__ChannelTerminals__${index}__TerminalIdentifier"
    $terminalIdText = Get-PrivateEnvironmentValue $environmentFile $terminalIdName
    $terminalId = [guid]::Empty
    if (-not [guid]::TryParse($terminalIdText, [ref]$terminalId) -or $terminalId -eq [guid]::Empty) {
        throw "The private Central PMS environment setting '$terminalIdName' is not a valid channel terminal ID."
    }

    [pscustomobject]@{
        ChannelTerminalId = $terminalId
        TerminalIdentifier = (Get-PrivateEnvironmentValue $environmentFile $terminalIdentifierName).Trim()
    }
}
if (($terminalMappings.ChannelTerminalId | Sort-Object -Unique).Count -ne $terminalMappings.Count -or
    ($terminalMappings.TerminalIdentifier | ForEach-Object { $_.ToUpperInvariant() } | Sort-Object -Unique).Count -ne $terminalMappings.Count) {
    throw 'The private Central PMS channel terminal mappings contain duplicate identities.'
}
if ($terminalMappings[0].TerminalIdentifier -cne $webPayTerminalIdentifier) {
    throw 'The private Central PMS WebPay terminal selection does not match its configured channel terminal mapping.'
}
$aptTerminalMappings = @($terminalMappings | Where-Object {
        $_.TerminalIdentifier -cne $webPayTerminalIdentifier
    })
if ($aptTerminalMappings.Count -ne 1) {
    throw 'The private Central PMS channel terminal mappings do not identify exactly one APT terminal.'
}
if ((Get-PrivateEnvironmentValue $environmentFile 'CentralPms__VendorPms__Provider') -ne 'SITE_ADAPTER' -or
    (Get-PrivateEnvironmentValue $environmentFile 'CentralPms__VendorSessionProjections__SchedulerEnabled') -ne 'true' -or
    (Get-PrivateEnvironmentValue $environmentFile 'CentralPms__VendorSessionProjections__RequiredForEnvironment') -ne 'true') {
    throw 'The private Central PMS environment is not the approved persistent PITX Site Adapter/projection configuration.'
}

$runningDatabase = (& docker.exe ps --filter "name=^/$databaseContainer$" --format '{{.Names}}')
if ($LASTEXITCODE -ne 0 -or $runningDatabase -ne $databaseContainer) {
    throw "The persistent ExitPass database is not running. Start container '$databaseContainer' before launching Central PMS."
}
$databaseInspectJson = (& docker.exe inspect $databaseContainer)
$databaseInspect = $databaseInspectJson | ConvertFrom-Json
if (-not $databaseInspect[0].State.Running) {
    throw "The persistent ExitPass database is not running. Start container '$databaseContainer' before launching Central PMS."
}
$mountedVolume = $databaseInspect[0].Mounts |
    Where-Object { $_.Destination -eq '/var/lib/postgresql/data' } |
    Select-Object -ExpandProperty Name -First 1
if ($mountedVolume -ne $databaseVolume) {
    throw "Container '$databaseContainer' is not attached to expected volume '$databaseVolume'."
}
Invoke-CheckedCommand docker.exe @(
    'exec', $databaseContainer, 'pg_isready', '-U', $databaseUser, '-d', $databaseName
) "The persistent ExitPass database is unavailable. Verify '$databaseContainer' and retry."

$aptRegistrationSql = @'
SELECT device.device_id::text || '|' || btrim(device.terminal_identifier)
FROM identity.devices device
JOIN identity.service_identities service
  ON service.service_identity_id = device.service_identity_id
JOIN sites.device_assignments assignment
  ON assignment.service_identity_id = service.service_identity_id
WHERE device.device_type = 'ASSISTED_PAYMENT_TERMINAL'
  AND device.lifecycle_status = 'ACTIVE'
  AND device.effective_from <= now()
  AND (device.effective_to IS NULL OR device.effective_to > now())
  AND service.identity_type = 'DEVICE'
  AND service.identity_status = 'ACTIVE'
  AND service.owning_service_name = 'ExitPass.AssistedPaymentTerminal'
  AND service.effective_from <= now()
  AND (service.effective_to IS NULL OR service.effective_to > now())
  AND service.revoked_at IS NULL
  AND (service.credential_expires_at IS NULL OR service.credential_expires_at > now())
  AND assignment.site_id = :'site_id'::uuid
  AND assignment.assignment_type = 'PAYMENT_DEVICE'
  AND assignment.assignment_status = 'ACTIVE'
  AND assignment.assigned_at <= now()
  AND assignment.unassigned_at IS NULL
  AND btrim(device.terminal_identifier) = :'terminal_identifier';
'@
$aptRegistrationOutput = @($aptRegistrationSql | & docker.exe exec -i $databaseContainer psql `
    -X -Atq -v ON_ERROR_STOP=1 -U $databaseUser -d $databaseName `
    -v "site_id=$($siteId.ToString('D'))" `
    -v "terminal_identifier=$($aptTerminalMappings[0].TerminalIdentifier)")
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to validate the configured APT terminal against Device Administration.'
}
$aptRegistrationRows = @($aptRegistrationOutput | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($aptRegistrationRows.Count -ne 1) {
    throw 'The configured APT terminal does not resolve to exactly one active Device Administration registration for the configured Site.'
}
$aptRegistrationFields = $aptRegistrationRows[0].Split('|')
if ($aptRegistrationFields.Count -ne 2 -or
    $aptRegistrationFields[1] -cne $aptTerminalMappings[0].TerminalIdentifier) {
    throw 'The configured APT terminal does not exactly match its canonical Device Administration identifier.'
}

$availableNetwork = (& docker.exe network ls --filter "name=^$networkName$" --format '{{.Name}}')
if ($LASTEXITCODE -ne 0 -or $availableNetwork -ne $networkName) {
    throw "Docker network '$networkName' is unavailable. Start the persistent IST resources first."
}
$existingContainer = (& docker.exe ps -a --filter "name=^/$containerName$" --format '{{.Names}}')
if (-not [string]::IsNullOrWhiteSpace($existingContainer)) {
    throw "Container '$containerName' already exists. Stop its launcher or remove the stale local-runtime container before retrying."
}

$sourceSha = (& git.exe -C $repoRoot rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceSha)) {
    throw 'Unable to determine the Central PMS source revision.'
}
$imageName = "exitpass-central-pms-local:$sourceSha"
$mtls = & $mtlsProvisionerPath -PrivateRoot $privateRoot -PassThru
$evidenceRuntime = & $evidenceServicesLauncherPath -PassThru
if ($null -eq $evidenceRuntime -or
    -not (Test-Path -LiteralPath $evidenceRuntime.CentralPmsEnvironmentFile -PathType Leaf) -or
    -not (Test-Path -LiteralPath $evidenceRuntime.MinioRootCertificatePath -PathType Leaf)) {
    throw 'Persistent PITX statutory evidence services did not provide the required private Central PMS configuration.'
}
if ($SkipEvidenceGovernanceInitialization) {
    Write-Host 'Statutory evidence governance initialization: SKIPPED by explicit runtime option'
}
else {
    & $evidenceGovernanceInitializerPath `
        -DatabaseContainer $databaseContainer `
        -DatabaseName $databaseName `
        -DatabaseUser $databaseUser
}

try {
    Invoke-CheckedCommand docker.exe @(
        'build', '--file', $dockerfilePath, '--tag', $imageName, $repoRoot
    ) 'Central PMS image build failed.'

    $containerId = (& docker.exe run --rm --detach `
        --name $containerName `
        --network $networkName `
        --network-alias 'exitpass-central-pms-pitx-local' `
        --env-file $environmentFile `
        --env-file $evidenceRuntime.CentralPmsEnvironmentFile `
        --env 'ASPNETCORE_URLS=http://+:8080;https://+:8443' `
        --env 'ASPNETCORE_Kestrel__Certificates__Default__Path=/run/exitpass/statutory-mtls/central-pms-server.pfx' `
        --env "ASPNETCORE_Kestrel__Certificates__Default__Password=$($mtls.ServerPassword)" `
        --env 'ASPNETCORE_Kestrel__EndpointDefaults__ClientCertificateMode=AllowCertificate' `
        --env 'InternalSecurity__Mtls__Enabled=true' `
        --env 'InternalSecurity__Mtls__RequireClientCertificate=false' `
        --env "InternalSecurity__Mtls__TrustedClientThumbprints__0=$($mtls.ClientThumbprint)" `
        --env "InternalSecurity__Mtls__ServicePrincipalCredentials__0__CertificateThumbprint=$($mtls.ClientThumbprint)" `
        --env "InternalSecurity__Mtls__ServicePrincipalCredentials__0__CredentialReference=$($mtls.CredentialReference)" `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__Audience=CENTRAL_PMS' `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__SourceChannel=WEBPAY' `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__Permissions__0=statutory-discounts.decision.read' `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__Permissions__1=statutory-discounts.decision.submit.webpay' `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__Permissions__2=statutory-discounts.pending-lifecycle.rediscover.webpay' `
        --env 'InternalSecurity__Mtls__ServicePrincipalCredentials__0__Permissions__3=statutory-discounts.evidence.capture.webpay' `
        --env 'HumanAuthentication__AllowedWebOrigins__0=http://127.0.0.1:5175' `
        --env 'HumanAuthentication__AllowedWebOrigins__1=http://127.0.0.1:5178' `
        --env "HumanAuthentication__AllowedWebOrigins__2=$normalizedOperatorConsolePublicOrigin" `
        --env 'HumanAuthentication__WebIdleMinutes=30' `
        --env 'HumanAuthentication__WebAbsoluteHours=8' `
        --publish '127.0.0.1:56065:8080' `
        --publish '127.0.0.1:56064:8443' `
        --mount "type=bind,source=$($mtls.ServerCertificatePath),target=/run/exitpass/statutory-mtls/central-pms-server.pfx,readonly" `
        --mount "type=bind,source=$($mtls.RootCertificatePemPath),target=/usr/local/share/ca-certificates/exitpass-local-statutory-root-ca.crt,readonly" `
        --mount "type=bind,source=$($evidenceRuntime.MinioRootCertificatePath),target=/usr/local/share/ca-certificates/exitpass-local-statutory-evidence-root-ca.crt,readonly" `
        --mount "type=bind,source=$posApiKeyFile,target=/run/exitpass/pos-api-key,readonly" `
        --mount "type=bind,source=$annexE1GovernedCorrectionApiKeyFile,target=$annexE1GovernedCorrectionApiKeyContainerPath,readonly" `
        --mount "type=bind,source=$adapterApiKeyFile,target=/run/exitpass/site-adapter-secrets/pitx-level-3/central-pms-api-key,readonly" `
        --mount "type=bind,source=$dataProtectionKeyDirectory,target=/root/.aspnet/DataProtection-Keys" `
        --label 'com.exitpass.local-runtime=persistent-pitx-central-pms' `
        --entrypoint '/bin/sh' `
        $imageName `
        '-c' 'update-ca-certificates >/dev/null && exec dotnet ExitPass.CentralPms.Api.dll').Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw 'Central PMS container failed to start. Confirm ports 56064 and 56065 are available.'
    }
    $containerStarted = $true

    Wait-ForHealth

    Write-Host 'Central PMS is ready with the persistent PITX configuration.'
    Write-Host "HTTPS: $httpsUrl"
    Write-Host "HTTP:  $httpUrl"
    Write-Host "Database: $databaseContainer/$databaseName (volume $databaseVolume)"
    Write-Host "Data Protection keys: $dataProtectionKeyDirectory"
    Write-Host "Statutory evidence storage: $($evidenceRuntime.MinioContainerName)/$($evidenceRuntime.BucketName) (private)"
    Write-Host "Statutory evidence scanner: $($evidenceRuntime.ClamAvContainerName) (CLAMAV_COMPATIBLE)"
    Write-Host 'Projection scheduler: enabled and required for this environment'
    Write-Host 'WebPay statutory service principal: HTTPS/mTLS enabled'

    if (-not $SmokeTest) {
        Write-Host 'Press Ctrl+C to stop the local Central PMS.'
        & docker.exe logs --follow $containerName
    }
}
finally {
    if ($containerStarted) {
        & docker.exe stop --time 10 $containerName 2>$null | Out-Null
    }
}
