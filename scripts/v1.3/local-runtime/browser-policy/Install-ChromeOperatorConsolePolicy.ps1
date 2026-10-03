#Requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Administrator rights are required to configure mandatory machine-level Chrome policy."
}

$chromePolicyRoot = "HKLM:\SOFTWARE\Policies\Google\Chrome"
$dwordPolicies = [ordered]@{
    PasswordManagerEnabled = 0
    SyncDisabled = 1
    BrowserSignin = 0
}

function Set-RequiredRegistryValue {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][ValidateSet("DWord", "String")][string]$PropertyType
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, "Create Chrome machine-policy key")) {
            New-Item -Path $Path -Force | Out-Null
        } else {
            return [pscustomobject]@{ Policy = $Name; Status = "PLANNED"; Value = $Value }
        }
    }

    $existing = Get-ItemProperty -LiteralPath $Path -Name $Name -ErrorAction SilentlyContinue
    $existingValue = if ($null -eq $existing) { $null } else { $existing.$Name }
    $existingKind = if ($null -eq $existing) {
        $null
    } else {
        (Get-Item -LiteralPath $Path).GetValueKind($Name).ToString()
    }

    if ($existingValue -eq $Value -and $existingKind -eq $PropertyType) {
        return [pscustomobject]@{ Policy = $Name; Status = "UNCHANGED"; Value = $Value }
    }

    if ($PSCmdlet.ShouldProcess("$Path\$Name", "Set mandatory Chrome policy to '$Value' ($PropertyType)")) {
        New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType $PropertyType -Force | Out-Null
        return [pscustomobject]@{ Policy = $Name; Status = "UPDATED"; Value = $Value }
    }

    return [pscustomobject]@{ Policy = $Name; Status = "PLANNED"; Value = $Value }
}

$results = foreach ($policy in $dwordPolicies.GetEnumerator()) {
    Set-RequiredRegistryValue -Path $chromePolicyRoot -Name $policy.Key -Value $policy.Value -PropertyType DWord
}

$extensionBlocklistPath = Join-Path $chromePolicyRoot "ExtensionInstallBlocklist"
$results += Set-RequiredRegistryValue -Path $extensionBlocklistPath -Name "1" -Value "*" -PropertyType String

$results | Format-Table -AutoSize
Write-Host "Chrome Operator Console machine-policy configuration completed. Fully restart Chrome before validation."
