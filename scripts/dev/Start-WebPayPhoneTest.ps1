param(
    [string]$RepoRoot = "D:\SourceCodes\ExitPass",
    [string]$ApiProxyTarget = "http://localhost:8082",
    [int]$WebPayPort = 5174,
    [switch]$SkipDocker,
    [switch]$SkipNgrok
)

$ErrorActionPreference = "Stop"

Write-Host "Starting ExitPass WebPay phone test environment..." -ForegroundColor Cyan

$dockerDir = Join-Path $RepoRoot "infra\docker"
$webPayDir = Join-Path $RepoRoot "src\Services\WebPayUi"

if (-not (Test-Path $RepoRoot)) {
    throw "Repo root not found: $RepoRoot"
}

if (-not (Test-Path $dockerDir)) {
    throw "Docker directory not found: $dockerDir"
}

if (-not (Test-Path $webPayDir)) {
    throw "WebPay UI directory not found: $webPayDir"
}

# Required for backend DB-backed tests and local service behavior.
$cs = "Host=127.0.0.1;Port=5433;Database=exitpass_v12_dev;Username=exitpass;Password=change_me;Include Error Detail=true"

$env:EXITPASS_TEST_MAIN_DB = $cs
$env:EXITPASS_INTEGRATION_DB = $cs
$env:EXITPASS_TEST_DB_CONNECTION_STRING = $cs
$env:ConnectionStrings__MainDatabase = $cs

if (-not $SkipDocker) {
    Write-Host "Starting backend Docker services..." -ForegroundColor Yellow

    Push-Location $dockerDir
    docker compose up -d postgres rabbitmq central-pms payment-orchestrator mock-vendor-pms mock-payment-provider
    Pop-Location

    Write-Host "Docker services:" -ForegroundColor Yellow
    docker ps --format "table {{.Names}}`t{{.Ports}}`t{{.Status}}"
}
else {
    Write-Host "Skipping Docker startup." -ForegroundColor DarkYellow
}

Write-Host "Starting WebPay UI dev server on port $WebPayPort..." -ForegroundColor Yellow

$webPayCommand = @"
cd '$webPayDir'
`$env:VITE_WEBPAY_API_PROXY_TARGET = '$ApiProxyTarget'
Remove-Item Env:\VITE_WEBPAY_API_BASE_URL -ErrorAction SilentlyContinue
npm run dev -- --host 0.0.0.0 --port $WebPayPort
"@

Start-Process powershell -ArgumentList "-NoExit", "-Command", $webPayCommand

Start-Sleep -Seconds 3

if (-not $SkipNgrok) {
    $ngrokExists = Get-Command ngrok -ErrorAction SilentlyContinue

    if (-not $ngrokExists) {
        Write-Host "ngrok is not installed or not in PATH." -ForegroundColor Red
        Write-Host "Install ngrok, then rerun this script with ngrok available." -ForegroundColor Red
    }
    else {
        Write-Host "Starting ngrok tunnel to WebPay UI..." -ForegroundColor Yellow

        $ngrokCommand = "ngrok http $WebPayPort"
        Start-Process powershell -ArgumentList "-NoExit", "-Command", $ngrokCommand

        Write-Host ""
        Write-Host "Open the ngrok HTTPS URL on your phone." -ForegroundColor Green
        Write-Host "Use HTTPS because mobile camera access requires a secure context." -ForegroundColor Green
    }
}
else {
    Write-Host "Skipping ngrok startup." -ForegroundColor DarkYellow
}

Write-Host ""
Write-Host "Local WebPay URL:" -ForegroundColor Cyan
Write-Host "http://localhost:$WebPayPort" -ForegroundColor White

Write-Host ""
Write-Host "Manual test data:" -ForegroundColor Cyan
Write-Host "Ticket Reference: TICKET-TEST-001"
Write-Host "Plate Number: ABC1234"
Write-Host "Payment Methods: QRPH, CARD, GCASH, MAYA"

Write-Host ""
Write-Host "Notes:" -ForegroundColor Cyan
Write-Host "- Leave VITE_WEBPAY_API_BASE_URL unset for ngrok phone testing."
Write-Host "- WebPay will call /v1/webpay/payment-intents through the Vite same-origin proxy."
Write-Host "- VITE_WEBPAY_API_PROXY_TARGET is set to $ApiProxyTarget."
Write-Host "- If ngrok asks for auth, run: ngrok config add-authtoken YOUR_AUTHTOKEN"