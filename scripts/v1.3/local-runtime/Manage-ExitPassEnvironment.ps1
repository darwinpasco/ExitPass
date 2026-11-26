[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Dev", "UAT", "Demo")]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Start", "Stop", "Status")]
    [string]$Action
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:StateRoot = "D:\SourceCodes\ExitPass.local\repeatable-runtime\managed-environments"
$script:GlobalNgrokConfig = Join-Path $env:LOCALAPPDATA "ngrok\ngrok.yml"
$script:DockerDesktop = "C:\Program Files\Docker\Docker\Docker Desktop.exe"
$script:ExpectedAptUser = "PPG-DAP25\darwi"
$script:ExpectedPhysicalSerial = "PF6BJ1WA"
$script:ExpectedDisplaySerial = "ACE4_2E00_653A_D952"
$script:ExitCode = 0

function New-ContainerDefinition {
    param(
        [string]$Component,
        [string]$Name,
        [int]$Order,
        [string]$ReadyUri = $null
    )

    [pscustomobject]@{
        Component = $Component
        Name = $Name
        Order = $Order
        ReadyUri = $ReadyUri
        Log = "docker logs $Name"
    }
}

function New-NodeDefinition {
    param(
        [string]$Component,
        [int]$Port,
        [string]$ReadyUri,
        [string]$NodeScript,
        [string]$WorkingDirectory,
        [string[]]$Arguments,
        [string[]]$Markers,
        [hashtable]$EnvironmentVariables,
        [string]$LogRoot
    )

    [pscustomobject]@{
        Component = $Component
        Port = $Port
        ReadyUri = $ReadyUri
        NodeScript = $NodeScript
        WorkingDirectory = $WorkingDirectory
        Arguments = $Arguments
        Markers = $Markers
        EnvironmentVariables = $EnvironmentVariables
        StdOut = Join-Path $LogRoot "$Component.stdout.log"
        StdErr = Join-Path $LogRoot "$Component.stderr.log"
    }
}

function Get-EnvironmentDefinition {
    param([string]$Name)

    $commonSiteId = "2d1dcdf8-f563-537c-8542-0bde7cc9da97"
    $commonSiteGroupId = "a6dbadf6-68b5-5bed-a7e0-a75faee70841"
    $commonPosServerId = "ed280e7d-86eb-435f-a730-b59c82338493"
    $commonServiceIdentityId = "337609f0-efe7-46a0-a7e9-3fe59feca401"
    $aptVite = "D:\SourceCodes\ExitPass-AssistedPaymentTerminal\.codex-runtime\release-fiscal-identity-apt-1a7226\node_modules\vite\bin\vite.js"
    $webPaySource = "D:\SourceCodes\ExitPass\worktrees\release-metadata-deploy-20261112\webpay\src\Services\WebPayUi"
    $webPayVite = Join-Path $webPaySource "node_modules\vite\bin\vite.js"
    $operatorSource = "D:\SourceCodes\ExitPass\.codex-runtime\deploy-pr849-2f7cfbb3\src\Services\OperatorConsoleUi"
    $operatorVite = Join-Path $operatorSource "node_modules\vite\bin\vite.js"
    $managementSource = "D:\SourceCodes\ExitPass\.codex-runtime\release-beneficiary-intake-management-b0f423fc"
    $managementVite = Join-Path $managementSource "node_modules\vite\bin\vite.js"

    switch ($Name) {
        "Dev" {
            $root = "D:\SourceCodes\ExitPass.dev"
            $logs = Join-Path $root "logs"
            $vite = Join-Path $root "build-src\management-platform-b0f423fc\node_modules\vite\bin\vite.js"
            $nodeCa = Join-Path $root "certs\dev-root-ca.crt"
            $containers = @(
                (New-ContainerDefinition "CentralDb" "exitpass-dev-20261122-central-db" 10),
                (New-ContainerDefinition "PosDb" "exitpass-dev-20261122-pos-db" 11),
                (New-ContainerDefinition "Minio" "exitpass-dev-20261122-minio" 20),
                (New-ContainerDefinition "ClamAv" "exitpass-dev-20261122-clamav" 21),
                (New-ContainerDefinition "HikCentral" "exitpass-dev-20261122-hikcentral" 30 "http://127.0.0.1:53477/__admin/mappings"),
                (New-ContainerDefinition "SiteAdapter" "exitpass-dev-20261122-site-adapter" 40 "http://127.0.0.1:53648/health/ready"),
                (New-ContainerDefinition "PosServer" "exitpass-dev-20261122-pos" 50 "http://127.0.0.1:58167/health/ready"),
                (New-ContainerDefinition "CentralPms" "exitpass-dev-20261122-central" 60 "http://127.0.0.1:58165/health/ready"),
                (New-ContainerDefinition "PaymentOrchestrator" "exitpass-dev-20261122-payment" 70 "http://127.0.0.1:58063/health/ready"),
                (New-ContainerDefinition "Ingress" "exitpass-dev-20261122-ingress" 80)
            )
            $nodes = @(
                (New-NodeDefinition "AptUi" 5373 "http://127.0.0.1:5373/" $vite $root @("preview", "--config", (Join-Path $root "config\apt-vite.config.mjs"), "--host", "127.0.0.1", "--port", "5373", "--strictPort", "--outDir", (Join-Path $root "apt\frontend-wwwroot")) @((Join-Path $root "config\apt-vite.config.mjs"), "--port 5373") @{ NODE_EXTRA_CA_CERTS = $nodeCa } $logs),
                (New-NodeDefinition "WebPay" 5374 "http://127.0.0.1:5374/webpay-app/" $vite $root @("preview", "--config", (Join-Path $root "config\webpay-vite.config.mjs"), "--host", "127.0.0.1", "--port", "5374", "--strictPort", "--outDir", (Join-Path $root "frontends\webpay")) @((Join-Path $root "config\webpay-vite.config.mjs"), "--port 5374") @{ NODE_EXTRA_CA_CERTS = $nodeCa } $logs),
                (New-NodeDefinition "OperatorConsole" 5375 "http://127.0.0.1:5375/" $vite $root @("preview", "--config", (Join-Path $root "config\operator-vite.config.mjs"), "--host", "127.0.0.1", "--port", "5375", "--strictPort", "--outDir", (Join-Path $root "frontends\operator-console")) @((Join-Path $root "config\operator-vite.config.mjs"), "--port 5375") @{ NODE_EXTRA_CA_CERTS = $nodeCa } $logs),
                (New-NodeDefinition "ManagementPlatform" 5378 "http://127.0.0.1:5378/management-platform/" $vite $root @("preview", "--config", (Join-Path $root "config\management-vite.config.mjs"), "--host", "127.0.0.1", "--port", "5378", "--strictPort", "--outDir", (Join-Path $root "frontends\management-platform")) @((Join-Path $root "config\management-vite.config.mjs"), "--port 5378") @{ NODE_EXTRA_CA_CERTS = $nodeCa } $logs)
            )
            $aptRoot = Join-Path $root "apt"
            $apt = [pscustomobject]@{
                Executable = Join-Path $aptRoot "release\AssistedPaymentTerminal.Desktop.exe"
                Release = Join-Path $aptRoot "release"
                FrontendConfig = Join-Path $aptRoot "frontend-wwwroot\apt-config.json"
                TrustState = Join-Path $aptRoot "DeviceTrust\state.bin"
                Database = Join-Path $aptRoot "LocalOperations\cash-journal.db"
                HumanSession = Join-Path $aptRoot "HumanSession\credential.bin"
                WebView = Join-Path $aptRoot "WebView2"
                CentralUrl = "https://localhost:58164"
                UiUrl = "http://127.0.0.1:5373"
                SiteId = $commonSiteId
                SiteGroupId = $commonSiteGroupId
                PosServerId = $commonPosServerId
                ServiceIdentityId = $commonServiceIdentityId
                TerminalId = "PITX-APT-01"
                DisplayName = "PITX Level 3 APT [DEV]"
                Log = Join-Path $logs "apt-desktop.log"
            }
            return [pscustomobject]@{
                Name = "Dev"; Root = $root; Network = "exitpass-dev-20261122-network"; Containers = $containers; Nodes = $nodes; Apt = $apt
                Ngrok = [pscustomobject]@{
                    Config = Join-Path $root "config\ngrok.yml"; AdminPort = 4043; Log = Join-Path $logs "ngrok.log"
                    PublicUrls = @("https://dev-webpay.ngrok.dev/webpay-app/", "https://dev-operator-console.ngrok.dev/", "https://dev-management-platform.ngrok.dev/management-platform/")
                    ExtraMarkers = @("dev-webpay.ngrok.dev", "dev-operator-console.ngrok.dev", "dev-management-platform.ngrok.dev")
                }
            }
        }
        "UAT" {
            $root = "D:\SourceCodes\ExitPass.local\clean-november-uat-20261106-222543"
            $logs = Join-Path $root "logs"
            $config = Join-Path $root "config"
            $containers = @(
                (New-ContainerDefinition "CentralDb" "exitpass-clean-november-central-db" 10),
                (New-ContainerDefinition "PosDb" "exitpass-clean-november-pos-db" 11),
                (New-ContainerDefinition "Minio" "exitpass-clean-november-minio" 20),
                (New-ContainerDefinition "ClamAv" "exitpass-clean-november-clamav" 21),
                (New-ContainerDefinition "HikCentral" "exitpass-clean-november-hikcentral" 30 "http://127.0.0.1:52477/__admin/mappings"),
                (New-ContainerDefinition "SiteAdapter" "exitpass-clean-november-site-adapter" 40 "http://127.0.0.1:52648/health/ready"),
                (New-ContainerDefinition "PosServer" "exitpass-clean-november-pos" 50 "http://127.0.0.1:57167/health/ready"),
                (New-ContainerDefinition "CentralPms" "exitpass-clean-november-central" 60 "http://127.0.0.1:57165/health/ready"),
                (New-ContainerDefinition "PaymentOrchestrator" "exitpass-clean-november-payment-orchestrator" 70 "http://127.0.0.1:57063/health/ready")
            )
            $aptDeployment = Join-Path $root "apt\governed-9a95baabf8e8"
            $webPayOut = Join-Path $root "webpay\release-a3bbfb1fe776\dist"
            $nodes = @(
                (New-NodeDefinition "AptUi" 5273 "http://127.0.0.1:5273/" $aptVite (Split-Path -Parent $aptVite) @("preview", "--host", "127.0.0.1", "--port", "5273", "--strictPort", "--outDir", (Join-Path $aptDeployment "frontend-wwwroot")) @((Join-Path $aptDeployment "frontend-wwwroot"), "--port 5273") @{} $logs),
                (New-NodeDefinition "WebPay" 5274 "http://127.0.0.1:5274/webpay-app/" $webPayVite $webPaySource @("preview", "--host", "127.0.0.1", "--port", "5274", "--strictPort", "--outDir", $webPayOut) @($webPayOut, "--port 5274") @{ VITE_WEBPAY_API_PROXY_TARGET = "http://127.0.0.1:57063"; VITE_WEBPAY_CENTRAL_PMS_PROXY_TARGET = "https://localhost:57164"; VITE_WEBPAY_DEFAULT_SITE_GROUP_ID = $commonSiteGroupId; VITE_WEBPAY_DEFAULT_SITE_ID = $commonSiteId; VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID = "afdefaab-6be4-6b25-8f3f-3ad8309662e8"; VITE_WEBPAY_API_BASE_URL = "" } $logs),
                (New-NodeDefinition "OperatorConsole" 5275 "http://127.0.0.1:5275/" $operatorVite $operatorSource @("preview", "--config", "D:\SourceCodes\ExitPass\.codex-runtime\clean-november-operator-vite.config.mjs", "--host", "127.0.0.1", "--port", "5275", "--strictPort", "--outDir", (Join-Path $operatorSource "dist")) @("clean-november-operator-vite.config.mjs", "--port 5275") @{} $logs),
                (New-NodeDefinition "ManagementPlatform" 5276 "http://127.0.0.1:5276/management-platform/" $managementVite $managementSource @("preview", "--config", (Join-Path $config "management-platform-clean-november-vite.config.mjs"), "--host", "127.0.0.1", "--port", "5276", "--strictPort", "--outDir", (Join-Path $managementSource "dist")) @((Join-Path $config "management-platform-clean-november-vite.config.mjs"), "--port 5276") @{} $logs)
            )
            $apt = [pscustomobject]@{
                Executable = Join-Path $aptDeployment "release\AssistedPaymentTerminal.Desktop.exe"
                Release = Join-Path $aptDeployment "release"
                FrontendConfig = Join-Path $aptDeployment "frontend-wwwroot\apt-config.json"
                TrustState = Join-Path $root "apt\DeviceTrust\state.bin"
                Database = Join-Path $root "apt\cp4-fresh\LocalOperations\cash-journal.db"
                HumanSession = Join-Path $root "apt\cp4-fresh\HumanSession\credential.bin"
                WebView = Join-Path $root "apt\cp4-fresh\WebView2"
                CentralUrl = "https://localhost:57164"
                UiUrl = "http://127.0.0.1:5273"
                SiteId = $commonSiteId
                SiteGroupId = $commonSiteGroupId
                PosServerId = $commonPosServerId
                ServiceIdentityId = $commonServiceIdentityId
                TerminalId = "PITX-APT-01"
                DisplayName = "PITX Level 3 APT [UAT]"
                Log = Join-Path $logs "apt-desktop.log"
            }
            return [pscustomobject]@{
                Name = "UAT"; Root = $root; Network = "exitpass-clean-november-uat"; Containers = $containers; Nodes = $nodes; Apt = $apt
                Ngrok = [pscustomobject]@{
                    Config = Join-Path $config "ngrok-clean-november-uat.yml"; AdminPort = 4040; Log = Join-Path $logs "ngrok-clean-november-uat.log"
                    PublicUrls = @("https://test1-webpay.ngrok.dev/webpay-app/", "https://test1-operator-console.ngrok.dev/", "https://test1-management-platform.ngrok.dev/management-platform/")
                    ExtraMarkers = @()
                }
            }
        }
        "Demo" {
            $root = "D:\SourceCodes\ExitPass.local\demo-rc1-20261112"
            $logs = Join-Path $root "logs"
            $config = Join-Path $root "config"
            $containers = @(
                (New-ContainerDefinition "CentralDb" "exitpass-demo-rc1-central-db" 10),
                (New-ContainerDefinition "PosDb" "exitpass-demo-rc1-pos-db" 11),
                # The existing POS container has the reserved 172.28.0.4 address. Start it
                # before dynamically addressed containers can claim that address.
                (New-ContainerDefinition "PosServer" "exitpass-demo-rc1-pos" 12 "http://127.0.0.1:57267/health/ready"),
                (New-ContainerDefinition "RabbitMq" "exitpass-demo-rc1-rabbitmq" 13),
                (New-ContainerDefinition "Minio" "exitpass-demo-rc1-minio" 20),
                (New-ContainerDefinition "ClamAv" "exitpass-demo-rc1-clamav" 21),
                (New-ContainerDefinition "HikCentral" "exitpass-demo-rc1-hikcentral" 30 "http://127.0.0.1:57477/__admin/mappings"),
                (New-ContainerDefinition "SiteAdapter" "exitpass-demo-rc1-site-adapter" 40 "http://127.0.0.1:57648/health/ready"),
                (New-ContainerDefinition "CentralPms" "exitpass-demo-rc1-central" 60 "http://127.0.0.1:57265/health/ready"),
                (New-ContainerDefinition "PaymentOrchestrator" "exitpass-demo-rc1-payment-orchestrator" 70 "http://127.0.0.1:57163/health/ready")
            )
            $aptDeployment = Join-Path $root "apt\governed-9a95baabf8e8-demo-20261113-171557"
            $webPayOut = Join-Path $root "runtime\webpay-dist"
            $nodes = @(
                (New-NodeDefinition "AptUi" 57173 "http://127.0.0.1:57173/" $aptVite (Split-Path -Parent $aptVite) @("preview", "--host", "127.0.0.1", "--port", "57173", "--strictPort", "--outDir", (Join-Path $aptDeployment "frontend-wwwroot")) @((Join-Path $aptDeployment "frontend-wwwroot"), "--port 57173") @{} $logs),
                (New-NodeDefinition "WebPay" 57174 "http://127.0.0.1:57174/webpay-app/" $webPayVite $webPaySource @("preview", "--host", "127.0.0.1", "--port", "57174", "--strictPort", "--outDir", $webPayOut) @($webPayOut, "--port 57174") @{ VITE_WEBPAY_API_PROXY_TARGET = "http://127.0.0.1:57163"; VITE_WEBPAY_CENTRAL_PMS_PROXY_TARGET = "https://localhost:57264" } $logs),
                (New-NodeDefinition "OperatorConsole" 57175 "http://127.0.0.1:57175/" $operatorVite $operatorSource @("preview", "--config", (Join-Path $config "operator-vite.config.mjs"), "--host", "127.0.0.1", "--port", "57175", "--strictPort", "--outDir", (Join-Path $root "runtime\operator-dist")) @((Join-Path $config "operator-vite.config.mjs"), "--port 57175") @{} $logs),
                (New-NodeDefinition "ManagementPlatform" 57178 "http://127.0.0.1:57178/management-platform/" $managementVite $managementSource @("preview", "--config", (Join-Path $config "management-vite.config.mjs"), "--host", "127.0.0.1", "--port", "57178", "--strictPort", "--outDir", (Join-Path $root "runtime\management-dist")) @((Join-Path $config "management-vite.config.mjs"), "--port 57178") @{} $logs)
            )
            $apt = [pscustomobject]@{
                Executable = Join-Path $aptDeployment "release\AssistedPaymentTerminal.Desktop.exe"
                Release = Join-Path $aptDeployment "release"
                FrontendConfig = Join-Path $aptDeployment "frontend-wwwroot\apt-config.json"
                TrustState = Join-Path $root "apt\DeviceTrust\state.bin"
                Database = Join-Path $root "apt\LocalOperations\cash-journal.db"
                HumanSession = Join-Path $root "apt\HumanSession\credential.bin"
                WebView = Join-Path $root "apt\launcher-rc1-demo\WebView2"
                CentralUrl = "http://127.0.0.1:57265"
                UiUrl = "http://127.0.0.1:57173"
                SiteId = $commonSiteId
                SiteGroupId = $commonSiteGroupId
                PosServerId = $commonPosServerId
                ServiceIdentityId = $commonServiceIdentityId
                TerminalId = "PITX-APT-01"
                DisplayName = "PITX Level 3 APT [DEMO]"
                Log = Join-Path $logs "apt-desktop.log"
            }
            return [pscustomobject]@{
                Name = "Demo"; Root = $root; Network = "exitpass-demo-rc1"; Containers = $containers; Nodes = $nodes; Apt = $apt
                Ngrok = [pscustomobject]@{
                    Config = Join-Path $config "ngrok-demo-rc1.yml"; AdminPort = 4040; Log = Join-Path $logs "ngrok-demo-rc1.log"
                    PublicUrls = @("https://demo-webpay.ngrok.dev/webpay-app/", "https://demo-operator-console.ngrok.dev/", "https://demo-management-platform.ngrok.dev/management-platform/")
                    ExtraMarkers = @()
                }
            }
        }
    }
}

function Write-LifecycleLog {
    param([object]$Definition, [string]$Message)

    $folder = Join-Path $script:StateRoot $Definition.Name
    [void](New-Item -ItemType Directory -Path $folder -Force)
    $line = "{0} {1}" -f ([DateTimeOffset]::Now.ToString("o")), $Message
    Add-Content -LiteralPath (Join-Path $folder "lifecycle.log") -Value $line -Encoding UTF8
}

function Invoke-Docker {
    param([string[]]$Arguments, [switch]$AllowFailure)

    $output = & docker @Arguments 2>&1
    $code = $LASTEXITCODE
    if (-not $AllowFailure -and $code -ne 0) {
        throw "docker $($Arguments -join ' ') failed: $($output -join ' ')"
    }
    [pscustomobject]@{ ExitCode = $code; Output = @($output) }
}

function Test-DockerEngine {
    $result = Invoke-Docker -Arguments @("info", "--format", "{{.ServerVersion}}") -AllowFailure
    return ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace(($result.Output -join "")))
}

function Ensure-DockerEngine {
    if (Test-DockerEngine) { return }
    if (-not (Test-Path -LiteralPath $script:DockerDesktop -PathType Leaf)) {
        throw "Docker Desktop is not running and its supported executable was not found: $script:DockerDesktop"
    }
    Write-Host "Starting Docker Desktop..."
    [void](Start-Process -FilePath $script:DockerDesktop -WindowStyle Hidden)
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        Start-Sleep -Seconds 2
        if (Test-DockerEngine) { return }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Docker Desktop did not make the engine available within 120 seconds."
}

function Get-ContainerInspect {
    param([string]$Name)

    $result = Invoke-Docker -Arguments @("inspect", $Name) -AllowFailure
    if ($result.ExitCode -ne 0) { return $null }
    return (($result.Output -join [Environment]::NewLine) | ConvertFrom-Json | Select-Object -First 1)
}

function Assert-ContainerOwnership {
    param([object]$Definition, [object]$Container)

    $inspect = Get-ContainerInspect -Name $Container.Name
    if ($null -eq $inspect) { throw "Required $($Definition.Name) container is missing: $($Container.Name)" }
    $networks = @($inspect.NetworkSettings.Networks.PSObject.Properties.Name)
    if ($networks -notcontains $Definition.Network) {
        throw "$($Container.Name) is not attached to the owned network $($Definition.Network)."
    }
    return $inspect
}

function Get-ProcessCommandLine {
    param([int]$Id)
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction SilentlyContinue
    if ($null -eq $process) { return $null }
    return [string]$process.CommandLine
}

function Get-ProcessesByMarkers {
    param([string]$ProcessName, [string[]]$Markers)

    return @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        if ($_.Name -ine $ProcessName) { return $false }
        $command = [string]$_.CommandLine
        foreach ($marker in $Markers) {
            if ($command.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false }
        }
        return $true
    })
}

function Get-PortOwner {
    param([int]$Port)

    $connection = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $connection) { return $null }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($connection.OwningProcess)" -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Id = [int]$connection.OwningProcess
        Name = if ($null -eq $process) { "unknown" } else { [string]$process.Name }
        CommandLine = if ($null -eq $process) { "" } else { [string]$process.CommandLine }
    }
}

function Test-HttpReady {
    param([string]$Uri, [int]$TimeoutSeconds = 4)

    try {
        $headers = @{}
        if ($Uri -like "https://*.ngrok.dev/*") { $headers["ngrok-skip-browser-warning"] = "1" }
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -Headers $headers -TimeoutSec $TimeoutSeconds
        return [pscustomobject]@{ Ready = ($response.StatusCode -eq 200); Detail = "HTTP $($response.StatusCode)" }
    }
    catch {
        return [pscustomobject]@{ Ready = $false; Detail = $_.Exception.Message }
    }
}

function Wait-HttpReady {
    param([string]$Uri, [int]$TimeoutSeconds = 90)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $last = $null
    do {
        $last = Test-HttpReady -Uri $Uri -TimeoutSeconds 4
        if ($last.Ready) { return }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for $Uri. Last result: $($last.Detail)"
}

function Wait-ContainerRunning {
    param([object]$Definition, [object]$Container, [int]$TimeoutSeconds = 90)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $inspect = Assert-ContainerOwnership -Definition $Definition -Container $Container
        if ($inspect.State.Status -eq "running") {
            $hasHealth = $inspect.State.PSObject.Properties.Name -contains "Health"
            if (-not $hasHealth -or $inspect.State.Health.Status -eq "healthy") { return }
            if ($hasHealth -and $inspect.State.Health.Status -eq "unhealthy") { throw "$($Container.Name) reported unhealthy. See: $($Container.Log)" }
        }
        if ($inspect.State.Status -eq "exited" -and [int]$inspect.State.ExitCode -ne 0) {
            throw "$($Container.Name) exited with code $($inspect.State.ExitCode). See: $($Container.Log)"
        }
        Start-Sleep -Seconds 1
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "$($Container.Name) did not reach running/healthy state. See: $($Container.Log)"
}

function Start-OwnedContainer {
    param([object]$Definition, [object]$Container)

    $inspect = Assert-ContainerOwnership -Definition $Definition -Container $Container
    if ($inspect.State.Status -ne "running") {
        Write-Host "Starting $($Container.Component) ($($Container.Name))..."
        [void](Invoke-Docker -Arguments @("start", $Container.Name))
    }
    else { Write-Host "$($Container.Component) is already running." }
    Wait-ContainerRunning -Definition $Definition -Container $Container
}

function Stop-OwnedContainer {
    param([object]$Definition, [object]$Container)

    $inspect = Assert-ContainerOwnership -Definition $Definition -Container $Container
    if ($inspect.State.Status -ne "running") {
        Write-Host "$($Container.Component) is already stopped."
        return
    }
    Write-Host "Stopping $($Container.Component) ($($Container.Name))..."
    [void](Invoke-Docker -Arguments @("stop", "--time", "30", $Container.Name))
}

function Invoke-WithEnvironmentVariables {
    param([hashtable]$Values, [scriptblock]$ActionBlock)

    $saved = @{}
    try {
        foreach ($name in $Values.Keys) {
            $saved[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
            [Environment]::SetEnvironmentVariable($name, [string]$Values[$name], "Process")
        }
        & $ActionBlock
    }
    finally {
        foreach ($name in $Values.Keys) {
            [Environment]::SetEnvironmentVariable($name, $saved[$name], "Process")
        }
    }
}

function Get-NodeStatus {
    param([object]$Definition, [object]$Node)

    $owned = @(Get-ProcessesByMarkers -ProcessName "node.exe" -Markers $Node.Markers)
    if ($owned.Count -gt 1) {
        return [pscustomobject]@{ Environment=$Definition.Name; Component=$Node.Component; State="unhealthy"; Identifier="PIDs $($owned.ProcessId -join ',')"; Url=$Node.ReadyUri; Detail="Duplicate owned processes. Log: $($Node.StdErr)" }
    }
    if ($owned.Count -eq 1) {
        $ready = Test-HttpReady -Uri $Node.ReadyUri
        return [pscustomobject]@{ Environment=$Definition.Name; Component=$Node.Component; State=$(if ($ready.Ready) { "ready" } else { "unhealthy" }); Identifier="PID $($owned[0].ProcessId)"; Url=$Node.ReadyUri; Detail=$(if ($ready.Ready) { $ready.Detail } else { "$($ready.Detail); log: $($Node.StdErr)" }) }
    }
    $owner = Get-PortOwner -Port $Node.Port
    if ($null -ne $owner) {
        return [pscustomobject]@{ Environment=$Definition.Name; Component=$Node.Component; State="blocked"; Identifier="PID $($owner.Id)"; Url=$Node.ReadyUri; Detail="Port $($Node.Port) is owned by $($owner.Name), not this environment." }
    }
    return [pscustomobject]@{ Environment=$Definition.Name; Component=$Node.Component; State="stopped"; Identifier="-"; Url=$Node.ReadyUri; Detail="Log: $($Node.StdErr)" }
}

function Start-NodeComponent {
    param([object]$Definition, [object]$Node)

    $status = Get-NodeStatus -Definition $Definition -Node $Node
    if ($status.State -eq "ready") { Write-Host "$($Node.Component) is already ready ($($status.Identifier))."; return }
    if ($status.State -ne "stopped") { throw "$($Node.Component) cannot start: $($status.Detail)" }
    foreach ($required in @($Node.NodeScript, $Node.WorkingDirectory)) {
        if (-not (Test-Path -LiteralPath $required)) { throw "$($Node.Component) artifact is missing: $required" }
    }
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $Node.StdOut) -Force)
    $process = Invoke-WithEnvironmentVariables -Values $Node.EnvironmentVariables -ActionBlock {
        Start-Process -FilePath "node.exe" -ArgumentList (@($Node.NodeScript) + @($Node.Arguments)) -WorkingDirectory $Node.WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $Node.StdOut -RedirectStandardError $Node.StdErr -PassThru
    }
    Wait-HttpReady -Uri $Node.ReadyUri -TimeoutSeconds 45
    Write-Host "$($Node.Component) started (PID $($process.Id))."
}

function Stop-NodeComponent {
    param([object]$Definition, [object]$Node)

    $owned = @(Get-ProcessesByMarkers -ProcessName "node.exe" -Markers $Node.Markers)
    if ($owned.Count -eq 0) { Write-Host "$($Node.Component) is already stopped."; return }
    foreach ($process in $owned) {
        Stop-Process -Id $process.ProcessId -ErrorAction Stop
        Wait-Process -Id $process.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) { throw "$($Node.Component) PID $($process.ProcessId) did not stop." }
        Write-Host "$($Node.Component) stopped (PID $($process.ProcessId))."
    }
}

function Get-AptProcesses {
    return @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -ieq "AssistedPaymentTerminal.Desktop.exe" })
}

function Get-AptOwnerName {
    param([object]$Process)

    $command = [string]$Process.CommandLine
    if ($command.IndexOf("D:\SourceCodes\ExitPass.dev", [StringComparison]::OrdinalIgnoreCase) -ge 0) { return "Dev" }
    if ($command.IndexOf("clean-november-uat-20261106-222543", [StringComparison]::OrdinalIgnoreCase) -ge 0) { return "UAT" }
    if ($command.IndexOf("demo-rc1-20261112", [StringComparison]::OrdinalIgnoreCase) -ge 0) { return "Demo" }
    return "Unknown"
}

function Assert-AptConfiguration {
    param([object]$Definition)

    $apt = $Definition.Apt
    foreach ($required in @($apt.Executable, $apt.FrontendConfig)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "APT artifact is missing: $required" }
    }
    if (-not (Test-Path -LiteralPath $apt.TrustState -PathType Leaf)) {
        throw "$($Definition.Name) APT enrollment is incomplete; trust state is missing: $($apt.TrustState)"
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    if ($identity -ine $script:ExpectedAptUser) {
        throw "APT must run as $script:ExpectedAptUser so CurrentUser certificates and DPAPI state remain usable. Current user: $identity"
    }
    $bios = (Get-CimInstance Win32_BIOS).SerialNumber.Trim()
    if ($bios -cne $script:ExpectedPhysicalSerial) {
        throw "APT physical BIOS serial mismatch. Expected $script:ExpectedPhysicalSerial; found $bios."
    }
    $config = Get-Content -LiteralPath $apt.FrontendConfig -Raw | ConvertFrom-Json
    $checks = @{
        APT_TERMINAL_ID = $apt.TerminalId
        APT_TERMINAL_SERIAL_NUMBER = $script:ExpectedDisplaySerial
        APT_TERMINAL_DISPLAY_NAME = $apt.DisplayName
        APT_SITE_ID = $apt.SiteId
        APT_SITE_GROUP_ID = $apt.SiteGroupId
        APT_POS_SERVER_ID = $apt.PosServerId
        CENTRAL_PMS_BASE_URL = $apt.CentralUrl
        APT_WEB_UI_URL = $apt.UiUrl
    }
    foreach ($name in $checks.Keys) {
        if ([string]$config.$name -cne [string]$checks[$name]) {
            throw "APT configuration mismatch for $name. Expected '$($checks[$name])'; found '$($config.$name)'."
        }
    }
}

function Get-AptStatus {
    param([object]$Definition)

    $apt = $Definition.Apt
    $processes = @(Get-AptProcesses)
    $owned = @($processes | Where-Object { ([string]$_.CommandLine).IndexOf($apt.Executable, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($owned.Count -eq 1) {
        return [pscustomobject]@{ Environment=$Definition.Name; Component="AptDesktop"; State="ready"; Identifier="PID $($owned[0].ProcessId)"; Url=$apt.UiUrl; Detail="Desktop executable verified; display serial $script:ExpectedDisplaySerial; BIOS $script:ExpectedPhysicalSerial." }
    }
    if ($owned.Count -gt 1) {
        return [pscustomobject]@{ Environment=$Definition.Name; Component="AptDesktop"; State="unhealthy"; Identifier="PIDs $($owned.ProcessId -join ',')"; Url=$apt.UiUrl; Detail="Multiple desktop instances use the same environment." }
    }
    if ($processes.Count -gt 0) {
        $ownerName = Get-AptOwnerName -Process $processes[0]
        $stopCommand = "& '$PSCommandPath' -Environment $ownerName -Action Stop"
        return [pscustomobject]@{ Environment=$Definition.Name; Component="AptDesktop"; State="blocked"; Identifier="PID $($processes[0].ProcessId)"; Url=$apt.UiUrl; Detail="Shared PITX-APT-01 is owned by $ownerName. Stop command: $stopCommand" }
    }
    if (-not (Test-Path -LiteralPath $apt.TrustState -PathType Leaf)) {
        return [pscustomobject]@{ Environment=$Definition.Name; Component="AptDesktop"; State="blocked"; Identifier="-"; Url=$apt.UiUrl; Detail="Enrollment trust state is missing: $($apt.TrustState)" }
    }
    return [pscustomobject]@{ Environment=$Definition.Name; Component="AptDesktop"; State="stopped"; Identifier="-"; Url=$apt.UiUrl; Detail="Executable: $($apt.Executable)" }
}

function Start-AptDesktop {
    param([object]$Definition)

    $status = Get-AptStatus -Definition $Definition
    if ($status.State -eq "ready") { Write-Host "AptDesktop is already ready ($($status.Identifier))."; return }
    if ($status.State -eq "blocked" -and $status.Detail -like "Shared PITX-APT-01*") { throw $status.Detail }
    Assert-AptConfiguration -Definition $Definition
    $apt = $Definition.Apt
    $central = Test-HttpReady -Uri "$($apt.CentralUrl.TrimEnd('/'))/health/ready"
    if (-not $central.Ready) { throw "APT Central PMS dependency is not ready at $($apt.CentralUrl): $($central.Detail)" }
    $ui = Test-HttpReady -Uri "$($apt.UiUrl.TrimEnd('/'))/"
    if (-not $ui.Ready) { throw "APT frontend is not ready at $($apt.UiUrl): $($ui.Detail)" }

    $values = @{
        APT_PROFILE = "CASHIER_ASSISTED_TERMINAL"
        APT_TERMINAL_ID = $apt.TerminalId
        APT_TERMINAL_DISPLAY_NAME = $apt.DisplayName
        APT_TERMINAL_SERIAL_NUMBER = $script:ExpectedDisplaySerial
        APT_SITE_ID = $apt.SiteId
        APT_SITE_NAME = "PITX Level 3"
        APT_SITE_GROUP_ID = $apt.SiteGroupId
        APT_POS_SERVER_ID = $apt.PosServerId
        APT_DEVICE_TRUST_STATE_PATH = $apt.TrustState
        APT_LOCAL_DB_PATH = $apt.Database
        APT_HUMAN_SESSION_CREDENTIAL_PATH = $apt.HumanSession
        CENTRAL_PMS_BASE_URL = $apt.CentralUrl
        APT_CENTRAL_PMS_SERVICE_IDENTITY_ID = $apt.ServiceIdentityId
        USE_MOCK_CENTRAL_PMS = "false"
        APT_ENABLE_CENTRAL_PMS_CASH_SUBMISSION = "true"
        APT_ENABLE_CENTRAL_PMS_FISCAL_ISSUANCE = "true"
        APT_ENABLE_CENTRAL_PMS_RECEIPT_RETRIEVAL = "true"
        APT_ENABLE_RECEIPT_PREVIEW = "true"
        APT_ENABLE_RECEIPT_PRINTING = "true"
        APT_RECEIPT_PRINTER_NAME = "TSC TTP-225"
        APT_RECEIPT_PAPER_WIDTH_MM = "57"
        APT_WEB_UI_URL = $apt.UiUrl
        APT_SITE_TIME_ZONE_ID = "Asia/Manila"
        WEBVIEW2_USER_DATA_FOLDER = $apt.WebView
    }
    $process = Invoke-WithEnvironmentVariables -Values $values -ActionBlock {
        Start-Process -FilePath $apt.Executable -WorkingDirectory $apt.Release -PassThru
    }
    Start-Sleep -Seconds 3
    if ($process.HasExited) { throw "APT desktop exited during startup with code $($process.ExitCode)." }
    Write-Host "AptDesktop started (PID $($process.Id)) under $script:ExpectedAptUser."
}

function Stop-AptDesktop {
    param([object]$Definition)

    $apt = $Definition.Apt
    $owned = @(Get-AptProcesses | Where-Object { ([string]$_.CommandLine).IndexOf($apt.Executable, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($owned.Count -eq 0) { Write-Host "AptDesktop is already stopped."; return }
    foreach ($entry in $owned) {
        $process = Get-Process -Id $entry.ProcessId -ErrorAction Stop
        if (-not $process.CloseMainWindow()) {
            throw "AptDesktop PID $($entry.ProcessId) has no closable window. It was left running so active shift/custody state is not interrupted."
        }
        Wait-Process -Id $entry.ProcessId -Timeout 20 -ErrorAction SilentlyContinue
        if (Get-Process -Id $entry.ProcessId -ErrorAction SilentlyContinue) {
            throw "AptDesktop PID $($entry.ProcessId) did not exit after a graceful window close. It was left running."
        }
        Write-Host "AptDesktop stopped gracefully (PID $($entry.ProcessId))."
    }
}

function Get-NgrokProcesses {
    param([object]$Definition)

    return @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        if ($_.Name -ine "ngrok.exe") { return $false }
        $command = [string]$_.CommandLine
        if ($command.IndexOf($Definition.Ngrok.Config, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        foreach ($marker in @($Definition.Ngrok.ExtraMarkers)) {
            if ($command.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        }
        return $false
    })
}

function Get-NgrokStatus {
    param([object]$Definition)

    $processes = @(Get-NgrokProcesses -Definition $Definition)
    if ($processes.Count -eq 0) {
        $owner = Get-PortOwner -Port $Definition.Ngrok.AdminPort
        if ($null -ne $owner) {
            return [pscustomobject]@{ Environment=$Definition.Name; Component="Ngrok"; State="blocked"; Identifier="PID $($owner.Id)"; Url=($Definition.Ngrok.PublicUrls -join ", "); Detail="Admin port $($Definition.Ngrok.AdminPort) is owned by $($owner.Name). Log: $($Definition.Ngrok.Log)" }
        }
        return [pscustomobject]@{ Environment=$Definition.Name; Component="Ngrok"; State="stopped"; Identifier="-"; Url=($Definition.Ngrok.PublicUrls -join ", "); Detail="Log: $($Definition.Ngrok.Log)" }
    }
    $failed = @()
    foreach ($uri in $Definition.Ngrok.PublicUrls) {
        $result = Test-HttpReady -Uri $uri -TimeoutSeconds 8
        if (-not $result.Ready) { $failed += "$uri ($($result.Detail))" }
    }
    return [pscustomobject]@{
        Environment=$Definition.Name; Component="Ngrok"; State=$(if ($failed.Count -eq 0) { "ready" } else { "unhealthy" })
        Identifier="PIDs $($processes.ProcessId -join ',')"; Url=($Definition.Ngrok.PublicUrls -join ", ")
        Detail=$(if ($failed.Count -eq 0) { "All configured public routes returned HTTP 200." } else { "$($failed -join '; '); log: $($Definition.Ngrok.Log)" })
    }
}

function Start-Ngrok {
    param([object]$Definition)

    $status = Get-NgrokStatus -Definition $Definition
    if ($status.State -eq "ready") { Write-Host "Ngrok is already ready ($($status.Identifier))."; return }
    if ($status.State -ne "stopped") { throw "Ngrok cannot start: $($status.Detail)" }
    foreach ($node in $Definition.Nodes | Where-Object { $_.Component -in @("WebPay", "OperatorConsole", "ManagementPlatform") }) {
        $nodeStatus = Get-NodeStatus -Definition $Definition -Node $node
        if ($nodeStatus.State -ne "ready") { throw "Ngrok upstream $($node.Component) is not ready: $($nodeStatus.Detail)" }
    }
    $ngrok = (Get-Command ngrok.exe -ErrorAction Stop).Source
    [void](& $ngrok config check --config $Definition.Ngrok.Config)
    if ($LASTEXITCODE -ne 0) { throw "Ngrok configuration is invalid: $($Definition.Ngrok.Config)" }
    $arguments = @("start", "--all", "--config", $script:GlobalNgrokConfig, "--config", $Definition.Ngrok.Config, "--log", $Definition.Ngrok.Log, "--log-format", "json")
    $process = Start-Process -FilePath $ngrok -ArgumentList $arguments -WorkingDirectory $Definition.Root -WindowStyle Hidden -PassThru
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { throw "Ngrok exited during startup. Review $($Definition.Ngrok.Log)" }
        $current = Get-NgrokStatus -Definition $Definition
        if ($current.State -eq "ready") { Write-Host "Ngrok started (PID $($process.Id))."; return }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Ngrok did not make every configured public route ready. Review $($Definition.Ngrok.Log)"
}

function Stop-Ngrok {
    param([object]$Definition)

    $processes = @(Get-NgrokProcesses -Definition $Definition)
    if ($processes.Count -eq 0) { Write-Host "Ngrok is already stopped."; return }
    foreach ($process in $processes) {
        Stop-Process -Id $process.ProcessId -ErrorAction Stop
        Wait-Process -Id $process.ProcessId -Timeout 15 -ErrorAction SilentlyContinue
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
        if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) { throw "Ngrok PID $($process.ProcessId) did not stop." }
        Write-Host "Ngrok stopped (PID $($process.ProcessId))."
    }
}

function Get-ContainerStatusRows {
    param([object]$Definition)

    $inspects = @{}
    $anyRunning = $false
    foreach ($container in $Definition.Containers) {
        $inspect = Get-ContainerInspect -Name $container.Name
        $inspects[$container.Name] = $inspect
        if ($null -ne $inspect -and $inspect.State.Status -eq "running") { $anyRunning = $true }
    }
    $rows = @()
    foreach ($container in $Definition.Containers | Sort-Object Order) {
        $inspect = $inspects[$container.Name]
        if ($null -eq $inspect) {
            $rows += [pscustomobject]@{ Environment=$Definition.Name; Component=$container.Component; State="blocked"; Identifier="missing"; Url=$container.ReadyUri; Detail="Required container is missing."
            }
            continue
        }
        $state = [string]$inspect.State.Status
        $detail = "Image $($inspect.Image.Substring(0,19)); log: $($container.Log)"
        if ($state -eq "running") {
            $hasHealth = $inspect.State.PSObject.Properties.Name -contains "Health"
            if ($hasHealth) {
                if ($inspect.State.Health.Status -eq "unhealthy") { $state = "unhealthy" }
                elseif ($inspect.State.Health.Status -eq "starting") { $state = "starting" }
                else { $state = "ready" }
            }
            else { $state = "ready" }
            if ($state -eq "ready" -and -not [string]::IsNullOrWhiteSpace($container.ReadyUri)) {
                $http = Test-HttpReady -Uri $container.ReadyUri
                if (-not $http.Ready) { $state = "unhealthy"; $detail = "$($http.Detail); log: $($container.Log)" }
                else { $detail = "$($http.Detail); image $($inspect.Image.Substring(0,19))" }
            }
        }
        elseif ($state -eq "exited" -and $anyRunning -and [int]$inspect.State.ExitCode -ne 0) {
            $state = "unhealthy"
            $detail = "Exited with code $($inspect.State.ExitCode); log: $($container.Log)"
        }
        else { $state = "stopped" }
        $rows += [pscustomobject]@{ Environment=$Definition.Name; Component=$container.Component; State=$state; Identifier=$inspect.Id.Substring(0,12); Url=$container.ReadyUri; Detail=$detail }
    }
    return $rows
}

function Get-EnvironmentStatus {
    param([object]$Definition)

    $rows = @()
    if (Test-DockerEngine) { $rows += @(Get-ContainerStatusRows -Definition $Definition) }
    else {
        foreach ($container in $Definition.Containers) {
            $rows += [pscustomobject]@{ Environment=$Definition.Name; Component=$container.Component; State="blocked"; Identifier="-"; Url=$container.ReadyUri; Detail="Docker engine unavailable." }
        }
    }
    foreach ($node in $Definition.Nodes) { $rows += Get-NodeStatus -Definition $Definition -Node $node }
    $rows += Get-AptStatus -Definition $Definition
    $rows += Get-NgrokStatus -Definition $Definition

    $ready = @($rows | Where-Object { $_.State -eq "ready" }).Count
    $active = @($rows | Where-Object { $_.State -in @("ready", "starting", "unhealthy") }).Count
    $problem = @($rows | Where-Object { $_.State -in @("blocked", "unhealthy", "starting") }).Count
    if ($active -eq 0) { $overall = "Stopped"; $code = 3 }
    elseif ($ready -eq $rows.Count) { $overall = "Ready"; $code = 0 }
    elseif ($problem -gt 0) { $overall = "Partial"; $code = 2 }
    else { $overall = "Partial"; $code = 2 }
    [pscustomobject]@{ Overall=$overall; ExitCode=$code; Rows=$rows }
}

function Write-EnvironmentStatus {
    param([object]$Definition, [object]$Status)

    Write-Host ""
    Write-Host "$($Definition.Name) environment: $($Status.Overall)"
    $Status.Rows | Select-Object Environment,Component,State,Identifier,Url | Format-Table -AutoSize -Wrap
    $attention = @($Status.Rows | Where-Object { $_.State -ne "ready" })
    if ($attention.Count -gt 0) {
        Write-Host "Details:"
        foreach ($row in $attention) {
            Write-Host " - $($row.Component) [$($row.State)]: $($row.Detail)"
        }
    }
    Write-Host "Exit codes: 0=ready/action complete, 1=operation failed, 2=partial or blocked, 3=stopped status."
}

function Start-Environment {
    param([object]$Definition)

    Ensure-DockerEngine
    $failures = [System.Collections.Generic.List[string]]::new()
    Write-LifecycleLog -Definition $Definition -Message "START requested."
    foreach ($container in $Definition.Containers | Sort-Object Order) {
        try { Start-OwnedContainer -Definition $Definition -Container $container }
        catch { $failures.Add("$($container.Component): $($_.Exception.Message)") }
    }
    foreach ($container in $Definition.Containers | Sort-Object Order) {
        if (-not [string]::IsNullOrWhiteSpace($container.ReadyUri)) {
            try { Wait-HttpReady -Uri $container.ReadyUri -TimeoutSeconds 60 }
            catch { $failures.Add("$($container.Component) readiness: $($_.Exception.Message)") }
        }
    }
    foreach ($node in $Definition.Nodes) {
        try { Start-NodeComponent -Definition $Definition -Node $node }
        catch { $failures.Add("$($node.Component): $($_.Exception.Message)") }
    }
    try { Start-AptDesktop -Definition $Definition }
    catch { $failures.Add("AptDesktop: $($_.Exception.Message)") }
    try { Start-Ngrok -Definition $Definition }
    catch { $failures.Add("Ngrok: $($_.Exception.Message)") }

    $status = Get-EnvironmentStatus -Definition $Definition
    Write-EnvironmentStatus -Definition $Definition -Status $status
    if ($failures.Count -gt 0) {
        Write-Warning "Startup completed with blockers:`n - $($failures -join "`n - ")"
        Write-LifecycleLog -Definition $Definition -Message "START partial: $($failures -join ' | ')"
        $script:ExitCode = 2
    }
    elseif ($status.Overall -eq "Ready") {
        Write-LifecycleLog -Definition $Definition -Message "START completed; environment ready."
        $script:ExitCode = 0
    }
    else {
        Write-LifecycleLog -Definition $Definition -Message "START completed with status $($status.Overall)."
        $script:ExitCode = $status.ExitCode
    }
}

function Stop-Environment {
    param([object]$Definition)

    if (-not (Test-DockerEngine)) {
        throw "Docker engine is unavailable. It was not started for a Stop action because container state cannot be verified safely."
    }
    $failures = [System.Collections.Generic.List[string]]::new()
    Write-LifecycleLog -Definition $Definition -Message "STOP requested."
    try { Stop-Ngrok -Definition $Definition } catch { $failures.Add("Ngrok: $($_.Exception.Message)") }
    try { Stop-AptDesktop -Definition $Definition } catch { $failures.Add("AptDesktop: $($_.Exception.Message)") }
    if (@($failures | Where-Object { $_ -like "AptDesktop:*" }).Count -eq 0) {
        foreach ($node in @($Definition.Nodes | Sort-Object { -1 * $_.Port })) {
            try { Stop-NodeComponent -Definition $Definition -Node $node }
            catch { $failures.Add("$($node.Component): $($_.Exception.Message)") }
        }
        foreach ($container in @($Definition.Containers | Sort-Object { -1 * $_.Order })) {
            try { Stop-OwnedContainer -Definition $Definition -Container $container }
            catch { $failures.Add("$($container.Component): $($_.Exception.Message)") }
        }
    }
    else {
        $failures.Add("Dependent frontends and infrastructure were retained because AptDesktop did not stop safely.")
    }
    $status = Get-EnvironmentStatus -Definition $Definition
    Write-EnvironmentStatus -Definition $Definition -Status $status
    if ($failures.Count -gt 0) {
        Write-Warning "Stop completed with blockers:`n - $($failures -join "`n - ")"
        Write-LifecycleLog -Definition $Definition -Message "STOP partial: $($failures -join ' | ')"
        $script:ExitCode = 1
    }
    elseif ($status.Overall -eq "Stopped") {
        Write-LifecycleLog -Definition $Definition -Message "STOP completed; persistent storage and credentials preserved."
        $script:ExitCode = 0
    }
    else {
        Write-LifecycleLog -Definition $Definition -Message "STOP completed with unexpected status $($status.Overall)."
        $script:ExitCode = 1
    }
}

$definition = Get-EnvironmentDefinition -Name $Environment
if (-not (Test-Path -LiteralPath $definition.Root -PathType Container)) {
    Write-Error "$Environment environment root is missing: $($definition.Root)"
    exit 1
}

try {
    switch ($Action) {
        "Start" { Start-Environment -Definition $definition }
        "Stop" { Stop-Environment -Definition $definition }
        "Status" {
            $status = Get-EnvironmentStatus -Definition $definition
            Write-EnvironmentStatus -Definition $definition -Status $status
            $script:ExitCode = $status.ExitCode
        }
    }
}
catch {
    Write-Error $_.Exception.Message
    try { Write-LifecycleLog -Definition $definition -Message "$Action failed: $($_.Exception.Message)" } catch { }
    $script:ExitCode = 1
}

exit $script:ExitCode
