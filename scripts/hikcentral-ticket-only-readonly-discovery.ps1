param(
    [string]$BeginTime = "",
    [string]$EndTime = "",
    [string]$CameraIndexCode = ""
)

$ErrorActionPreference = "Stop"

function Require-Env([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required environment variable: $Name"
    }
    return $value
}

Require-Env "HIKCENTRAL_BASE_URL" | Out-Null
Require-Env "HIKCENTRAL_APP_KEY" | Out-Null
Require-Env "HIKCENTRAL_APP_SECRET" | Out-Null
Require-Env "HIKCENTRAL_TEST_PARKING_LOT_INDEX_CODE" | Out-Null

if ([Environment]::GetEnvironmentVariable("HIKCENTRAL_CONFIRM_PAYMENT_ENABLED") -ne "false") {
    throw "HIKCENTRAL_CONFIRM_PAYMENT_ENABLED must be false for read-only discovery."
}

if ([Environment]::GetEnvironmentVariable("HIKCENTRAL_GATE_OPEN_ALLOWED") -ne "false") {
    throw "HIKCENTRAL_GATE_OPEN_ALLOWED must be false for read-only discovery."
}

[Environment]::SetEnvironmentVariable("EXITPASS_RUN_HIKCENTRAL_TICKET_DISCOVERY", "true", "Process")

if (-not [string]::IsNullOrWhiteSpace($BeginTime)) {
    [Environment]::SetEnvironmentVariable("HIKCENTRAL_TICKET_DISCOVERY_BEGIN_TIME", $BeginTime, "Process")
}

if (-not [string]::IsNullOrWhiteSpace($EndTime)) {
    [Environment]::SetEnvironmentVariable("HIKCENTRAL_TICKET_DISCOVERY_END_TIME", $EndTime, "Process")
}

if (-not [string]::IsNullOrWhiteSpace($CameraIndexCode)) {
    [Environment]::SetEnvironmentVariable("HIKCENTRAL_TEST_CAMERA_INDEX_CODE", $CameraIndexCode, "Process")
}

dotnet test `
    "src\Services\VendorPmsAdapter\tests\ExitPass.VendorPmsAdapter.IntegrationTests\ExitPass.VendorPmsAdapter.IntegrationTests.csproj" `
    --filter "FullyQualifiedName~HikCentralTicketOnlyReadonlyDiscoveryLiveTests" `
    --logger "console;verbosity=detailed"
