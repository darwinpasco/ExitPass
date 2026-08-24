using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.Payments;

public interface IDigitalPaymentFiscalContextReader
{
    Task<DigitalPaymentFiscalContext> ReadAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        Guid parkingSessionId,
        CancellationToken cancellationToken);
}

public interface IDigitalPaymentFiscalIssuanceService
{
    Task<DigitalPaymentFiscalIssuanceResult> IssueOrReadAsync(
        DigitalPaymentFiscalIssuanceCommand command,
        CancellationToken cancellationToken);
}

public sealed class DigitalPaymentFiscalIssuanceService : IDigitalPaymentFiscalIssuanceService
{
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";
    private static readonly Guid FiscalLineTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000201");
    private static readonly Guid FiscalTenderTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000301");
    private static readonly Guid FiscalTaxTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000401");
    private static readonly Guid FiscalTaxClassificationCodeId = Guid.Parse("10000000-0000-0000-0000-000000000402");
    private static readonly Guid FiscalDiscountPrivilegeTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000501");
    private static readonly Guid FiscalTotalTypeCodeId = Guid.Parse("10000000-0000-0000-0000-000000000601");
    private readonly IDigitalPaymentFiscalContextReader _contextReader;
    private readonly IFiscalIssuanceReferenceRepository _references;
    private readonly IFiscalIssuanceOrchestrationService _orchestration;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServer;

    public DigitalPaymentFiscalIssuanceService(
        IDigitalPaymentFiscalContextReader contextReader,
        IFiscalIssuanceReferenceRepository references,
        IFiscalIssuanceOrchestrationService orchestration,
        IFiscalIssuancePosServerLiveIntegrationService posServer)
    {
        _contextReader = contextReader;
        _references = references;
        _orchestration = orchestration;
        _posServer = posServer;
    }

    public async Task<DigitalPaymentFiscalIssuanceResult> IssueOrReadAsync(
        DigitalPaymentFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await _references.FindByPaymentConfirmationIdAsync(
            command.PaymentConfirmationId,
            cancellationToken);
        if (existing is not null &&
            FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(existing))
        {
            return ToResult(existing, false, null);
        }

        if (existing is not null && !CanRetry(existing))
        {
            return ToResult(existing, false, existing.LatestErrorCode);
        }

        var context = await _contextReader.ReadAsync(
            command.PaymentAttemptId,
            command.PaymentConfirmationId,
            command.ParkingSessionId,
            cancellationToken);
        var upstreamReference = $"PAYMENT_CONFIRMATION:{command.PaymentConfirmationId:D}";
        FiscalIssuanceReferenceRecord reference;
        if (existing is null)
        {
            reference = await _orchestration.PreparePendingAsync(
                new PrepareFiscalIssuanceCommand(
                    command.PaymentConfirmationId,
                    command.PaymentAttemptId,
                    command.ParkingSessionId,
                    context.TariffSnapshotId,
                    context.SiteId,
                    context.SitePosServerId,
                    context.SitePosServerRef,
                    null,
                    FiscalDocumentTypeCodeKey,
                    context.TariffSnapshotId.ToString("D"),
                    upstreamReference,
                    command.CorrelationId,
                    command.ServiceIdentityId),
                cancellationToken);
        }
        else
        {
            EnsureExistingReferenceMatches(existing, command, context, upstreamReference);
            reference = existing;
        }

        var mapping = BuildMapping(reference, context, command, upstreamReference);

        var issue = await _posServer.TryIssueFiscalDocumentViaPosServerAsync(
            reference.FiscalIssuanceReferenceId,
            mapping,
            new PosServerCreateResultRecordingContext(
                upstreamReference,
                reference.SitePosServerId,
                reference.FiscalDocumentTypeCodeId,
                command.CorrelationId,
                DateTimeOffset.UtcNow,
                command.ServiceIdentityId),
            cancellationToken);

        return ToResult(
            issue.FiscalIssuanceReference ?? reference,
            issue.MappedRequest is not null && issue.PosServerResult is not null,
            issue.PosServerResult?.Succeeded == false ? issue.PosServerResult.Code : null);
    }

    private static CentralPmsFiscalDocumentMappingContext BuildMapping(
        FiscalIssuanceReferenceRecord reference,
        DigitalPaymentFiscalContext context,
        DigitalPaymentFiscalIssuanceCommand command,
        string upstreamReference)
    {
        var statutory = context.AppliedStatutoryFiscalContext;
        EnsureStatutoryContextMatches(context, command, statutory);
        var amount = statutory?.FinalPayableAmountMinorUnits ?? context.AmountMinorUnits;
        var lineGrossAmount = statutory?.VatExclusiveBasisAmountMinorUnits ?? amount;
        var discountAmount = statutory?.StatutoryDiscountAmountMinorUnits ?? 0;
        var attemptRef = command.PaymentAttemptId.ToString("D");
        var confirmationRef = command.PaymentConfirmationId.ToString("D");
        var payableBasisContext = new Dictionary<string, string>
        {
            ["source"] = statutory is null
                ? "central-pms-authoritative-tariff"
                : "central-pms-applied-statutory-payable-basis",
            ["appliedTariffSnapshotId"] = context.TariffSnapshotId.ToString("D")
        };
        var lineContext = new Dictionary<string, string>();
        var referenceContext = new Dictionary<string, string>
        {
            ["site_id"] = context.SiteId.ToString("D"),
            ["site_group_id"] = context.SiteGroupId.ToString("D"),
            ["payment_channel"] = "DIGITAL",
            ["fiscal_issuance_reference_id"] = reference.FiscalIssuanceReferenceId.ToString("D")
        };
        if (statutory is not null)
        {
            AddStatutoryReferences(payableBasisContext, statutory);
            AddStatutoryReferences(lineContext, statutory);
            AddStatutoryReferences(referenceContext, statutory);
            lineContext["originalAmountMinorUnits"] = statutory.OriginalAmountMinorUnits.ToString();
            lineContext["vatExclusiveBasisAmountMinorUnits"] = statutory.VatExclusiveBasisAmountMinorUnits.ToString();
            lineContext["vatAmountMinorUnits"] = statutory.VatAmountMinorUnits.ToString();
            lineContext["finalPayableAmountMinorUnits"] = statutory.FinalPayableAmountMinorUnits.ToString();
        }

        return new CentralPmsFiscalDocumentMappingContext(
            reference.SitePosServerId,
            reference.SitePosServerRef,
            reference.FiscalDocumentTypeCodeId,
            reference.FiscalDocumentTypeCodeKey,
            null,
            DateOnly.FromDateTime(context.ConfirmedAt.UtcDateTime),
            command.ParkingSessionId.ToString("D"),
            attemptRef,
            confirmationRef,
            new CentralPmsPayableBasisContext(
                context.TariffSnapshotId.ToString("D"),
                upstreamReference,
                context.Currency,
                amount,
                BuildDiscountReferences(statutory),
                payableBasisContext),
            [new CentralPmsFiscalDocumentLineContext(
                1,
                statutory is null ? null : FiscalLineTypeCodeId,
                statutory is null ? "Parking fee - digital payment" : "Parking fee - statutory discount applied",
                1m,
                lineGrossAmount,
                lineGrossAmount,
                discountAmount,
                0,
                amount,
                context.Currency,
                null,
                context.TariffSnapshotId.ToString("D"),
                lineContext)],
            [new CentralPmsFiscalTenderContext(
                statutory is null ? null : FiscalTenderTypeCodeId,
                amount,
                context.Currency,
                attemptRef,
                confirmationRef,
                upstreamReference,
                command.ProviderReference,
                new Dictionary<string, string> { ["channel"] = "DIGITAL" })],
            BuildTaxDetails(statutory, context.Currency),
            BuildDiscountPrivilegeDetails(statutory, context.Currency),
            [new CentralPmsFiscalTotalContext(
                statutory is null ? null : FiscalTotalTypeCodeId,
                amount,
                context.Currency,
                new Dictionary<string, string> { ["kind"] = statutory is null ? "grand_total" : "final_statutory_payable" })],
            referenceContext,
            upstreamReference,
            null,
            BuildAppliedStatutoryFiscalFacts(context, statutory));
    }

    private static void EnsureStatutoryContextMatches(
        DigitalPaymentFiscalContext context,
        DigitalPaymentFiscalIssuanceCommand command,
        TerminalCashStatutoryFiscalLinkageContext? statutory)
    {
        if (statutory is null)
        {
            return;
        }

        if (statutory.ParkingSessionId != command.ParkingSessionId ||
            statutory.AppliedTariffSnapshotId != context.TariffSnapshotId ||
            statutory.SiteId.HasValue && statutory.SiteId != context.SiteId ||
            statutory.SiteGroupId.HasValue && statutory.SiteGroupId != context.SiteGroupId)
        {
            throw new InvalidOperationException("STATUTORY_FISCAL_ROUTING_CONTEXT_MISMATCH");
        }

        if (!string.Equals(statutory.SourceChannel, "WEBPAY", StringComparison.Ordinal) ||
            !string.Equals(statutory.Currency, context.Currency, StringComparison.Ordinal) ||
            statutory.FinalPayableAmountMinorUnits != context.AmountMinorUnits)
        {
            throw new InvalidOperationException("STATUTORY_FISCAL_AMOUNT_OR_CHANNEL_MISMATCH");
        }

        if (statutory.StatutoryDiscountDecisionCommandId == Guid.Empty ||
            statutory.StatutoryDiscountPayableBasisApplicationCommandId == Guid.Empty ||
            statutory.StatutoryDiscountValidationId == Guid.Empty ||
            statutory.OriginalTariffSnapshotId == Guid.Empty ||
            !statutory.AppliedPolicyReferenceId.HasValue ||
            statutory.AppliedPolicyReferenceId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("STATUTORY_FISCAL_REQUIRED_FACTS_UNAVAILABLE");
        }

        if (statutory.EntitlementType is not ("SENIOR_CITIZEN" or "PWD"))
        {
            throw new InvalidOperationException("STATUTORY_FISCAL_ENTITLEMENT_UNSUPPORTED");
        }

        if (statutory.FinalPayableAmountMinorUnits <= 0 ||
            statutory.OriginalAmountMinorUnits <= statutory.FinalPayableAmountMinorUnits ||
            statutory.StatutoryDiscountAmountMinorUnits <= 0 ||
            statutory.VatExclusiveBasisAmountMinorUnits - statutory.StatutoryDiscountAmountMinorUnits !=
                statutory.FinalPayableAmountMinorUnits ||
            statutory.VatExclusiveBasisAmountMinorUnits + statutory.VatAmountMinorUnits !=
                statutory.OriginalAmountMinorUnits)
        {
            throw new InvalidOperationException("STATUTORY_FISCAL_ARITHMETIC_INVALID");
        }
    }

    private static IReadOnlyList<CentralPmsFiscalDiscountReferenceContext> BuildDiscountReferences(
        TerminalCashStatutoryFiscalLinkageContext? statutory) =>
        statutory is null
            ? []
            :
            [
                new CentralPmsFiscalDiscountReferenceContext(
                    statutory.StatutoryDiscountValidationId.ToString("D"),
                    "approved",
                    true,
                    new Dictionary<string, string>
                    {
                        ["statutoryDiscountPayableBasisApplicationCommandId"] = statutory.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D"),
                        ["entitlementType"] = statutory.EntitlementType,
                        ["source"] = "central-pms-canonical-statutory-discount"
                    })
                {
                    StatutoryDiscountDecisionCommandRef = statutory.StatutoryDiscountDecisionCommandId.ToString("D"),
                    EntitlementType = statutory.EntitlementType,
                    AppliedPolicyReferenceRef = statutory.AppliedPolicyReferenceId?.ToString("D"),
                    OriginalTariffSnapshotRef = statutory.OriginalTariffSnapshotId.ToString("D"),
                    AppliedTariffSnapshotRef = statutory.AppliedTariffSnapshotId.ToString("D"),
                    OriginalAmountMinorUnits = statutory.OriginalAmountMinorUnits,
                    VatExclusiveBasisAmountMinorUnits = statutory.VatExclusiveBasisAmountMinorUnits,
                    VatTreatment = statutory.VatTreatment,
                    DiscountAmountMinorUnits = statutory.StatutoryDiscountAmountMinorUnits,
                    FinalPayableAmountMinorUnits = statutory.FinalPayableAmountMinorUnits,
                    DecisionTimestamp = statutory.DecisionTimestamp,
                    SourceChannel = statutory.SourceChannel
                }
            ];

    private static IReadOnlyList<CentralPmsFiscalTaxDetailContext> BuildTaxDetails(
        TerminalCashStatutoryFiscalLinkageContext? statutory,
        string currency) =>
        statutory is null
            ? []
            :
            [
                new CentralPmsFiscalTaxDetailContext(
                    FiscalTaxTypeCodeId,
                    FiscalTaxClassificationCodeId,
                    statutory.VatExclusiveBasisAmountMinorUnits,
                    statutory.VatAmountMinorUnits,
                    currency,
                    1,
                    12m,
                    new Dictionary<string, string> { ["basis"] = statutory.VatTreatment })
            ];

    private static IReadOnlyList<CentralPmsFiscalDiscountPrivilegeDetailContext> BuildDiscountPrivilegeDetails(
        TerminalCashStatutoryFiscalLinkageContext? statutory,
        string currency) =>
        statutory is null
            ? []
            :
            [
                new CentralPmsFiscalDiscountPrivilegeDetailContext(
                    FiscalDiscountPrivilegeTypeCodeId,
                    statutory.VatExclusiveBasisAmountMinorUnits,
                    statutory.StatutoryDiscountAmountMinorUnits,
                    statutory.VatAmountMinorUnits,
                    currency,
                    1,
                    null,
                    null,
                    statutory.StatutoryDiscountValidationId.ToString("D"),
                    new Dictionary<string, string>
                    {
                        ["entitlementType"] = statutory.EntitlementType,
                        ["discountBaseScope"] = statutory.VatTreatment,
                        ["statutoryDiscountDecisionCommandId"] = statutory.StatutoryDiscountDecisionCommandId.ToString("D"),
                        ["statutoryDiscountPayableBasisApplicationCommandId"] = statutory.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D")
                    })
            ];

    private static CentralPmsAppliedStatutoryFiscalFactsContext? BuildAppliedStatutoryFiscalFacts(
        DigitalPaymentFiscalContext context,
        TerminalCashStatutoryFiscalLinkageContext? statutory)
    {
        if (statutory is null)
        {
            return null;
        }

        return new CentralPmsAppliedStatutoryFiscalFactsContext(
            statutory.StatutoryDiscountDecisionCommandId,
            statutory.StatutoryDiscountValidationId,
            statutory.StatutoryDiscountPayableBasisApplicationCommandId,
            statutory.StatutoryDiscountValidationId,
            statutory.ParkingSessionId,
            context.SiteId,
            context.SiteGroupId,
            statutory.EntitlementType,
            ResolveBenefitClassification(statutory),
            new CentralPmsAppliedStatutoryPolicyReferenceContext(
                NormalizePolicyResolutionBasis(statutory.PolicyResolutionBasis),
                AppliedPolicyReferenceId: statutory.AppliedPolicyReferenceId),
            statutory.OriginalTariffSnapshotId,
            statutory.AppliedTariffSnapshotId,
            statutory.OriginalAmountMinorUnits,
            statutory.VatExclusiveBasisAmountMinorUnits,
            statutory.VatAmountMinorUnits,
            statutory.VatTreatment,
            statutory.StatutoryDiscountAmountMinorUnits,
            statutory.FinalPayableAmountMinorUnits,
            statutory.Currency,
            statutory.AppliedAt ?? context.ConfirmedAt,
            "WEBPAY");
    }

    private static string ResolveBenefitClassification(TerminalCashStatutoryFiscalLinkageContext statutory) =>
        statutory.FinalPayableAmountMinorUnits == 0
            ? "FREE_PARKING"
            : statutory.VatAmountMinorUnits > 0 && statutory.StatutoryDiscountAmountMinorUnits > 0
                ? "VAT_EXEMPTION_AND_STATUTORY_DISCOUNT"
                : statutory.VatAmountMinorUnits > 0
                    ? "VAT_EXEMPTION_ONLY"
                    : statutory.StatutoryDiscountAmountMinorUnits > 0
                        ? "STATUTORY_DISCOUNT_ONLY"
                        : "REDUCED_PARKING_RATE";

    private static string NormalizePolicyResolutionBasis(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        return normalized switch
        {
            "NATIONAL_LAW" or "LOCAL_ORDINANCE" or "MIXED" or "INTERNAL_POLICY_REFERENCE" => normalized,
            "NATIONAL_LAW_FALLBACK" => "NATIONAL_LAW",
            _ => "INTERNAL_POLICY_REFERENCE"
        };
    }

    private static void AddStatutoryReferences(
        IDictionary<string, string> target,
        TerminalCashStatutoryFiscalLinkageContext statutory)
    {
        target["statutoryDiscountValidationId"] = statutory.StatutoryDiscountValidationId.ToString("D");
        target["statutoryDiscountDecisionCommandId"] = statutory.StatutoryDiscountDecisionCommandId.ToString("D");
        target["statutoryDiscountPayableBasisApplicationCommandId"] = statutory.StatutoryDiscountPayableBasisApplicationCommandId.ToString("D");
        target["originalTariffSnapshotId"] = statutory.OriginalTariffSnapshotId.ToString("D");
        target["appliedTariffSnapshotId"] = statutory.AppliedTariffSnapshotId.ToString("D");
        target["entitlementType"] = statutory.EntitlementType;
    }

    private static bool CanRetry(FiscalIssuanceReferenceRecord reference) =>
        reference.FiscalIssuanceState is
            FiscalIssuanceIntegrationState.PendingFiscalIssuance or
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested or
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedService &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterServiceRecovery ||
        reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection;

    private static void EnsureExistingReferenceMatches(
        FiscalIssuanceReferenceRecord reference,
        DigitalPaymentFiscalIssuanceCommand command,
        DigitalPaymentFiscalContext context,
        string upstreamReference)
    {
        if (reference.PaymentConfirmationId != command.PaymentConfirmationId ||
            reference.PaymentAttemptId != command.PaymentAttemptId ||
            reference.ParkingSessionId != command.ParkingSessionId ||
            reference.TariffSnapshotId != context.TariffSnapshotId ||
            reference.SiteId != context.SiteId ||
            reference.SitePosServerId != context.SitePosServerId ||
            !string.Equals(reference.SitePosServerRef, context.SitePosServerRef, StringComparison.Ordinal) ||
            !string.Equals(reference.PayableBasisRef, context.TariffSnapshotId.ToString("D"), StringComparison.Ordinal) ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamReference, StringComparison.Ordinal) ||
            !string.Equals(reference.FiscalDocumentTypeCodeKey, FiscalDocumentTypeCodeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("digital_payment_fiscal_routing_context_mismatch");
        }
    }

    private static DigitalPaymentFiscalIssuanceResult ToResult(
        FiscalIssuanceReferenceRecord reference,
        bool posServerCallAttempted,
        string? safeErrorCode) =>
        new(
            reference.FiscalIssuanceReferenceId,
            FiscalIssuanceOrchestrationService.IsNormalExitAuthorizationGatingReady(reference),
            posServerCallAttempted,
            safeErrorCode,
            reference.SitePosServerId,
            reference.SitePosServerRef,
            reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceFailedService &&
            reference.LatestErrorPosture == FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);
}

public sealed record DigitalPaymentFiscalIssuanceCommand(
    Guid PaymentAttemptId,
    Guid PaymentConfirmationId,
    Guid ParkingSessionId,
    string ProviderReference,
    Guid CorrelationId,
    Guid? ServiceIdentityId);

public sealed record DigitalPaymentFiscalContext(
    Guid SiteId,
    Guid SiteGroupId,
    Guid TariffSnapshotId,
    long AmountMinorUnits,
    string Currency,
    DateTimeOffset ConfirmedAt,
    Guid SitePosServerId,
    string SitePosServerRef,
    TerminalCashStatutoryFiscalLinkageContext? AppliedStatutoryFiscalContext = null);

public sealed record DigitalPaymentFiscalIssuanceResult(
    Guid FiscalIssuanceReferenceId,
    bool ReadyForExitAuthorization,
    bool PosServerCallAttempted,
    string? SafeErrorCode,
    Guid? SitePosServerId,
    string? SitePosServerRef,
    bool RetryableAfterServiceRecovery = false);
