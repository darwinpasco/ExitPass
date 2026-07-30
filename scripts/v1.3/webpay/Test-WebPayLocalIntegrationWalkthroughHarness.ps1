$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
$startScript = Join-Path $repoRoot "scripts\v1.3\webpay\Start-WebPayLocalIntegrationWalkthrough.ps1"
$stopScript = Join-Path $repoRoot "scripts\v1.3\webpay\Stop-WebPayLocalIntegrationWalkthrough.ps1"
$paymentProofScript = Join-Path $repoRoot "scripts\v1.3\webpay\Test-WebPayLocalIntegrationPaymentHandoff.ps1"
$seedSql = Join-Path $repoRoot "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql"
$verifySql = Join-Path $repoRoot "scripts\v1.3\webpay\Verify-WebPayLocalIntegrationWalkthrough.sql"
$runbook = Join-Path $repoRoot "docs\v1.3\webpay\runbooks\ExitPass_WebPay_Local_Integration_Walkthrough_v1.0.md"
$canonicalSql = "D:\SourceCodes\exitpassdb_v1.2\build\generated\exitpass-full-object.generated.sql"

foreach ($path in @($startScript, $stopScript, $paymentProofScript, $seedSql, $verifySql, $runbook)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required walkthrough asset missing: $path"
    }
}

if (-not (Test-Path -LiteralPath $canonicalSql)) {
    throw "Canonical generated baseline missing: $canonicalSql"
}

$startOutput = & powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $startScript -DryRun -StartServices -SkipInfrastructure -SkipDatabaseRebuild -SkipSeed 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Startup dry-run failed: $startOutput"
}

$startText = $startOutput -join "`n"
foreach ($expected in @(
    "exitpass_webpay_local_walkthrough",
    "WEBPAY-LOCAL-ORDINARY-001",
    "LOCALPAY001",
    "PHP 137.50",
    "http://localhost:5173",
    "http://localhost:8082",
    "WEBPAY_PUBLIC_BASE_URL",
    "VITE_WEBPAY_DEFAULT_SITE_GROUP_ID",
    "VITE_WEBPAY_DEFAULT_SITE_ID",
    "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID",
    "would discover fixture IDs",
    "WebhookSecretKey",
    "/v1/checkout_sessions",
    "exitpass-full-object.generated.sql"
)) {
    if ($startText -notlike "*$expected*") {
        throw "Startup dry-run output did not include expected value: $expected"
    }
}

$stopOutput = & powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $stopScript -DryRun -RemoveDisposableDatabase 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Teardown dry-run failed: $stopOutput"
}

$paymentProofOutput = & powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File $paymentProofScript -DryRun 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "Payment handoff proof dry-run failed: $paymentProofOutput"
}

$paymentProofText = $paymentProofOutput -join "`n"
foreach ($expected in @(
    "v1/webpay/parking-session",
    "v1/webpay/payment-intents",
    "WEBPAY_LOCAL_MOCK_PMS/LOCAL",
    "PHP 137.50",
    "__admin/requests",
    "payment_attempts",
    "provider_sessions"
)) {
    if ($paymentProofText -notlike "*$expected*") {
        throw "Payment proof dry-run output did not include expected value: $expected"
    }
}

$allText = Get-Content -LiteralPath $startScript, $stopScript, $paymentProofScript, $seedSql, $verifySql, $runbook -Raw
foreach ($forbidden in @(
    "Senior Citizen",
    "PWD",
    "OPEN_GATE",
    "sk_live_",
    "pk_live_",
    "Authorization: Bearer",
    "ExitPass_DBv1.2",
    "ExitPass_Full_Database_Creation_DDL_v1.2.sql"
)) {
    if ($allText -like "*$forbidden*") {
        throw "Forbidden walkthrough token found: $forbidden"
    }
}

if ($allText -like "*exitpass_v12_dev*DROP*") {
    throw "Harness must not drop the normal developer database."
}

Write-Host "WebPay local integration walkthrough harness dry-run validation passed." -ForegroundColor Green
