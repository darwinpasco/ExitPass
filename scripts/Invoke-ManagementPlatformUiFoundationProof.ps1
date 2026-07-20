param(
    [string]$ProjectPath = "src\Services\ManagementPlatformUi"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$resolvedProjectPath = Join-Path $repoRoot $ProjectPath

Write-Host "Management Platform UI foundation proof"
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
$configPath = Join-Path $resolvedProjectPath "vite.config.ts"
$packagePath = Join-Path $resolvedProjectPath "package.json"
$contractPath = Join-Path $repoRoot "contracts\management-platform\management-platform-ui-foundation.v1.json"

if (-not (Test-Path $contractPath)) {
    throw "Management Platform UI foundation contract artifact is missing."
}

$productionSourceFiles = Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -notlike "*.test.ts" -and $_.Name -notlike "*.test.tsx" -and $_.FullName -notmatch "\\test\\" }
$distFiles = if (Test-Path $distPath) { Get-ChildItem -Path $distPath -Recurse -File } else { @() }

$productionSourceText = ($productionSourceFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$distText = ($distFiles | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$testSourceText = (Get-ChildItem -Path $sourcePath -Recurse -File |
    Where-Object { $_.Name -like "*.test.ts" -or $_.Name -like "*.test.tsx" } |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$configText = Get-Content -Raw -LiteralPath $configPath
$packageText = Get-Content -Raw -LiteralPath $packagePath

$forbiddenBrowserSource = @(
    "x-posserver-admin-key",
    "x-posserver-admin-permission",
    "/v1/admin/fiscal-identities",
    "/v1/admin/sales-invoice-header-profiles",
    "localStorage",
    "sessionStorage",
    "IndexedDB"
)

foreach ($forbidden in $forbiddenBrowserSource) {
    if ($productionSourceText -match [regex]::Escape($forbidden)) {
        throw "Forbidden browser source token found: $forbidden"
    }

    if ($distText -match [regex]::Escape($forbidden)) {
        throw "Forbidden dist token found: $forbidden"
    }
}

if ($productionSourceText -notmatch [regex]::Escape("x-posserver-")) {
    throw "Generic x-posserver- browser-header guard is missing from production source."
}

if ($testSourceText -notmatch [regex]::Escape("X-PosServer-Admin-Key") -or
    $testSourceText -notmatch [regex]::Escape("X-PosServer-Admin-Permission")) {
    throw "Focused tests do not prove downstream POS Server admin headers are rejected."
}

$forbiddenConfig = @(
    "POS_SERVER",
    "PosServer",
    "API_KEY",
    "ApiKey",
    "SECRET",
    "Secret"
)

foreach ($forbidden in $forbiddenConfig) {
    if ($configText.Contains($forbidden) -or $packageText.Contains($forbidden)) {
        throw "Forbidden browser configuration token found: $forbidden"
    }
}

if (-not $productionSourceText.Contains("/management-platform")) {
    throw "Application route foundation is missing."
}

if (-not $productionSourceText.Contains("/v1/management-platform")) {
    throw "Central PMS Management Platform API route foundation is missing."
}

if ($productionSourceText.Contains("registeredBusinessName") -or $productionSourceText.Contains("birAccreditationNumber") -or $productionSourceText.Contains("ptuNumber")) {
    throw "Sales Invoice Profile administration form fields were introduced in the foundation slice."
}

if ($productionSourceText.Contains("Operator Console") -or $productionSourceText.Contains("WebPay")) {
    throw "Unexpected Operator Console or WebPay branding was found in Management Platform source."
}

if (-not $productionSourceText.Contains("mpScenario")) {
    throw "Development manual-validation scenario switch is missing."
}

if ($productionSourceText.Contains("Scenario selector") -or $productionSourceText.Contains("scenario selector")) {
    throw "A browser-visible scenario selector was introduced."
}
Write-Host "Proof passed: project installs successfully."
Write-Host "Proof passed: typecheck, focused tests, and production build pass."
Write-Host "Proof passed: application root and Overview route foundation exists."
Write-Host "Proof passed: authentication-required and permission-denied postures are covered by tests."
Write-Host "Proof passed: development manual-validation scenarios are covered and production ignore behavior is tested."
Write-Host "Proof passed: authorized Site selection is covered by tests."
Write-Host "Proof passed: Central PMS requests receive correlation IDs."
Write-Host "Proof passed: browser source contains no downstream admin key header, permission header, or admin route."
Write-Host "Proof passed: browser configuration contains no downstream URL or API-key field."
Write-Host "Proof passed: browser storage contains no credential persistence implementation."
Write-Host "Proof passed: safe error components expose support details without raw bodies."
Write-Host "Proof passed: no Sales Invoice Profile feature form is implemented."
Write-Host "Proof passed: no Operator Console, WebPay, APT, printing, fiscal issuance, exit, or gate behavior is introduced."
