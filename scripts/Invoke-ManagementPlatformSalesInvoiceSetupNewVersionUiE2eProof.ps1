param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath
$expectedRepoRoot = "D:\SourceCodes\ExitPass-APT-NewVersion"
$expectedBranch = "feature/management-platform-sales-invoice-setup-new-version-ui"
$e2ePort = if ($env:MANAGEMENT_PLATFORM_E2E_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PORT } else { "5179" }
$productionPort = if ($env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT) { $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT } else { "5180" }

Write-Host "Management Platform Sales Invoice Setup Create New Version UI complete E2E proof"
Write-Host "Repository: $repoRoot"
Write-Host "Project: $resolvedProjectPath"
Write-Host "E2E port: $e2ePort"
Write-Host "Production E2E port: $productionPort"

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

function Invoke-ProofScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $fullPath = Join-Path $repoRoot $RelativePath
    & powershell -ExecutionPolicy Bypass -File $fullPath
    if ($LASTEXITCODE -ne 0) {
        throw "$RelativePath failed."
    }
}

function Assert-RepositoryAndBranch {
    $actualRoot = (& git -C $repoRoot rev-parse --show-toplevel).Trim() -replace "/", "\"
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve git repository root."
    }
    if ($actualRoot -ne $expectedRepoRoot) {
        throw "Unexpected repository root: $actualRoot"
    }

    $actualBranch = (& git -C $repoRoot branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to resolve git branch."
    }
    if ($actualBranch -ne $expectedBranch) {
        throw "Unexpected git branch: $actualBranch"
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
        "Activate after creation",
        "Retire source",
        "Mutation accepted",
        "Clone Profile",
        "Copy Header Profile"
    )

    foreach ($token in $forbidden) {
        if ($productionSourceText.Contains($token)) {
            throw "Forbidden production source token found: $token"
        }
        if ($distText.Contains($token)) {
            throw "Forbidden production dist token found: $token"
        }
    }

    if ($productionSourceText.Contains("localStorage") -or
        $productionSourceText.Contains("sessionStorage") -or
        $productionSourceText.Contains("indexedDB") -or
        $distText.Contains("localStorage") -or
        $distText.Contains("sessionStorage") -or
        $distText.Contains("indexedDB")) {
        throw "Browser-storage implementation token found in production source or dist."
    }

    if (-not $productionSourceText.Contains("x-posserver-")) {
        throw "Generic x-posserver browser-header rejection guard is missing."
    }
}

if (-not (Test-Path $resolvedProjectPath)) {
    throw "Management Platform UI project was not found."
}

Assert-RepositoryAndBranch

Push-Location $resolvedProjectPath
try {
    Invoke-NpmCommand @("ci")
    Invoke-NpxCommand @("playwright", "install", "chromium")
    Invoke-NpmCommand @("run", "typecheck")
    Invoke-NpmCommand @("test")
    Invoke-NpmCommand @("run", "build")
    $env:MANAGEMENT_PLATFORM_E2E_PORT = $e2ePort
    $env:MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT = $productionPort
    Invoke-NpmCommand @("run", "test:e2e")
}
finally {
    Pop-Location
}

Invoke-ProofScript "scripts\Invoke-ManagementPlatformUiFoundationProof.ps1"
Invoke-ProofScript "scripts\Invoke-ManagementPlatformSalesInvoiceProfileReadUiProof.ps1"
Invoke-ProofScript "scripts\Invoke-ManagementPlatformSalesInvoiceProfileManageUiProof.ps1"
Invoke-ProofScript "scripts\Invoke-ManagementPlatformSalesInvoiceProfileApproveRetireUiProof.ps1"
Invoke-ProofScript "scripts\Invoke-ManagementPlatformSalesInvoiceSetupNewVersionUiProof.ps1"

Assert-StaticBrowserBoundary
Assert-NoGeneratedArtifactsStaged

Write-Host "Proof passed: repository and branch matched the Codex H worktree contract."
Write-Host "Proof passed: npm ci, Chromium install, typecheck, unit tests, production build, and Playwright E2E completed on isolated ports."
Write-Host "Proof passed: foundation, read UI, Manage UI, activation/retirement UI, and Create New Setup Version proof scripts completed."
Write-Host "Proof passed: terminology, production source security, production dist security, browser-storage implementation, and generated-artifact staging scans completed."
Write-Host "Proof passed: cleanup is delegated to npm run test:e2e, which stops only the Vite process IDs it starts."
Write-Host "Proof passed: script exits successfully."
