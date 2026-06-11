namespace ExitPass.CentralPms.Contracts.OperatorConsole;

/// <summary>
/// Request body for dry-run validation of production statutory discount policy import candidates.
/// </summary>
public sealed record OperatorConsoleProductionPolicyImportDryRunRequest(
    string CsvContent,
    string? FileName = null,
    Guid? SubmittedByOperatorId = null,
    Guid? CorrelationId = null);

/// <summary>
/// Response body for production statutory discount policy import dry-run validation.
/// </summary>
public sealed record OperatorConsoleProductionPolicyImportDryRunResponse(
    bool Imported,
    int ImportedRowCount,
    bool DryRunOnly,
    string Message,
    OperatorConsoleProductionPolicyImportDryRunSummary Summary,
    IReadOnlyList<OperatorConsoleProductionPolicyImportDryRunRow> Rows,
    Guid CorrelationId);

public sealed record OperatorConsoleProductionPolicyImportDryRunSummary(
    int TotalRows,
    int PassCount,
    int WarnCount,
    int FailCount,
    int ImportableCount,
    int ManualReviewCount,
    int NotImportableCount,
    int DryRunOnlyCount,
    int DuplicateCount);

public sealed record OperatorConsoleProductionPolicyImportDryRunRow(
    int RowNumber,
    string? PolicyCode,
    string? EntitlementType,
    string Decision,
    IReadOnlyList<OperatorConsoleProductionPolicyImportDryRunFinding> Findings);

public sealed record OperatorConsoleProductionPolicyImportDryRunFinding(
    string Severity,
    string Code,
    string Message,
    string? FieldName);

/// <summary>
/// Request body for submitting a dry-run production policy import result to DB-backed review.
/// </summary>
public sealed record OperatorConsoleProductionPolicyImportReviewSubmitRequest(
    OperatorConsoleProductionPolicyImportDryRunResponse DryRunResult,
    string? FileName = null,
    Guid? SubmittedByOperatorId = null,
    Guid? CorrelationId = null);

/// <summary>
/// Request body for deciding a DB-backed production policy import review.
/// </summary>
public sealed record OperatorConsoleProductionPolicyImportReviewDecisionRequest(
    string Action,
    string? Reason = null,
    Guid? ReviewerOperatorId = null,
    Guid? CorrelationId = null);

/// <summary>
/// Response body for production policy import review submission and decisions.
/// </summary>
public sealed record OperatorConsoleProductionPolicyImportReviewResponse(
    bool Imported,
    bool ProductionPolicyActivationBlocked,
    string Message,
    OperatorConsoleProductionPolicyImportReviewSubmission Submission,
    IReadOnlyList<OperatorConsoleProductionPolicyImportReviewFinding> Findings,
    Guid CorrelationId);

public sealed record OperatorConsoleProductionPolicyImportReviewListResponse(
    bool Imported,
    bool ProductionPolicyActivationBlocked,
    IReadOnlyList<OperatorConsoleProductionPolicyImportReviewListItem> Items,
    int TotalCount,
    int Limit,
    int Offset,
    Guid CorrelationId);

public sealed record OperatorConsoleProductionPolicyImportReviewListItem(
    bool Imported,
    bool ProductionPolicyActivationBlocked,
    OperatorConsoleProductionPolicyImportReviewSubmission Submission,
    IReadOnlyList<OperatorConsoleProductionPolicyImportReviewFinding> Findings);

public sealed record OperatorConsoleProductionPolicyImportReviewSubmission(
    Guid ReviewId,
    Guid MakerOperatorId,
    string? FileName,
    string Status,
    OperatorConsoleProductionPolicyImportDryRunSummary DryRunSummary,
    IReadOnlyList<OperatorConsoleProductionPolicyImportReviewDecision> ReviewerDecisions,
    IReadOnlyList<OperatorConsoleProductionPolicyImportReviewHistoryEntry> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OperatorConsoleProductionPolicyImportReviewDecision(
    string ReviewerRole,
    string Action,
    Guid ReviewerOperatorId,
    string? Reason,
    DateTimeOffset DecidedAt,
    Guid CorrelationId);

public sealed record OperatorConsoleProductionPolicyImportReviewHistoryEntry(
    string Action,
    string Status,
    Guid ActorOperatorId,
    string? ReviewerRole,
    string? Reason,
    DateTimeOffset OccurredAt,
    Guid CorrelationId);

public sealed record OperatorConsoleProductionPolicyImportReviewFinding(
    string Severity,
    string Message,
    string? FieldName);
