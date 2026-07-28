namespace ExitPass.CentralPms.Application.TerminalCashPayments;

public interface ITerminalCashStatutoryFiscalLinkageReader
{
    Task<TerminalCashStatutoryFiscalLinkageResult> ReadByAppliedTariffSnapshotAsync(
        TerminalCashPaymentReadback cashPayment,
        CancellationToken cancellationToken);
}

public enum TerminalCashStatutoryFiscalLinkageStatus
{
    NotApplicable = 1,
    CompleteApprovedContext = 2,
    RetryableUnavailable = 3,
    TerminallyInconsistent = 4
}

public sealed record TerminalCashStatutoryFiscalLinkageResult(
    TerminalCashStatutoryFiscalLinkageStatus Status,
    TerminalCashStatutoryFiscalLinkageContext? Context,
    string? SafeErrorCode,
    string? RecoveryClassification,
    string? RecoveryAction)
{
    public static TerminalCashStatutoryFiscalLinkageResult NotApplicable() =>
        new(
            TerminalCashStatutoryFiscalLinkageStatus.NotApplicable,
            Context: null,
            SafeErrorCode: null,
            RecoveryClassification: "NONE",
            RecoveryAction: null);

    public static TerminalCashStatutoryFiscalLinkageResult Complete(
        TerminalCashStatutoryFiscalLinkageContext context) =>
        new(
            TerminalCashStatutoryFiscalLinkageStatus.CompleteApprovedContext,
            context,
            SafeErrorCode: null,
            RecoveryClassification: "NONE",
            RecoveryAction: null);

    public static TerminalCashStatutoryFiscalLinkageResult RetryableUnavailable(string safeErrorCode) =>
        new(
            TerminalCashStatutoryFiscalLinkageStatus.RetryableUnavailable,
            Context: null,
            safeErrorCode,
            RecoveryClassification: "WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY",
            RecoveryAction: "WAIT_AND_RETRY");

    public static TerminalCashStatutoryFiscalLinkageResult TerminallyInconsistent(string safeErrorCode) =>
        new(
            TerminalCashStatutoryFiscalLinkageStatus.TerminallyInconsistent,
            Context: null,
            safeErrorCode,
            RecoveryClassification: "NOT_RECOVERABLE",
            RecoveryAction: "DO_NOT_RETRY");
}

public sealed record TerminalCashStatutoryFiscalLinkageContext(
    Guid StatutoryDiscountDecisionCommandId,
    Guid StatutoryDiscountPayableBasisApplicationCommandId,
    Guid StatutoryDiscountValidationId,
    Guid? StatutoryDiscountPayableBasisApplicationId,
    Guid ParkingSessionId,
    Guid? SiteId,
    Guid? SiteGroupId,
    Guid OriginalTariffSnapshotId,
    Guid AppliedTariffSnapshotId,
    Guid? AppliedPolicyReferenceId,
    string? PolicyResolutionBasis,
    string EntitlementType,
    string SourceChannel,
    long OriginalAmountMinorUnits,
    long VatExclusiveBasisAmountMinorUnits,
    long VatAmountMinorUnits,
    string VatTreatment,
    long StatutoryDiscountAmountMinorUnits,
    long FinalPayableAmountMinorUnits,
    string Currency,
    DateTimeOffset? DecisionTimestamp,
    DateTimeOffset? AppliedAt,
    string? MaskedIdReference);
