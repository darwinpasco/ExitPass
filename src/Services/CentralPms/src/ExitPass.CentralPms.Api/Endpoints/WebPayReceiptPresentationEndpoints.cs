using System.Diagnostics;
using ExitPass.CentralPms.Application.WebPay;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Public;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// WebPay-facing readback endpoints for POS Server-owned receipt presentation.
/// </summary>
public static class WebPayReceiptPresentationEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.WebPayReceiptPresentation");

    public static IEndpointRouteBuilder MapWebPayReceiptPresentationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/webpay/payment-attempts")
            .WithTags("WebPay");

        group.MapGet("/{paymentAttemptId:guid}/receipt-presentation", ReadReceiptPresentationAsync)
            .WithName("GetWebPayReceiptPresentation")
            .Produces<WebPayReceiptPresentationResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> ReadReceiptPresentationAsync(
        Guid paymentAttemptId,
        HttpRequest request,
        IWebPayReceiptPresentationService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetWebPayReceiptPresentation", ActivityKind.Server);
        activity?.SetTag("http.route", "GET /v1/webpay/payment-attempts/{paymentAttemptId}/receipt-presentation");
        activity?.SetTag("payment_attempt_id", paymentAttemptId);

        if (!TryReadCorrelationId(request, out var correlationId, out var headerError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, headerError!.Message);
            return Results.BadRequest(headerError);
        }

        try
        {
            var result = await service.GetByPaymentAttemptIdAsync(
                    paymentAttemptId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("fiscal_issuance_reference_id", result.FiscalIssuanceReferenceId);
            activity?.SetTag("pos_fiscal_document_id", result.PosFiscalDocumentId);
            return Results.Ok(ToResponse(result));
        }
        catch (WebPayReceiptPresentationRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.Json(
                BuildError(ex.ErrorCode, ex.Message, correlationId, ex.Retryable),
                statusCode: ex.HttpStatusCode);
        }
    }

    private static bool TryReadCorrelationId(
        HttpRequest request,
        out Guid correlationId,
        out ErrorResponse? error)
    {
        var correlationIdRaw = request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (!Guid.TryParse(correlationIdRaw, out correlationId))
        {
            error = BuildError("INVALID_REQUEST", "X-Correlation-Id header is required.", Guid.Empty, retryable: false);
            return false;
        }

        error = null;
        return true;
    }

    private static WebPayReceiptPresentationResponse ToResponse(WebPayReceiptPresentationResult result) =>
        new(
            result.PaymentAttemptId,
            result.PaymentConfirmationId,
            result.FiscalIssuanceReferenceId,
            ToWireValue(result.FiscalIssuanceState),
            result.PosFiscalDocumentId,
            result.FiscalDocumentNumber,
            result.FiscalDocumentStatus,
            result.ReceiptAvailabilityState,
            result.PresentationVersion,
            result.TemplateVersion,
            result.ContentType,
            result.AuthoritativePresentation,
            result.VoidStatus,
            result.VoidReasonCode,
            result.VoidedAt,
            result.CreatedAt,
            result.UpdatedAt,
            result.CorrelationId);

    private static string ToWireValue(FiscalIssuanceIntegrationState value) =>
        value switch
        {
            FiscalIssuanceIntegrationState.NotRequired => "NOT_REQUIRED",
            FiscalIssuanceIntegrationState.PendingFiscalIssuance => "PENDING_FISCAL_ISSUANCE",
            FiscalIssuanceIntegrationState.FiscalIssuanceRequested => "FISCAL_ISSUANCE_REQUESTED",
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded => "FISCAL_ISSUANCE_RECORDED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed => "FISCAL_ISSUANCE_REPLAYED",
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict => "FISCAL_ISSUANCE_CONFLICT",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest => "FISCAL_ISSUANCE_FAILED_REQUEST",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration => "FISCAL_ISSUANCE_FAILED_CONFIGURATION",
            FiscalIssuanceIntegrationState.FiscalIssuanceFailedService => "FISCAL_ISSUANCE_FAILED_SERVICE",
            FiscalIssuanceIntegrationState.FiscalIssuanceUnknown => "FISCAL_ISSUANCE_UNKNOWN",
            FiscalIssuanceIntegrationState.FiscalIssuanceManualReview => "FISCAL_ISSUANCE_MANUAL_REVIEW",
            FiscalIssuanceIntegrationState.FiscalIssuanceExceptionReleased => "FISCAL_ISSUANCE_EXCEPTION_RELEASED",
            FiscalIssuanceIntegrationState.FiscalIssuanceReconciled => "FISCAL_ISSUANCE_RECONCILED",
            _ => value.ToString()
        };

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId, bool retryable) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = retryable
        };
}
