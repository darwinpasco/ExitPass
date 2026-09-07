[CmdletBinding()]
param([switch] $SmokeTest)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'DeveloperRuntime.ps1')
$developer = Initialize-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot
$state = $developer.State
$containerName = 'exitpass-payment-orchestrator-developer-local'
$centralContainer = 'exitpass-central-pms-developer-local'
$httpUrl = 'http://127.0.0.1:56063'
$httpsUrl = 'https://localhost:56062'
$privateRoot = if ([string]::IsNullOrWhiteSpace($env:EXITPASS_PERSISTENT_IST_ROOT)) { 'D:\SourceCodes\ExitPass.local\persistent-ist' } else { $env:EXITPASS_PERSISTENT_IST_ROOT }
$providerEnvironment = Join-Path $privateRoot 'runtime\restart-41-business\payment.env'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-payment-orchestrator-developer-local-$PID"
$certificatePath = Join-Path $temporaryRoot 'payment-orchestrator.pfx'
$dockerfile = Join-Path $repoRoot 'src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Dockerfile'
$runtimeDockerfile = Join-Path $repoRoot 'infra\docker\Dockerfile.aspnet-base'
$started = $false

function Invoke-Checked([string] $FilePath, [string[]] $Arguments, [string] $Failure) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}
function Get-Setting([string] $Name) {
    $line = Get-Content -LiteralPath $providerEnvironment | Where-Object { $_.StartsWith("$Name=", [StringComparison]::Ordinal) } | Select-Object -Last 1
    if ($null -eq $line) { return $null }
    return $line.Substring($Name.Length + 1)
}
function Wait-Ready {
    for ($attempt = 1; $attempt -le 180; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "$httpUrl/health/ready" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        } catch { }
        if ($attempt -eq 180) { throw "Developer Payment Orchestrator did not become ready. Inspect: docker logs $containerName" }
        Start-Sleep -Seconds 1
    }
}

if (-not (Test-Path -LiteralPath $providerEnvironment -PathType Leaf)) { throw 'Approved private PayMongo TEST configuration is unavailable.' }
if ((Get-Setting 'Payments__Providers__PayMongo__IsLiveMode') -ne 'false') { throw 'DEVELOPER refuses PayMongo LIVE configuration.' }
foreach ($name in @('Payments__Providers__PayMongo__SecretKey','Payments__Providers__PayMongo__PublicKey','Payments__Providers__PayMongo__BaseUrl','Payments__Providers__PayMongo__WebhookSecretKey')) {
    if ([string]::IsNullOrWhiteSpace((Get-Setting $name))) { throw "Approved PayMongo TEST configuration is missing '$name'." }
}
$central = (& docker.exe ps --filter "name=^/$centralContainer$" --format '{{.Names}}')
if ($central -ne $centralContainer) { throw 'Developer Central PMS is not running. Start it with: .\scripts\v1.3\local-runtime\Start-CentralPms.ps1' }
$existing = (& docker.exe ps -a --filter "name=^/$containerName$" --format '{{.Names}}')
if (-not [string]::IsNullOrWhiteSpace($existing)) { throw "Container '$containerName' already exists." }

[void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
$certificatePassword = New-ExitPassDeveloperSecret
$sourceSha = (& git.exe -C $repoRoot rev-parse --short=12 HEAD).Trim()
$image = "exitpass-payment-orchestrator-developer:$sourceSha"
try {
    if ([string]::IsNullOrWhiteSpace((& docker.exe images --quiet 'exitpass/aspnet:8.0-krb5'))) {
        Invoke-Checked docker.exe @('build','--file',$runtimeDockerfile,'--tag','exitpass/aspnet:8.0-krb5',$repoRoot) 'Payment Orchestrator runtime base image build failed.'
    }
    Invoke-Checked dotnet.exe @('dev-certs','https','--export-path',$certificatePath,'--password',$certificatePassword) 'Unable to export the local HTTPS certificate.'
    Invoke-Checked docker.exe @('build','--file',$dockerfile,'--tag',$image,$repoRoot) 'Developer Payment Orchestrator image build failed.'
    $id = (& docker.exe run --rm --detach --name $containerName --network 'exitpass-dev-synthetic' `
        --env-file $providerEnvironment `
        --env "ConnectionStrings__MainDatabase=$($developer.DatabaseConnectionString)" `
        --env 'Integrations__CentralPms__BaseUrl=http://exitpass-central-pms-developer-local:8080' `
        --env 'Payments__Providers__PayMongo__IsLiveMode=false' `
        --env 'Payments__Providers__PayMongo__AllowedPaymentMethodTypes__0=qrph' `
        --env 'Payments__Providers__PayMongo__AllowedPaymentMethodTypes__1=gcash' `
        --env 'Payments__Providers__PayMongo__AllowedPaymentMethodTypes__2=paymaya' `
        --env 'Payments__Providers__PayMongo__AllowedPaymentMethodTypes__3=card' `
        --env 'WEBPAY_PUBLIC_BASE_URL=http://localhost:5174' `
        --env 'EXITPASS_RUNTIME_PROFILE=DEVELOPER' `
        --env 'EXITPASS_RUNTIME_PROFILE_LABEL=DEVELOPER - SIMULATED HIKCENTRAL' `
        --env 'ASPNETCORE_URLS=http://+:8080;https://+:8443' `
        --env 'ASPNETCORE_Kestrel__Certificates__Default__Path=/https/payment-orchestrator.pfx' `
        --env "ASPNETCORE_Kestrel__Certificates__Default__Password=$certificatePassword" `
        --publish '127.0.0.1:56063:8080' --publish '127.0.0.1:56062:8443' `
        --mount "type=bind,source=$certificatePath,target=/https/payment-orchestrator.pfx,readonly" `
        --label 'com.exitpass.runtime-profile=DEVELOPER' `
        --label 'com.exitpass.resource-owner=developer-runtime' $image).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($id)) { throw 'Developer Payment Orchestrator container failed to start.' }
    $started = $true
    Wait-Ready
    Write-Host 'Payment Orchestrator Developer runtime is ready.'
    Write-Host "HTTPS: $httpsUrl"
    Write-Host "HTTP:  $httpUrl"
    Write-Host 'PayMongo: approved private TEST configuration (QRPH, GCASH, MAYA, CARD)'
    if (-not $SmokeTest) { & docker.exe logs --follow $containerName }
}
finally {
    if ($started) { & docker.exe stop --time 10 $containerName 2>$null | Out-Null }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
