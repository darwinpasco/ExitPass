[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
. (Join-Path $PSScriptRoot 'RuntimeProfile.ps1')

function Assert-Equal($Expected, $Actual, [string] $Context) {
    if ($Expected -ne $Actual) {
        throw "$Context expected '$Expected' but received '$Actual'."
    }
}

function Assert-Throws([scriptblock] $Action, [string] $ExpectedMessage, [string] $Context) {
    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Context threw '$($_.Exception.Message)' instead of a message containing '$ExpectedMessage'."
        }
        return
    }
    throw "$Context did not fail closed."
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "exitpass-runtime-profile-test-$PID"
$profileFile = Join-Path $temporaryRoot 'infra\docker\.env'
[void](New-Item -ItemType Directory -Path (Split-Path $profileFile) -Force)

try {
    foreach ($profile in @('DEVELOPER', 'TESTING', 'PRODUCTION')) {
        Set-Content -LiteralPath $profileFile -Value "EXITPASS_RUNTIME_PROFILE=$profile"
        Assert-Equal $profile (Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot) "$profile parsing"
    }

    Set-Content -LiteralPath $profileFile -Value '  EXITPASS_RUNTIME_PROFILE = testing  '
    Assert-Equal 'TESTING' (Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot) 'case-insensitive parsing'

    Remove-Item -LiteralPath $profileFile
    Assert-Throws { Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot } 'profile file is missing' 'missing .env'

    Set-Content -LiteralPath $profileFile -Value 'UNRELATED=value'
    Assert-Throws { Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot } 'is missing' 'missing selector'

    Set-Content -LiteralPath $profileFile -Value 'EXITPASS_RUNTIME_PROFILE=  '
    Assert-Throws { Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot } 'is blank' 'blank selector'

    Set-Content -LiteralPath $profileFile -Value 'EXITPASS_RUNTIME_PROFILE=normal'
    Assert-Throws { Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot } 'Unsupported' 'unknown selector'

    Set-Content -LiteralPath $profileFile -Value @('EXITPASS_RUNTIME_PROFILE=TESTING', 'EXITPASS_RUNTIME_PROFILE=DEVELOPER')
    Assert-Throws { Get-ExitPassRuntimeProfile -RepositoryRoot $temporaryRoot } 'conflicting values' 'conflicting selector'

    $testing = Get-ExitPassRuntimeProfileDefinition -Profile TESTING
    Assert-Equal 'exitpass-ist-persistent-db' $testing.ExitPassDatabaseContainer 'Testing ExitPass database'
    Assert-Equal 'exitpass-pos-ist-persistent-db' $testing.PosDatabaseContainer 'Testing POS database'
    Assert-Equal 'REAL_HIKCENTRAL' $testing.VendorMode 'Testing vendor mode'
    Assert-Equal 'TEST' $testing.PaymentEnvironment 'Testing payment environment'

    $developer = Get-ExitPassRuntimeProfileDefinition -Profile DEVELOPER
    Assert-Equal 'exitpass-dev-synthetic-db' $developer.ExitPassDatabaseContainer 'Developer ExitPass database'
    Assert-Equal 'exitpass-dev-synthetic' $developer.Network 'Developer network'
    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile DEVELOPER -DatabaseProfile TESTING -VendorMode WIREMOCK_HIKCENTRAL -PaymentEnvironment TEST -SiteMode REAL_SITE -SyntheticFixturesEnabled $true
    } 'cannot use TESTING database resources' 'Developer Testing database guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile DEVELOPER -DatabaseProfile PRODUCTION -VendorMode WIREMOCK_HIKCENTRAL -PaymentEnvironment TEST -SiteMode REAL_SITE -SyntheticFixturesEnabled $true
    } 'cannot use PRODUCTION database resources' 'Developer Production database guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile DEVELOPER -DatabaseProfile DEVELOPER -VendorMode WIREMOCK_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode REAL_SITE -SyntheticFixturesEnabled $true
    } 'cannot use payment environment LIVE' 'Developer live-payment guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile TESTING -DatabaseProfile TESTING -VendorMode WIREMOCK_HIKCENTRAL -PaymentEnvironment TEST -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use vendor mode WIREMOCK_HIKCENTRAL' 'Testing WireMock guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile TESTING -DatabaseProfile DEVELOPER -VendorMode REAL_HIKCENTRAL -PaymentEnvironment TEST -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use DEVELOPER database resources' 'Testing Developer database guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile PRODUCTION -VendorMode REAL_HIKCENTRAL -PaymentEnvironment TEST -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use payment environment TEST' 'Production test-payment guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile PRODUCTION -VendorMode WIREMOCK_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use vendor mode WIREMOCK_HIKCENTRAL' 'Production WireMock guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile DEVELOPER -VendorMode REAL_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use DEVELOPER database resources' 'Production Developer database guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile TESTING -VendorMode REAL_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode REAL_SITE -SyntheticFixturesEnabled $false
    } 'cannot use TESTING database resources' 'Production Testing guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile PRODUCTION -VendorMode REAL_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode TEST_SITE -SyntheticFixturesEnabled $false
    } 'cannot use TEST_SITE' 'Production TEST_SITE guard'

    Assert-Throws {
        Assert-ExitPassRuntimeProfileCombination -Profile PRODUCTION -DatabaseProfile PRODUCTION -VendorMode REAL_HIKCENTRAL -PaymentEnvironment LIVE -SiteMode REAL_SITE -SyntheticFixturesEnabled $true
    } 'synthetic fixture setting is invalid' 'Production synthetic fixture guard'

    foreach ($launcher in @('Start-CentralPms.ps1', 'Start-PaymentOrchestrator.ps1', 'Start-WebPayPitx.ps1', 'Start-OperatorConsole.ps1')) {
        $launcherText = Get-Content -LiteralPath (Join-Path $PSScriptRoot $launcher) -Raw
        if ($launcherText -notmatch 'Initialize-ExitPassLocalRuntimeProfile') {
            throw "$launcher does not require the authoritative runtime profile."
        }
        if ($launcherText -notmatch 'EXITPASS_RUNTIME_PROFILE') {
            throw "$launcher does not propagate the authoritative runtime profile."
        }
    }

    $legacyEnvironment = Get-Content -LiteralPath (Join-Path $repoRoot 'infra\env\.env.dev') -Raw
    if ($legacyEnvironment -match '(?m)^\s*EXITPASS_RUNTIME_PROFILE\s*=') {
        throw 'infra/env/.env.dev still declares an independent runtime profile.'
    }

    Set-Content -LiteralPath $profileFile -Value 'EXITPASS_RUNTIME_PROFILE=DEVELOPER'
    Assert-Throws {
        Initialize-ExitPassLocalRuntimeProfile -RepositoryRoot $temporaryRoot -Component 'test component'
    } 'DEVELOPER startup is not yet available' 'Developer startup guard'

    Set-Content -LiteralPath $profileFile -Value 'EXITPASS_RUNTIME_PROFILE=PRODUCTION'
    Assert-Throws {
        Initialize-ExitPassLocalRuntimeProfile -RepositoryRoot $temporaryRoot -Component 'test component'
    } 'PRODUCTION startup is unavailable' 'Production startup guard'

    Write-Host 'Runtime profile foundation tests: PASS'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
