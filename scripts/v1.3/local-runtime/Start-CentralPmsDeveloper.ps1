[CmdletBinding()]
param([switch] $SmokeTest)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'DeveloperRuntime.ps1')
$developer = Initialize-ExitPassDeveloperRuntime -RepositoryRoot $repoRoot
$state = $developer.State
$containerName = 'exitpass-central-pms-developer-local'
$httpUrl = 'http://127.0.0.1:56065'
$httpsUrl = 'https://localhost:56064'
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-central-pms-developer-local-$PID"
$certificatePath = Join-Path $temporaryRoot 'central-pms.pfx'
$environmentFile = Join-Path $state.Root 'central-pms.env'
$dockerfile = Join-Path $repoRoot 'src\Services\CentralPms\src\ExitPass.CentralPms.Api\Dockerfile'
$started = $false

function Invoke-Checked([string] $FilePath, [string[]] $Arguments, [string] $Failure) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}
function Wait-Ready {
    for ($attempt = 1; $attempt -le 180; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "$httpUrl/health/ready" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { return }
        } catch { }
        if ($attempt -eq 180) { throw "Developer Central PMS did not become ready. Inspect: docker logs $containerName" }
        Start-Sleep -Seconds 1
    }
}

$existing = (& docker.exe ps -a --filter "name=^/$containerName$" --format '{{.Names}}')
if (-not [string]::IsNullOrWhiteSpace($existing)) { throw "Container '$containerName' already exists." }
[void](New-Item -ItemType Directory -Path $temporaryRoot -Force)
$certificatePassword = New-ExitPassDeveloperSecret
$sourceSha = (& git.exe -C $repoRoot rev-parse --short=12 HEAD).Trim()
$image = "exitpass-central-pms-developer:$sourceSha"

Set-ExitPassDeveloperPrivateFile $environmentFile (@(
    'ASPNETCORE_ENVIRONMENT=Development'
    'ConnectionStrings__MainDatabase=' + $developer.DatabaseConnectionString
    'InternalSecurity__Mtls__Enabled=false'
    'HumanAuthentication__TotpProtectionKeyBase64=' + $developer.TotpKey
    'HumanAuthentication__TotpProtectionKeyReference=developer-runtime'
    'HumanAuthentication__TotpProtectionKeyVersion=1'
    'HumanAuthentication__AllowedWebOrigins__0=http://127.0.0.1:5175'
    'HumanAuthentication__AllowedWebOrigins__1=http://127.0.0.1:5178'
    'CentralPms__VendorPms__Provider=SITE_ADAPTER'
    'CentralPms__VendorPms__Environment=DEVELOPER'
    'CentralPms__VendorPms__CentralPmsServiceIdentityId=12000000-0000-0000-0000-000000000002'
    'CentralPms__VendorPms__AdapterSecretMountRoot=/run/exitpass/site-adapter-secrets'
    'CentralPms__VendorPms__AllowTaskOwnedHttp=true'
    'CentralPms__VendorSessionProjections__SchedulerEnabled=true'
    'CentralPms__VendorSessionProjections__RequiredForEnvironment=true'
    'CentralPms__VendorSessionProjections__ActivationMode=MANAGED_DEPLOYMENT'
    'CentralPms__VendorSessionProjections__ActivationEnvironment=Development'
    'CentralPms__VendorSessionProjections__ManagedDeploymentApproved=true'
    'CentralPms__VendorSessionProjections__AllowNonLoopbackDatabase=true'
    'CentralPms__VendorSessionProjections__AllowProductionEndpoint=false'
    'CentralPms__VendorSessionProjections__ExpectedDatabaseName=exitpass_dev'
    'CentralPms__VendorSessionProjections__ExpectedTargetSiteId=12000000-0000-0000-0000-000000000302'
    'CentralPms__VendorSessionProjections__ExpectedTargetSiteGroupId=12000000-0000-0000-0000-000000000301'
    'CentralPms__VendorSessionProjections__ExpectedTargetVendorSystemId=de100000-0000-0000-0000-000000000003'
    'CentralPms__VendorSessionProjections__ExpectedTargetParkingLotIndexCode=DEV-LOT-1'
    'CentralPms__VendorSessionProjections__DefaultPollIntervalSeconds=30'
    'CentralPms__VendorSessionProjections__NormalFreshnessTargetSeconds=60'
    'CentralPms__VendorSessionProjections__MaxProjectionAgeMinutes=1'
    'CentralPms__VendorSessionProjections__StartupDelaySeconds=1'
    'CentralPms__VendorSessionProjections__SchedulerScanIntervalSeconds=15'
    'CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled=false'
    'Messaging__RabbitMq__ReconciliationOutbox__Enabled=false'
    'FiscalIssuance__PosServerIntegration__EnablePosServerFiscalIssuanceLiveCall=false'
    'FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromPaymentFlow=false'
    'FiscalIssuance__PosServerIntegration__EnableLiveFiscalIssuanceFromExitFlow=false'
    'VENDOR_PMS_CONFIRM_PAYMENT_ENABLED=true'
    'EXITPASS_RUNTIME_PROFILE=DEVELOPER'
    'EXITPASS_RUNTIME_PROFILE_LABEL=DEVELOPER - SIMULATED HIKCENTRAL'
) -join "`n")

try {
    Invoke-Checked dotnet.exe @('dev-certs','https','--export-path',$certificatePath,'--password',$certificatePassword) 'Unable to export the local HTTPS certificate.'
    Invoke-Checked docker.exe @('build','--file',$dockerfile,'--tag',$image,$repoRoot) 'Developer Central PMS image build failed.'
    $id = (& docker.exe run --rm --detach --name $containerName --network 'exitpass-dev-synthetic' `
        --network-alias $containerName --env-file $environmentFile `
        --env 'ASPNETCORE_URLS=http://+:8080;https://+:8443' `
        --env 'ASPNETCORE_Kestrel__Certificates__Default__Path=/https/central-pms.pfx' `
        --env "ASPNETCORE_Kestrel__Certificates__Default__Password=$certificatePassword" `
        --publish '127.0.0.1:56065:8080' --publish '127.0.0.1:56064:8443' `
        --mount "type=bind,source=$certificatePath,target=/https/central-pms.pfx,readonly" `
        --mount "type=bind,source=$($state.CentralAdapterKeyFile),target=/run/exitpass/site-adapter-secrets/developer-site-adapter.key,readonly" `
        --label 'com.exitpass.runtime-profile=DEVELOPER' `
        --label 'com.exitpass.resource-owner=developer-runtime' $image).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($id)) { throw 'Developer Central PMS container failed to start.' }
    $started = $true
    Wait-Ready
    Write-Host 'Central PMS Developer runtime is ready.'
    Write-Host "HTTPS: $httpsUrl"
    Write-Host "HTTP:  $httpUrl"
    Write-Host 'Vendor boundary: Site Adapter -> WireMock HikCentral'
    if (-not $SmokeTest) { & docker.exe logs --follow $containerName }
}
finally {
    if ($started) { & docker.exe stop --time 10 $containerName 2>$null | Out-Null }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
