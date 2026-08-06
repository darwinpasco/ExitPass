namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Catalog of Operator Console controlled actions.
///
/// Design reference: docs/operator-console/OperatorConsole_Access_Readiness_API_Backend_Design_v1.md.
/// Invariant: controlled actions require explicit readiness metadata before production enforcement.
/// </summary>
public sealed class OperatorConsoleActionCatalog
{
    private static readonly IReadOnlyList<OperatorConsoleActionCatalogEntry> Entries =
    [
        Write(OperatorConsoleActionCodes.SessionLookup, roleExpectation: "OPERATOR_OR_SUPERVISOR", auditClassification: "CONTROLLED_LOOKUP"),
        Write(OperatorConsoleActionCodes.CreateStatutoryDiscountDraft, roleExpectation: "OPERATOR_OR_SUPERVISOR", auditClassification: "CONTROLLED_WRITE"),
        Read(OperatorConsoleActionCodes.ViewStatutoryDiscountDraft, roleExpectation: "OPERATOR_SUPERVISOR_OR_AUDITOR", auditClassification: "SENSITIVE_READ"),
        Write(OperatorConsoleActionCodes.DecideStatutoryDiscount, roleExpectation: "OPERATOR_APPROVER_OR_SUPERVISOR", auditClassification: "CONTROLLED_DECISION"),
        Write(OperatorConsoleActionCodes.CaptureEvidence, roleExpectation: "OPERATOR_OR_SUPERVISOR", auditClassification: "SENSITIVE_WRITE"),
        Read(OperatorConsoleActionCodes.ViewEvidence, roleExpectation: "OPERATOR_SUPERVISOR_OR_COMPLIANCE", auditClassification: "SENSITIVE_READ"),
        Read(OperatorConsoleActionCodes.ReviewEvidence, roleExpectation: "AUTHORIZED_REVIEWER_OR_SUPERVISOR", auditClassification: "RESTRICTED_EVIDENCE_READ"),
        Write(OperatorConsoleActionCodes.ApplyStatutoryDiscountPayableBasis, roleExpectation: "OPERATOR_APPROVER_OR_SUPERVISOR", auditClassification: "CONTROLLED_WRITE"),
        Read(OperatorConsoleActionCodes.ViewPolicyResolution, roleExpectation: "OPERATOR_SUPERVISOR_OR_AUDITOR", auditClassification: "POLICY_READ"),
        Read(OperatorConsoleActionCodes.SupervisorReview, roleExpectation: "SUPERVISOR", auditClassification: "SUPERVISOR_REVIEW"),
        Write(OperatorConsoleActionCodes.SupervisorOverride, roleExpectation: "SUPERVISOR_OR_OPERATIONS_ADMIN", auditClassification: "CONTROLLED_OVERRIDE"),
        Read(OperatorConsoleActionCodes.ViewAuditReport, roleExpectation: "SUPERVISOR_OPERATIONS_OR_COMPLIANCE", auditClassification: "AUDIT_READ"),
        Write(OperatorConsoleActionCodes.VoidFiscalDocument, roleExpectation: "SUPERVISOR_OR_AUTHORIZED_OPERATOR", auditClassification: "CONTROLLED_FISCAL_VOID"),
        Read(OperatorConsoleActionCodes.ViewFiscalStatusViewAuditReport, roleExpectation: "SUPERVISOR_COMPLIANCE_OR_SUPPORT", auditClassification: "AUDIT_READ"),
        Read(OperatorConsoleActionCodes.ViewFiscalVoidActionAuditReport, roleExpectation: "SUPERVISOR_COMPLIANCE_OR_SUPPORT", auditClassification: "AUDIT_READ")
    ];

    private static readonly IReadOnlyDictionary<string, OperatorConsoleActionCatalogEntry> EntriesByCode =
        Entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);

    /// <summary>Returns all known Operator Console action metadata.</summary>
    public IReadOnlyList<OperatorConsoleActionCatalogEntry> GetAll() => Entries;

    /// <summary>Returns true when the action code is known to the readiness foundation.</summary>
    public bool Contains(string? actionCode) =>
        !string.IsNullOrWhiteSpace(actionCode) &&
        EntriesByCode.ContainsKey(Normalize(actionCode));

    /// <summary>Returns action metadata, if present.</summary>
    public OperatorConsoleActionCatalogEntry? Find(string? actionCode) =>
        !string.IsNullOrWhiteSpace(actionCode) && EntriesByCode.TryGetValue(Normalize(actionCode), out var entry)
            ? entry
            : null;

    private static OperatorConsoleActionCatalogEntry Read(
        string code,
        string roleExpectation,
        string auditClassification) =>
        new(
            code,
            roleExpectation,
            DeviceRequired: true,
            ActiveShiftRequired: true,
            SiteMatchRequired: true,
            Classification: "READ",
            auditClassification,
            ProductionBlockerIfNotEnforced: false);

    private static OperatorConsoleActionCatalogEntry Write(
        string code,
        string roleExpectation,
        string auditClassification) =>
        new(
            code,
            roleExpectation,
            DeviceRequired: true,
            ActiveShiftRequired: true,
            SiteMatchRequired: true,
            Classification: "WRITE",
            auditClassification,
            ProductionBlockerIfNotEnforced: true);

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

/// <summary>Metadata for an Operator Console action code.</summary>
public sealed record OperatorConsoleActionCatalogEntry(
    string Code,
    string RoleExpectation,
    bool DeviceRequired,
    bool ActiveShiftRequired,
    bool SiteMatchRequired,
    string Classification,
    string AuditClassification,
    bool ProductionBlockerIfNotEnforced);
