[CmdletBinding()]
param(
    [switch]$PreflightOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$uiRoot = Join-Path $repoRoot "src\Services\OperatorConsoleUi"
$defaultApiProxyTarget = "https://localhost:56064"
$apiProxyTarget = if ([string]::IsNullOrWhiteSpace($env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET)) {
    $defaultApiProxyTarget
} else {
    $env:VITE_OPERATOR_CONSOLE_API_PROXY_TARGET.TrimEnd("/")
}
$localHttpsProbe = @'
const https = require(\"https\");
const request = https.get(process.argv[1], { rejectUnauthorized: false }, response => {
  response.resume();
  response.on(\"end\", () => process.exit(response.statusCode === 200 ? 0 : 1));
});
request.setTimeout(5000, () => request.destroy(new Error(\"timeout\")));
request.on(\"error\", error => { console.error(error.message); process.exit(1); });
'@

try {
    if ($apiProxyTarget -eq $defaultApiProxyTarget) {
        & node.exe -e $localHttpsProbe "$apiProxyTarget/health/ready"
        if ($LASTEXITCODE -ne 0) {
            throw "HTTPS probe exit code $LASTEXITCODE"
        }
    } else {
        $response = Invoke-WebRequest -Uri "$apiProxyTarget/health/ready" -UseBasicParsing -TimeoutSec 5
        if ($response.StatusCode -ne 200) {
            throw "HTTP $($response.StatusCode)"
        }
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
