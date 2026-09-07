[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'RuntimeProfile.ps1')
. (Join-Path $PSScriptRoot 'DeveloperRuntime.ps1')

function Assert-Equal($Expected, $Actual, [string] $Context) {
    if ($Expected -ne $Actual) { throw "$Context expected '$Expected' but received '$Actual'." }
}

$definition = Get-ExitPassRuntimeProfileDefinition DEVELOPER
Assert-Equal $true $definition.LocalConfigurationAvailable 'Developer availability'
Assert-Equal 'exitpass-dev-synthetic-db' $definition.ExitPassDatabaseContainer 'Developer database isolation'
Assert-Equal 'exitpass-dev-synthetic' $definition.Network 'Developer network isolation'
Assert-Equal 'WIREMOCK_HIKCENTRAL' $definition.VendorMode 'Developer vendor boundary'
if ($definition.ExitPassDatabaseContainer -match 'ist-persistent' -or $definition.Network -match 'ist-persistent') {
    throw 'Developer resource definition references Testing resources.'
}

$fixturePath = Join-Path $repoRoot 'infra\docker\developer-runtime\fixtures.json'
$fixtures = Get-Content -LiteralPath $fixturePath -Raw | ConvertFrom-Json
$expected = @('DEV-QRPH-001','DEV-GCASH-001','DEV-MAYA-001','DEV-CARD-001','DEV-APT-CASH-001')
Assert-Equal 5 $fixtures.Count 'fixture count'
Assert-Equal ($expected -join '|') (($fixtures.ticketReference) -join '|') 'fixture priority'
Assert-Equal 5 @($fixtures.vendorRecordGuid | Select-Object -Unique).Count 'unique vendor records'
Assert-Equal 5 @($fixtures.plateLicense | Select-Object -Unique).Count 'unique plates'

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-developer-runtime-test-$PID"
try {
    New-ExitPassDeveloperWireMockMappings -RepositoryRoot $repoRoot -DestinationRoot $temporaryRoot
    $mappings = @(Get-ChildItem (Join-Path $temporaryRoot 'mappings') -File | ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    })
    Assert-Equal 14 $mappings.Count 'WireMock mapping count'
    foreach ($path in @(
        '/artemis/api/vehicle/v1/parkinglot/passageway/record',
        '/artemis/api/vehicle/v1/parkingfee/calculate',
        '/artemis/api/vehicle/v1/parkingfee/confirm')) {
        if (@($mappings | Where-Object { $_.priority -eq 1 -and $_.request.urlPath -eq $path }).Count -eq 0) {
            throw "No successful strict mapping exists for $path."
        }
        if (@($mappings | Where-Object { $_.priority -eq 10 -and $_.request.urlPath -eq $path }).Count -ne 1) {
            throw "No malformed-request rejection mapping exists for $path."
        }
    }
    $successful = @($mappings | Where-Object priority -eq 1)
    foreach ($mapping in $successful) {
        foreach ($header in @('Content-Type','X-Ca-Key','X-Ca-Signature','X-Ca-Timestamp','X-Ca-Signature-Headers','userId')) {
            if ($null -eq $mapping.request.headers.$header) { throw "$($mapping.name) does not match required header $header." }
        }
    }
    foreach ($fixture in $fixtures) {
        $calculate = @($mappings | Where-Object name -eq "developer-calculate-$($fixture.ticketReference)")
        $confirm = @($mappings | Where-Object name -eq "developer-confirm-$($fixture.ticketReference)")
        Assert-Equal 1 $calculate.Count "calculate mapping $($fixture.ticketReference)"
        Assert-Equal 1 $confirm.Count "confirm mapping $($fixture.ticketReference)"
        Assert-Equal $fixture.fee $calculate[0].response.jsonBody.data.fee "calculate fee $($fixture.ticketReference)"
        Assert-Equal $fixture.fee $confirm[0].response.jsonBody.data.fee "confirm fee $($fixture.ticketReference)"
    }

    $centralLauncher = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Start-CentralPmsDeveloper.ps1') -Raw
    if ($centralLauncher -match 'exitpass-ist-persistent|pitx-site-adapter|sys-service\.exitpass\.test') {
        throw 'Developer Central PMS launcher references Testing resources.'
    }
    if ($centralLauncher -notmatch 'DegradedResolveFallbackEnabled=false') {
        throw 'Developer Central PMS launcher does not preserve authoritative live resolve.'
    }
    $paymentLauncher = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Start-PaymentOrchestratorDeveloper.ps1') -Raw
    if ($paymentLauncher -notmatch "IsLiveMode'\) -ne 'false'" -or $paymentLauncher -notmatch 'AllowedPaymentMethodTypes__3=card') {
        throw 'Developer PayMongo TEST guard or method configuration is missing.'
    }
    Write-Host 'Developer runtime focused tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
