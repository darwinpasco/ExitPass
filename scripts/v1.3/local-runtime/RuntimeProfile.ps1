Set-StrictMode -Version Latest

function Get-ExitPassRuntimeProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [string] $EnvironmentFilePath
    )

    $profileFile = if ([string]::IsNullOrWhiteSpace($EnvironmentFilePath)) {
        Join-Path $RepositoryRoot 'infra\docker\.env'
    }
    else {
        $EnvironmentFilePath
    }

    if (-not (Test-Path -LiteralPath $profileFile -PathType Leaf)) {
        throw "ExitPass runtime profile file is missing: $profileFile"
    }

    $values = @(
        Get-Content -LiteralPath $profileFile |
            ForEach-Object {
                if ($_ -match '^\s*EXITPASS_RUNTIME_PROFILE\s*=(.*)$') {
                    $Matches[1].Trim()
                }
            }
    )

    if ($values.Count -eq 0) {
        throw "EXITPASS_RUNTIME_PROFILE is missing from $profileFile."
    }
    if (@($values | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        throw "EXITPASS_RUNTIME_PROFILE is blank in $profileFile."
    }

    $normalizedValues = @($values | ForEach-Object { $_.ToUpperInvariant() } | Select-Object -Unique)
    if ($normalizedValues.Count -gt 1) {
        throw "EXITPASS_RUNTIME_PROFILE has conflicting values in $profileFile."
    }

    $profile = $normalizedValues[0]
    if ($profile -notin @('DEVELOPER', 'TESTING', 'PRODUCTION')) {
        throw "Unsupported EXITPASS_RUNTIME_PROFILE '$profile'. Allowed values: DEVELOPER, TESTING, PRODUCTION."
    }

    return $profile
}

function Get-ExitPassRuntimeProfileDefinition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('DEVELOPER', 'TESTING', 'PRODUCTION')]
        [string] $Profile
    )

    switch ($Profile.ToUpperInvariant()) {
        'DEVELOPER' {
            return [pscustomobject]@{
                Name = 'DEVELOPER'
                DisplayLabel = 'DEVELOPER - SIMULATED HIKCENTRAL'
                ExitPassDatabaseContainer = 'exitpass-dev-synthetic-db'
                ExitPassDatabaseVolume = 'exitpass-dev-synthetic-data'
                PosDatabaseContainer = 'exitpass-pos-dev-synthetic-db'
                PosDatabaseVolume = 'exitpass-pos-dev-synthetic-data'
                Network = 'exitpass-dev-synthetic'
                VendorMode = 'WIREMOCK_HIKCENTRAL'
                PaymentEnvironment = 'TEST'
                SyntheticFixturesEnabled = $true
                LocalConfigurationAvailable = $true
            }
        }
        'TESTING' {
            return [pscustomobject]@{
                Name = 'TESTING'
                DisplayLabel = 'TESTING - REAL HIKCENTRAL / PAYMONGO TEST'
                ExitPassDatabaseContainer = 'exitpass-ist-persistent-db'
                ExitPassDatabaseVolume = 'exitpass-ist-persistent-data'
                PosDatabaseContainer = 'exitpass-pos-ist-persistent-db'
                PosDatabaseVolume = 'exitpass-pos-ist-persistent-data'
                Network = 'exitpass-ist-persistent'
                VendorMode = 'REAL_HIKCENTRAL'
                PaymentEnvironment = 'TEST'
                SyntheticFixturesEnabled = $false
                LocalConfigurationAvailable = $true
            }
        }
        'PRODUCTION' {
            return [pscustomobject]@{
                Name = 'PRODUCTION'
                DisplayLabel = 'PRODUCTION'
                ExitPassDatabaseContainer = $null
                ExitPassDatabaseVolume = $null
                PosDatabaseContainer = $null
                PosDatabaseVolume = $null
                Network = $null
                VendorMode = 'REAL_HIKCENTRAL'
                PaymentEnvironment = 'LIVE'
                SyntheticFixturesEnabled = $false
                LocalConfigurationAvailable = $false
            }
        }
    }
}

function Assert-ExitPassRuntimeProfileCombination {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('DEVELOPER', 'TESTING', 'PRODUCTION')][string] $Profile,
        [Parameter(Mandatory)][ValidateSet('DEVELOPER', 'TESTING', 'PRODUCTION')][string] $DatabaseProfile,
        [Parameter(Mandatory)][ValidateSet('WIREMOCK_HIKCENTRAL', 'REAL_HIKCENTRAL')][string] $VendorMode,
        [Parameter(Mandatory)][ValidateSet('TEST', 'LIVE')][string] $PaymentEnvironment,
        [Parameter(Mandatory)][ValidateSet('REAL_SITE', 'TEST_SITE')][string] $SiteMode,
        [Parameter(Mandatory)][bool] $SyntheticFixturesEnabled
    )

    $definition = Get-ExitPassRuntimeProfileDefinition -Profile $Profile
    if ($DatabaseProfile -ne $definition.Name) {
        throw "$($definition.Name) cannot use $DatabaseProfile database resources."
    }
    if ($VendorMode -ne $definition.VendorMode) {
        throw "$($definition.Name) cannot use vendor mode $VendorMode."
    }
    if ($PaymentEnvironment -ne $definition.PaymentEnvironment) {
        throw "$($definition.Name) cannot use payment environment $PaymentEnvironment."
    }
    if ($Profile -eq 'PRODUCTION' -and $SiteMode -eq 'TEST_SITE') {
        throw 'PRODUCTION cannot use TEST_SITE configuration.'
    }
    if ($SyntheticFixturesEnabled -ne $definition.SyntheticFixturesEnabled) {
        throw "$($definition.Name) synthetic fixture setting is invalid."
    }
}

function Initialize-ExitPassLocalRuntimeProfile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $Component
    )

    $profile = Get-ExitPassRuntimeProfile -RepositoryRoot $RepositoryRoot
    $definition = Get-ExitPassRuntimeProfileDefinition -Profile $profile
    Write-Host "ExitPass Runtime Profile: $($definition.Name)"
    Write-Host $definition.DisplayLabel

    if ($profile -eq 'PRODUCTION') {
        throw "$Component PRODUCTION startup is unavailable because no approved local Production configuration is present. Testing, Developer, WireMock, and synthetic resources are not permitted."
    }

    Assert-ExitPassRuntimeProfileCombination `
        -Profile $profile `
        -DatabaseProfile $definition.Name `
        -VendorMode $definition.VendorMode `
        -PaymentEnvironment $definition.PaymentEnvironment `
        -SiteMode 'REAL_SITE' `
        -SyntheticFixturesEnabled $definition.SyntheticFixturesEnabled

    return $definition
}
