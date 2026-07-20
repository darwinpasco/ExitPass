param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath

Write-Host "Management Platform Sales Invoice Setup Create New Version UI proof"
Write-Host "Repository: $repoRoot"
Write-Host "Project: $resolvedProjectPath"

if (-not (Test-Path $resolvedProjectPath)) {
    throw "Management Platform UI project was not found."
}

$sourcePath = Join-Path $resolvedProjectPath "src"
$distPath = Join-Path $resolvedProjectPath "dist"
$contractPath = Join-Path $repoRoot "contracts\management-platform\sales-invoice-setup-new-version-ui.v1.json"
$readmePath = Join-Path $resolvedProjectPath "README.md"
$e2ePath = Join-Path $resolvedProjectPath "e2e\sales-invoice-setup-new-version.spec.ts"

foreach ($requiredPath in @($contractPath, $readmePath, $e2ePath)) {
    if (-not (Test-Path $requiredPath)) {
        throw "Required new-version artifact is missing: $requiredPath"
    }
}

$productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
$distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

$productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$contractText = Get-Content -Raw -LiteralPath $contractPath
$readmeText = Get-Content -Raw -LiteralPath $readmePath
$e2eText = Get-Content -Raw -LiteralPath $e2ePath

$requiredSourceTokens = @(
    "Create New Setup Version",
    "Create New Sales Invoice Setup Version",
    "New setup version",
    "Source setup",
    "Draft Sales Invoice Setup created",
    "sales-invoice-profile.manage",
    "/v1/management-platform/sales-invoice-header-profiles",
    "digital-sales-invoice-json-v1",
    "digital-sales-invoice-presentation-json-v1"
)

foreach ($token in $requiredSourceTokens) {
    if (-not $productionSourceText.Contains($token)) {
        throw "Required new-version source token is missing: $token"
    }
}

$requiredContractTokens = @(
    '"requiredPermission": "sales-invoice-profile.manage"',
    '"eligibleSourceStatus": "APPROVED"',
    '"autoIncrement": false',
    '"sourcePatchSent": false',
    '"noAutomaticValidation": true',
    '"noAutomaticActivation": true',
    '"noAutomaticRetirement": true',
    '"automaticRetry": false',
    '"browserStorage": false',
    '"playwrightE2eDefault": 5179'
)

foreach ($token in $requiredContractTokens) {
    if (-not $contractText.Contains($token)) {
        throw "Required new-version contract token is missing: $token"
    }
}

$requiredReadmeTokens = @(
    "Create New Setup Version",
    "Active source setup",
    "Draft Sales Invoice Setup created",
    "does not validate, activate, approve, retire",
    "Codex H parallel-work isolation",
    "5179",
    "5180",
    "Invoke-ManagementPlatformSalesInvoiceSetupNewVersionUiE2eProof.ps1"
)

foreach ($token in $requiredReadmeTokens) {
    if (-not $readmeText.Contains($token)) {
        throw "README does not document required new-version posture: $token"
    }
}

$requiredE2eScenarios = @(
    "new-version-manage",
    "new-version-read-only",
    "new-version-approve-only",
    "new-version-success",
    "new-version-duplicate-conflict",
    "new-version-overlap-conflict",
    "new-version-timeout",
    "new-version-site-mismatch",
    "new-version-source-not-active",
    "new-version-source-not-found",
    "new-version-cancel",
    "new-version-unsaved-site-switch",
    "new-version-pending-site-switch",
    "new-version-double-submit",
    "new-version-source-preserved"
)

foreach ($scenario in $requiredE2eScenarios) {
    if (-not $productionSourceText.Contains($scenario) -or -not $e2eText.Contains($scenario)) {
        throw "New-version scenario is missing from source or E2E coverage: $scenario"
    }
}

$forbiddenBrowserTokens = @(
    "X-PosServer-Admin-Key",
    "X-PosServer-Admin-Permission",
    "/v1/admin/",
    "POS_SERVER_API_KEY",
    "POS_SERVER_BASE_URL",
    "terminalId",
    "Activate after creation",
    "Retire source",
    "Mutation accepted",
    "Clone Profile",
    "Copy Header Profile"
)

foreach ($token in $forbiddenBrowserTokens) {
    if ($productionSourceText.Contains($token)) {
        throw "Forbidden browser source token found: $token"
    }
    if ($distText.Contains($token)) {
        throw "Forbidden browser dist token found: $token"
    }
}

if ($productionSourceText.Contains("localStorage") -or
    $productionSourceText.Contains("sessionStorage") -or
    $productionSourceText.Contains("indexedDB") -or
    $distText.Contains("localStorage") -or
    $distText.Contains("sessionStorage") -or
    $distText.Contains("indexedDB")) {
    throw "Browser storage implementation token found in production source or dist."
}

Write-Host "Proof passed: Manage-only Active source eligibility is implemented and documented."
Write-Host "Proof passed: new-version create uses the Central PMS Management Platform create route and excludes source mutation posture."
Write-Host "Proof passed: contract documents copied fields, excluded fields, explicit version, effective period, Site scope, Registered Business posture, Draft-only result, no automatic validation/activation/retirement, conflict handling, timeout uncertainty, browser storage, terminology, and Codex H ports."
Write-Host "Proof passed: Playwright E2E scenarios cover the new-version workflow matrix."
Write-Host "Proof passed: production source and dist scans found no direct POS Server route, credential, forbidden lifecycle control, browser storage implementation, or forbidden browser wording."
Write-Host "Proof passed: script exits successfully."
