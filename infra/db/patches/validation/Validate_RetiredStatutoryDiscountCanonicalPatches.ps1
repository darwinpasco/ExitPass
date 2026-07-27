param(
    [string] $CanonicalDbRepo = "D:\SourceCodes\exitpassdb_v1.2"
)

$ErrorActionPreference = "Stop"

$patchRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$retiredRoot = Join-Path $patchRoot "retired"
$retiredValidationRoot = Join-Path $retiredRoot "validation"
$repoRoot = Resolve-Path (Join-Path $patchRoot "..\..\..")
$canonicalGeneratedSql = Join-Path $CanonicalDbRepo "build\generated\exitpass-full-object.generated.sql"
$canonicalValidationSql = Join-Path $CanonicalDbRepo "scripts\validation\Validate-V13CentralPmsAlignment.sql"
$retirementManifest = Join-Path $patchRoot "ExitPass_AppLocal_Db_Patch_Retirement_Manifest_v1.0.md"

$retiredPatches = @(
    "ExitPass_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql",
    "ExitPass_StatutoryDiscountDecisionFacade_v1.3.sql",
    "ExitPass_StatutoryDiscountStagedCanonicalCommands_v1.3.sql",
    "ExitPass_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql",
    "ExitPass_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql",
    "ExitPass_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql"
)

$retiredValidators = @(
    "Validate_OperatorConsoleStatutoryDiscountDecisionConvergence_v1.3.sql",
    "Validate_StatutoryDiscountDecisionFacade_v1.3.sql",
    "Validate_StatutoryDiscountStagedCanonicalCommands_v1.3.sql",
    "Validate_StatutoryDiscountServiceChannelPendingReviewIntake_v1.3.sql",
    "Validate_StatutoryDiscountServiceChannelOperatorConsoleReviewLinkage_v1.3.sql",
    "Validate_StatutoryDiscountServiceChannelPostApprovalApplicationIntent_v1.3.sql"
)

if (-not (Test-Path $retiredRoot -PathType Container)) {
    throw "Retired patch directory not found: $retiredRoot"
}

if (-not (Test-Path $retiredValidationRoot -PathType Container)) {
    throw "Retired validation directory not found: $retiredValidationRoot"
}

$activePatchNames = Get-ChildItem -Path $patchRoot -File -Filter "*.sql" |
    Select-Object -ExpandProperty Name
$activeValidatorNames = Get-ChildItem -Path (Join-Path $patchRoot "validation") -File -Filter "*.sql" |
    Select-Object -ExpandProperty Name

$activeRetiredPatches = $retiredPatches | Where-Object { $activePatchNames -contains $_ }
if ($activeRetiredPatches.Count -gt 0) {
    throw "Promoted statutory patches are still in active top-level patch inventory: $($activeRetiredPatches -join ', ')"
}

$activeRetiredValidators = $retiredValidators | Where-Object { $activeValidatorNames -contains $_ }
if ($activeRetiredValidators.Count -gt 0) {
    throw "Promoted statutory patch validators are still in active validation inventory: $($activeRetiredValidators -join ', ')"
}

$missingRetiredPatches = $retiredPatches | Where-Object {
    -not (Test-Path (Join-Path $retiredRoot $_) -PathType Leaf)
}
if ($missingRetiredPatches.Count -gt 0) {
    throw "Retired statutory patch files are missing: $($missingRetiredPatches -join ', ')"
}

$missingRetiredValidators = $retiredValidators | Where-Object {
    -not (Test-Path (Join-Path $retiredValidationRoot $_) -PathType Leaf)
}
if ($missingRetiredValidators.Count -gt 0) {
    throw "Retired statutory validation files are missing: $($missingRetiredValidators -join ', ')"
}

$duplicateRetiredPatches = $retiredPatches | Where-Object {
    (Test-Path (Join-Path $patchRoot $_) -PathType Leaf) -and
    (Test-Path (Join-Path $retiredRoot $_) -PathType Leaf)
}
if ($duplicateRetiredPatches.Count -gt 0) {
    throw "Promoted statutory patches exist in both active and retired inventory: $($duplicateRetiredPatches -join ', ')"
}

$fixtureRoot = Join-Path $repoRoot "src\Services\CentralPms\tests"
$fixtureReferences = foreach ($name in ($retiredPatches + $retiredValidators)) {
    Get-ChildItem -Path $fixtureRoot -Recurse -File -Filter "*.cs" |
        Select-String -SimpleMatch $name |
        ForEach-Object { "$($_.Path):$($_.LineNumber):$($_.Line.Trim())" }
}
if ($fixtureReferences.Count -gt 0) {
    throw "Active statutory test fixtures still reference promoted retired patches: $($fixtureReferences -join '; ')"
}

if (-not (Test-Path $canonicalGeneratedSql -PathType Leaf)) {
    throw "Canonical generated SQL not found: $canonicalGeneratedSql"
}

if (-not (Test-Path $canonicalValidationSql -PathType Leaf)) {
    throw "Canonical alignment validation SQL not found: $canonicalValidationSql"
}

if (-not (Test-Path $retirementManifest -PathType Leaf)) {
    throw "Patch retirement manifest not found: $retirementManifest"
}

$canonicalSql = Get-Content -Path $canonicalGeneratedSql -Raw
$canonicalValidation = Get-Content -Path $canonicalValidationSql -Raw
$manifest = Get-Content -Path $retirementManifest -Raw

$requiredCanonicalSqlPatterns = @(
    'CREATE TABLE "discounts"."statutory_discount_decision_commands"',
    'CREATE TABLE "discounts"."statutory_discount_payable_basis_application_commands"',
    'CREATE TABLE "operator_console"."statutory_discount_service_channel_reviews"',
    '"id_document_type"',
    '"masked_id_reference"',
    '"statutory_discount_validation_id"',
    'AWAITING_REVIEW',
    'NOT_DECIDED'
)

$missingCanonicalSqlPatterns = $requiredCanonicalSqlPatterns | Where-Object {
    $canonicalSql -notlike "*$_*"
}
if ($missingCanonicalSqlPatterns.Count -gt 0) {
    throw "Canonical generated SQL is missing statutory promoted objects or columns: $($missingCanonicalSqlPatterns -join ', ')"
}

$requiredCanonicalValidationPatterns = @(
    "discounts.statutory_discount_decision_commands",
    "discounts.statutory_discount_payable_basis_application_commands",
    "operator_console.statutory_discount_service_channel_reviews",
    "AWAITING_REVIEW",
    "NOT_DECIDED",
    "service-channel review statutory_discount_validation_id",
    "fk_stat_disc_svc_reviews__validation",
    "ux_stat_disc_svc_reviews__validation"
)

$missingCanonicalValidationPatterns = $requiredCanonicalValidationPatterns | Where-Object {
    $canonicalValidation -notlike "*$_*"
}
if ($missingCanonicalValidationPatterns.Count -gt 0) {
    throw "Canonical alignment validation is missing statutory promoted checks: $($missingCanonicalValidationPatterns -join ', ')"
}

$missingManifestEntries = $retiredPatches | Where-Object {
    $manifest -notlike "*$_*" -or $manifest -notlike "*RETIRED_CANONICAL_SUPERSEDED*"
}
if ($missingManifestEntries.Count -gt 0) {
    throw "Retirement manifest is missing statutory retirement mapping entries: $($missingManifestEntries -join ', ')"
}

Write-Host "Retired statutory discount canonical patch validation passed."
Write-Host "Active top-level patch inventory excludes: $($retiredPatches -join ', ')"
Write-Host "Retired historical patch files are retained under: $retiredRoot"
Write-Host "Retired validation files are retained under: $retiredValidationRoot"
Write-Host "Canonical authority verified: $canonicalGeneratedSql"
