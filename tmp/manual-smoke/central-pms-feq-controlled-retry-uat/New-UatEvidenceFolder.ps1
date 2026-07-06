param(
    [string] $RunId = ("feq-controlled-retry-uat-" + (Get-Date -Format "yyyyMMdd-HHmmss")),
    [string] $OutputRoot = (Join-Path $PSScriptRoot "evidence")
)

$ErrorActionPreference = "Stop"

$runRoot = Join-Path $OutputRoot $RunId
$logs = Join-Path $runRoot "logs"
$screenshots = Join-Path $runRoot "screenshots"
$queries = Join-Path $runRoot "query-exports"

New-Item -ItemType Directory -Force -Path $logs | Out-Null
New-Item -ItemType Directory -Force -Path $screenshots | Out-Null
New-Item -ItemType Directory -Force -Path $queries | Out-Null

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$checklistSource = Join-Path $repoRoot "docs\v1.3\fiscal-exception-queue\runbooks\ExitPass_FEQ_Controlled_Retry_Execution_UAT_Evidence_Checklist_v1.0.md"
$checklistTarget = Join-Path $runRoot "Evidence_Checklist.md"

if (Test-Path $checklistSource) {
    Copy-Item -LiteralPath $checklistSource -Destination $checklistTarget -Force
}

$manifest = @(
    "# FEQ Controlled Retry Execution UAT Evidence",
    "",
    "Run id: $RunId",
    "Created at: $(Get-Date -Format o)",
    "",
    "Folders:",
    "- logs",
    "- screenshots",
    "- query-exports",
    "",
    "Do not store secrets, raw payment payloads, raw POS Server payloads, customer PII, statutory evidence, or canonical source text in this folder."
)

Set-Content -LiteralPath (Join-Path $runRoot "README.md") -Value $manifest -Encoding UTF8

Write-Host "Created UAT evidence folder: $runRoot"
Write-Host "Checklist: $checklistTarget"
