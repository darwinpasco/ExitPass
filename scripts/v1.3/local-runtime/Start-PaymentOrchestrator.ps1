[CmdletBinding()]
param(
    [switch] $SmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'RuntimeProfile.ps1')
$runtimeProfile = Initialize-ExitPassLocalRuntimeProfile -RepositoryRoot $repoRoot -Component 'Payment Orchestrator'
if ($runtimeProfile.Name -eq 'DEVELOPER') {
    & (Join-Path $PSScriptRoot 'Start-PaymentOrchestratorDeveloper.ps1') -SmokeTest:$SmokeTest
    exit $LASTEXITCODE
}

$databaseContainer = 'exitpass-ist-persistent-db'
$databaseName = 'exitpass_ist'
$databaseUser = 'exitpass_ist'
$databaseVolume = 'exitpass-ist-persistent-data'
$networkName = 'exitpass-ist-persistent'
$centralPmsContainer = 'exitpass-central-pms-pitx-local'
$centralPmsInternalUrl = 'http://exitpass-central-pms-pitx-local:8080'
$centralPmsHostUrl = 'http://127.0.0.1:56065'
$containerName = 'exitpass-payment-orchestrator-pitx-local'
$httpUrl = 'http://127.0.0.1:56063'
$httpsUrl = 'https://localhost:56062'

$dockerfilePath = Join-Path $repoRoot 'src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Dockerfile'
$runtimeDockerfilePath = Join-Path $repoRoot 'infra\docker\Dockerfile.aspnet-base'
$privateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_PERSISTENT_IST_ROOT)) {
    'D:\SourceCodes\ExitPass.local\persistent-ist'
}
else {
    $env:EXITPASS_PERSISTENT_IST_ROOT
}
$environmentFile = Join-Path $privateRoot 'runtime\restart-41-business\payment.env'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-payment-orchestrator-pitx-local-$PID"
$certificatePath = Join-Path $temporaryRoot 'exitpass-payment-orchestrator-local.pfx'
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
        throw "The private Payment Orchestrator environment is missing required setting '$Name'."
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
    param([int] $Attempts = 90)

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
                throw "Payment Orchestrator did not become ready at $uri. Inspect with: docker logs $containerName"
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
foreach ($requiredFile in @($dockerfilePath, $runtimeDockerfilePath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw 'Run this launcher from a complete ExitPass source checkout.'
    }
}
if (-not (Test-Path -LiteralPath $environmentFile -PathType Leaf)) {
    throw "Persistent PITX Payment Orchestrator configuration was not found at $environmentFile."
}

$databaseConnection = Get-PrivateEnvironmentValue $environmentFile 'ConnectionStrings__MainDatabase'
if ([string]::IsNullOrWhiteSpace($databaseConnection) -or
    $databaseConnection -notmatch '(?i)(^|;)\s*Host=exitpass-ist-persistent-db\s*(;|$)' -or
    $databaseConnection -notmatch '(?i)(^|;)\s*Database=exitpass_ist\s*(;|$)') {
    throw 'The private Payment Orchestrator environment does not target the authoritative persistent PITX IST database.'
}

foreach ($requiredName in @(
        'Integrations__CentralPms__BaseUrl',
        'Payments__Providers__PayMongo__SecretKey',
        'Payments__Providers__PayMongo__PublicKey',
        'Payments__Providers__PayMongo__BaseUrl',
        'Payments__Providers__PayMongo__WebhookSecretKey',
        'Payments__Providers__PayMongo__IsLiveMode',
        'Payments__Providers__PayMongo__AllowedPaymentMethodTypes__0',
        'WEBPAY_PUBLIC_BASE_URL',
        'WEBPAY_PAYMENT_SUCCESS_PATH',
        'WEBPAY_PAYMENT_CANCEL_PATH'
    )) {
    Assert-PrivateEnvironmentValue $requiredName
}
if ((Get-PrivateEnvironmentValue $environmentFile 'Payments__Providers__PayMongo__IsLiveMode') -ne 'false') {
    throw 'The private Payment Orchestrator environment is not configured for approved PayMongo test mode.'
}

$runningDatabase = (& docker.exe ps --filter "name=^/$databaseContainer$" --format '{{.Names}}')
if ($LASTEXITCODE -ne 0 -or $runningDatabase -ne $databaseContainer) {
    throw "The persistent ExitPass database is not running. Start container '$databaseContainer' before launching Payment Orchestrator."
}
$databaseInspectJson = (& docker.exe inspect $databaseContainer)
$databaseInspect = $databaseInspectJson | ConvertFrom-Json
if (-not $databaseInspect[0].State.Running) {
    throw "The persistent ExitPass database is not running. Start container '$databaseContainer' before launching Payment Orchestrator."
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
$runningCentralPms = (& docker.exe ps --filter "name=^/$centralPmsContainer$" --format '{{.Names}}')
if ($LASTEXITCODE -ne 0 -or $runningCentralPms -ne $centralPmsContainer) {
    throw "Central PMS is not running. Start it with: .\scripts\v1.3\local-runtime\Start-CentralPms.ps1"
}
$centralInspectJson = (& docker.exe inspect $centralPmsContainer)
$centralInspect = $centralInspectJson | ConvertFrom-Json
if (-not $centralInspect[0].State.Running) {
    throw "Central PMS is not running. Start it with: .\scripts\v1.3\local-runtime\Start-CentralPms.ps1"
}
try {
    $centralPmsReadiness = Invoke-WebRequest -UseBasicParsing -Uri "$centralPmsHostUrl/health/ready" -TimeoutSec 5
    if ($centralPmsReadiness.StatusCode -ne 200) {
        throw "HTTP $($centralPmsReadiness.StatusCode)"
    }
}
catch {
    throw "Central PMS is not ready. Start it with: .\scripts\v1.3\local-runtime\Start-CentralPms.ps1"
}
$existingContainer = (& docker.exe ps -a --filter "name=^/$containerName$" --format '{{.Names}}')
if (-not [string]::IsNullOrWhiteSpace($existingContainer)) {
    throw "Container '$containerName' already exists. Stop its launcher or remove the stale local-runtime container before retrying."
}

$runtimeImage = 'exitpass/aspnet:8.0-krb5'
$runtimeImageId = (& docker.exe images --quiet $runtimeImage)
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to inspect the local Payment Orchestrator runtime base image.'
}
if ([string]::IsNullOrWhiteSpace($runtimeImageId)) {
    Invoke-CheckedCommand docker.exe @(
        'build', '--file', $runtimeDockerfilePath, '--tag', $runtimeImage, $repoRoot
    ) 'Payment Orchestrator ASP.NET runtime base image build failed.'
}

$sourceSha = (& git.exe -C $repoRoot rev-parse --short=12 HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceSha)) {
    throw 'Unable to determine the Payment Orchestrator source revision.'
}
$imageName = "exitpass-payment-orchestrator-local:$sourceSha"

[void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
$certificatePassword = New-RandomSecret

try {
    Invoke-CheckedCommand dotnet.exe @(
        'dev-certs', 'https', '--export-path', $certificatePath, '--password', $certificatePassword
    ) 'Unable to export the local ASP.NET Core HTTPS development certificate.'

    Invoke-CheckedCommand docker.exe @(
        'build', '--file', $dockerfilePath, '--tag', $imageName, $repoRoot
    ) 'Payment Orchestrator image build failed.'

    $containerId = (& docker.exe run --rm --detach `
        --name $containerName `
        --network $networkName `
        --env-file $environmentFile `
        --env "EXITPASS_RUNTIME_PROFILE=$($runtimeProfile.Name)" `
        --env "EXITPASS_RUNTIME_PROFILE_LABEL=$($runtimeProfile.DisplayLabel)" `
        --env "Integrations__CentralPms__BaseUrl=$centralPmsInternalUrl" `
        --env 'ASPNETCORE_URLS=http://+:8080;https://+:8443' `
        --env 'ASPNETCORE_Kestrel__Certificates__Default__Path=/https/exitpass-payment-orchestrator-local.pfx' `
        --env "ASPNETCORE_Kestrel__Certificates__Default__Password=$certificatePassword" `
        --publish '127.0.0.1:56063:8080' `
        --publish '127.0.0.1:56062:8443' `
        --mount "type=bind,source=$certificatePath,target=/https/exitpass-payment-orchestrator-local.pfx,readonly" `
        --label 'com.exitpass.local-runtime=persistent-pitx-payment-orchestrator' `
        $imageName).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw 'Payment Orchestrator container failed to start. Confirm ports 56062 and 56063 are available.'
    }
    $containerStarted = $true

    Wait-ForHealth

    Write-Host 'Payment Orchestrator is ready with the persistent PITX configuration.'
    Write-Host "HTTPS: $httpsUrl"
    Write-Host "HTTP:  $httpUrl"
    Write-Host "Central PMS: $centralPmsInternalUrl"
    Write-Host 'PayMongo: approved private test-mode configuration loaded'

    if (-not $SmokeTest) {
        Write-Host 'Press Ctrl+C to stop the local Payment Orchestrator.'
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
                "$systemTemporaryRoot\exitpass-payment-orchestrator-pitx-local-",
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected temporary path: $resolvedTemporaryRoot"
        }
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
