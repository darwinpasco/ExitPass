param(
    [string] $CanonicalDbRepo = "D:\SourceCodes\exitpassdb_v1.2"
)

$ErrorActionPreference = "Stop"

$patchRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$retiredRoot = Join-Path $patchRoot "retired"
$canonicalGeneratedSql = Join-Path $CanonicalDbRepo "build\generated\exitpass-full-object.generated.sql"
$canonicalValidationSql = Join-Path $CanonicalDbRepo "scripts\validation\Validate-V13CentralPmsAlignment.sql"

$retiredPatches = @(
    "ExitPass_GateAuthorizationConsumedProcessingInbox_v1.2.sql",
    "ExitPass_GateCommandLifecycle_v1.2.sql",
    "ExitPass_GateCommandRetryFailurePolicy_v1.2.sql",
    "ExitPass_HikCentralGateActionAudit_v1.2.sql"
)

if (-not (Test-Path $retiredRoot -PathType Container)) {
    throw "Retired patch directory not found: $retiredRoot"
}

$activePatchNames = Get-ChildItem -Path $patchRoot -File -Filter "*.sql" |
    Select-Object -ExpandProperty Name

$activeRetiredPatches = $retiredPatches | Where-Object { $activePatchNames -contains $_ }
if ($activeRetiredPatches.Count -gt 0) {
    throw "Retired canonical gate patches are still in the active top-level patch inventory: $($activeRetiredPatches -join ', ')"
}

$missingRetiredFiles = $retiredPatches | Where-Object {
    -not (Test-Path (Join-Path $retiredRoot $_) -PathType Leaf)
}
if ($missingRetiredFiles.Count -gt 0) {
    throw "Retired historical gate patch files are missing: $($missingRetiredFiles -join ', ')"
}

$activePatchFiles = Get-ChildItem -Path $patchRoot -File -Filter "*.sql"
$activeObjectReferences = foreach ($file in $activePatchFiles) {
    $sql = Get-Content -Path $file.FullName -Raw
    if ($sql -match "gate_authorization_consumed_processing" -or
        $sql -match "hikcentral_gate_action_audits" -or
        $sql -match "gate_commands" -or
        ($sql -match "retry_policy_code" -and $sql -match "terminal_failure_at")) {
        $file.Name
    }
}

if ($activeObjectReferences.Count -gt 0) {
    throw "Active top-level app-local patches still reference retired canonical gate objects: $($activeObjectReferences -join ', ')"
}

if (-not (Test-Path $canonicalGeneratedSql -PathType Leaf)) {
    throw "Canonical generated SQL not found: $canonicalGeneratedSql"
}

if (-not (Test-Path $canonicalValidationSql -PathType Leaf)) {
    throw "Canonical validation SQL not found: $canonicalValidationSql"
}

$canonicalSql = Get-Content -Path $canonicalGeneratedSql -Raw
$canonicalValidation = Get-Content -Path $canonicalValidationSql -Raw

$requiredCanonicalSqlPatterns = @(
    'CREATE TABLE "gates"."gate_authorization_consumed_processing"',
    'CREATE TABLE "gates"."gate_commands"',
    '"retry_policy_code"',
    '"terminal_failure_at"',
    'CREATE TABLE "gates"."hikcentral_gate_action_audits"'
)

$missingCanonicalSqlPatterns = $requiredCanonicalSqlPatterns | Where-Object {
    $canonicalSql -notlike "*$_*"
}
if ($missingCanonicalSqlPatterns.Count -gt 0) {
    throw "Canonical generated SQL is missing expected gate replacement objects: $($missingCanonicalSqlPatterns -join ', ')"
}

$requiredCanonicalValidationPatterns = @(
    "gates.gate_authorization_consumed_processing",
    "gates.gate_commands",
    "retry_policy_code",
    "terminal_failure_at",
    "gates.hikcentral_gate_action_audits"
)

$missingCanonicalValidationPatterns = $requiredCanonicalValidationPatterns | Where-Object {
    $canonicalValidation -notlike "*$_*"
}
if ($missingCanonicalValidationPatterns.Count -gt 0) {
    throw "Canonical alignment validation is missing expected gate replacement checks: $($missingCanonicalValidationPatterns -join ', ')"
}

Write-Host "Retired canonical gate patch validation passed."
Write-Host "Active top-level patch inventory excludes: $($retiredPatches -join ', ')"
Write-Host "Historical files are retained under: $retiredRoot"
Write-Host "Canonical authority verified: $canonicalGeneratedSql"
