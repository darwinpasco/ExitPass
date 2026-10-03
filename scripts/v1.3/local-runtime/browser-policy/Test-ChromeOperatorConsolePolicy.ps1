#Requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RegistryPolicyCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][object]$ExpectedValue,
        [Parameter(Mandatory = $true)][ValidateSet("DWord", "String")][string]$ExpectedKind
    )

    $property = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
    if ($null -eq $property) {
        return [pscustomobject]@{
            Policy = $Name
            Present = $false
            MandatoryMachineScope = $true
            Expected = $ExpectedValue
            Actual = $null
            Type = $null
            Pass = $false
        }
    }

    $actualValue = $property.$Name
    $actualKind = (Get-Item -LiteralPath $Path).GetValueKind($Name).ToString()
    return [pscustomobject]@{
        Policy = $Name
        Present = $true
        MandatoryMachineScope = $true
        Expected = $ExpectedValue
        Actual = $actualValue
        Type = $actualKind
        Pass = $actualValue -eq $ExpectedValue -and $actualKind -eq $ExpectedKind
    }
}

function Test-ChromeOperatorConsolePolicyState {
    param(
        [Parameter(Mandatory = $true)][bool]$ChromeInstalled,
        [Parameter(Mandatory = $true)][bool]$PolicyRootPresent,
        [Parameter(Mandatory = $true)][object[]]$Checks
    )

    return $ChromeInstalled -and $PolicyRootPresent -and @($Checks | Where-Object { -not $_.Pass }).Count -eq 0
}

function Invoke-ChromeOperatorConsolePolicyValidation {
    $chromePaths = @(
        (Join-Path $env:ProgramFiles "Google\Chrome\Application\chrome.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Google\Chrome\Application\chrome.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $chromeInstalled = @($chromePaths | Where-Object { Test-Path -LiteralPath $_ }).Count -gt 0

    $chromePolicyRoot = "HKLM:\SOFTWARE\Policies\Google\Chrome"
    $policyRootPresent = Test-Path -LiteralPath $chromePolicyRoot
    $checks = @(
        Get-RegistryPolicyCheck -Path $chromePolicyRoot -Name "PasswordManagerEnabled" -ExpectedValue 0 -ExpectedKind DWord
        Get-RegistryPolicyCheck -Path $chromePolicyRoot -Name "SyncDisabled" -ExpectedValue 1 -ExpectedKind DWord
        Get-RegistryPolicyCheck -Path $chromePolicyRoot -Name "BrowserSignin" -ExpectedValue 0 -ExpectedKind DWord
        Get-RegistryPolicyCheck -Path (Join-Path $chromePolicyRoot "ExtensionInstallBlocklist") -Name "1" -ExpectedValue "*" -ExpectedKind String
    )

    Write-Host "Chrome installed: $(if ($chromeInstalled) { 'YES' } else { 'NO' })"
    Write-Host "Machine policy root present: $(if ($policyRootPresent) { 'YES' } else { 'NO' })"
    Write-Host "Mandatory machine scope: YES (HKLM)"
    $checks | Format-Table Policy, Present, MandatoryMachineScope, Expected, Actual, Type, Pass -AutoSize | Out-Host

    if (Test-ChromeOperatorConsolePolicyState -ChromeInstalled $chromeInstalled -PolicyRootPresent $policyRootPresent -Checks $checks) {
        Write-Host "Chrome Operator Console policy validation: PASS"
        return $true
    }

    Write-Host "Chrome Operator Console policy validation: FAIL"
    return $false
}

if ($MyInvocation.InvocationName -ne ".") {
    if (-not (Invoke-ChromeOperatorConsolePolicyValidation)) {
        exit 1
    }
}
