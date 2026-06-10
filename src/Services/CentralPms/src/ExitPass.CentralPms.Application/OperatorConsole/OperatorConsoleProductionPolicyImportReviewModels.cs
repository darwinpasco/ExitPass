namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed record ProductionPolicyImportReviewSubmission(
    Guid ReviewId,
    Guid MakerOperatorId,
    string? FileName,
    string SubmissionFingerprint,
    ProductionPolicyImportReviewSubmissionStatus Status,
    ProductionPolicyImportDryRunResult DryRunResult,
    IReadOnlyList<ProductionPolicyImportReviewDecision> ReviewerDecisions,
    IReadOnlyList<ProductionPolicyImportReviewHistoryEntry> History,
    IReadOnlyList<ProductionPolicyImportReviewFinding> Findings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CorrelationId);

public sealed record ProductionPolicyImportReviewFinding(
    ProductionPolicyImportFindingSeverity Severity,
    string Message,
    string? Field = null);

public sealed record ProductionPolicyImportReviewHistoryEntry(
    ProductionPolicyImportReviewDecisionAction Action,
    ProductionPolicyImportReviewSubmissionStatus Status,
    Guid ActorOperatorId,
    ProductionPolicyImportReviewerRole? ReviewerRole,
    string? Reason,
    DateTimeOffset OccurredAt,
    Guid CorrelationId);

public sealed record ProductionPolicyImportReviewSubmitRequest(
    Guid MakerOperatorId,
    string? FileName,
    ProductionPolicyImportDryRunResult DryRunResult,
    Guid? CorrelationId = null);

public sealed record ProductionPolicyImportReviewSubmitResult(
    ProductionPolicyImportReviewSubmission Submission,
    bool PoliciesImported,
    string Message,
    IReadOnlyList<ProductionPolicyImportReviewFinding> Findings);

public sealed record ProductionPolicyImportReviewDecisionRequest(
    Guid ReviewId,
    Guid ReviewerOperatorId,
    ProductionPolicyImportReviewDecisionAction Action,
    string? Reason = null,
    Guid? CorrelationId = null);

public sealed record ProductionPolicyImportReviewDecisionResult(
    ProductionPolicyImportReviewSubmission Submission,
    bool PoliciesImported,
    string Message,
    IReadOnlyList<ProductionPolicyImportReviewFinding> Findings);

public sealed record ProductionPolicyImportReviewDecision(
    ProductionPolicyImportReviewerRole ReviewerRole,
    ProductionPolicyImportReviewDecisionAction Action,
    Guid ReviewerOperatorId,
    string? Reason,
    DateTimeOffset DecidedAt,
    Guid CorrelationId);

public enum ProductionPolicyImportReviewSubmissionStatus
{
    DRAFT_DRY_RUN,
    SUBMITTED_FOR_REVIEW,
    LEGAL_REVIEW_PENDING,
    OPS_REVIEW_PENDING,
    QA_REVIEW_PENDING,
    DB_REVIEW_PENDING,
    APPROVED_FOR_DB_REPO_ALIGNMENT,
    REJECTED,
    CANCELLED,
    SUPERSEDED
}

public enum ProductionPolicyImportReviewDecisionAction
{
    SUBMIT_FOR_REVIEW,
    REQUEST_CHANGES,
    APPROVE_LEGAL,
    APPROVE_OPS,
    APPROVE_QA,
    APPROVE_DB,
    REJECT,
    ESCALATE,
    CANCEL,
    MARK_SUPERSEDED
}

public enum ProductionPolicyImportReviewerRole
{
    LEGAL,
    OPS,
    QA,
    DB
}
