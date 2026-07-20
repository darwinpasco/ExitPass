param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath
$e2ePort = if ($env:MANAGEMENT_PLATFORM_E2E_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PORT } else { "5177" }
$productionPort = if ($env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT } else { "5178" }

Write-Host "Management Platform Sales Invoice Setup activate/retire UI E2E proof"
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
        "IndexedDB"
    )

    foreach ($token in $forbidden) {
        if ($productionSourceText.Contains($token)) {
            throw "Forbidden production source token found: $token"
        }
        if ($distText.Contains($token)) {
            throw "Forbidden production dist token found: $token"
        }
    }

    $forbiddenRenderedTerms = @(
        "Approve Profile",
        "Profile administration",
        "Mutation accepted",
        "Effective readiness",
        "Immutable usage"
    )

    foreach ($term in $forbiddenRenderedTerms) {
        if ($productionSourceText.Contains($term) -or $distText.Contains($term)) {
            throw "Forbidden user-facing terminology found: $term"
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

    $proofs = @(
        "scripts\Invoke-ManagementPlatformUiFoundationProof.ps1",
        "scripts\Invoke-ManagementPlatformSalesInvoiceProfileReadUiProof.ps1",
        "scripts\Invoke-ManagementPlatformSalesInvoiceProfileManageUiProof.ps1",
        "scripts\Invoke-ManagementPlatformSalesInvoiceProfileManageUiE2eProof.ps1",
        "scripts\Invoke-ManagementPlatformSalesInvoiceProfileApproveRetireUiProof.ps1"
    )

    foreach ($proof in $proofs) {
        & powershell -ExecutionPolicy Bypass -File (Join-Path $repoRoot $proof)
        if ($LASTEXITCODE -ne 0) {
            throw "$proof failed."
        }
    }

    Assert-StaticBrowserBoundary
    Assert-NoGeneratedArtifactsStaged

    Write-Host "Proof passed: Playwright Chromium E2E covers approve permission separation, activation validation gating, single-submit activation/retirement, conflict and timeout uncertainty, pending Site-switch blocking, keyboard dialogs, responsive dialogs, terminology, storage, and route/header boundaries."
    Write-Host "Proof passed: static production source and dist scans found no direct POS Server route, key header, permission header, API-key configuration, POS Server base URL, browser storage implementation, or forbidden user-facing terminology."
    Write-Host "Proof passed: generated Playwright artifacts are not staged."
    Write-Host "Proof passed: script exits successfully."
}
finally {
    Stop-ProjectE2eProcesses
}
