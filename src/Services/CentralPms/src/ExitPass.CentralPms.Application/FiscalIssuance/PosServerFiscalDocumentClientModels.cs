using System.Text.Json;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IPosServerFiscalDocumentClient
{
    Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
        PosServerFiscalDocumentCreateRequest request,
        CancellationToken cancellationToken);

    Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
        Guid fiscalDocumentId,
        CancellationToken cancellationToken);

    Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
        Guid fiscalDocumentId,
        Guid? correlationId,
        CancellationToken cancellationToken);

    Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
        Guid fiscalDocumentId,
        PosServerFiscalDocumentVoidRequest request,
        CancellationToken cancellationToken);
}

public sealed record CentralPmsFiscalDocumentMappingContext(
    Guid? SitePosServerId,
    string? SitePosServerRef,
    Guid? FiscalDocumentTypeCodeId,
    string? FiscalDocumentTypeCodeKey,
    Guid? FiscalDocumentStatusCodeId,
    DateOnly? BusinessDayDate,
    string CentralPmsParkingSessionRef,
    string CentralPmsPaymentAttemptRef,
    string CentralPmsPaymentConfirmationRef,
    CentralPmsPayableBasisContext PayableBasis,
    IReadOnlyList<CentralPmsFiscalDocumentLineContext> DocumentLines,
    IReadOnlyList<CentralPmsFiscalTenderContext> Tenders,
    IReadOnlyList<CentralPmsFiscalTaxDetailContext> TaxDetails,
    IReadOnlyList<CentralPmsFiscalDiscountPrivilegeDetailContext> DiscountPrivilegeDetails,
    IReadOnlyList<CentralPmsFiscalTotalContext> Totals,
    IReadOnlyDictionary<string, string> ReferenceContext,
    string? PaymentFinalityRef,
    string? VendorAckRef,
    CentralPmsAppliedStatutoryFiscalFactsContext? AppliedStatutoryFiscalFacts = null);

public sealed record CentralPmsAppliedStatutoryFiscalFactsContext(
    Guid StatutoryDiscountDecisionCommandId,
    Guid StatutoryRequestReference,
    Guid StatutoryPayableBasisApplicationCommandId,
    Guid StatutoryValidationId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string BenefitClassification,
    CentralPmsAppliedStatutoryPolicyReferenceContext PolicyReference,
    Guid OriginalTariffSnapshotId,
    Guid AppliedTariffSnapshotId,
    long OriginalAmountMinorUnits,
    long VatExclusiveBasisAmountMinorUnits,
    long VatAmountMinorUnits,
    string VatTreatment,
    long StatutoryDiscountAmountMinorUnits,
    long FinalPayableAmountMinorUnits,
    string Currency,
    DateTimeOffset AppliedAt,
    string SourcePaymentChannel,
    Guid? TerminalCashTenderId = null);

public sealed record CentralPmsAppliedStatutoryPolicyReferenceContext(
    string ResolutionBasis,
    Guid? AppliedPolicyReferenceId = null,
    string? PolicyCode = null,
    Guid? PolicyVersionId = null,
    string? NationalLawReference = null,
    string? OrdinanceReference = null);

public sealed record CentralPmsPayableBasisContext(
    string PayableBasisRef,
    string UpstreamFinalityRef,
    string CurrencyCode,
    long PayableAmountMinorUnits,
    IReadOnlyList<CentralPmsFiscalDiscountReferenceContext> DiscountReferences,
    IReadOnlyDictionary<string, string> ReferenceContext);

public sealed record CentralPmsFiscalDiscountReferenceContext(
    string DiscountValidationRef,
    string Status,
    bool AppliesStatutoryDiscountTreatment,
    IReadOnlyDictionary<string, string> ReferenceContext)
{
    public string? StatutoryDiscountDecisionCommandRef { get; init; }

    public string? EntitlementType { get; init; }

    public string? AppliedPolicyReferenceRef { get; init; }

    public string? OriginalTariffSnapshotRef { get; init; }

    public string? AppliedTariffSnapshotRef { get; init; }

    public long? OriginalAmountMinorUnits { get; init; }

    public long? VatExclusiveBasisAmountMinorUnits { get; init; }

    public string? VatTreatment { get; init; }

    public long? DiscountAmountMinorUnits { get; init; }

    public long? FinalPayableAmountMinorUnits { get; init; }

    public DateTimeOffset? DecisionTimestamp { get; init; }

    public string? SourceChannel { get; init; }
}

public sealed record CentralPmsFiscalDocumentLineContext(
    int LineSequence,
    Guid? LineTypeCodeId,
    string Description,
    decimal Quantity,
    long UnitAmountMinorUnits,
    long GrossAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long TaxAmountMinorUnits,
    long NetAmountMinorUnits,
    string CurrencyCode,
    Guid? LineStatusCodeId,
    string? SourceRef,
    IReadOnlyDictionary<string, string> LineContext);

public sealed record CentralPmsFiscalTenderContext(
    Guid? TenderTypeCodeId,
    long AmountMinorUnits,
    string CurrencyCode,
    string? CentralPmsPaymentAttemptRef,
    string? CentralPmsPaymentConfirmationRef,
    string? PaymentFinalityRef,
    string? ProviderRef,
    IReadOnlyDictionary<string, string> TenderContext);

public sealed record CentralPmsFiscalTaxDetailContext(
    Guid? TaxTypeCodeId,
    Guid? TaxClassificationCodeId,
    long TaxableAmountMinorUnits,
    long TaxAmountMinorUnits,
    string CurrencyCode,
    int? LineSequence,
    decimal? TaxRate,
    IReadOnlyDictionary<string, string> TaxContext);

public sealed record CentralPmsFiscalDiscountPrivilegeDetailContext(
    Guid? DiscountPrivilegeTypeCodeId,
    long BasisAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long VatPrivilegeAmountMinorUnits,
    string CurrencyCode,
    int? LineSequence,
    string? BeneficiaryRef,
    string? EvidenceRef,
    string? ApprovalRef,
    IReadOnlyDictionary<string, string> DiscountPrivilegeContext);

public sealed record CentralPmsFiscalTotalContext(
    Guid? TotalTypeCodeId,
    long AmountMinorUnits,
    string CurrencyCode,
    IReadOnlyDictionary<string, string> TotalContext);

public sealed record PosServerFiscalDocumentCreateRequest(
    string? SitePosServerRef,
    string? FiscalDocumentTypeCodeKey,
    PosServerPayableBasisRequest PayableBasis,
    Guid? SitePosServerId,
    Guid? ChannelTerminalId,
    Guid? FiscalDocumentTypeCodeId,
    Guid? FiscalDocumentStatusCodeId,
    DateOnly? BusinessDayDate,
    string CentralPmsParkingSessionRef,
    string CentralPmsPaymentAttemptRef,
    string CentralPmsPaymentConfirmationRef,
    string UpstreamFinalityRef,
    string? PaymentFinalityRef,
    string? VendorAckRef,
    IReadOnlyList<PosServerFiscalDocumentLineRequest> DocumentLines,
    IReadOnlyList<PosServerFiscalDocumentLineRequest> Lines,
    IReadOnlyList<PosServerFiscalTenderRequest> Tenders,
    IReadOnlyList<PosServerFiscalTaxDetailRequest> TaxDetails,
    IReadOnlyList<PosServerFiscalDiscountPrivilegeDetailRequest> DiscountPrivilegeDetails,
    IReadOnlyList<PosServerFiscalTotalRequest> Totals,
    IReadOnlyDictionary<string, string> ReferenceContext,
    PosServerAppliedStatutoryFiscalFactsRequest? AppliedStatutoryFiscalFacts = null);

public sealed record PosServerAppliedStatutoryFiscalFactsRequest(
    Guid StatutoryDiscountDecisionCommandId,
    Guid StatutoryRequestReference,
    Guid StatutoryPayableBasisApplicationCommandId,
    Guid StatutoryValidationId,
    Guid ParkingSessionId,
    Guid SiteId,
    Guid SiteGroupId,
    string EntitlementType,
    string BenefitClassification,
    PosServerAppliedStatutoryPolicyReferenceRequest PolicyReference,
    Guid OriginalTariffSnapshotId,
    Guid AppliedTariffSnapshotId,
    long OriginalAmountMinorUnits,
    long VatExclusiveBasisAmountMinorUnits,
    long VatAmountMinorUnits,
    string VatTreatment,
    long StatutoryDiscountAmountMinorUnits,
    long FinalPayableAmountMinorUnits,
    string Currency,
    DateTimeOffset AppliedAt,
    string SourcePaymentChannel,
    Guid? TerminalCashTenderId = null);

public sealed record PosServerAppliedStatutoryPolicyReferenceRequest(
    string ResolutionBasis,
    Guid? AppliedPolicyReferenceId = null,
    string? PolicyCode = null,
    Guid? PolicyVersionId = null,
    string? NationalLawReference = null,
    string? OrdinanceReference = null);

public sealed record PosServerPayableBasisRequest(
    string PayableBasisRef,
    string UpstreamFinalityRef,
    string CurrencyCode,
    long PayableAmountMinorUnits,
    IReadOnlyList<PosServerFiscalDiscountReferenceRequest> DiscountReferences,
    IReadOnlyDictionary<string, string> ReferenceContext);

public sealed record PosServerFiscalDiscountReferenceRequest(
    string DiscountValidationRef,
    string Status,
    bool AppliesStatutoryDiscountTreatment,
    IReadOnlyDictionary<string, string> ReferenceContext)
{
    public string? StatutoryDiscountDecisionCommandRef { get; init; }

    public string? EntitlementType { get; init; }

    public string? AppliedPolicyReferenceRef { get; init; }

    public string? OriginalTariffSnapshotRef { get; init; }

    public string? AppliedTariffSnapshotRef { get; init; }

    public long? OriginalAmountMinorUnits { get; init; }

    public long? VatExclusiveBasisAmountMinorUnits { get; init; }

    public string? VatTreatment { get; init; }

    public long? DiscountAmountMinorUnits { get; init; }

    public long? FinalPayableAmountMinorUnits { get; init; }

    public DateTimeOffset? DecisionTimestamp { get; init; }

    public string? SourceChannel { get; init; }
}

public sealed record PosServerFiscalDocumentLineRequest(
    int LineSequence,
    Guid? LineTypeCodeId,
    string Description,
    decimal Quantity,
    long UnitAmountMinorUnits,
    long GrossAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long TaxAmountMinorUnits,
    long NetAmountMinorUnits,
    string CurrencyCode,
    Guid? LineStatusCodeId,
    string? SourceRef,
    IReadOnlyDictionary<string, string> LineContext);

public sealed record PosServerFiscalTenderRequest(
    Guid? TenderTypeCodeId,
    long AmountMinorUnits,
    string CurrencyCode,
    string? CentralPmsPaymentAttemptRef,
    string? CentralPmsPaymentConfirmationRef,
    string? PaymentFinalityRef,
    string? ProviderRef,
    IReadOnlyDictionary<string, string> TenderContext);

public sealed record PosServerFiscalTaxDetailRequest(
    Guid? TaxTypeCodeId,
    Guid? TaxClassificationCodeId,
    long TaxableAmountMinorUnits,
    long TaxAmountMinorUnits,
    string CurrencyCode,
    int? LineSequence,
    decimal? TaxRate,
    IReadOnlyDictionary<string, string> TaxContext);

public sealed record PosServerFiscalDiscountPrivilegeDetailRequest(
    Guid? DiscountPrivilegeTypeCodeId,
    long BasisAmountMinorUnits,
    long DiscountAmountMinorUnits,
    long VatPrivilegeAmountMinorUnits,
    string CurrencyCode,
    int? LineSequence,
    string? BeneficiaryRef,
    string? EvidenceRef,
    string? ApprovalRef,
    IReadOnlyDictionary<string, string> DiscountPrivilegeContext);

public sealed record PosServerFiscalTotalRequest(
    Guid? TotalTypeCodeId,
    long AmountMinorUnits,
    string CurrencyCode,
    IReadOnlyDictionary<string, string> TotalContext);

public enum PosServerFiscalDocumentOutcome
{
    Accepted = 1,
    Conflict = 2,
    FailedRequest = 3,
    FailedConfiguration = 4,
    FailedService = 5,
    InvalidResponse = 6
}

public sealed record PosServerFiscalDocumentCreateResult(
    PosServerFiscalDocumentOutcome Outcome,
    bool Succeeded,
    int HttpStatusCode,
    string Code,
    string Message,
    Guid? FiscalDocumentId,
    FiscalIssuanceResultClassification? ResultClassification,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    Guid? FiscalIdentityId,
    Guid? FiscalDocumentStatusCodeId,
    Guid? FiscalSequencePolicyId,
    long? FiscalSequenceValue,
    string? FiscalDocumentNumber,
    string? FiscalSeries,
    string? FiscalNumberPrefixText,
    string? FiscalNumberSuffixText,
    DateTimeOffset? FiscalNumberAssignedAt,
    string? FiscalNumberAssignedByRef,
    FiscalIssuanceErrorPosture? ErrorPosture);

public sealed record PosServerFiscalDocumentReadResult(
    PosServerFiscalDocumentOutcome Outcome,
    bool Succeeded,
    int HttpStatusCode,
    string Code,
    string Message,
    Guid? FiscalDocumentId,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    Guid? FiscalDocumentStatusCodeId,
    string? IdempotencyScope = null,
    string? IdempotencyKey = null,
    string? IdempotencyKeySource = null,
    string? SemanticRequestHash = null,
    string? SemanticRequestHashVersion = null,
    string? SemanticRequestHashStatus = null,
    string? FiscalDocumentStatusCodeKey = null,
    Guid? FiscalIdentityId = null,
    Guid? FiscalSequencePolicyId = null,
    long? FiscalSequenceValue = null,
    string? FiscalDocumentNumber = null,
    string? FiscalSeries = null,
    string? FiscalNumberPrefixText = null,
    string? FiscalNumberSuffixText = null,
    DateTimeOffset? FiscalNumberAssignedAt = null,
    string? FiscalNumberAssignedByRef = null,
    string? VoidStatus = null,
    string? VoidReasonCode = null,
    DateTimeOffset? VoidedAt = null);

public sealed record PosServerFiscalDocumentPresentationReadResult(
    PosServerFiscalDocumentOutcome Outcome,
    bool Succeeded,
    int HttpStatusCode,
    string Code,
    string Message,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    string? FiscalDocumentStatus,
    string? FiscalNumberAssignmentState,
    Guid? FiscalDocumentStatusCodeId,
    string? FiscalDocumentType,
    Guid? FiscalDocumentTypeCodeId,
    string? FiscalSeries,
    string? FiscalNumberPrefixText,
    string? FiscalNumberSuffixText,
    DateTimeOffset? FiscalNumberAssignedAt,
    DateTimeOffset? RecordedAt,
    string? VoidStatus,
    string? VoidReasonCode,
    DateTimeOffset? VoidedAt,
    string? PresentationVersion,
    string? TemplateVersion,
    string? ContentType,
    JsonElement? AuthoritativeResponse);

public sealed record PosServerFiscalDocumentVoidRequest(
    string IdempotencyKey,
    string ReasonCode,
    string? ReasonText,
    string RequestedByRef,
    DateTimeOffset? RequestedAt,
    string CorrelationId,
    string SourceSystemRef,
    DateOnly? BusinessDayDate);

public enum PosServerFiscalDocumentVoidOutcome
{
    NewlyVoided = 1,
    IdempotentReplay = 2,
    AlreadyVoided = 3,
    Conflict = 4,
    Rejected = 5,
    NotFound = 6,
    FailedService = 7,
    InvalidResponse = 8
}

public sealed record PosServerFiscalDocumentVoidResult(
    PosServerFiscalDocumentVoidOutcome Outcome,
    bool Succeeded,
    int HttpStatusCode,
    string Code,
    string Message,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    long? FiscalSequenceValue,
    string? FiscalDocumentStatus,
    string? VoidStatus,
    DateTimeOffset? VoidedAt,
    string? VoidReasonCode,
    string? VoidReasonText,
    string? RequestedByRef,
    string? IdempotencyKey,
    string? ResultClassification,
    string? CorrelationId,
    string? ErrorPosture);
