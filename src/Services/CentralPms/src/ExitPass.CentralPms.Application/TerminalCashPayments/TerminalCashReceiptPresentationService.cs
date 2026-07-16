using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.TerminalCashPayments;

/// <summary>
/// Resolves terminal cash tenders to POS Server-owned receipt presentation without reconstructing fiscal content.
/// </summary>
public sealed class TerminalCashReceiptPresentationService : ITerminalCashReceiptPresentationService
{
    private const string ConfirmedCanonicalPaymentStatus = "CONFIRMED";
    private const int Status400BadRequest = 400;
    private const int Status404NotFound = 404;
    private const int Status409Conflict = 409;
    private const int Status503ServiceUnavailable = 503;

    private readonly ITerminalCashPaymentService _terminalCashPayments;
    private readonly IFiscalIssuanceReferenceRepository _fiscalReferences;
    private readonly IPosServerFiscalDocumentClient _posServerClient;

    public TerminalCashReceiptPresentationService(
        ITerminalCashPaymentService terminalCashPayments,
        IFiscalIssuanceReferenceRepository fiscalReferences,
        IPosServerFiscalDocumentClient posServerClient)
    {
        _terminalCashPayments = terminalCashPayments;
        _fiscalReferences = fiscalReferences;
        _posServerClient = posServerClient;
    }

    public async Task<TerminalCashReceiptPresentationResult> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (terminalCashTenderId == Guid.Empty)
        {
            throw Rejected(
                "TERMINAL_CASH_TENDER_ID_REQUIRED",
                "Terminal cash tender reference is required.",
                Status400BadRequest);
        }

        if (correlationId == Guid.Empty)
        {
            throw Rejected(
                "CORRELATION_ID_REQUIRED",
                "X-Correlation-Id header is required.",
                Status400BadRequest);
        }

        var cashPayment = await _terminalCashPayments.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                cancellationToken)
            .ConfigureAwait(false);
        if (cashPayment is null)
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_NOT_FOUND",
                "Terminal cash payment was not found.",
                Status404NotFound);
        }

        EnsureConfirmed(cashPayment);

        var reference = await _fiscalReferences.FindByPaymentConfirmationIdAsync(
                cashPayment.PaymentConfirmationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            throw Rejected(
                "TERMINAL_CASH_FISCAL_ISSUANCE_NOT_FOUND",
                "Fiscal issuance was not found for the terminal cash tender reference.",
                Status404NotFound);
        }

        EnsureReferenceMatchesTerminalCashPayment(reference, cashPayment);
        EnsureFiscalRecorded(reference);

        if (reference.PosServerFiscalDocumentId is null || reference.PosServerFiscalDocumentId == Guid.Empty)
        {
            throw Rejected(
                "POS_FISCAL_DOCUMENT_ID_MISSING",
                "Recorded fiscal issuance does not have a POS Server fiscal document reference.",
                Status409Conflict);
        }

        PosServerFiscalDocumentPresentationReadResult posPresentation;
        try
        {
            posPresentation = await _posServerClient.GetFiscalDocumentPresentationAsync(
                    reference.PosServerFiscalDocumentId.Value,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Rejected(
                "POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE",
                "POS Server receipt presentation is unavailable.",
                Status503ServiceUnavailable);
        }

        EnsurePosPresentationAvailable(reference, posPresentation);

        return new TerminalCashReceiptPresentationResult(
            cashPayment.TerminalCashTenderId,
            reference.PaymentAttemptId,
            reference.PaymentConfirmationId,
            reference.FiscalIssuanceReferenceId,
            reference.FiscalIssuanceState,
            reference.PosServerFiscalDocumentId.Value,
            posPresentation.FiscalDocumentNumber ?? reference.FiscalDocumentNumber,
            posPresentation.FiscalDocumentStatus,
            ReceiptAvailabilityState(posPresentation),
            posPresentation.PresentationVersion,
            posPresentation.TemplateVersion,
            posPresentation.ContentType,
            posPresentation.AuthoritativeResponse!.Value,
            posPresentation.VoidStatus,
            posPresentation.VoidReasonCode,
            posPresentation.VoidedAt,
            reference.FirstRecordedAt,
            reference.LastUpdatedAt,
            correlationId);
    }

    private static void EnsureConfirmed(TerminalCashPaymentReadback cashPayment)
    {
        if (!string.Equals(cashPayment.CanonicalPaymentStatus, ConfirmedCanonicalPaymentStatus, StringComparison.Ordinal))
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_NOT_CONFIRMED",
                "Terminal cash payment is not canonically confirmed.",
                Status409Conflict);
        }

        if (cashPayment.PaymentAttemptId == Guid.Empty || cashPayment.PaymentConfirmationId == Guid.Empty)
        {
            throw Rejected(
                "TERMINAL_CASH_PAYMENT_CONFIRMATION_MISSING",
                "Terminal cash payment confirmation is missing.",
                Status409Conflict);
        }
    }

    private static void EnsureReferenceMatchesTerminalCashPayment(
        FiscalIssuanceReferenceRecord reference,
        TerminalCashPaymentReadback cashPayment)
    {
        if (reference.PaymentConfirmationId != cashPayment.PaymentConfirmationId ||
            reference.PaymentAttemptId != cashPayment.PaymentAttemptId ||
            reference.ParkingSessionId != cashPayment.ParkingSessionId)
        {
            throw Rejected(
                "TERMINAL_CASH_RECEIPT_REFERENCE_CONFLICT",
                "Terminal cash payment is linked to conflicting fiscal receipt references.",
                Status409Conflict);
        }
    }

    private static void EnsureFiscalRecorded(FiscalIssuanceReferenceRecord reference)
    {
        if (reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled)
        {
            return;
        }

        throw Rejected(
            "TERMINAL_CASH_RECEIPT_PRESENTATION_NOT_READY",
            "Fiscal issuance is not recorded; receipt presentation is not available.",
            Status409Conflict);
    }

    private static void EnsurePosPresentationAvailable(
        FiscalIssuanceReferenceRecord reference,
        PosServerFiscalDocumentPresentationReadResult posPresentation)
    {
        if (posPresentation.Succeeded &&
            posPresentation.AuthoritativeResponse is not null &&
            posPresentation.FiscalDocumentId == reference.PosServerFiscalDocumentId)
        {
            return;
        }

        if (posPresentation.HttpStatusCode == Status404NotFound ||
            posPresentation.FiscalDocumentId is not null &&
            posPresentation.FiscalDocumentId != reference.PosServerFiscalDocumentId)
        {
            throw Rejected(
                "POS_FISCAL_DOCUMENT_PRESENTATION_INCONSISTENT",
                "POS Server fiscal document presentation does not match the recorded fiscal reference.",
                Status409Conflict);
        }

        if (posPresentation.HttpStatusCode == Status503ServiceUnavailable)
        {
            throw Rejected(
                "POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE",
                "POS Server receipt presentation is unavailable.",
                Status503ServiceUnavailable);
        }

        throw Rejected(
            "POS_SERVER_RECEIPT_PRESENTATION_NOT_READY",
            "POS Server receipt presentation is not ready.",
            Status409Conflict);
    }

    private static string ReceiptAvailabilityState(PosServerFiscalDocumentPresentationReadResult posPresentation) =>
        string.Equals(posPresentation.VoidStatus, "voided", StringComparison.OrdinalIgnoreCase)
            ? "VOIDED_PRESENTATION_AVAILABLE"
            : "AVAILABLE";

    private static TerminalCashReceiptPresentationRejectedException Rejected(
        string errorCode,
        string message,
        int httpStatusCode) =>
        new(errorCode, message, httpStatusCode);
}
