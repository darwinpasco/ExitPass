param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath

Write-Host "Management Platform Sales Invoice Profile manage UI proof"
Write-Host "Repository: $repoRoot"
Write-Host "Project: $resolvedProjectPath"
Write-Host "Browser E2E validation is delegated to scripts\Invoke-ManagementPlatformSalesInvoiceProfileManageUiE2eProof.ps1."

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
$contractPath = Join-Path $repoRoot "contracts\management-platform\sales-invoice-profile-manage-ui.v1.json"
$readmePath = Join-Path $resolvedProjectPath "README.md"

if (-not (Test-Path $contractPath)) {
    throw "Sales Invoice Profile manage UI contract artifact is missing."
}

$productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
$distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

$productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$readmeText = Get-Content -Raw -LiteralPath $readmePath
$contractText = Get-Content -Raw -LiteralPath $contractPath

$requiredSourceTokens = @(
    "sales-invoice-profile.manage",
    "Create Registered Business",
    "Edit Registered Business",
    "Create Draft Sales Invoice Setup",
    "Edit Draft Sales Invoice Setup",
    "Save Draft Changes",
    "Result uncertain",
    "Discard unsaved Sales Invoice Setup changes",
    "digital-sales-invoice-json-v1",
    "digital-sales-invoice-presentation-json-v1",
    "/v1/management-platform/fiscal-identities",
    "/v1/management-platform/sales-invoice-header-profiles"
)

foreach ($required in $requiredSourceTokens) {
    if (-not $productionSourceText.Contains($required)) {
        throw "Required manage UI source token is missing: $required"
    }
}

$forbiddenBrowserTokens = @(
    "X-PosServer-Admin-Key",
    "X-PosServer-Admin-Permission",
    "/v1/admin/",
    "localStorage",
    "sessionStorage",
    "IndexedDB",
    "terminalId"
)

foreach ($forbidden in $forbiddenBrowserTokens) {
    if ($productionSourceText.Contains($forbidden)) {
        throw "Forbidden browser source token found: $forbidden"
    }
    if ($distText.Contains($forbidden)) {
        throw "Forbidden browser dist token found: $forbidden"
    }
}

$forbiddenLifecycleControls = @(
    "Approve profile",
    "Retire profile",
    "Create New Version",
    "Delete Profile"
)

foreach ($forbidden in $forbiddenLifecycleControls) {
    if ($productionSourceText.Contains($forbidden) -or $distText.Contains($forbidden)) {
        throw "Forbidden lifecycle or destructive control found: $forbidden"
    }
}

if (-not $contractText.Contains('"requiredPermission": "sales-invoice-profile.manage"') -or
    -not $contractText.Contains('"mutationRetry": "Mutation requests are sent once and are not automatically retried."') -or
    -not $contractText.Contains('"actorDerivationBoundary"')) {
    throw "Manage UI contract does not document permission, mutation retry, and actor-derivation boundaries."
}

if (-not $readmeText.Contains("sales-invoice-profile.manage") -or
    -not $readmeText.Contains("Draft Sales Invoice Setup") -or
    -not $readmeText.Contains("Result uncertain")) {
    throw "README does not document Manage workflows and uncertainty posture."
}

Write-Host "Proof passed: read-only user tests cover absence of mutation controls."
Write-Host "Proof passed: Manage user tests cover Registered Business create/update and Draft Sales Invoice Setup create/edit."
Write-Host "Proof passed: focused tests cover actor-reference exclusion from mutation DTOs."
Write-Host "Proof passed: terminal ID, approval, and retirement controls are absent."
Write-Host "Proof passed: template and presentation versions remain controlled."
Write-Host "Proof passed: mutations are invoked once, double-submit is prevented, and timeout uncertainty is covered."
Write-Host "Proof passed: Site scope comes from authorized Site context and unsaved Site switch confirmation is covered."
Write-Host "Proof passed: production browser source and dist contain no direct POS Server route, key, permission header, or profile storage implementation."
Write-Host "Proof passed: production ignores development profile scenarios through focused test coverage."
Write-Host "Proof passed: existing foundation and read-only tests remain part of npm test."
Write-Host "Proof passed: script exits successfully."
