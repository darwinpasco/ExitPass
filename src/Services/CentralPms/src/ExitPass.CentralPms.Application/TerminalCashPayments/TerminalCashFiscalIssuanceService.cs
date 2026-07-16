using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Starts fiscal issuance for confirmed terminal cash payments by reusing the existing Central PMS fiscal path.
/// </summary>
public sealed class TerminalCashFiscalIssuanceService : ITerminalCashFiscalIssuanceService
{
    private const string ConfirmedCanonicalPaymentStatus = "CONFIRMED";
    private const string FiscalDocumentTypeCodeKey = "sales_invoice";

    private readonly ITerminalCashPaymentService _terminalCashPayments;
    private readonly IFiscalIssuanceReferenceRepository _fiscalReferences;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _posServerIntegration;

    public TerminalCashFiscalIssuanceService(
        ITerminalCashPaymentService terminalCashPayments,
        IFiscalIssuanceReferenceRepository fiscalReferences,
        IFiscalIssuanceOrchestrationService orchestrationService,
        IFiscalIssuancePosServerLiveIntegrationService posServerIntegration)
    {
        _terminalCashPayments = terminalCashPayments;
        _fiscalReferences = fiscalReferences;
        _orchestrationService = orchestrationService;
        _posServerIntegration = posServerIntegration;
    }

    public async Task<TerminalCashFiscalIssuanceResult> IssueOrReadAsync(
        TerminalCashFiscalIssuanceCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var cashPayment = await ReadConfirmedCashPaymentAsync(command.TerminalCashTenderId, cancellationToken)
            .ConfigureAwait(false);
        var upstreamFinalityReference = BuildUpstreamFinalityReference(cashPayment);

        var existingByConfirmation = await _fiscalReferences.FindByPaymentConfirmationIdAsync(
                cashPayment.PaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingByConfirmation is not null)
        {
            EnsureExistingReferenceMatchesTerminalCashPayment(existingByConfirmation, cashPayment, upstreamFinalityReference);
            return ToResult(
                cashPayment,
                existingByConfirmation,
                command.CorrelationId,
                posServerCallAttempted: false);
        }

        var existingByUpstream = await _fiscalReferences.FindByUpstreamFinalityReferenceAsync(
                upstreamFinalityReference,
                sitePosServerId: null,
                fiscalDocumentTypeCodeId: null,
                cancellationToken)
            .ConfigureAwait(false);
        if (existingByUpstream is not null)
        {
            EnsureExistingReferenceMatchesTerminalCashPayment(existingByUpstream, cashPayment, upstreamFinalityReference);
            return ToResult(
                cashPayment,
                existingByUpstream,
                command.CorrelationId,
                posServerCallAttempted: false);
        }

        var prepared = await _orchestrationService.PreparePendingAsync(
                BuildPrepareCommand(cashPayment, upstreamFinalityReference, command.CorrelationId),
                cancellationToken)
            .ConfigureAwait(false);

        var fiscalContext = BuildFiscalContext(cashPayment, prepared);
        var recordingContext = new PosServerCreateResultRecordingContext(
            UpstreamFinalityReference: upstreamFinalityReference,
            SitePosServerId: prepared.SitePosServerId,
            FiscalDocumentTypeCodeId: prepared.FiscalDocumentTypeCodeId,
            CorrelationId: command.CorrelationId,
            PosServerResponseTimestamp: DateTimeOffset.UtcNow,
            ServiceIdentityId: null);

        var issueResult = await _posServerIntegration.TryIssueFiscalDocumentViaPosServerAsync(
                prepared.FiscalIssuanceReferenceId,
                fiscalContext,
                recordingContext,
                cancellationToken)
            .ConfigureAwait(false);

        return ToResult(
            cashPayment,
            issueResult.FiscalIssuanceReference ?? prepared,
            command.CorrelationId,
            posServerCallAttempted: issueResult.MappedRequest is not null && issueResult.PosServerResult is not null,
            safeErrorCode: issueResult.PosServerResult?.Succeeded == false
                ? issueResult.PosServerResult.Code
                : issueResult.Status is FiscalIssuancePosServerLiveIntegrationStatus.Applied ? null : issueResult.Code,
            safeErrorPosture: issueResult.PosServerResult?.ErrorPosture?.ToString());
    }

    public async Task<TerminalCashFiscalIssuanceResult?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (terminalCashTenderId == Guid.Empty)
        {
            throw Rejected("TERMINAL_CASH_TENDER_ID_REQUIRED", "Terminal cash tender reference is required.");
        }

        var cashPayment = await _terminalCashPayments.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cashPayment is null)
        {
            return null;
        }

        var reference = await _fiscalReferences.FindByPaymentConfirmationIdAsync(
                cashPayment.PaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);

        return reference is null
            ? null
            : ToResult(cashPayment, reference, correlationId, posServerCallAttempted: false);
    }

    private async Task<TerminalCashPaymentReadback> ReadConfirmedCashPaymentAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken)
    {
        var cashPayment = await _terminalCashPayments.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cashPayment is null)
        {
            throw new TerminalCashFiscalIssuanceRejectedException(
                "TERMINAL_CASH_PAYMENT_NOT_FOUND",
                "Terminal cash payment was not found.",
                isNotFound: true);
        }

        if (!string.Equals(
                cashPayment.CanonicalPaymentStatus,
                ConfirmedCanonicalPaymentStatus,
                StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_NOT_CONFIRMED",
                "Terminal cash payment is not canonically confirmed.");
        }

        if (cashPayment.PaymentAttemptId == Guid.Empty || cashPayment.PaymentConfirmationId == Guid.Empty)
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_CONFIRMATION_MISSING",
                "Terminal cash payment confirmation is missing.");
        }

        if (string.IsNullOrWhiteSpace(cashPayment.PosServerId))
        {
            throw Rejected(
                "SITE_POS_SERVER_BINDING_MISSING",
                "Terminal cash payment does not have a POS Server reference.");
        }

        return cashPayment;
    }

    private static PrepareFiscalIssuanceCommand BuildPrepareCommand(
        TerminalCashPaymentReadback cashPayment,
        string upstreamFinalityReference,
        Guid correlationId) =>
        new(
            PaymentConfirmationId: cashPayment.PaymentConfirmationId,
            PaymentAttemptId: cashPayment.PaymentAttemptId,
            ParkingSessionId: cashPayment.ParkingSessionId,
            TariffSnapshotId: cashPayment.TariffSnapshotId,
            SiteId: cashPayment.SiteId,
            SitePosServerId: null,
            SitePosServerRef: cashPayment.PosServerId.Trim(),
            FiscalDocumentTypeCodeId: null,
            FiscalDocumentTypeCodeKey: FiscalDocumentTypeCodeKey,
            PayableBasisRef: cashPayment.TariffSnapshotId.ToString("D"),
            UpstreamFinalityReference: upstreamFinalityReference,
            CorrelationId: correlationId,
            ServiceIdentityId: null);

    private static CentralPmsFiscalDocumentMappingContext BuildFiscalContext(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference)
    {
        var currency = cashPayment.Currency.Trim().ToUpperInvariant();
        var amount = cashPayment.AmountDueMinorUnits;
        var paymentAttemptRef = cashPayment.PaymentAttemptId.ToString("D");
        var paymentConfirmationRef = cashPayment.PaymentConfirmationId.ToString("D");

        return new CentralPmsFiscalDocumentMappingContext(
            SitePosServerId: reference.SitePosServerId,
            SitePosServerRef: reference.SitePosServerRef,
            FiscalDocumentTypeCodeId: reference.FiscalDocumentTypeCodeId,
            FiscalDocumentTypeCodeKey: reference.FiscalDocumentTypeCodeKey,
            FiscalDocumentStatusCodeId: null,
            BusinessDayDate: DateOnly.FromDateTime(cashPayment.ConfirmedAt.UtcDateTime),
            CentralPmsParkingSessionRef: cashPayment.ParkingSessionId.ToString("D"),
            CentralPmsPaymentAttemptRef: paymentAttemptRef,
            CentralPmsPaymentConfirmationRef: paymentConfirmationRef,
            PayableBasis: new CentralPmsPayableBasisContext(
                PayableBasisRef: cashPayment.TariffSnapshotId.ToString("D"),
                UpstreamFinalityRef: reference.UpstreamFinalityReference,
                CurrencyCode: currency,
                PayableAmountMinorUnits: amount,
                DiscountReferences: Array.Empty<CentralPmsFiscalDiscountReferenceContext>(),
                ReferenceContext: new Dictionary<string, string>
                {
                    ["tariffSnapshotId"] = cashPayment.TariffSnapshotId.ToString("D"),
                    ["paymentMethod"] = "CASH"
                }),
            DocumentLines:
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: null,
                    Description: "Parking fee - cash",
                    Quantity: 1m,
                    UnitAmountMinorUnits: amount,
                    GrossAmountMinorUnits: amount,
                    DiscountAmountMinorUnits: 0,
                    TaxAmountMinorUnits: 0,
                    NetAmountMinorUnits: amount,
                    CurrencyCode: currency,
                    LineStatusCodeId: null,
                    SourceRef: cashPayment.TariffSnapshotId.ToString("D"),
                    LineContext: new Dictionary<string, string>
                    {
                        ["source"] = "terminal-cash-payment",
                        ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D")
                    })
            ],
            Tenders:
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: null,
                    AmountMinorUnits: amount,
                    CurrencyCode: currency,
                    CentralPmsPaymentAttemptRef: paymentAttemptRef,
                    CentralPmsPaymentConfirmationRef: paymentConfirmationRef,
                    PaymentFinalityRef: reference.UpstreamFinalityReference,
                    ProviderRef: "CASH",
                    TenderContext: new Dictionary<string, string>
                    {
                        ["paymentMethod"] = "CASH",
                        ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D")
                    })
            ],
            TaxDetails: Array.Empty<CentralPmsFiscalTaxDetailContext>(),
            DiscountPrivilegeDetails: Array.Empty<CentralPmsFiscalDiscountPrivilegeDetailContext>(),
            Totals:
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: null,
                    AmountMinorUnits: amount,
                    CurrencyCode: currency,
                    TotalContext: new Dictionary<string, string> { ["kind"] = "grand_total" })
            ],
            ReferenceContext: new Dictionary<string, string>
            {
                ["terminalCashTenderId"] = cashPayment.TerminalCashTenderId.ToString("D"),
                ["cashCustodySessionId"] = cashPayment.CashCustodySessionId.ToString("D"),
                ["fiscalIssuanceReferenceId"] = reference.FiscalIssuanceReferenceId.ToString("D")
            },
            PaymentFinalityRef: reference.UpstreamFinalityReference,
            VendorAckRef: null);
    }

    private static void EnsureExistingReferenceMatchesTerminalCashPayment(
        FiscalIssuanceReferenceRecord reference,
        TerminalCashPaymentReadback cashPayment,
        string upstreamFinalityReference)
    {
        if (reference.PaymentConfirmationId != cashPayment.PaymentConfirmationId ||
            reference.PaymentAttemptId != cashPayment.PaymentAttemptId ||
            reference.ParkingSessionId != cashPayment.ParkingSessionId ||
            !string.Equals(reference.UpstreamFinalityReference, upstreamFinalityReference, StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_SEMANTIC_CONFLICT",
                "Terminal cash payment is already linked to a conflicting fiscal issuance request.");
        }
    }

    private static TerminalCashFiscalIssuanceResult ToResult(
        TerminalCashPaymentReadback cashPayment,
        FiscalIssuanceReferenceRecord reference,
        Guid? correlationId,
        bool posServerCallAttempted,
        string? safeErrorCode = null,
        string? safeErrorPosture = null) =>
        new(
            TerminalCashTenderId: cashPayment.TerminalCashTenderId,
            PaymentAttemptId: reference.PaymentAttemptId,
            PaymentConfirmationId: reference.PaymentConfirmationId,
            FiscalIssuanceReferenceId: reference.FiscalIssuanceReferenceId,
            FiscalIssuanceState: reference.FiscalIssuanceState,
            ResultClassification: reference.ResultClassification,
            PosFiscalDocumentId: reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: reference.FiscalDocumentNumber,
            FiscalNumberAssignedAt: reference.FiscalNumberAssignedAt,
            SemanticHashSourceVersion: reference.SemanticRequestHashSourceVersion,
            CreatedAt: reference.FirstRecordedAt,
            UpdatedAt: reference.LastUpdatedAt,
            CorrelationId: correlationId ?? reference.CorrelationId,
            SafeErrorCode: safeErrorCode ?? reference.LatestErrorCode,
            SafeErrorPosture: safeErrorPosture ?? reference.LatestErrorPosture?.ToString(),
            PosServerCallAttempted: posServerCallAttempted,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false);

    private static void Validate(TerminalCashFiscalIssuanceCommand command)
    {
        if (command.TerminalCashTenderId == Guid.Empty)
        {
            throw Rejected("TERMINAL_CASH_TENDER_ID_REQUIRED", "Terminal cash tender reference is required.");
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw Rejected("IDEMPOTENCY_KEY_REQUIRED", "Idempotency-Key header is required.");
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw Rejected("CORRELATION_ID_REQUIRED", "X-Correlation-Id header is required.");
        }
    }

    private static string BuildUpstreamFinalityReference(TerminalCashPaymentReadback cashPayment) =>
        $"terminal-cash-payment-confirmation:{cashPayment.PaymentConfirmationId:D}:sales_invoice";

    private static TerminalCashFiscalIssuanceRejectedException Rejected(string errorCode, string message) =>
        new(errorCode, message);
}
