[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$ExpectedDatabaseName,
    [Parameter(Mandatory)] [guid]$SiteId,
    [Parameter(Mandatory)] [guid]$SiteGroupId,
    [Parameter(Mandatory)] [guid]$VendorSystemId,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string]$ParkingLotIndexCode,
    [Parameter(Mandatory)] [switch]$AcknowledgeNonProductionEndpoint
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$project = Join-Path $repositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj"
$walkthroughState = Join-Path $repositoryRoot ".local\webpay-statutory-discount-walkthrough\state.json"

if (-not $AcknowledgeNonProductionEndpoint) {
    throw "Explicit non-Production endpoint acknowledgement is required."
}

if (Test-Path -LiteralPath $walkthroughState) {
    throw "WebPay statutory walkthrough state exists. Stop and clean that owned walkthrough before local projection activation."
}

$legacy = Get-ChildItem Env: | Where-Object { $_.Name -like "HIKCENTRAL__*" }
if ($legacy) {
    throw "Legacy HIKCENTRAL__ configuration is present. Remove it from this dedicated process before activation."
}

$requiredVariables = @(
    "ConnectionStrings__MainDatabase",
    "CentralPms__VendorPms__HikCentral__BaseUrl",
    "CentralPms__VendorPms__HikCentral__AppKey",
    "CentralPms__VendorPms__HikCentral__AppSecret"
)
$missing = @($requiredVariables | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_, "Process"))
})
if ($missing.Count -gt 0) {
    throw "Required process-scoped configuration is missing: $($missing -join ', '). Values were not read or displayed."
}

$endpoint = [uri][Environment]::GetEnvironmentVariable(
    "CentralPms__VendorPms__HikCentral__BaseUrl",
    "Process")
if ($endpoint.Scheme -notin @("http", "https")) {
    throw "The configured HikCentral endpoint scheme is not allowed."
}

if ($endpoint.Host -match '(^|[.-])(prod|production)([.-]|$)') {
    throw "The local projection launcher refuses an endpoint whose host is marked as Production."
}

$runningScheduler = Get-CimInstance Win32_Process | Where-Object {
    $_.CommandLine -like "*ExitPass.CentralPms.Api*" -and
    $_.CommandLine -like "*HikCentralLocal*"
}
if ($runningScheduler) {
    throw "A dedicated HikCentralLocal Central PMS process is already running."
}

$env:CentralPms__VendorPms__Provider = "HIKCENTRAL"
$env:CentralPms__VendorSessionProjections__SchedulerEnabled = "true"
$env:CentralPms__VendorSessionProjections__RequiredForEnvironment = "true"
$env:CentralPms__VendorSessionProjections__ActivationMode = "LOCAL_PROFILE"
$env:CentralPms__VendorSessionProjections__DefaultPollIntervalSeconds = "60"
$env:CentralPms__VendorSessionProjections__NormalFreshnessTargetSeconds = "60"
$env:CentralPms__VendorSessionProjections__MaxProjectionAgeMinutes = "1"
$env:CentralPms__VendorSessionProjections__DegradedResolveFallbackEnabled = "false"
$env:CentralPms__VendorSessionProjections__ActivationEnvironment = "HikCentralLocal"
$env:CentralPms__VendorSessionProjections__LocalNonProductionEndpointAcknowledged = "true"
$env:CentralPms__VendorSessionProjections__ExpectedDatabaseName = $ExpectedDatabaseName
$env:CentralPms__VendorSessionProjections__ExpectedTargetSiteId = $SiteId.ToString("D")
$env:CentralPms__VendorSessionProjections__ExpectedTargetSiteGroupId = $SiteGroupId.ToString("D")
$env:CentralPms__VendorSessionProjections__ExpectedTargetVendorSystemId = $VendorSystemId.ToString("D")
$env:CentralPms__VendorSessionProjections__ExpectedTargetParkingLotIndexCode = $ParkingLotIndexCode.Trim()

Write-Host "Presence checks passed. No configuration values or secrets were printed."
Write-Host "Starting dedicated HikCentralLocal Central PMS. Press Ctrl+C to stop."
& dotnet run --project $project --configuration Release --launch-profile HikCentralLocal
exit $LASTEXITCODE
