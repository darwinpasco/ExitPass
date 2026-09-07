[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
. (Join-Path $PSScriptRoot 'RuntimeProfile.ps1')
$runtimeProfile = Initialize-ExitPassLocalRuntimeProfile -RepositoryRoot $repoRoot -Component 'WebPay'
$uiRoot = Join-Path $repoRoot "src\Services\WebPayUi"
$apiProxyTarget = if ([string]::IsNullOrWhiteSpace($env:VITE_WEBPAY_API_PROXY_TARGET)) {
    "http://127.0.0.1:56063"
} else {
    $env:VITE_WEBPAY_API_PROXY_TARGET.TrimEnd("/")
}

try {
    $response = Invoke-WebRequest -Uri "$apiProxyTarget/health/ready" -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ne 200) {
        throw "HTTP $($response.StatusCode)"
    }
} catch {
    throw "Payment Orchestrator is not running at $apiProxyTarget. Start it with: powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-PaymentOrchestrator.ps1"
}

if ($runtimeProfile.Name -eq 'DEVELOPER') {
    $env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = '12000000-0000-0000-0000-000000000301'
    $env:VITE_WEBPAY_DEFAULT_SITE_ID = '12000000-0000-0000-0000-000000000302'
    $env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = 'de100000-0000-0000-0000-000000000003'
}
else {
    $env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "a6dbadf6-68b5-5bed-a7e0-a75faee70841"
    $env:VITE_WEBPAY_DEFAULT_SITE_ID = "2d1dcdf8-f563-537c-8542-0bde7cc9da97"
    $env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "HIKCENTRAL"
}
$env:VITE_EXITPASS_RUNTIME_PROFILE = $runtimeProfile.Name
$env:VITE_EXITPASS_RUNTIME_PROFILE_LABEL = $runtimeProfile.DisplayLabel
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue

Write-Host "Payment Orchestrator readiness: PASS ($apiProxyTarget)"
if ($runtimeProfile.Name -eq 'DEVELOPER') {
    Write-Host 'WebPay Developer context: ExitPass Developer Parking / HIKCENTRAL_DEV'
}
else {
    Write-Host "WebPay PITX context: PITX Level 3 / HIKCENTRAL"
}
if ($PreflightOnly) {
    exit 0
}

Push-Location $uiRoot
try {
    & npm.cmd run dev -- --host localhost --port 5174 --strictPort
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
