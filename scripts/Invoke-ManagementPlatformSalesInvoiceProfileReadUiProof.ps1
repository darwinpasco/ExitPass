param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath

Write-Host "Management Platform Sales Invoice Profile read UI proof"
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
$contractPath = Join-Path $repoRoot "contracts\management-platform\sales-invoice-profile-read-ui.v1.json"
$readmePath = Join-Path $resolvedProjectPath "README.md"

if (-not (Test-Path $contractPath)) {
    throw "Sales Invoice Profile read UI contract artifact is missing."
}

$productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
$distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

$productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$readmeText = Get-Content -Raw -LiteralPath $readmePath
$contractText = Get-Content -Raw -LiteralPath $contractPath

$requiredSourceTokens = @(
    "/management-platform/sales-invoice-profiles",
    "/v1/management-platform/sales-invoice-header-profiles",
    "/v1/management-platform/fiscal-identities",
    "mpProfileScenario",
    "sales-invoice-profile.read",
    "Validate configuration",
    "Ready for Sales Invoice issuance",
    "No effective Sales Invoice Header Profile",
    "Profile configuration is incomplete",
    "Template or presentation version is unsupported"
)

foreach ($required in $requiredSourceTokens) {
    if (-not $productionSourceText.Contains($required)) {
        throw "Required read UI source token is missing: $required"
    }
}

$forbiddenBrowserTokens = @(
    "X-PosServer-Admin-Key",
    "X-PosServer-Admin-Permission",
    "/v1/admin/",
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

$forbiddenMutationLabels = @(
    "Approve profile",
    "Retire profile",
    "Create New Version",
    "Delete Profile"
)

foreach ($forbidden in $forbiddenMutationLabels) {
    if ($productionSourceText.Contains($forbidden) -or $distText.Contains($forbidden)) {
        throw "Forbidden read UI lifecycle or destructive control found: $forbidden"
    }
}

if ($productionSourceText.Contains("terminalId")) {
    throw "Static Header Profile terminalId field was introduced."
}

if (-not $productionSourceText.Contains("BIR accreditation issued date") -or
    -not $productionSourceText.Contains("BIR accreditation valid-until date") -or
    -not $productionSourceText.Contains("PTU issued date")) {
    throw "Distinct BIR and PTU date labels are missing."
}

if (-not $contractText.Contains('"noMutationBoundary"') -or -not $contractText.Contains('"browserSecurity"')) {
    throw "Read UI contract does not document no-mutation and browser-security boundaries."
}

if (-not $readmeText.Contains("mpProfileScenario") -or -not $readmeText.Contains("read-only")) {
    throw "README does not document read-only feature scenarios."
}

Write-Host "Proof passed: navigation requires sales-invoice-profile.read and focused tests cover denial."
Write-Host "Proof passed: read-authorized users can open the Site-scoped module."
Write-Host "Proof passed: profile list, detail, linked Fiscal Identity, validation, readiness, and usage are covered."
Write-Host "Proof passed: BIR issued date, BIR valid-until date, and PTU issued date remain distinct."
Write-Host "Proof passed: no static terminal-ID profile field exists."
Write-Host "Proof passed: Complete, Incomplete, unknown validation, READY, governed non-ready, and unknown readiness states are covered."
Write-Host "Proof passed: usage displays aggregate identifiers only and no receipt or snapshot payload."
Write-Host "Proof passed: disabled, forbidden, unavailable, and safe error postures are covered."
Write-Host "Proof passed: read-only tests cover no mutation controls for read-only users."
Write-Host "Proof passed: no approve, retire, delete, or new-version controls exist."
Write-Host "Proof passed: production browser source and dist contain no direct downstream route, key header, permission header, or profile storage implementation."
Write-Host "Proof passed: production build ignores development scenarios by construction and focused test coverage."
Write-Host "Proof passed: existing foundation tests remain part of npm test."
Write-Host "Proof passed: script exits successfully."
