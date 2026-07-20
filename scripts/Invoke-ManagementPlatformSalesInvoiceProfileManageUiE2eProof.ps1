param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath
$e2ePort = if ($env:MANAGEMENT_PLATFORM_E2E_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PORT } else { "5177" }
$productionPort = if ($env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT } else { "5178" }

Write-Host "Management Platform Sales Invoice Profile manage UI E2E proof"
Write-Host "Repository: $repoRoot"
Write-Host "Project: $resolvedProjectPath"
Write-Host "E2E port: $e2ePort"
Write-Host "Production E2E port: $productionPort"

if (-not (Test-Path $resolvedProjectPath)) {
    throw "Management Platform UI project was not found."
}

function Invoke-NpmCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & npm.cmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "npm $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-NpxCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & npx.cmd @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "npx $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Stop-ProjectE2eProcesses {
    $escapedProjectPath = [Regex]::Escape($resolvedProjectPath)
    $portPattern = "($e2ePort|$productionPort)"
    $processes = Get-CimInstance Win32_Process |
        Where-Object {
            $_.CommandLine -and
            $_.CommandLine -match $escapedProjectPath -and
            $_.CommandLine -match "vite" -and
            $_.CommandLine -match $portPattern
        }

    foreach ($process in $processes) {
        Write-Host "Stopping stale ManagementPlatformUi E2E process $($process.ProcessId)."
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Assert-NoGeneratedArtifactsStaged {
    $staged = & git -C $repoRoot diff --cached --name-only
    if ($LASTEXITCODE -ne 0) {
        throw "git staged-artifact check failed."
    }

    $forbiddenStaged = $staged | Where-Object {
        $_ -match "src/Services/ManagementPlatformUi/(test-results|playwright-report|\.playwright)/"
    }

    if ($forbiddenStaged) {
        throw "Generated Playwright artifacts are staged: $($forbiddenStaged -join ', ')"
    }
}

function Assert-StaticBrowserBoundary {
    $sourcePath = Join-Path $resolvedProjectPath "src"
    $distPath = Join-Path $resolvedProjectPath "dist"

    $productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
        Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
    $distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

    $productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
    $distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"

    $forbidden = @(
        "X-PosServer-Admin-Key",
        "X-PosServer-Admin-Permission",
        "/v1/admin/",
        "POS_SERVER_API_KEY",
        "POS_SERVER_BASE_URL",
        "localStorage",
        "sessionStorage",
        "IndexedDB",
        "terminalId"
    )

    foreach ($token in $forbidden) {
        if ($productionSourceText.Contains($token)) {
            throw "Forbidden production source token found: $token"
        }
        if ($distText.Contains($token)) {
            throw "Forbidden production dist token found: $token"
        }
    }

    if (-not $productionSourceText.Contains("x-posserver-")) {
        throw "Generic x-posserver browser-header rejection guard is missing."
    }
}

Stop-ProjectE2eProcesses
try {
    Push-Location $resolvedProjectPath
    try {
        Invoke-NpmCommand @("ci")
        Invoke-NpxCommand @("playwright", "install", "chromium")
        Invoke-NpmCommand @("run", "typecheck")
        Invoke-NpmCommand @("test")
        Invoke-NpmCommand @("run", "build")
        Invoke-NpmCommand @("run", "test:e2e")
    }
    finally {
        Pop-Location
    }

    & powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\Invoke-ManagementPlatformUiFoundationProof.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Foundation proof failed."
    }

    & powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\Invoke-ManagementPlatformSalesInvoiceProfileReadUiProof.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Read UI proof failed."
    }

    & powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot "scripts\Invoke-ManagementPlatformSalesInvoiceProfileManageUiProof.ps1")
    if ($LASTEXITCODE -ne 0) {
        throw "Manage UI proof failed."
    }

    Assert-StaticBrowserBoundary
    Assert-NoGeneratedArtifactsStaged

    Write-Host "Proof passed: Playwright Chromium E2E matrix covers read-only, manage, create/update, DRAFT edit, conflicts, timeout uncertainty, Site switching, responsive layout, keyboard access, route/header boundaries, storage safety, console safety, and production scenario isolation."
    Write-Host "Proof passed: static production source and dist scans found no direct POS Server route, key header, permission header, API-key configuration, POS Server base URL, terminal field, or browser storage implementation."
    Write-Host "Proof passed: generated Playwright artifacts are not staged."
    Write-Host "Proof passed: script exits successfully."
}
finally {
    Stop-ProjectE2eProcesses
}
