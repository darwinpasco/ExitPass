[CmdletBinding()]
param(
    [switch] $SmokeTest
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
$adapterApiKeyFile = Join-Path $privateRoot 'site-adapters\pitx-level-3\central-pms-api-key'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-central-pms-pitx-local-$PID"
$certificatePath = Join-Path $temporaryRoot 'exitpass-central-pms-local.pfx'
$containerStarted = $false

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

function New-RandomSecret {
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
if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
    throw 'The .NET 8 SDK is required to export the local HTTPS certificate.'
}
if (-not (Test-Path -LiteralPath $dockerfilePath)) {
    throw 'Run this launcher from a complete ExitPass source checkout.'
}
foreach ($requiredFile in @($environmentFile, $posApiKeyFile, $adapterApiKeyFile)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required persistent PITX private configuration was not found at $requiredFile."
    }
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
        'CentralPms__VendorSessionProjections__SchedulerEnabled',
        'CentralPms__VendorSessionProjections__RequiredForEnvironment',
        'CentralPms__VendorSessionProjections__SchedulerScanIntervalSeconds'
    )) {
    Assert-PrivateEnvironmentValue $requiredName
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

[void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
$certificatePassword = New-RandomSecret

try {
    Invoke-CheckedCommand dotnet.exe @(
        'dev-certs', 'https', '--export-path', $certificatePath, '--password', $certificatePassword
    ) 'Unable to export the local ASP.NET Core HTTPS development certificate.'

    Invoke-CheckedCommand docker.exe @(
        'build', '--file', $dockerfilePath, '--tag', $imageName, $repoRoot
    ) 'Central PMS image build failed.'

    $containerId = (& docker.exe run --rm --detach `
        --name $containerName `
        --network $networkName `
        --network-alias 'exitpass-central-pms-pitx-local' `
        --env-file $environmentFile `
        --env 'ASPNETCORE_URLS=http://+:8080;https://+:8443' `
        --env 'ASPNETCORE_Kestrel__Certificates__Default__Path=/https/exitpass-central-pms-local.pfx' `
        --env "ASPNETCORE_Kestrel__Certificates__Default__Password=$certificatePassword" `
        --env 'HumanAuthentication__AllowedWebOrigins__0=http://127.0.0.1:5175' `
        --env 'HumanAuthentication__AllowedWebOrigins__1=http://127.0.0.1:5178' `
        --publish '127.0.0.1:56065:8080' `
        --publish '127.0.0.1:56064:8443' `
        --mount "type=bind,source=$certificatePath,target=/https/exitpass-central-pms-local.pfx,readonly" `
        --mount "type=bind,source=$posApiKeyFile,target=/run/exitpass/pos-api-key,readonly" `
        --mount "type=bind,source=$adapterApiKeyFile,target=/run/exitpass/site-adapter-secrets/pitx-level-3/central-pms-api-key,readonly" `
        --label 'com.exitpass.local-runtime=persistent-pitx-central-pms' `
        $imageName).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw 'Central PMS container failed to start. Confirm ports 56064 and 56065 are available.'
    }
    $containerStarted = $true

    Wait-ForHealth

    Write-Host 'Central PMS is ready with the persistent PITX configuration.'
    Write-Host "HTTPS: $httpsUrl"
    Write-Host "HTTP:  $httpUrl"
    Write-Host "Database: $databaseContainer/$databaseName (volume $databaseVolume)"
    Write-Host 'Projection scheduler: enabled and required for this environment'

    if (-not $SmokeTest) {
        Write-Host 'Press Ctrl+C to stop the local Central PMS.'
        & docker.exe logs --follow $containerName
    }
}
finally {
    if ($containerStarted) {
        & docker.exe stop --time 10 $containerName 2>$null | Out-Null
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = (Resolve-Path -LiteralPath $temporaryRoot).Path
        $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedTemporaryRoot.StartsWith(
                "$systemTemporaryRoot\exitpass-central-pms-pitx-local-",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected temporary path: $resolvedTemporaryRoot"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
