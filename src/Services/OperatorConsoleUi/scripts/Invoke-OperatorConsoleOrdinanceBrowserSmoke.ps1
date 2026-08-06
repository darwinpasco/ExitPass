param(
    [switch] $Headed,
    [switch] $ServerOnly,
    [switch] $Ui
)

$ErrorActionPreference = "Stop"

$script:serverProcess = $null
$port = if ($env:OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT) { [int] $env:OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT } else { 5197 }
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
        throw "Port $port is already in use. Set OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT to a free port or stop the existing test server."
    }

    if (-not (Test-Path -LiteralPath (Join-Path (Get-Location) "dist/index.html"))) {
        throw "Operator Console dist output was not found. Run npm.cmd run build before browser smoke."
    }

    $env:OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT = [string] $port
    $script:serverProcess = Start-Process `
        -FilePath "node" `
        -ArgumentList "./e2e/fixtures/operator-console-ordinance-browser-smoke-server.mjs" `
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
        throw "Operator Console ordinance browser-smoke fixture server did not become ready on port $port."
    }

    if ($ServerOnly) {
        Write-Host "Operator Console ordinance browser-smoke fixture is ready at http://127.0.0.1:$port"
        Write-Host "Open these deterministic scenario URLs:"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/senior-representative-optional"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/pwd-representative-unspecified"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/residency-required"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/driver-required"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/passenger-required"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/missing-evidence"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/malformed-authority"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/unsupported-effect"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/paranaque-operational"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/approved-request"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/rejected-request"
        Write-Host "H-005 secure evidence-review scenarios:"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-eligible-jpeg"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-eligible-png"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-unsupported-pdf"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-validation-pending"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-malware-detected"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-replaced"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-preview-storage-outage"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-permission-denied"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-cross-site-denied"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-cross-site-group-denied"
        Write-Host "  http://127.0.0.1:$port/operator-console/statutory-discounts/evidence-decision-switch"
        Read-Host "Press Enter to stop the fixture server"
        return
    }

    $arguments = @("playwright", "test", "--config", "playwright.config.ts")
    if ($Ui) {
        $arguments = @("playwright", "test", "--config", "playwright.config.ts", "--ui")
    }
    elseif ($Headed) {
        $arguments += "--headed"
    }

    & npx.cmd @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Operator Console ordinance browser smoke failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (-not $Ui) {
        Stop-SmokeServer
    }
    else {
        Write-Host "Playwright UI mode exited. Stopping fixture server on port $port."
        Stop-SmokeServer
    }
}
