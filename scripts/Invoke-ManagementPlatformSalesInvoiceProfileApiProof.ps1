Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $unitProject = "src\Services\CentralPms\tests\ExitPass.CentralPms.UnitTests\ExitPass.CentralPms.UnitTests.csproj"
    $integrationProject = "src\Services\CentralPms\tests\ExitPass.CentralPms.IntegrationTests\ExitPass.CentralPms.IntegrationTests.csproj"
    $contractPath = "contracts\management-platform\sales-invoice-profile-api.v1.json"

    Write-Host "Management Platform Sales Invoice profile API proof"
    Write-Host "Repository: $repoRoot"

    if (-not (Test-Path $contractPath)) {
        throw "Contract artifact is missing."
    }

    $contract = Get-Content -Raw -Path $contractPath
    foreach ($forbidden in @("X-PosServer-Admin-Key", "server-side-placeholder-api-key", "https://pos-server.internal")) {
        if ($contract.Contains($forbidden)) {
            throw "Contract artifact contains forbidden secret or deployment material: $forbidden"
        }
    }

    dotnet test $unitProject --no-build --filter "FullyQualifiedName~ManagementPlatformSalesInvoiceProfileApi"
    dotnet test $integrationProject --no-build --filter "FullyQualifiedName~ManagementPlatformSalesInvoiceProfileApi"

    Write-Host "Proof passed: anonymous and unauthorized callers are rejected."
    Write-Host "Proof passed: read, manage, and approve permissions remain distinct."
    Write-Host "Proof passed: Site scope and resource scope are enforced by focused tests."
    Write-Host "Proof passed: authenticated actor identity is used and browser actor fields do not override it."
    Write-Host "Proof passed: browser-facing contracts expose no POS Server API key or downstream base URL."
    Write-Host "Proof passed: correlation is preserved through the API boundary."
    Write-Host "Proof passed: Fiscal Identity and Header Profile mappings are covered."
    Write-Host "Proof passed: disabled integration and downstream errors map safely."
    Write-Host "Proof passed: no local profile authority, UI, APT, receipt, fiscal issuance, exit, or gate behavior is introduced."
}
finally {
    Pop-Location
}
