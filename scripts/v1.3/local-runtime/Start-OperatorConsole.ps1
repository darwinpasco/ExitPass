[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$uiRoot = Join-Path $repoRoot "src\Services\OperatorConsoleUi"
$apiProxyTarget = if ([string]::IsNullOrWhiteSpace($env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET)) {
    "http://127.0.0.1:56065"
} else {
    $env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET.TrimEnd("/")
}

try {
    $response = Invoke-WebRequest -Uri "$apiProxyTarget/health/ready" -UseBasicParsing -TimeoutSec 5
    if ($response.StatusCode -ne 200) {
        throw "HTTP $($response.StatusCode)"
    }
} catch {
    throw "Central PMS is not running at $apiProxyTarget. Start it with: powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\Start-CentralPms.ps1"
}

Write-Host "Central PMS readiness: PASS ($apiProxyTarget)"
if ($PreflightOnly) {
    exit 0
}

Push-Location $uiRoot
try {
    & npm.cmd run dev -- --host 127.0.0.1 --port 5175 --strictPort
    exit $LASTEXITCODE
} finally {
    Pop-Location
}
