[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Join-Path (Split-Path -Parent $PSScriptRoot) 'New-ExitPassDevParkingSession.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors)
if (@($parseErrors).Count -gt 0) { throw "Dev parking-session helper does not parse: $($parseErrors[0].Message)" }
foreach ($function in $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)) {
    Invoke-Expression $function.Extent.Text
}

$environmentName = 'DEV'
$composeProject = 'exitpass-dev-20261122'
$networkName = 'exitpass-dev-20261122-network'
$expectedParkingLot = '1'
$invariant = [Globalization.CultureInfo]::InvariantCulture
$script:Passed = [Collections.Generic.List[string]]::new()

function Assert-Equal([object]$Expected, [object]$Actual, [string]$Because) {
    if ([string]$Expected -cne [string]$Actual) { throw "$Because Expected '$Expected'; found '$Actual'." }
}

function Assert-True([bool]$Value, [string]$Because) {
    if (-not $Value) { throw $Because }
}

function Assert-Throws([scriptblock]$ActionBlock, [string]$Pattern, [string]$Because) {
    try { & $ActionBlock }
    catch {
        if ($_.Exception.Message -like $Pattern) { return }
        throw "$Because Unexpected error: $($_.Exception.Message)"
    }
    throw "$Because No error was raised."
}

function Invoke-Case([string]$Name, [scriptblock]$ActionBlock) {
    & $ActionBlock
    $script:Passed.Add($Name)
    Write-Host "PASS $Name"
}

function New-LabelObject([string]$Project, [string]$Service) {
    $labels = [pscustomobject]@{}
    $labels | Add-Member -NotePropertyName 'com.docker.compose.project' -NotePropertyValue $Project
    $labels | Add-Member -NotePropertyName 'com.docker.compose.service' -NotePropertyValue $Service
    return $labels
}

function New-NetworkObject([string]$Alias, [switch]$AddForeignNetwork) {
    $networks = [pscustomobject]@{}
    $networks | Add-Member -NotePropertyName $networkName -NotePropertyValue ([pscustomobject]@{ Aliases = @('container-name', $Alias) })
    if ($AddForeignNetwork) {
        $networks | Add-Member -NotePropertyName 'unexpected-network' -NotePropertyValue ([pscustomobject]@{ Aliases = @('unexpected') })
    }
    return $networks
}

function New-FakeContainer([string]$Project = $composeProject, [string]$Alias = 'hikcentral', [string]$Service = 'hikcentral', [switch]$AddForeignNetwork) {
    return [pscustomobject]@{
        Name = '/exitpass-dev-20261122-hikcentral'
        State = [pscustomobject]@{ Running = $true }
        Config = [pscustomobject]@{ Labels = (New-LabelObject $Project $Service) }
        NetworkSettings = [pscustomobject]@{ Networks = (New-NetworkObject $Alias -AddForeignNetwork:$AddForeignNetwork) }
    }
}

Invoke-Case 'PowerShell 5.1-compatible public parameters remain present' {
    $parameterNames = @($ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
    Assert-ExactStringSet @('Ticket', 'Plate', 'VehicleType', 'Fee', 'EntryTime', 'PreflightOnly') $parameterNames 'Public helper parameters'
    $entry = @($ast.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'EntryTime' })[0]
    Assert-Equal 'DateTimeOffset' $entry.StaticType.Name 'EntryTime must remain strongly typed.'
    $source = Get-Content -LiteralPath $target -Raw
    Assert-True ($source -match '\[string\]\$VehicleType\s*=\s*''CAR''') 'VehicleType must retain the CAR default.'
    Assert-True ($source -match '\[decimal\]\$Fee\s*=\s*50\.00') 'Fee must retain the 50.00 default.'
}

Invoke-Case 'approved Dev ownership is accepted' {
    Assert-ContainerIdentity (New-FakeContainer) 'hikcentral' 'hikcentral'
}

Invoke-Case 'wrong Compose project is refused' {
    Assert-Throws { Assert-ContainerIdentity (New-FakeContainer -Project 'exitpass-clean-november-uat') 'hikcentral' 'hikcentral' } '*not owned by the approved Dev Compose project/service*' 'A UAT-owned target must be refused.'
}

Invoke-Case 'unavailable Dev target is refused' {
    $stopped = New-FakeContainer
    $stopped.State.Running = $false
    Assert-Throws { Assert-ContainerIdentity $stopped 'hikcentral' 'hikcentral' } '*is not running*' 'A stopped required Dev target must be refused.'
}

Invoke-Case 'unexpected network is refused' {
    Assert-Throws { Assert-ContainerIdentity (New-FakeContainer -AddForeignNetwork) 'hikcentral' 'hikcentral' } '*attached to an unexpected network*' 'A cross-environment network attachment must be refused.'
}

Invoke-Case 'exact persistence set accepts reordering' {
    Assert-ExactStringSet @('a.json', 'b.json') @('b.json', 'a.json') 'Test mapping set'
}

Invoke-Case 'persistence mismatch is refused' {
    Assert-Throws { Assert-ExactStringSet @('a.json', 'b.json') @('a.json', 'c.json') 'Test mapping set' } '*does not match the approved Dev runtime*' 'A missing or unexpected persisted mapping must fail closed.'
}

Invoke-Case 'new session identifiers are accepted' {
    $inventory = @([pscustomobject]@{
        personInfo = [pscustomobject]@{ cardNum = 'EXISTING-TICKET' }
        carInfo = [pscustomobject]@{ plateLicense = 'EXISTING-PLATE' }
    })
    $mappings = @([pscustomobject]@{ id = 'm1'; name = 'existing'; response = [pscustomobject]@{} })
    Assert-SessionDoesNotExist $mappings $inventory 'NEW-TICKET' 'NEW-PLATE'
}

Invoke-Case 'duplicate ticket is refused without overwrite' {
    $inventory = @([pscustomobject]@{
        personInfo = [pscustomobject]@{ cardNum = 'EXISTING-TICKET' }
        carInfo = [pscustomobject]@{ plateLicense = 'EXISTING-PLATE' }
    })
    Assert-Throws { Assert-SessionDoesNotExist @() $inventory 'EXISTING-TICKET' 'NEW-PLATE' } '*Existing sessions are never overwritten*' 'A duplicate ticket must fail before persistence.'
}

Invoke-Case 'duplicate plate in a mapping is refused' {
    $mapping = [pscustomobject]@{ id = 'm1'; request = [pscustomobject]@{ bodyPatterns = @([pscustomobject]@{ equalToJson = '{"plateLicense":"EXISTING-PLATE"}' }) } }
    Assert-Throws { Assert-SessionDoesNotExist @($mapping) @() 'NEW-TICKET' 'EXISTING-PLATE' } '*Existing sessions are never overwritten*' 'A duplicate plate must fail before persistence.'
}

Invoke-Case 'mapping filename is deterministic and bounded' {
    Assert-Equal 'exitpass-mock-confirm-25741195933-11111111-1111-1111-1111-111111111111.json' (New-MappingFileName '25741195933' 'confirm' '11111111-1111-1111-1111-111111111111') 'Durable mapping filenames must identify the session and mapping.'
}

Invoke-Case 'Dev-only durable targets are explicit' {
    $source = Get-Content -LiteralPath $target -Raw
    foreach ($required in @(
        "`$environmentRoot = 'D:\SourceCodes\ExitPass.dev'",
        "`$wireMockVolume = 'exitpass-dev-20261122-wiremock'",
        "D:\SourceCodes\ExitPass.dev\data\wiremock\mappings",
        "D:\SourceCodes\ExitPass.dev\data\wiremock-runtime\mappings",
        "`$centralPmsUrl = 'http://127.0.0.1:58165'"
    )) {
        Assert-True ($source.Contains($required)) "Missing explicit Dev target: $required"
    }
    Assert-True (-not $source.Contains('clean-november-uat')) 'The helper must not embed the UAT runtime.'
    Assert-True (-not $source.Contains('demo-rc1')) 'The helper must not embed the Demo runtime.'
}

Write-Host "PASS total=$($script:Passed.Count)"
