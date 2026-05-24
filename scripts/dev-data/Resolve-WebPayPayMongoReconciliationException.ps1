<#
.SYNOPSIS
Developer/operator helper for WebPay PayMongo reconciliation exception workflow.

.DESCRIPTION
Adds notes, submits resolution requests, approves/rejects requests, and reads
workflow history using the existing reconciliation workflow tables only.

This script does not mutate payment attempts, provider sessions, payment
confirmations, exit authorizations, gate authorization consumptions, historical
audit rows, domain events, outbox events, settlement records, or payout records.
#>

param(
    [switch] $AddNote,
    [switch] $SubmitResolutionRequest,
    [switch] $ApproveResolutionRequest,
    [switch] $RejectResolutionRequest,
    [switch] $ReadWorkflow,
    [string] $ExceptionId,
    [string] $RequestId,
    [string] $NoteText,
    [ValidateSet("REVIEW_NOTE", "PROVIDER_CHECK_NOTE", "INTERNAL_CHECK_NOTE", "FINANCIAL_IMPACT_NOTE", "SYSTEM_NOTE")]
    [string] $NoteType = "REVIEW_NOTE",
    [ValidateSet("RESOLVE_NO_ADJUSTMENT", "RESOLVE_WITH_OPERATIONAL_NOTE", "REQUEST_FINANCIAL_ADJUSTMENT", "ACCEPT_PROVIDER_EVIDENCE", "OVERRIDE_RECONCILIATION_STATUS", "REOPEN_EXCEPTION", "CLOSE_EXCEPTION", "CANCEL_EXCEPTION")]
    [string] $ResolutionAction,
    [string] $ResolutionReason,
    [ValidateSet("NONE", "POSSIBLE", "DEFINITE", "CONTROL_ONLY")]
    [string] $FinancialImpact = "NONE",
    [switch] $AdjustmentRequired,
    [ValidateSet("RESOLVED", "REJECTED", "ESCALATED", "CLOSED", "CANCELLED")]
    [string] $ProposedExceptionStatus = "RESOLVED",
    [ValidateSet("APPROVED", "REJECTED")]
    [string] $Decision = "APPROVED",
    [string] $ApprovalReason,
    [string] $RejectionReason,
    [string] $ActorUserId,
    [string] $CorrelationId,
    [string] $DockerComposePath = "infra/docker",
    [string] $DatabaseName = "exitpass_v12_dev",
    [string] $DatabaseUser = "exitpass",
    [switch] $ScriptSelfTest
)

$ErrorActionPreference = "Stop"

$ResultColumns = @(
    "result_status",
    "reconciliation_exception_id",
    "reconciliation_exception_note_id",
    "reconciliation_exception_resolution_request_id",
    "reconciliation_exception_resolution_approval_id",
    "reconciliation_run_id",
    "reconciliation_item_id",
    "workflow_status",
    "summary",
    "correlation_id"
)

$WorkflowColumns = @(
    "record_type",
    "reconciliation_exception_id",
    "reconciliation_exception_note_id",
    "reconciliation_exception_resolution_request_id",
    "reconciliation_exception_resolution_approval_id",
    "reconciliation_exception_status_history_id",
    "reconciliation_run_id",
    "reconciliation_item_id",
    "status",
    "reason_code",
    "summary",
    "detail",
    "actor_user_id",
    "occurred_at",
    "correlation_id"
)

function Resolve-RepoRoot {
    $scriptPath = $PSCommandPath
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        $scriptPath = $MyInvocation.MyCommand.Path
    }

    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        return (Get-Location).Path
    }

    return (Resolve-Path (Join-Path (Split-Path -Parent $scriptPath) "..\..")).Path
}

function Assert-CommandExists {
    param([string] $Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "REQUIRED_COMMAND_NOT_FOUND: $Name"
    }
}

function Split-CsvLine {
    param([string] $Line)

    $reader = [System.IO.StringReader]::new($Line)
    try {
        $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($reader)
        try {
            $parser.SetDelimiters(",")
            $parser.HasFieldsEnclosedInQuotes = $true
            return @($parser.ReadFields())
        }
        finally {
            $parser.Dispose()
        }
    }
    finally {
        $reader.Dispose()
    }
}

function ConvertFrom-StableCsv {
    param(
        [string[]] $Lines,
        [string[]] $Columns
    )

    $header = ($Columns -join ",")
    $headerIndex = -1
    for ($index = 0; $index -lt $Lines.Count; $index++) {
        if ($Lines[$index] -eq $header) {
            $headerIndex = $index
        }
    }

    if ($headerIndex -lt 0) {
        throw "RESULTSET_NOT_FOUND: expected CSV header was not found."
    }

    $rows = @()
    for ($index = $headerIndex + 1; $index -lt $Lines.Count; $index++) {
        $line = $Lines[$index]
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $fields = @(Split-CsvLine $line)
        if ($fields.Count -ne $Columns.Count) {
            break
        }

        $row = [ordered]@{}
        for ($columnIndex = 0; $columnIndex -lt $Columns.Count; $columnIndex++) {
            $row[$Columns[$columnIndex]] = $fields[$columnIndex]
        }

        $rows += [PSCustomObject]$row
    }

    return @($rows)
}

function Invoke-DockerPsql {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser,
        [string] $SqlPath,
        [hashtable] $Variables,
        [string[]] $Columns
    )

    if (-not (Test-Path $SqlPath)) {
        throw "SQL_HELPER_NOT_FOUND: $SqlPath"
    }

    $arguments = @(
        "compose", "exec", "-T", "postgres", "psql",
        "-U", $DatabaseUser,
        "-d", $DatabaseName,
        "-v", "ON_ERROR_STOP=1",
        "-P", "format=csv",
        "-P", "footer=off",
        "-f", "-"
    )

    foreach ($key in ($Variables.Keys | Sort-Object)) {
        $value = $Variables[$key]
        if ($null -eq $value) {
            $value = ""
        }

        $arguments = @("compose", "exec", "-T", "postgres", "psql",
            "-U", $DatabaseUser,
            "-d", $DatabaseName,
            "-v", "ON_ERROR_STOP=1",
            "-v", "$key=$value",
            "-P", "format=csv",
            "-P", "footer=off",
            "-f", "-") + @()
        break
    }

    $arguments = @("compose", "exec", "-T", "postgres", "psql", "-U", $DatabaseUser, "-d", $DatabaseName, "-v", "ON_ERROR_STOP=1")
    foreach ($key in ($Variables.Keys | Sort-Object)) {
        $value = $Variables[$key]
        if ($null -eq $value) {
            $value = ""
        }
        $arguments += @("-v", "$key=$value")
    }
    $arguments += @("-P", "format=csv", "-P", "footer=off", "-f", "-")

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    Push-Location $composePathResolved
    try {
        $output = Get-Content $SqlPath | & docker @arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "WORKFLOW_SQL_FAILED: $message"
    }

    return @(ConvertFrom-StableCsv -Lines @($output | ForEach-Object { $_.ToString() }) -Columns $Columns)
}

function Invoke-SchemaCheck {
    param(
        [string] $RepoRoot,
        [string] $DockerComposePath,
        [string] $DatabaseName,
        [string] $DatabaseUser
    )

    $sql = @"
SELECT expected.table_name, CASE WHEN actual.table_name IS NULL THEN 'MISSING' ELSE 'FOUND' END AS status
FROM (VALUES
    ('reconciliation_exception_notes'),
    ('reconciliation_exception_resolution_requests'),
    ('reconciliation_exception_resolution_approvals'),
    ('reconciliation_exception_status_history')
) AS expected(table_name)
LEFT JOIN information_schema.tables actual
  ON actual.table_schema = 'reconciliation'
 AND actual.table_name = expected.table_name
ORDER BY expected.table_name;
"@

    $composePathResolved = Resolve-Path (Join-Path $RepoRoot $DockerComposePath)
    Push-Location $composePathResolved
    try {
        $output = $sql | docker compose exec -T postgres psql `
            -U $DatabaseUser `
            -d $DatabaseName `
            -v ON_ERROR_STOP=1 `
            -P format=csv `
            -P footer=off
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "SCHEMA_CHECK_FAILED: $($output | Out-String)"
    }

    $rows = @(ConvertFrom-StableCsv -Lines @($output | ForEach-Object { $_.ToString() }) -Columns @("table_name", "status"))
    $missing = @($rows | Where-Object { $_.status -ne "FOUND" })
    if ($missing.Count -gt 0) {
        throw "SCHEMA_CHECK_FAILED: workflow tables missing: $($missing.table_name -join ', ')"
    }

    return $rows
}

function Assert-SingleMode {
    $modeCount = @($AddNote, $SubmitResolutionRequest, $ApproveResolutionRequest, $RejectResolutionRequest, $ReadWorkflow | Where-Object { $_ }).Count
    if ($modeCount -ne 1) {
        throw "INVALID_MODE: choose exactly one of -AddNote, -SubmitResolutionRequest, -ApproveResolutionRequest, -RejectResolutionRequest, or -ReadWorkflow."
    }
}

function Invoke-SelfTest {
    $repoRoot = Resolve-RepoRoot
    $requiredFiles = @(
        "scripts/dev-data/add-webpay-paymongo-reconciliation-exception-note.sql",
        "scripts/dev-data/submit-webpay-paymongo-reconciliation-resolution-request.sql",
        "scripts/dev-data/decide-webpay-paymongo-reconciliation-resolution-request.sql",
        "scripts/dev-data/read-webpay-paymongo-reconciliation-workflow.sql"
    )

    foreach ($relativePath in $requiredFiles) {
        if (-not (Test-Path (Join-Path $repoRoot $relativePath))) {
            throw "SELFTEST_FAILED: missing $relativePath"
        }
    }

    $scriptText = Get-Content $PSCommandPath -Raw
    $sqlText = ($requiredFiles | ForEach-Object { Get-Content (Join-Path $repoRoot $_) -Raw }) -join "`n"
    $outOfScopeProviderToken = -join ([char[]](65, 85, 66))
    if ($scriptText -match $outOfScopeProviderToken -or $sqlText -match $outOfScopeProviderToken) {
        throw "SELFTEST_FAILED: out-of-scope provider reference found."
    }

    Invoke-SchemaCheck `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser | Out-Null

    Write-Host "SELFTEST PASS"
    Write-Host "Schema inspection passed"
    Write-Host "Workflow tables found"
}

Add-Type -AssemblyName Microsoft.VisualBasic

if ($ScriptSelfTest) {
    Assert-CommandExists "docker"
    Invoke-SelfTest
    exit 0
}

Assert-CommandExists "docker"
Assert-SingleMode

$repoRoot = Resolve-RepoRoot

if ($AddNote) {
    if ([string]::IsNullOrWhiteSpace($ExceptionId)) { throw "MISSING_EXCEPTION_ID" }
    if ([string]::IsNullOrWhiteSpace($NoteText)) { throw "MISSING_NOTE_TEXT" }

    $rows = Invoke-DockerPsql `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -SqlPath (Join-Path $repoRoot "scripts/dev-data/add-webpay-paymongo-reconciliation-exception-note.sql") `
        -Variables @{ exception_id = $ExceptionId; note_text = $NoteText; note_type = $NoteType; actor_user_id = $ActorUserId; correlation_id = $CorrelationId } `
        -Columns $ResultColumns
}
elseif ($SubmitResolutionRequest) {
    if ([string]::IsNullOrWhiteSpace($ExceptionId)) { throw "MISSING_EXCEPTION_ID" }
    if ([string]::IsNullOrWhiteSpace($ResolutionAction)) { throw "MISSING_RESOLUTION_ACTION" }
    if ([string]::IsNullOrWhiteSpace($ResolutionReason)) { throw "MISSING_RESOLUTION_REASON" }
    if (($AdjustmentRequired -or $ResolutionAction -eq "REQUEST_FINANCIAL_ADJUSTMENT") -and $FinancialImpact -notin @("POSSIBLE", "DEFINITE")) {
        throw "FINANCIAL_IMPACT_REQUIRED"
    }

    $rows = Invoke-DockerPsql `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -SqlPath (Join-Path $repoRoot "scripts/dev-data/submit-webpay-paymongo-reconciliation-resolution-request.sql") `
        -Variables @{ exception_id = $ExceptionId; resolution_action = $ResolutionAction; resolution_reason = $ResolutionReason; financial_impact = $FinancialImpact; adjustment_required = $AdjustmentRequired.IsPresent.ToString().ToLowerInvariant(); actor_user_id = $ActorUserId; correlation_id = $CorrelationId; proposed_exception_status = $ProposedExceptionStatus } `
        -Columns $ResultColumns
}
elseif ($ApproveResolutionRequest -or $RejectResolutionRequest) {
    if ([string]::IsNullOrWhiteSpace($RequestId)) { throw "MISSING_REQUEST_ID" }
    $effectiveDecision = if ($RejectResolutionRequest) { "REJECTED" } else { $Decision }
    $reason = if ($RejectResolutionRequest) { $RejectionReason } else { $ApprovalReason }
    if ([string]::IsNullOrWhiteSpace($reason)) {
        if ($RejectResolutionRequest) { throw "MISSING_REJECTION_REASON" }
        throw "MISSING_APPROVAL_REASON"
    }

    $rows = Invoke-DockerPsql `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -SqlPath (Join-Path $repoRoot "scripts/dev-data/decide-webpay-paymongo-reconciliation-resolution-request.sql") `
        -Variables @{ request_id = $RequestId; decision = $effectiveDecision; decision_reason = $reason; actor_user_id = $ActorUserId; correlation_id = $CorrelationId } `
        -Columns $ResultColumns
}
elseif ($ReadWorkflow) {
    if ([string]::IsNullOrWhiteSpace($ExceptionId) -and [string]::IsNullOrWhiteSpace($RequestId)) {
        throw "MISSING_WORKFLOW_SCOPE"
    }

    $rows = Invoke-DockerPsql `
        -RepoRoot $repoRoot `
        -DockerComposePath $DockerComposePath `
        -DatabaseName $DatabaseName `
        -DatabaseUser $DatabaseUser `
        -SqlPath (Join-Path $repoRoot "scripts/dev-data/read-webpay-paymongo-reconciliation-workflow.sql") `
        -Variables @{ exception_id = $ExceptionId; request_id = $RequestId } `
        -Columns $WorkflowColumns

    $notFound = @($rows | Where-Object { $_.record_type -in @("RECONCILIATION_EXCEPTION_NOT_FOUND", "RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND", "MISSING_WORKFLOW_SCOPE") })
    if ($notFound.Count -gt 0) {
        Write-Host $notFound[0].record_type
        exit 0
    }

    Write-Host "Reconciliation exception workflow"
    Write-Host "Rows: $($rows.Count)"
    $rows | Format-Table record_type, status, reason_code, summary, occurred_at, reconciliation_exception_resolution_request_id, reconciliation_exception_resolution_approval_id -AutoSize
    exit 0
}

if ($rows.Count -eq 0) {
    throw "NO_WORKFLOW_RESULT"
}

$status = $rows[0].result_status
Write-Host $status
$rows | Format-Table result_status, reconciliation_exception_id, reconciliation_exception_note_id, reconciliation_exception_resolution_request_id, reconciliation_exception_resolution_approval_id, workflow_status, summary -AutoSize

if ($status -in @("RECONCILIATION_EXCEPTION_NOT_FOUND", "RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND", "MISSING_EXCEPTION_ID", "MISSING_REQUEST_ID", "MISSING_NOTE_TEXT", "MISSING_RESOLUTION_ACTION", "MISSING_RESOLUTION_REASON", "MISSING_DECISION_REASON", "RECONCILIATION_RESOLUTION_REQUEST_ALREADY_DECIDED")) {
    exit 0
}
