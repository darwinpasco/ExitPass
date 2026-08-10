[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$CanonicalDatabaseRepository = "D:\SourceCodes\exitpassdb_v1.2"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")).Path
}

$authorizedRelativePaths = @(
    "docs\v1.3\webpay\runbooks\ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md",
    "scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql",
    "scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1",
    "scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1",
    "scripts\v1.3\webpay\Test-WebPayStatutoryDiscountWalkthroughHarness.ps1",
    "scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"
)

$assets = @{}
foreach ($relativePath in $authorizedRelativePaths) {
    $path = Join-Path $RepositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required walkthrough asset is missing: $relativePath"
    }

    $assets[$relativePath] = Get-Content -LiteralPath $path -Raw
}

function Assert-Contains {
    param([string]$Text, [string]$Literal, [string]$Context)
    if ($Text.IndexOf($Literal, [StringComparison]::Ordinal) -lt 0) {
        throw "$Context is missing required literal: $Literal"
    }
}

function Assert-NotContains {
    param([string]$Text, [string]$Literal, [string]$Context)
    if ($Text.IndexOf($Literal, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "$Context contains forbidden literal: $Literal"
    }
}

function Assert-PowerShellParses {
    param([string]$Path)
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        $detail = ($errors | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Message)" }) -join "; "
        throw "PowerShell syntax failed for ${Path}: $detail"
    }
}

$powerShellPaths = @(
    (Join-Path $PSScriptRoot "Start-WebPayStatutoryDiscountWalkthrough.ps1"),
    (Join-Path $PSScriptRoot "Stop-WebPayStatutoryDiscountWalkthrough.ps1"),
    (Join-Path $PSScriptRoot "Test-WebPayStatutoryDiscountWalkthroughHarness.ps1")
)
foreach ($path in $powerShellPaths) {
    Assert-PowerShellParses -Path $path
}

$start = $assets["scripts\v1.3\webpay\Start-WebPayStatutoryDiscountWalkthrough.ps1"]
$stop = $assets["scripts\v1.3\webpay\Stop-WebPayStatutoryDiscountWalkthrough.ps1"]
$seed = $assets["scripts\v1.3\webpay\Seed-WebPayStatutoryDiscountWalkthrough.sql"]
$verify = $assets["scripts\v1.3\webpay\Verify-WebPayStatutoryDiscountWalkthrough.sql"]
$runbook = $assets["docs\v1.3\webpay\runbooks\ExitPass_WebPay_Statutory_Discount_Local_Walkthrough_v1.0.md"]
$operationalText = @($start, $stop, $seed, $verify, $runbook) -join "`n"
$executableText = @($start, $stop, $seed, $verify) -join "`n"

# Current tracked composition and source references.
$trackedReferences = @(
    "scripts\v1.3\webpay\Seed-WebPayLocalIntegrationWalkthrough.sql",
    "scripts\operator-console\Seed-StatutoryDiscountPilotFixture.sql",
    "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql",
    "infra\db\patches\ExitPass_PaymentProviderRoutingPolicy_v1.2.sql",
    "infra\db\patches\ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql",
    "src\Services\CentralPms\src\ExitPass.CentralPms.Api\ExitPass.CentralPms.Api.csproj",
    "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\ExitPass.PaymentOrchestrator.Api.csproj",
    "src\Services\WebPayUi\package.json",
    "src\Services\OperatorConsoleUi\package.json"
)
foreach ($relativePath in $trackedReferences) {
    if (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $relativePath) -PathType Leaf)) {
        throw "Current tracked dependency reference is missing: $relativePath"
    }
}

$paymentEndpointSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Endpoints\WebPayPaymentIntentEndpoints.cs") -Raw
$evidenceEndpointSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\PaymentOrchestrator\src\ExitPass.PaymentOrchestrator.Api\Endpoints\WebPayStatutoryEvidenceEndpoints.cs") -Raw
$humanAuthenticationSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\HumanAuthenticationEndpoints.cs") -Raw
$operatorReviewSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\OperatorConsoleStatutoryDiscountDraftEndpoints.cs") -Raw
$operatorEvidenceSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Endpoints\OperatorConsoleStatutoryEvidenceReviewEndpoints.cs") -Raw
$fixtureGuardSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Api\Security\ProductionFixtureIdentityHeaderGuardMiddleware.cs") -Raw
$rbacCatalogSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "src\Services\CentralPms\src\ExitPass.CentralPms.Application\Security\CentralPmsRbacPolicyCatalog.cs") -Raw
$rbacSource = Get-Content -LiteralPath (Join-Path $RepositoryRoot "scripts\management-platform\Seed-ManagementPlatformUatIdentityRbac.sql") -Raw

$canonicalDdl = Join-Path $CanonicalDatabaseRepository "build\generated\exitpass-full-object.generated.sql"
$canonicalValidator = Join-Path $CanonicalDatabaseRepository "scripts\validation\Validate-V13CentralPmsAlignment.sql"
foreach ($path in @($canonicalDdl, $canonicalValidator)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Canonical database dependency is missing: $path"
    }
}

# Current configuration and authentication boundaries.
foreach ($literal in @(
    "VITE_WEBPAY_DEFAULT_VENDOR_SYSTEM_ID",
    "VITE_WEBPAY_DEFAULT_SITE_ID",
    "VITE_WEBPAY_DEFAULT_SITE_GROUP_ID",
    "Integrations__CentralPms__StatutoryDiscounts__WebPayServiceIdentityId",
    "CentralPms__StatutoryEvidence__Upload",
    "CentralPms__StatutoryEvidence__Channel",
    "CentralPms__StatutoryEvidence__ScanWorker",
    "ASPNETCORE_ENVIRONMENT",
    "Production",
    "EXITPASS_WEBPAY_STATUTORY_REVIEWER_PASSWORD",
    "EXITPASS_WEBPAY_STATUTORY_TOTP_PROTECTION_KEY"
)) {
    Assert-Contains -Text $start -Literal $literal -Context "startup script"
}

foreach ($literal in @(
    "/v1/human-authentication/login",
    "/v1/human-authentication/session",
    "/v1/human-authentication/logout",
    "X-ExitPass-User-Id",
    "X-Operator-User-Id",
    "fixture identity",
    "Site Group",
    "GLOBAL"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook authentication boundary"
}

foreach ($permission in @(
    "statutory-discounts.review.queue.read",
    "statutory-discounts.review.detail.read",
    "statutory-discounts.decision.review",
    "statutory-discounts.decision.approve",
    "statutory-discounts.decision.reject",
    "statutory-discounts.evidence.review.view"
)) {
    Assert-Contains -Text $seed -Literal $permission -Context "seed reviewer authority"
    Assert-Contains -Text $runbook -Literal $permission -Context "runbook reviewer authority"
    Assert-Contains -Text $rbacSource -Literal $permission -Context "tracked RBAC authority source"
}

# Current endpoint paths.
foreach ($path in @(
    "/v1/webpay/statutory-discounts/availability",
    "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover",
    "/v1/webpay/statutory-discounts/decisions",
    "/apply-payable-basis",
    "/v1/webpay/statutory-discounts/evidence/bootstrap",
    "/v1/webpay/statutory-discounts/evidence/status",
    "/v1/webpay/statutory-discounts/evidence/upload-sessions",
    "/finalize",
    "/v1/ops/operator-console/statutory-discounts/reviews/pending",
    "/v1/ops/operator-console/statutory-discounts/reviews/",
    "/evidence/",
    "/preview",
    "/v1/webpay/payment-intents"
)) {
    Assert-Contains -Text $runbook -Literal $path -Context "runbook endpoint map"
}
foreach ($path in @(
    "/v1/webpay/statutory-discounts/availability",
    "/v1/webpay/statutory-discounts/pending-lifecycle/rediscover",
    "/v1/webpay/statutory-discounts/decisions",
    "/apply-payable-basis",
    "/v1/webpay/payment-intents"
)) {
    Assert-Contains -Text $paymentEndpointSource -Literal $path -Context "current Payment Orchestrator endpoint source"
}
foreach ($path in @(
    "/v1/webpay/statutory-discounts/evidence/bootstrap",
    "/v1/webpay/statutory-discounts/evidence/status",
    "/v1/webpay/statutory-discounts/evidence/upload-sessions",
    "/finalize"
)) {
    Assert-Contains -Text $evidenceEndpointSource -Literal $path -Context "current evidence endpoint source"
}
foreach ($path in @("/v1/human-authentication", "/login", "/session", "/logout")) {
    Assert-Contains -Text $humanAuthenticationSource -Literal $path -Context "current human-authentication source"
}
foreach ($path in @("/statutory-discounts/reviews/pending", "/statutory-discounts/reviews/{statutoryDiscountDecisionCommandId:guid}", "/decision")) {
    Assert-Contains -Text $operatorReviewSource -Literal $path -Context "current Operator Console review source"
}
foreach ($path in @("/v1/ops/operator-console/statutory-discounts/reviews", "/evidence", "/preview")) {
    Assert-Contains -Text $operatorEvidenceSource -Literal $path -Context "current Operator Console evidence source"
}
foreach ($header in @("X-ExitPass-User-Id", "X-ExitPass-Permissions")) {
    Assert-Contains -Text $rbacCatalogSource -Literal $header -Context "fixture identity header catalog"
}
foreach ($literal in @("X-Operator-User-Id", "AllowFixtureIdentityHeaders", "FIXTURE_IDENTITY_HEADER_PROHIBITED")) {
    Assert-Contains -Text $fixtureGuardSource -Literal $literal -Context "Production fixture identity guard"
}

# Availability, lifecycle, decision, application, and handoff coverage.
foreach ($literal in @(
    "Supported jurisdiction",
    "Unsupported entitlement",
    "Missing jurisdiction",
    "Ambiguous jurisdiction",
    "No applicable ordinance",
    "Manipulated direct submission",
    "ordinary-payment fallback",
    "opaque upload",
    "streaming",
    "finalization",
    "validation",
    "malware",
    "reviewable",
    "pending review",
    "second tab",
    "idempotent replay",
    "restart",
    "applied payable basis",
    "provider session",
    "payment handoff"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook scenario coverage"
}

# Seed and verifier SQL structure.
Assert-Contains -Text $seed -Literal "BEGIN;" -Context "seed SQL"
Assert-Contains -Text $seed -Literal "COMMIT;" -Context "seed SQL"
Assert-Contains -Text $seed -Literal "current_database() !~ '^exitpass_webpay_local_walkthrough_statutory" -Context "seed database guard"
Assert-Contains -Text $seed -Literal "username_normalized" -Context "seed business-key discovery"
Assert-Contains -Text $seed -Literal "statutory_evidence_principal_scope_grants" -Context "seed evidence scope"
Assert-Contains -Text $seed -Literal "SENIOR_CITIZEN_ID" -Context "seed current evidence type"
Assert-Contains -Text $verify -Literal "BEGIN TRANSACTION READ ONLY;" -Context "verification SQL"
Assert-Contains -Text $verify -Literal "ROLLBACK;" -Context "verification SQL"
foreach ($staleColumn in @("provider_status_code", "recovery_action")) {
    Assert-NotContains -Text ($seed + $verify) -Literal $staleColumn -Context "SQL package"
}
foreach ($currentColumn in @("command_status", "result_classification", "recovery_classification", "safe_error_code", "session_status")) {
    Assert-Contains -Text $verify -Literal $currentColumn -Context "verification SQL current columns"
}
if ($verify -match '(?im)^\s*(INSERT|UPDATE|DELETE|MERGE|TRUNCATE|CREATE|ALTER|DROP|CALL|DO)\b') {
    throw "Verification SQL contains a mutating statement."
}
if (($seed.ToCharArray() | Where-Object { $_ -eq '(' }).Count -ne ($seed.ToCharArray() | Where-Object { $_ -eq ')' }).Count) {
    throw "Seed SQL has unbalanced parentheses."
}
if (($verify.ToCharArray() | Where-Object { $_ -eq '(' }).Count -ne ($verify.ToCharArray() | Where-Object { $_ -eq ')' }).Count) {
    throw "Verification SQL has unbalanced parentheses."
}

$canonicalTableSources = @(
    "objects\schemas\discounts\tables\discounts.statutory_discount_decision_commands.sql",
    "objects\schemas\discounts\tables\discounts.statutory_discount_payable_basis_application_commands.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_sets.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_items.sql",
    "objects\schemas\discounts\tables\discounts.statutory_evidence_scan_attempts.sql",
    "objects\schemas\core\tables\core.payment_attempts.sql",
    "objects\schemas\payments\tables\payments.provider_sessions.sql"
)
$canonicalTableText = @($canonicalTableSources | ForEach-Object {
    $path = Join-Path $CanonicalDatabaseRepository $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Canonical table source missing: $path" }
    Get-Content -LiteralPath $path -Raw
}) -join "`n"
foreach ($column in @("command_status", "result_classification", "recovery_classification", "safe_error_code", "session_status")) {
    Assert-Contains -Text $canonicalTableText -Literal $column -Context "canonical current database columns"
}
foreach ($staleColumn in @("provider_status_code", "recovery_action")) {
    Assert-NotContains -Text $canonicalTableText -Literal $staleColumn -Context "canonical current database columns"
}

# Startup, shutdown, and cleanup safety.
foreach ($literal in @(
    "exitpass_webpay_local_walkthrough_statutory",
    "exitpass.walkthrough=webpay-statutory-discount",
    "Get-NetTCPConnection",
    "ExecutablePath",
    "CommandLineMarkers",
    "StartTimeUtc",
    "RestartServicesOnly"
)) {
    Assert-Contains -Text $start -Literal $literal -Context "startup ownership/recovery"
}
foreach ($literal in @(
    "ExecutablePath",
    "CommandLineMarkers",
    "StartTimeUtc",
    "refusing to stop",
    "RemoveDisposableDatabase",
    "RemoveGeneratedState",
    "StopWalkthroughContainers"
)) {
    Assert-Contains -Text $stop -Literal $literal -Context "shutdown ownership guard"
}
Assert-NotContains -Text $operationalText -Literal "infra\docker\docker-compose.yml" -Context "walkthrough package"
Assert-NotContains -Text $executableText -Literal "git clean" -Context "walkthrough executable assets"
Assert-NotContains -Text $executableText -Literal "DROP DATABASE IF EXISTS exitpass_v12_dev" -Context "walkthrough executable assets"
Assert-NotContains -Text $start -Literal 'Invoke-PostgresFile $DatabaseName $rbacSource' -Context "startup fixture-guard handling"

# Secret-shaped values and unsafe credential transport. Environment-variable names
# and documentation about prohibited secrets are allowed; literal values are not.
$secretPatterns = @(
    '(?i)sk_live_[A-Za-z0-9]+',
    '(?i)pk_live_[A-Za-z0-9]+',
    '(?i)-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)otpauth://',
    '(?i)Authorization\s*:\s*Bearer\s+[A-Za-z0-9._-]+'
)
foreach ($pattern in $secretPatterns) {
    if ($operationalText -match $pattern) {
        throw "Secret-shaped literal found by pattern: $pattern"
    }
}
$assignmentPattern = '(?i)(password|secret|token)\s*=\s*["''][^$%{][^"'']{7,}["'']'
foreach ($match in [regex]::Matches($operationalText, $assignmentPattern)) {
    if ($match.Value -notmatch '\$env:') {
        throw "Secret-shaped assignment found in walkthrough assets."
    }
}

foreach ($claimPattern in @(
    '(?im)^\s*Controlled UAT\s*:\s*(passed|complete|ready|authorized)',
    '(?im)^\s*Production (validation|rollout)\s*:\s*(passed|complete|ready|authorized)',
    '(?i)\b(is|was)\s+(BIR|compliance) certified\b'
)) {
    if ($operationalText -match $claimPattern) {
        throw "Walkthrough assets contain an unauthorized validation claim."
    }
}

# Confirm the package itself states the intended limitations.
foreach ($literal in @(
    "local-development validation",
    "not Controlled UAT",
    "not compliance certification",
    "not production validation",
    "not production rollout authorization",
    "static harness does not execute the walkthrough"
)) {
    Assert-Contains -Text $runbook -Literal $literal -Context "runbook exclusions"
}

Write-Host "WebPay statutory-discount walkthrough static validation passed." -ForegroundColor Green
