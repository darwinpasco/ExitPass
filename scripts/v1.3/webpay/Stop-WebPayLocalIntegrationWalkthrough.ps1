param(
    [string]$RepositoryRoot,
    [string]$DatabaseName,
    [string]$PostgresContainerName,
    [string]$DatabaseUser = "exitpass",
    [int]$CentralPmsPort = 8080,
    [int]$PaymentOrchestratorPort = 8082,
    [int]$WebPayPort = 5173,
    [switch]$RemoveDisposableDatabase,
    [switch]$RemoveGeneratedState,
    [switch]$DryRun,
    [switch]$StopInfrastructure
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$stateRoot = Join-Path $RepositoryRoot ".local\webpay-local-integration-walkthrough"
$statePath = Join-Path $stateRoot "state.json"

function Assert-SafeDatabaseName {
    param([string]$Name)

    if ($Name -notmatch '^exitpass_webpay_local_walkthrough(_[a-z0-9_]+)?$') {
        throw "Refusing to remove database '$Name'. Only exitpass_webpay_local_walkthrough disposable databases are allowed."
    }
}

function Assert-SafeGeneratedStatePath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $RepositoryRoot ".local"))
    if (-not $fullPath.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove generated state outside .local: $fullPath"
    }

    if ($fullPath -notlike "*webpay-local-integration-walkthrough*") {
        throw "Refusing to remove unrelated generated state: $fullPath"
    }
}

function Stop-RecordedProcess {
    param(
        [string]$Name,
        [int]$Id
    )

    $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-Host "$Name process $Id is not running."
        return
    }

    if ($DryRun) {
        Write-Host "DRY RUN: would stop $Name process $Id." -ForegroundColor DarkYellow
    }
    else {
        Write-Host "Stopping $Name process $Id..." -ForegroundColor Yellow
        Stop-Process -Id $Id -Force
    }
}

function Get-ListeningProcessIds {
    param([int]$Port)

    $matches = netstat -ano | Select-String -Pattern "LISTENING"
    foreach ($match in $matches) {
        $line = $match.Line.Trim()
        if ($line -notmatch "[:.]$Port\s+") {
            continue
        }

        $parts = $line -split "\s+"
        $pidText = $parts[$parts.Length - 1]
        $processId = 0
        if ([int]::TryParse($pidText, [ref]$processId)) {
            $processId
        }
    }
}

function Get-ProcessCommandLine {
    param([int]$Id)

    try {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$Id" -ErrorAction Stop
        return $process.CommandLine
    }
    catch {
        $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
        if ($process -and $process.Path) {
            return $process.Path
        }

        return $null
    }
}

function Stop-WorktreeListener {
    param(
        [string]$Name,
        [int]$Port,
        [string[]]$RequiredCommandMarkers
    )

    $pids = @(Get-ListeningProcessIds -Port $Port | Select-Object -Unique)
    foreach ($listenerProcessId in $pids) {
        $commandLine = Get-ProcessCommandLine -Id $listenerProcessId
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            Write-Host "Could not verify $Name listener process $listenerProcessId on port $Port; leaving it running." -ForegroundColor Yellow
            continue
        }

        $isWalkthroughProcess = $commandLine -like "*$RepositoryRoot*"
        foreach ($marker in $RequiredCommandMarkers) {
            if ($commandLine -notlike "*$marker*") {
                $isWalkthroughProcess = $false
            }
        }

        if (-not $isWalkthroughProcess) {
            Write-Host "Leaving $Name listener process $listenerProcessId on port $Port because it is not from this worktree." -ForegroundColor Yellow
            continue
        }

        if ($DryRun) {
            Write-Host "DRY RUN: would stop $Name listener process $listenerProcessId on port $Port." -ForegroundColor DarkYellow
        }
        else {
            Write-Host "Stopping $Name listener process $listenerProcessId on port $Port..." -ForegroundColor Yellow
            Stop-Process -Id $listenerProcessId -Force
        }
    }
}

$state = $null
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
}

if ([string]::IsNullOrWhiteSpace($DatabaseName) -and $null -ne $state) {
    $DatabaseName = $state.DatabaseName
}

if ([string]::IsNullOrWhiteSpace($PostgresContainerName) -and $null -ne $state) {
    $PostgresContainerName = $state.PostgresContainerName
}

if ([string]::IsNullOrWhiteSpace($DatabaseName)) {
    $DatabaseName = "exitpass_webpay_local_walkthrough"
}

if ([string]::IsNullOrWhiteSpace($PostgresContainerName)) {
    $PostgresContainerName = "exitpass-postgres"
}

Assert-SafeDatabaseName -Name $DatabaseName

if ($null -ne $state -and $state.Processes) {
    foreach ($process in $state.Processes) {
        Stop-RecordedProcess -Name $process.Name -Id ([int]$process.Id)
    }
}
else {
    Write-Host "No walkthrough process state found at $statePath."
}

Stop-WorktreeListener -Name "Central PMS" -Port $CentralPmsPort -RequiredCommandMarkers @("ExitPass.CentralPms.Api")
Stop-WorktreeListener -Name "Payment Orchestrator" -Port $PaymentOrchestratorPort -RequiredCommandMarkers @("ExitPass.PaymentOrchestrator.Api")
Stop-WorktreeListener -Name "WebPay UI" -Port $WebPayPort -RequiredCommandMarkers @("vite", "$WebPayPort")

if ($RemoveDisposableDatabase) {
    if ($DryRun) {
        Write-Host "DRY RUN: would remove disposable database $DatabaseName." -ForegroundColor DarkYellow
    }
    else {
        Write-Host "Removing disposable database $DatabaseName..." -ForegroundColor Yellow
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$DatabaseName';"
        docker exec -i $PostgresContainerName psql -v ON_ERROR_STOP=1 -U $DatabaseUser -d postgres -c "DROP DATABASE IF EXISTS $DatabaseName;"
    }
}
else {
    Write-Host "Preserving disposable database $DatabaseName. Use -RemoveDisposableDatabase to drop it." -ForegroundColor Cyan
}

if ($StopInfrastructure) {
    Write-Host "Stopping walkthrough infrastructure containers only when they match the standard names..." -ForegroundColor Yellow
    foreach ($containerName in @("exitpass-postgres", "exitpass-rabbitmq", "exitpass-mock-payment-provider")) {
        if ($containerName -eq $PostgresContainerName -or $containerName -in @("exitpass-rabbitmq", "exitpass-mock-payment-provider")) {
            if ($DryRun) {
                Write-Host "DRY RUN: would stop $containerName"
            }
            else {
                docker stop $containerName | Out-Null
            }
        }
    }
}

if ($RemoveGeneratedState) {
    Assert-SafeGeneratedStatePath -Path $stateRoot
    if ($DryRun) {
        Write-Host "DRY RUN: would remove generated harness state at $stateRoot." -ForegroundColor DarkYellow
    }
    elseif (Test-Path -LiteralPath $stateRoot) {
        Remove-Item -LiteralPath $stateRoot -Recurse -Force
        Write-Host "Removed generated harness state: $stateRoot" -ForegroundColor Green
    }
}
elseif (Test-Path -LiteralPath $statePath) {
    Write-Host "State file remains for inspection: $statePath"
}

Write-Host "WebPay local walkthrough teardown complete." -ForegroundColor Green
