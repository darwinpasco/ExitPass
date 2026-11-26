[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$target = Join-Path (Split-Path -Parent $PSScriptRoot) "Manage-ExitPassEnvironment.ps1"
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($target, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { throw "Lifecycle script does not parse: $($parseErrors[0].Message)" }
foreach ($function in $ast.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst]
}, $true)) {
    Invoke-Expression $function.Extent.Text
}

$script:ExpectedDisplaySerial = "ACE4_2E00_653A_D952"
$script:ExpectedPhysicalSerial = "PF6BJ1WA"
$script:ContainerStopTimeoutSeconds = 0
$script:Passed = [System.Collections.Generic.List[string]]::new()

function Assert-Equal {
    param([object]$Expected, [object]$Actual, [string]$Because)
    if ([string]$Expected -cne [string]$Actual) {
        throw "$Because Expected '$Expected'; found '$Actual'."
    }
}

function Assert-True {
    param([bool]$Value, [string]$Because)
    if (-not $Value) { throw $Because }
}

function Assert-Throws {
    param([scriptblock]$ActionBlock, [string]$Pattern, [string]$Because)
    try { & $ActionBlock }
    catch {
        if ($_.Exception.Message -like $Pattern) { return }
        throw "$Because Unexpected error: $($_.Exception.Message)"
    }
    throw "$Because No error was raised."
}

function Invoke-Case {
    param([string]$Name, [scriptblock]$ActionBlock)
    & $ActionBlock
    $script:Passed.Add($Name)
    Write-Host "PASS $Name"
}

function New-FakeInspect {
    param([string]$State, [bool]$Paused = $false, [bool]$Restarting = $false, [int]$ExitCode = 0)
    [pscustomobject]@{
        Id = "a" * 64
        Image = "sha256:" + ("b" * 64)
        State = [pscustomobject]@{
            Status = $State
            Paused = $Paused
            Restarting = $Restarting
            ExitCode = $ExitCode
        }
    }
}

Invoke-Case "Demo APT uses governed HTTPS endpoint" {
    $demo = Get-EnvironmentDefinition -Name "Demo"
    Assert-Equal "https://localhost:57264" $demo.Apt.CentralUrl "Demo APT must target the governed HTTPS listener."
}

$container = [pscustomobject]@{ Component="CentralPms"; Name="test-central"; Order=1; ReadyUri=$null; Log="docker logs test-central" }
$definition = [pscustomobject]@{ Name="Test"; Containers=@($container); Nodes=@(); Apt=[pscustomobject]@{}; Ngrok=[pscustomobject]@{} }
$script:ContainerState = "paused"
$script:DockerCalls = [System.Collections.Generic.List[string]]::new()
$script:IgnoreStop = $false

function Assert-ContainerOwnership {
    param([object]$Definition, [object]$Container)
    switch ($script:ContainerState) {
        "paused" { return New-FakeInspect -State "running" -Paused $true }
        "restarting" { return New-FakeInspect -State "restarting" -Restarting $true }
        "running" { return New-FakeInspect -State "running" }
        "exited" { return New-FakeInspect -State "exited" }
        default { return New-FakeInspect -State $script:ContainerState }
    }
}

function Invoke-Docker {
    param([string[]]$Arguments)
    $script:DockerCalls.Add(($Arguments -join " "))
    if ($Arguments[0] -eq "unpause") { $script:ContainerState = "running" }
    if ($Arguments[0] -eq "stop" -and -not $script:IgnoreStop) { $script:ContainerState = "exited" }
    return @()
}

Invoke-Case "paused container uses unpause and verified stop" {
    $script:ContainerState = "paused"
    $script:IgnoreStop = $false
    $script:DockerCalls.Clear()
    Stop-OwnedContainer -Definition $definition -Container $container
    Assert-Equal "unpause test-central" $script:DockerCalls[0] "Paused shutdown must use Docker unpause first."
    Assert-Equal "stop --time 30 test-central" $script:DockerCalls[1] "Paused shutdown must use the governed stop path."
    Assert-Equal "exited" $script:ContainerState "Paused shutdown must verify an inactive state."
}

Invoke-Case "restarting container cannot produce false shutdown success" {
    $script:ContainerState = "restarting"
    $script:IgnoreStop = $true
    $script:DockerCalls.Clear()
    Assert-Throws { Stop-OwnedContainer -Definition $definition -Container $container } "*did not reach an inactive state*" "A container still restarting after docker stop must fail shutdown."
}

function Get-ContainerInspect {
    param([string]$Name)
    return $script:StatusInspect
}
function Test-HttpReady { [pscustomobject]@{ Ready=$true; Detail="HTTP 200" } }

Invoke-Case "paused and restarting containers are not classified stopped" {
    $script:StatusInspect = New-FakeInspect -State "running" -Paused $true
    $paused = @(Get-ContainerStatusRows -Definition $definition)[0]
    Assert-Equal "unhealthy" $paused.State "A paused container must be unhealthy."
    $script:StatusInspect = New-FakeInspect -State "restarting" -Restarting $true
    $restarting = @(Get-ContainerStatusRows -Definition $definition)[0]
    Assert-Equal "starting" $restarting.State "A restarting container must be starting."
}

Invoke-Case "APT startup-error UI cannot produce ready" {
    $errorState = Resolve-AptDesktopWindowState -Responding $true -TopLevelTitles @("ExitPass Assisted Payment Terminal") -HasStartupErrorControls $true -HasOperationalShell $false
    Assert-Equal "unhealthy" $errorState.State "Visible Retry/Close startup-error controls must fail readiness."
}

Invoke-Case "APT modal startup-failure dialog cannot produce ready" {
    $errorState = Resolve-AptDesktopWindowState -Responding $true -TopLevelTitles @("ExitPass Assisted Payment Terminal startup failure") -HasStartupErrorControls $false -HasOperationalShell $false
    Assert-Equal "unhealthy" $errorState.State "A modal startup-failure dialog must fail readiness."
}

Invoke-Case "operational APT shell produces ready" {
    $readyState = Resolve-AptDesktopWindowState -Responding $true -TopLevelTitles @("ExitPass Assisted Payment Terminal") -HasStartupErrorControls $false -HasOperationalShell $true
    Assert-Equal "ready" $readyState.State "The visible functional WebView shell must pass readiness."
}

$devDefinitionTemplate = Get-EnvironmentDefinition -Name "Dev"
$missingTrustPath = Join-Path $env:TEMP "exitpass-lifecycle-missing-$([guid]::NewGuid().ToString('N')).bin"
$missingTrustDefinition = [pscustomobject]@{
    Name = "Dev"
    Root = $devDefinitionTemplate.Root
    Network = $devDefinitionTemplate.Network
    Containers = $devDefinitionTemplate.Containers
    Nodes = $devDefinitionTemplate.Nodes
    Apt = [pscustomobject]@{
        Executable = $target
        FrontendConfig = $target
        TrustState = $missingTrustPath
        UiUrl = "http://127.0.0.1:5373"
    }
    Ngrok = $devDefinitionTemplate.Ngrok
}
$script:LaunchAttempted = $false
function Get-AptProcesses { return @() }
function Invoke-WithEnvironmentVariables {
    param([hashtable]$Values, [scriptblock]$ActionBlock)
    $script:LaunchAttempted = $true
    throw "APT launch was unexpectedly reached."
}
function Test-DockerEngine { return $true }
function Get-ContainerStatusRows {
    param([object]$Definition)
    return @($Definition.Containers | ForEach-Object {
        [pscustomobject]@{ Environment=$Definition.Name; Component=$_.Component; State="stopped"; Identifier="test"; Url=$_.ReadyUri; Detail="Stopped" }
    })
}
function Get-NodeStatus {
    param([object]$Definition, [object]$Node)
    return [pscustomobject]@{ Environment=$Definition.Name; Component=$Node.Component; State="stopped"; Identifier="-"; Url=$Node.ReadyUri; Detail="Stopped" }
}
function Get-NgrokStatus {
    param([object]$Definition)
    return [pscustomobject]@{ Environment=$Definition.Name; Component="Ngrok"; State="stopped"; Identifier="-"; Url=""; Detail="Stopped" }
}
function Stop-Ngrok { }
function Stop-NodeComponent { }
function Stop-OwnedContainer { }
function Write-LifecycleLog { }
function Write-EnvironmentStatus { }

Invoke-Case "missing Dev enrollment reports stopped and repeated Stop succeeds" {
    $aptStatus = Get-AptStatus -Definition $missingTrustDefinition
    Assert-Equal "stopped" $aptStatus.State "A non-running APT with missing enrollment must still be stopped."
    Assert-True ($aptStatus.Detail -like "*Enrollment trust state is missing*") "Stopped APT detail must retain the missing-enrollment explanation."
    $status = Get-EnvironmentStatus -Definition $missingTrustDefinition
    Assert-Equal "Stopped" $status.Overall "Every stopped Dev component must aggregate to Stopped despite missing APT enrollment."
    Assert-Equal 3 $status.ExitCode "Stopped Status must retain exit code 3."
    foreach ($attempt in 1..2) {
        $script:ExitCode = -1
        Stop-Environment -Definition $missingTrustDefinition
        Assert-Equal 0 $script:ExitCode "Repeated Stop attempt $attempt must succeed."
    }
}

Invoke-Case "Start still rejects missing Dev enrollment before launch" {
    $script:LaunchAttempted = $false
    Assert-Throws { Start-AptDesktop -Definition $missingTrustDefinition } "*enrollment is incomplete; trust state is missing*" "Start must enforce the trust-state prerequisite."
    Assert-True (-not $script:LaunchAttempted) "Missing trust state must fail before process launch."
}

$script:ConfigurationChecked = $false
function Get-AptProcesses {
    @(
        [pscustomobject]@{ ProcessId=101; CommandLine="`"C:\apt\AssistedPaymentTerminal.Desktop.exe`"" },
        [pscustomobject]@{ ProcessId=102; CommandLine="`"C:\apt\AssistedPaymentTerminal.Desktop.exe`"" }
    )
}
function Assert-AptConfiguration { $script:ConfigurationChecked = $true }
$aptDefinition = [pscustomobject]@{
    Name="Test"
    Apt=[pscustomobject]@{ Executable="C:\apt\AssistedPaymentTerminal.Desktop.exe"; UiUrl="http://127.0.0.1:1" }
}

Invoke-Case "duplicate APT processes prevent another launch" {
    $script:ConfigurationChecked = $false
    Assert-Throws { Start-AptDesktop -Definition $aptDefinition } "*Multiple owned desktop instances*" "Duplicate owned APT processes must block before launch."
    Assert-True (-not $script:ConfigurationChecked) "Duplicate APT detection must stop before configuration and launch work."
}

function Test-DockerEngine { return $script:DockerAvailable }
function Get-ContainerStatusRows { return @($script:ContainerRows) }
function Get-NodeStatus { throw "No node rows are expected in focused aggregate tests." }
function Get-AptStatus { return $script:AptRow }
function Get-NgrokStatus { return $script:NgrokRow }

$definition.Name = "Dev"
$script:AptRow = [pscustomobject]@{ Environment="Dev"; Component="AptDesktop"; State="stopped"; Identifier="-"; Url=""; Detail="Stopped" }
$script:NgrokRow = [pscustomobject]@{ Environment="Dev"; Component="Ngrok"; State="stopped"; Identifier="-"; Url=""; Detail="Stopped" }

Invoke-Case "missing required container produces partial" {
    $script:DockerAvailable = $true
    $script:ContainerRows = @([pscustomobject]@{ Environment="Dev"; Component="CentralPms"; State="blocked"; Identifier="missing"; Url=""; Detail="Required container is missing." })
    $status = Get-EnvironmentStatus -Definition $definition
    Assert-Equal "Partial" $status.Overall "A missing required container must not look stopped."
    Assert-Equal 2 $status.ExitCode "A missing required container must use the partial/blocked exit code."
}

Invoke-Case "unavailable Docker state produces partial" {
    $script:DockerAvailable = $false
    $status = Get-EnvironmentStatus -Definition $definition
    Assert-Equal "Partial" $status.Overall "Unavailable Docker state must not look stopped."
    Assert-Equal "blocked" $status.Rows[0].State "Unavailable Docker state must be explicit on required containers."
}

Invoke-Case "same-environment shared-resource owner remains a conflict" {
    $script:DockerAvailable = $true
    $script:ContainerRows = @([pscustomobject]@{ Environment="Dev"; Component="CentralPms"; State="stopped"; Identifier="abc"; Url=""; Detail="Stopped" })
    $script:AptRow = [pscustomobject]@{ Environment="Dev"; Component="AptDesktop"; State="blocked"; Identifier="PID 11"; Url=""; Detail="Shared PITX-APT-01 is owned by Dev. Stop command: omitted" }
    $script:NgrokRow = [pscustomobject]@{ Environment="Dev"; Component="Ngrok"; State="blocked"; Identifier="PID 12"; Url=""; Detail="Shared ngrok admin port 4040 is owned by ExitPass environment Dev (PID 12). Log: test" }
    $status = Get-EnvironmentStatus -Definition $definition
    Assert-Equal "Partial" $status.Overall "Dev resources reported as owned by Dev must remain a conflict."
    Assert-Equal 2 $status.ExitCode "Same-environment ownership conflict must use the partial/blocked exit code."
}

Invoke-Case "fully stopped Dev remains stopped with UAT shared owners" {
    $script:AptRow = [pscustomobject]@{ Environment="Dev"; Component="AptDesktop"; State="blocked"; Identifier="PID 11"; Url=""; Detail="Shared PITX-APT-01 is owned by UAT. Stop command: omitted" }
    $script:NgrokRow = [pscustomobject]@{ Environment="Dev"; Component="Ngrok"; State="blocked"; Identifier="PID 12"; Url=""; Detail="Shared ngrok admin port 4040 is owned by ExitPass environment UAT (PID 12). Log: test" }
    $status = Get-EnvironmentStatus -Definition $definition
    Assert-Equal "Stopped" $status.Overall "External shared APT/ngrok ownership must not make a stopped target partial."
    Assert-Equal 3 $status.ExitCode "A confirmed stopped target must retain the stopped status code."
}

Invoke-Case "unrelated ngrok admin-port owner remains partial" {
    $script:NgrokRow = [pscustomobject]@{ Environment="Dev"; Component="Ngrok"; State="blocked"; Identifier="PID 99"; Url=""; Detail="Admin port 4040 is owned by other.exe, but no other ExitPass environment ownership was verified. Log: test" }
    $status = Get-EnvironmentStatus -Definition $definition
    Assert-Equal "Partial" $status.Overall "An unrelated admin-port owner must not be treated as a valid shared ExitPass owner."
    Assert-Equal 2 $status.ExitCode "An unrelated admin-port owner must use the partial/blocked exit code."
}

Write-Host "Focused lifecycle tests passed: $($script:Passed.Count)"
