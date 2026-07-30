param(
    [switch] $AcknowledgeValidationOnly,
    [switch] $Headed,
    [switch] $KeepArtifacts,
    [int] $Port = 5206
)

$ErrorActionPreference = "Stop"

$script:serverProcess = $null
$artifactRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..\..\.local") -ErrorAction SilentlyContinue
if (-not $artifactRoot) {
    $artifactRoot = New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot "..\..\..\..\.local")
}

$artifactPath = Join-Path $artifactRoot "webpay-ordinance-validation"
$healthUrl = "http://127.0.0.1:$Port/__validation/health"
$nonceBytes = [byte[]]::new(24)
$randomNumberGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomNumberGenerator.GetBytes($nonceBytes)
}
finally {
    $randomNumberGenerator.Dispose()
}
$validationNonce = ([System.BitConverter]::ToString($nonceBytes) -replace "-", "").ToLowerInvariant()

function Stop-ValidationServer {
    if ($script:serverProcess -and -not $script:serverProcess.HasExited) {
        Stop-Process -Id $script:serverProcess.Id -Force -ErrorAction SilentlyContinue
        $script:serverProcess.WaitForExit(5000) | Out-Null
    }
}

function Remove-ValidationArtifacts {
    if ($KeepArtifacts) {
        return
    }

    if (Test-Path -LiteralPath $artifactPath) {
        $resolvedArtifactPath = (Resolve-Path -LiteralPath $artifactPath).Path
        $resolvedArtifactRoot = (Resolve-Path -LiteralPath $artifactRoot).Path
        if (-not $resolvedArtifactPath.StartsWith($resolvedArtifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove validation artifacts outside .local: $resolvedArtifactPath"
        }

        Remove-Item -LiteralPath $resolvedArtifactPath -Recurse -Force
    }
}

if (-not $AcknowledgeValidationOnly) {
    throw "Pass -AcknowledgeValidationOnly to run the loopback-only G-004 validation harness."
}

if ($Port -lt 1024 -or $Port -gt 65535) {
    throw "Port $Port is outside the allowed validation range."
}

$existingConnection = Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($existingConnection) {
    throw "Port $Port is already listening on loopback. Stop the existing process or pass a different -Port."
}

$wildcardConnection = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    Where-Object { $_.LocalAddress -ne "127.0.0.1" -and $_.LocalAddress -ne "::1" }
if ($wildcardConnection) {
    throw "Port $Port is already bound by a non-loopback listener. The validation harness refuses non-loopback binding."
}

$previousSmokePort = $env:WEBPAY_BROWSER_SMOKE_PORT
$previousArtifactRoot = $env:WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT
$previousValidationPort = $env:WEBPAY_ORDINANCE_VALIDATION_PORT
$previousValidationNonce = $env:WEBPAY_ORDINANCE_VALIDATION_NONCE
$previousSiteGroup = $env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID
$previousSite = $env:VITE_WEBPAY_DEFAULT_SITE_ID
$previousVendor = $env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID
$previousApiBase = $env:VITE_WEBPAY_API_BASE_URL

try {
    $env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = "41000000-0000-4000-8000-000000000001"
    $env:VITE_WEBPAY_DEFAULT_SITE_ID = "51000000-0000-4000-8000-000000000001"
    $env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "61000000-0000-4000-8000-000000000001"
    $env:VITE_WEBPAY_API_BASE_URL = ""

    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) {
        throw "WebPay production build failed with exit code $LASTEXITCODE."
    }

    $env:WEBPAY_BROWSER_SMOKE_PORT = [string] $Port
    $env:WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT = "../../../.local/webpay-ordinance-validation"
    $env:WEBPAY_ORDINANCE_VALIDATION_PORT = [string] $Port
    $env:WEBPAY_ORDINANCE_VALIDATION_NONCE = $validationNonce

    $script:serverProcess = Start-Process `
        -FilePath "node" `
        -ArgumentList "./e2e/fixtures/webpay-statutory-ordinance-validation-server.mjs" `
        -WorkingDirectory (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")) `
        -WindowStyle Hidden `
        -PassThru

    $ready = $false
    for ($attempt = 1; $attempt -le 60; $attempt += 1) {
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
        throw "G-004 validation fixture server did not become ready on loopback port $Port."
    }

    Write-Host "G-004 WebPay statutory ordinance validation" -ForegroundColor Cyan
    Write-Host "Browser origin: http://127.0.0.1:$Port"
    Write-Host "Browser URL: http://127.0.0.1:$Port/?ticketReference=WEBPAY-ORD-G004-001&webpayStatutoryRecoveryReset=1"
    Write-Host "Routing model: same-origin WebPay route fixture"
    Write-Host "Validation control: loopback-only nonce, not browser-facing"
    Write-Host "Scenario source: process-local validation fixture state"
    Write-Host "Fixture server PID: $($script:serverProcess.Id)"

    $arguments = @(
        "playwright",
        "test",
        "e2e/webpay-statutory-ordinance-validation.spec.ts",
        "--config",
        "playwright.config.ts"
    )
    if ($Headed) {
        $arguments += "--headed"
    }

    & npx.cmd @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "G-004 WebPay statutory ordinance validation failed with exit code $LASTEXITCODE."
    }

    Write-Host "G-004 WebPay statutory ordinance validation passed." -ForegroundColor Green
}
finally {
    Stop-ValidationServer

    $env:WEBPAY_BROWSER_SMOKE_PORT = $previousSmokePort
    $env:WEBPAY_BROWSER_SMOKE_ARTIFACT_ROOT = $previousArtifactRoot
    $env:WEBPAY_ORDINANCE_VALIDATION_PORT = $previousValidationPort
    $env:WEBPAY_ORDINANCE_VALIDATION_NONCE = $previousValidationNonce
    $env:VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = $previousSiteGroup
    $env:VITE_WEBPAY_DEFAULT_SITE_ID = $previousSite
    $env:VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = $previousVendor
    $env:VITE_WEBPAY_API_BASE_URL = $previousApiBase

    Remove-ValidationArtifacts
}
