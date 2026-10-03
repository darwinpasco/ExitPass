#Requires -Version 5.1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$installerPath = Join-Path $PSScriptRoot "Install-ChromeOperatorConsolePolicy.ps1"
$validatorPath = Join-Path $PSScriptRoot "Test-ChromeOperatorConsolePolicy.ps1"
$installerSource = Get-Content -LiteralPath $installerPath -Raw
$validatorSource = Get-Content -LiteralPath $validatorPath -Raw

foreach ($path in @($installerPath, $validatorPath)) {
    $tokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "PowerShell syntax validation failed for $path`: $($parseErrors.Message -join '; ')"
    }
}

$requiredInstallerFragments = @(
    "WindowsBuiltInRole]::Administrator",
    'HKLM:\SOFTWARE\Policies\Google\Chrome',
    "PasswordManagerEnabled = 0",
    "SyncDisabled = 1",
    "BrowserSignin = 0",
    '-Name "1" -Value "*" -PropertyType String',
    "existingValue -eq `$Value"
)
foreach ($fragment in $requiredInstallerFragments) {
    if (-not $installerSource.Contains($fragment)) {
        throw "Installer validation failed: required fragment '$fragment' is missing."
    }
}

$forbiddenInstallerCommands = @("Remove-Item", "Remove-ItemProperty", "Clear-Item", "Clear-ItemProperty")
foreach ($command in $forbiddenInstallerCommands) {
    if ($installerSource -match "(?m)^\s*$([regex]::Escape($command))\b") {
        throw "Installer validation failed: forbidden command '$command' is present."
    }
}

$tokens = $null
$parseErrors = $null
$validatorAst = [Management.Automation.Language.Parser]::ParseInput($validatorSource, [ref]$tokens, [ref]$parseErrors)
$validatorCommands = $validatorAst.FindAll({ param($node) $node -is [Management.Automation.Language.CommandAst] }, $true) |
    ForEach-Object { $_.GetCommandName() } |
    Where-Object { $_ }
$mutatingCommands = @("New-Item", "New-ItemProperty", "Set-Item", "Set-ItemProperty", "Remove-Item", "Remove-ItemProperty", "Clear-Item", "Clear-ItemProperty")
if (@($validatorCommands | Where-Object { $_ -in $mutatingCommands }).Count -gt 0) {
    throw "Validator validation failed: the read-only validator contains a mutating registry command."
}

. $validatorPath
$passingChecks = @(
    [pscustomobject]@{ Pass = $true },
    [pscustomobject]@{ Pass = $true },
    [pscustomobject]@{ Pass = $true },
    [pscustomobject]@{ Pass = $true }
)
$missingChecks = @($passingChecks[0..2] + [pscustomobject]@{ Pass = $false })
$malformedChecks = @([pscustomobject]@{ Pass = $false })

if (-not (Test-ChromeOperatorConsolePolicyState -ChromeInstalled $true -PolicyRootPresent $true -Checks $passingChecks)) {
    throw "Synthetic complete policy state should pass."
}
if (Test-ChromeOperatorConsolePolicyState -ChromeInstalled $true -PolicyRootPresent $true -Checks $missingChecks) {
    throw "Synthetic missing policy state should fail."
}
if (Test-ChromeOperatorConsolePolicyState -ChromeInstalled $true -PolicyRootPresent $true -Checks $malformedChecks) {
    throw "Synthetic malformed policy state should fail."
}

$forbiddenProfileReferences = @("Login Data", "Web Data", "User Data", "Cookies", "Local State", "sqlite")
foreach ($reference in $forbiddenProfileReferences) {
    if ($installerSource.Contains($reference) -or $validatorSource.Contains($reference)) {
        throw "Policy artifacts must not access Chrome profile data ('$reference')."
    }
}

Write-Host "Chrome Operator Console policy artifact validation: PASS"
