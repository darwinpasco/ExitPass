param(
    [switch] $Headed
)

$ErrorActionPreference = "Stop"

$script:serverProcess = $null
$port = if ($env:WEBPAY_BROWSER_SMOKE_PORT) { [int] $env:WEBPAY_BROWSER_SMOKE_PORT } else { 5196 }
$healthUrl = "http://127.0.0.1:$port/__fixture/health"

function Stop-SmokeServer {
    if ($script:serverProcess -and -not $script:serverProcess.HasExited) {
        Stop-Process -Id $script:serverProcess.Id -Force -ErrorAction SilentlyContinue
        $script:serverProcess.WaitForExit(5000) | Out-Null
    }
}

try {
    $existingConnection = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if ($existingConnection) {
        throw "Port $port is already in use. Set WEBPAY_BROWSER_SMOKE_PORT to a free port or stop the existing test server."
    }

    $env:WEBPAY_BROWSER_SMOKE_PORT = [string] $port
    $script:serverProcess = Start-Process `
        -FilePath "node" `
        -ArgumentList "./e2e/fixtures/webpay-browser-smoke-server.mjs" `
        -WorkingDirectory (Get-Location) `
        -WindowStyle Hidden `
        -PassThru

    $ready = $false
    for ($attempt = 1; $attempt -le 50; $attempt += 1) {
        try {
            $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 100
        }
    }

    if (-not $ready) {
        throw "WebPay browser-smoke fixture server did not become ready on port $port."
    }

    $arguments = @("playwright", "test", "--config", "playwright.config.ts")
    if ($Headed) {
        $arguments += "--headed"
    }

    & npx.cmd @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright browser smoke failed with exit code $LASTEXITCODE."
    }
}
finally {
    Stop-SmokeServer
}
