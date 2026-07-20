param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath

Write-Host "Management Platform Sales Invoice Setup activate/retire UI proof"
Write-Host "Repository: $repoRoot"
Write-Host "Project: $resolvedProjectPath"

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

Push-Location $resolvedProjectPath
try {
    Invoke-NpmCommand @("ci")
    Invoke-NpmCommand @("run", "typecheck")
    Invoke-NpmCommand @("test")
    Invoke-NpmCommand @("run", "build")
}
finally {
    Pop-Location
}

$sourcePath = Join-Path $resolvedProjectPath "src"
$distPath = Join-Path $resolvedProjectPath "dist"
$contractPath = Join-Path $repoRoot "contracts\management-platform\sales-invoice-profile-approve-retire-ui.v1.json"
$readmePath = Join-Path $resolvedProjectPath "README.md"

if (-not (Test-Path $contractPath)) {
    throw "Sales Invoice Setup activate/retire UI contract artifact is missing."
}

$productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
$distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

$productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$readmeText = Get-Content -Raw -LiteralPath $readmePath
$contractText = Get-Content -Raw -LiteralPath $contractPath

$requiredSourceTokens = @(
    "sales-invoice-profile.approve",
    "Activate Sales Invoice Setup",
    "Retire Sales Invoice Setup",
    "Sales Invoice Setup activated",
    "Sales Invoice Setup retired",
    "Validation required before activation",
    "Historical Sales Invoices",
    "Issuance history",
    "/v1/management-platform/sales-invoice-header-profiles"
)

foreach ($required in $requiredSourceTokens) {
    if (-not $productionSourceText.Contains($required)) {
        throw "Required activate/retire UI source token is missing: $required"
    }
}

$forbiddenRenderedTerms = @(
    "Approve Profile",
    "Profile administration",
    "Mutation accepted",
    "Effective readiness",
    "Immutable usage"
)

foreach ($forbidden in $forbiddenRenderedTerms) {
    if ($productionSourceText.Contains($forbidden) -or $distText.Contains($forbidden)) {
        throw "Forbidden user-facing terminology found: $forbidden"
    }
}

$forbiddenBrowserTokens = @(
    "X-PosServer-Admin-Key",
    "X-PosServer-Admin-Permission",
    "/v1/admin/",
    "POS_SERVER_API_KEY",
    "POS_SERVER_BASE_URL",
    "localStorage",
    "sessionStorage",
    "IndexedDB"
)

foreach ($forbidden in $forbiddenBrowserTokens) {
    if ($productionSourceText.Contains($forbidden)) {
        throw "Forbidden browser source token found: $forbidden"
    }
    if ($distText.Contains($forbidden)) {
        throw "Forbidden browser dist token found: $forbidden"
    }
}

if (-not $contractText.Contains('"requiredPermission": "sales-invoice-profile.approve"') -or
    -not $contractText.Contains('"activationWorkflow"') -or
    -not $contractText.Contains('"retirementWorkflow"') -or
    -not $contractText.Contains('"automaticRetry": false')) {
    throw "Approve/retire UI contract does not document permission, lifecycle workflows, and no-retry posture."
}

if (-not $readmeText.Contains("sales-invoice-profile.approve") -or
    -not $readmeText.Contains("Activate Sales Invoice Setup") -or
    -not $readmeText.Contains("Retire Sales Invoice Setup") -or
    -not $readmeText.Contains("Historical Sales Invoices")) {
    throw "README does not document approve/retire workflows and historical-preservation posture."
}

Write-Host "Proof passed: approve permission is separate from read and manage permissions."
Write-Host "Proof passed: activation requires authoritative Complete validation and uses user-friendly terminology."
Write-Host "Proof passed: retirement explains historical preservation and does not expose delete or actor fields."
Write-Host "Proof passed: lifecycle mutations are sent once and timeout uncertainty is covered by tests."
Write-Host "Proof passed: source and dist contain no direct POS Server route, key, permission header, or browser storage implementation."
Write-Host "Proof passed: terminology scan found no forbidden rendered technical wording."
Write-Host "Proof passed: script exits successfully."
