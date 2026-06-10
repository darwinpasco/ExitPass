namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed record ProductionPolicyImportCandidate(
    int RowNumber,
    string PolicyCode,
    string PolicyName,
    string EntitlementType,
    string LguCode,
    string JurisdictionName,
    string SiteGroupCode,
    string SiteCode,
    string PolicyLevel,
    string PolicyType,
    string PolicyResolutionBasis,
    string BenefitType,
    string DiscountBaseScope,
    string FreeDurationMinutes,
    string InitialRateExempt,
    string FullFeeExempt,
    string OvernightExcluded,
    string ValetExcluded,
    string StandaloneParkingExcluded,
    string DriverOrPassengerRequired,
    string BeneficiaryResidencyScope,
    string RequiresEvidence,
    string RequiredEvidenceType,
    string RequiresOperatorValidation,
    string LegalBasisReference,
    string OrdinanceReference,
    string NationalLawReference,
    string SourceReference,
    string VerificationStatus,
    string EffectiveFrom,
    string EffectiveTo,
    string ReviewedBy,
    string ReviewedAt,
    string ApprovedBy,
    string ApprovedAt,
    string Notes,
    string? ReviewStatus,
    string? ReviewOwner,
    string? LegalReviewDecision,
    string? ProductReviewDecision,
    string? OpsReviewDecision,
    string? EngineeringReviewDecision,
    string? QaReviewDecision,
    string? ApprovalNotes,
    IReadOnlyDictionary<string, string> RawValues);

public sealed record ProductionPolicyImportDryRunRequest(
    string CsvContent,
    string? FileName = null,
    Guid? CorrelationId = null);

public sealed record ProductionPolicyImportDryRunResult(
    bool IsDryRun,
    bool PoliciesImported,
    int TotalRows,
    int ImportableRows,
    int ManualReviewRows,
    int NotImportableRows,
    int DryRunOnlyRows,
    int DuplicateRows,
    int PassCount,
    int WarnCount,
    int FailCount,
    IReadOnlyList<ProductionPolicyImportRowResult> Rows,
    IReadOnlyList<ProductionPolicyImportFinding> Findings,
    Guid? CorrelationId);

public sealed record ProductionPolicyImportRowResult(
    int RowNumber,
    string? PolicyCode,
    string? EntitlementType,
    ProductionPolicyImportRowDecision Decision,
    IReadOnlyList<ProductionPolicyImportFinding> Findings);

public sealed record ProductionPolicyImportFinding(
    ProductionPolicyImportFindingSeverity Severity,
    string Message,
    int? RowNumber = null,
    string? Field = null);

public enum ProductionPolicyImportFindingSeverity
{
    PASS,
    WARN,
    FAIL
}

public enum ProductionPolicyImportRowDecision
{
    IMPORTABLE_AFTER_APPROVAL,
    MANUAL_REVIEW_REQUIRED,
    NOT_IMPORTABLE,
    DRY_RUN_ONLY,
    DUPLICATE_IN_FILE
}
