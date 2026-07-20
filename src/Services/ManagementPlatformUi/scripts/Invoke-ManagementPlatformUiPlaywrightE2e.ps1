param(
    [string]$E2ePort = $(if ($env:MANAGEMENT_PLATFORM_E2E_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PORT } else { "5179" }),
    [string]$ProductionPort = $(if ($env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT } else { "5180" })
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path $PSScriptRoot -Parent
$viteEntry = Join-Path $projectRoot "node_modules\vite\bin\vite.js"
$permissionEnv = "management-platform.overview.read,sales-invoice-profile.read,sales-invoice-profile.manage,sales-invoice-profile.approve"
$startedProcesses = New-Object System.Collections.Generic.List[System.Diagnostics.Process]
$previousPermissions = $env:VITE_MANAGEMENT_PLATFORM_PERMISSIONS

function Start-ProjectViteProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $process = Start-Process `
        -FilePath "node.exe" `
        -ArgumentList $Arguments `
        -WorkingDirectory $projectRoot `
        -PassThru `
        -WindowStyle Hidden
    $startedProcesses.Add($process)
    return $process
}

function Wait-ForServer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Url,
        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Process]$Process
    )

    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) {
            throw "Server process $($Process.Id) exited before $Url became available."
        }

        try {
            Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2 | Out-Null
            return
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "Timed out waiting for $Url."
}

function Stop-StartedProcesses {
    for ($index = $startedProcesses.Count - 1; $index -ge 0; $index--) {
        $process = $startedProcesses[$index]
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                Write-Host "Stopping ManagementPlatformUi E2E process $($process.Id)."
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
        } catch {
            Write-Host "ManagementPlatformUi E2E process cleanup skipped for $($process.Id): $($_.Exception.Message)"
        }
    }
}

if (-not (Test-Path $viteEntry)) {
    throw "Vite entrypoint was not found. Run npm ci before E2E tests."
}

try {
    $env:MANAGEMENT_PLATFORM_E2E_PORT = $E2ePort
    $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT = $ProductionPort
    $env:VITE_MANAGEMENT_PLATFORM_PERMISSIONS = $permissionEnv

    $devProcess = Start-ProjectViteProcess -Arguments @($viteEntry, "--host", "127.0.0.1", "--port", $E2ePort, "--strictPort")
    $previewProcess = Start-ProjectViteProcess -Arguments @($viteEntry, "preview", "--host", "127.0.0.1", "--port", $ProductionPort, "--strictPort")

    Wait-ForServer "http://127.0.0.1:$E2ePort/management-platform/" $devProcess
    Wait-ForServer "http://127.0.0.1:$ProductionPort/management-platform/" $previewProcess

    & npx.cmd playwright test --reporter=list
    if ($LASTEXITCODE -ne 0) {
        throw "Playwright E2E failed with exit code $LASTEXITCODE."
    }
} finally {
    Stop-StartedProcesses
    $env:VITE_MANAGEMENT_PLATFORM_PERMISSIONS = $previousPermissions
}
