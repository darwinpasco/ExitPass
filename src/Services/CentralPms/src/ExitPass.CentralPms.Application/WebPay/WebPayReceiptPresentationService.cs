using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.WebPay;

/// <summary>
/// Resolves WebPay payment attempts to POS Server-owned receipt presentation without reconstructing fiscal content.
/// </summary>
public sealed class WebPayReceiptPresentationService : IWebPayReceiptPresentationService
{
    private const int Status400BadRequest = 400;
    private const int Status404NotFound = 404;
    private const int Status409Conflict = 409;
    private const int Status503ServiceUnavailable = 503;

    private readonly IFiscalIssuanceReferenceRepository _fiscalReferences;
    private readonly IPosServerFiscalDocumentClient _posServerClient;

    public WebPayReceiptPresentationService(
        IFiscalIssuanceReferenceRepository fiscalReferences,
        IPosServerFiscalDocumentClient posServerClient)
    {
        _fiscalReferences = fiscalReferences;
        _posServerClient = posServerClient;
    }

    public async Task<WebPayReceiptPresentationResult> GetByPaymentAttemptIdAsync(
        Guid paymentAttemptId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (paymentAttemptId == Guid.Empty)
        {
            throw Rejected(
                "PAYMENT_ATTEMPT_ID_REQUIRED",
                "Payment attempt reference is required.",
                Status400BadRequest,
                retryable: false);
        }

        if (correlationId == Guid.Empty)
        {
            throw Rejected(
                "CORRELATION_ID_REQUIRED",
                "X-Correlation-Id header is required.",
                Status400BadRequest,
                retryable: false);
        }

        var reference = await _fiscalReferences.FindLatestByPaymentAttemptIdAsync(
                paymentAttemptId,
                cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            throw Rejected(
                "WEBPAY_FISCAL_ISSUANCE_NOT_FOUND",
                "Fiscal issuance was not found for the WebPay payment attempt.",
                Status404NotFound,
                retryable: true);
        }

        EnsureFiscalRecorded(reference);

        if (reference.PosServerFiscalDocumentId is null || reference.PosServerFiscalDocumentId == Guid.Empty)
        {
            throw Rejected(
                "POS_FISCAL_DOCUMENT_ID_MISSING",
                "Recorded fiscal issuance does not have a POS Server fiscal document reference.",
                Status409Conflict,
                retryable: true);
        }

        PosServerFiscalDocumentPresentationReadResult posPresentation;
        try
        {
            posPresentation = await _posServerClient.GetFiscalDocumentPresentationAsync(
                    reference.PosServerFiscalDocumentId.Value,
                    correlationId,
                    PosServerRoutingContext.Create(reference.SitePosServerId, reference.SitePosServerRef),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw Rejected(
                "POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE",
                "POS Server receipt presentation is temporarily unavailable.",
                Status503ServiceUnavailable,
                retryable: true);
        }

        EnsurePosPresentationAvailable(reference, posPresentation);

        return new WebPayReceiptPresentationResult(
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

    private static void EnsureFiscalRecorded(FiscalIssuanceReferenceRecord reference)
    {
        if (reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
            or FiscalIssuanceIntegrationState.FiscalIssuanceReconciled)
        {
            return;
        }

        var retryable = reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.PendingFiscalIssuance
            or FiscalIssuanceIntegrationState.FiscalIssuanceRequested
            or FiscalIssuanceIntegrationState.FiscalIssuanceUnknown
            or FiscalIssuanceIntegrationState.FiscalIssuanceManualReview;

        throw Rejected(
            "WEBPAY_RECEIPT_PRESENTATION_NOT_READY",
            "Fiscal issuance is not recorded; Sales Invoice presentation is not available yet.",
            Status409Conflict,
            retryable);
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
                Status409Conflict,
                retryable: false);
        }

        if (posPresentation.HttpStatusCode == Status503ServiceUnavailable)
        {
            throw Rejected(
                "POS_SERVER_RECEIPT_PRESENTATION_UNAVAILABLE",
                "POS Server receipt presentation is temporarily unavailable.",
                Status503ServiceUnavailable,
                retryable: true);
        }

        throw Rejected(
            "POS_SERVER_RECEIPT_PRESENTATION_NOT_READY",
            "POS Server receipt presentation is not ready.",
            Status409Conflict,
            retryable: true);
    }

    private static string ReceiptAvailabilityState(PosServerFiscalDocumentPresentationReadResult posPresentation) =>
        string.Equals(posPresentation.VoidStatus, "voided", StringComparison.OrdinalIgnoreCase)
            ? "VOIDED_PRESENTATION_AVAILABLE"
            : "AVAILABLE";

    private static WebPayReceiptPresentationRejectedException Rejected(
        string errorCode,
        string message,
        int httpStatusCode,
        bool retryable) =>
        new(errorCode, message, httpStatusCode, retryable);
}

public sealed class WebPayPaymentAttemptStatusService : IWebPayPaymentAttemptStatusService
{
    private readonly IWebPayPaymentAttemptStatusRepository _repository;

    public WebPayPaymentAttemptStatusService(IWebPayPaymentAttemptStatusRepository repository)
    {
        _repository = repository;
    }

    public async Task<WebPayPaymentAttemptStatus> GetAsync(
        Guid paymentAttemptId,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        if (paymentAttemptId == Guid.Empty)
        {
            throw new WebPayPaymentAttemptStatusRejectedException(
                "PAYMENT_ATTEMPT_ID_REQUIRED", "Payment reference is required.", 400, false);
        }

        if (correlationId == Guid.Empty)
        {
            throw new WebPayPaymentAttemptStatusRejectedException(
                "CORRELATION_ID_REQUIRED", "X-Correlation-Id header is required.", 400, false);
        }

        var record = await _repository.FindAsync(paymentAttemptId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new WebPayPaymentAttemptStatusRejectedException(
                "WEBPAY_PAYMENT_ATTEMPT_NOT_FOUND", "Payment status was not found.", 404, false);
        }

        return new WebPayPaymentAttemptStatus(
            record.PaymentAttemptId,
            record.ParkingSessionId,
            record.TariffSnapshotId,
            record.SiteGroupId,
            record.SiteId,
            record.SiteGroupName,
            record.SiteName,
            record.TicketReference,
            record.PlateNumber,
            record.AmountMinorUnits,
            record.Currency,
            record.PaymentMethod,
            record.PaymentProvider,
            record.PaymentReference,
            record.EntryTime,
            record.PaymentTime,
            record.PaymentStatus,
            record.ParkingStatus,
            record.ExitAuthorizationId,
            record.ExitAuthorizationStatus,
            record.ExitAuthorizationExpiresAt,
            correlationId);
    }
}
