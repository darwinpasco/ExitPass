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
