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
    string? VendorAckRef);

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
    IReadOnlyDictionary<string, string> ReferenceContext);

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
    IReadOnlyDictionary<string, string> ReferenceContext);

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
    IReadOnlyDictionary<string, string> ReferenceContext);

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
    Guid? FiscalDocumentStatusCodeId);
