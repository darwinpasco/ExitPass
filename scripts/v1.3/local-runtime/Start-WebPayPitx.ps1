[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
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

$env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "a6dbadf6-68b5-5bed-a7e0-a75faee70841"
$env:VITE_WEBPAY_DEFAULT_SITE_ID = "2d1dcdf8-f563-537c-8542-0bde7cc9da97"
$env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "HIKCENTRAL"
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue

Write-Host "Payment Orchestrator readiness: PASS ($apiProxyTarget)"
Write-Host "WebPay PITX context: PITX Level 3 / HIKCENTRAL"
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
