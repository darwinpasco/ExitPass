using System.Diagnostics;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Terminal-facing Central PMS cash-payment command and readback endpoints.
/// </summary>
public static class TerminalCashPaymentEndpoints
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.CentralPms.Api.TerminalCashPayments");

    /// <summary>
    /// Maps terminal cash-payment command and readback endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapTerminalCashPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/terminal-cash-payments")
            .WithTags("TerminalCashPayments");

        group.MapPost("", CreateAsync)
            .WithName("CreateTerminalCashPayment")
            .Produces<TerminalCashPaymentResponse>(StatusCodes.Status201Created)
            .Produces<TerminalCashPaymentResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/references/{terminalCashTenderId:guid}", ReadbackAsync)
            .WithName("GetTerminalCashPaymentByTenderReference")
            .Produces<TerminalCashPaymentReadbackResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        HttpRequest request,
        TerminalCashPaymentRequest? body,
        ITerminalCashPaymentService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("CreateTerminalCashPayment", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/terminal-cash-payments");
        activity?.SetTag("terminal_cash_tender_id", body?.TerminalCashTenderId);

        if (body is null)
        {
            return Results.BadRequest(BuildError("INVALID_REQUEST", "Request body is required.", Guid.Empty));
        }

        if (!TryReadHeaders(request, out var idempotencyKey, out var correlationId, out var headerError))
        {
            activity?.SetStatus(ActivityStatusCode.Error, headerError!.Message);
            return Results.BadRequest(headerError);
        }

        try
        {
            var command = new TerminalCashPaymentCommand(
                body.TerminalCashTenderId,
                body.CashCustodySessionId,
                body.ParkingSessionId,
                body.TariffSnapshotId,
                body.CashierId,
                body.CashierSessionReference,
                body.CashierShiftId,
                body.TerminalId,
                body.SiteId,
                body.SiteGroupId,
                body.PosServerId,
                body.Currency,
                body.AmountDueMinorUnits,
                body.AmountTenderedMinorUnits,
                body.ChangeDueMinorUnits,
                body.CashReceivedAt,
                (body.DenominationEntries ?? [])
                    .Select(entry => new TerminalCashDenominationEntry(
                        entry.DenominationCode,
                        entry.DenominationValueMinorUnits,
                        entry.Quantity))
                    .ToArray(),
                body.LocalEventReference,
                idempotencyKey!,
                correlationId);

            var result = await service.CreateOrReadAsync(command, cancellationToken);
            var response = ToResponse(result);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("payment_confirmation_id", result.PaymentConfirmationId);
            activity?.SetTag("result_classification", result.ResultClassification);

            return string.Equals(result.ResultClassification, "CREATED", StringComparison.Ordinal)
                ? Results.Created($"/v1/terminal-cash-payments/references/{result.TerminalCashTenderId}", response)
                : Results.Ok(response);
        }
        catch (TerminalCashPaymentRejectedException ex) when (IsBadRequest(ex.ErrorCode))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (TerminalCashPaymentRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.Conflict(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
    }

    private static async Task<IResult> ReadbackAsync(
        Guid terminalCashTenderId,
        HttpRequest request,
        ITerminalCashPaymentService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetTerminalCashPayment", ActivityKind.Server);
        activity?.SetTag("http.route", "GET /v1/terminal-cash-payments/references/{terminalCashTenderId}");
        activity?.SetTag("terminal_cash_tender_id", terminalCashTenderId);

        var correlationId = Guid.Empty;
        if (request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader))
        {
            _ = Guid.TryParse(correlationHeader.ToString(), out correlationId);
        }

        var readback = await service.GetByTerminalCashTenderIdAsync(terminalCashTenderId, cancellationToken);
        if (readback is null)
        {
            return Results.NotFound(BuildError(
                "MISSING_TERMINAL_CASH_TENDER_RECORD",
                "Terminal cash tender reference was not found.",
                correlationId));
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("payment_confirmation_id", readback.PaymentConfirmationId);
        return Results.Ok(ToReadbackResponse(readback));
    }

    private static bool TryReadHeaders(
        HttpRequest request,
        out string? idempotencyKey,
        out Guid correlationId,
        out ErrorResponse? error)
    {
        idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
        var correlationIdRaw = request.Headers["X-Correlation-Id"].FirstOrDefault();
        correlationId = Guid.Empty;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            error = BuildError("INVALID_REQUEST", "Idempotency-Key header is required.", Guid.Empty);
            return false;
        }

        if (!Guid.TryParse(correlationIdRaw, out correlationId))
        {
            error = BuildError("INVALID_REQUEST", "X-Correlation-Id header is required.", Guid.Empty);
            return false;
        }

        error = null;
        idempotencyKey = idempotencyKey.Trim();
        return true;
    }

    private static bool IsBadRequest(string errorCode)
    {
        return errorCode.EndsWith("_REQUIRED", StringComparison.Ordinal) ||
               errorCode.StartsWith("INVALID_CASH_AMOUNTS", StringComparison.Ordinal) ||
               errorCode is "UNSUPPORTED_CURRENCY" or "INVALID_DENOMINATION_ENTRY";
    }

    private static TerminalCashPaymentResponse ToResponse(TerminalCashPaymentResult result)
    {
        return new TerminalCashPaymentResponse(
            result.TerminalCashTenderId,
            result.PaymentAttemptId,
            result.PaymentConfirmationId,
            result.CanonicalPaymentStatus,
            result.ResultClassification,
            result.IdempotencyScope,
            result.SemanticHashSourceVersion,
            result.CreatedAt,
            result.ConfirmedAt,
            result.LastUpdatedAt,
            result.CorrelationId,
            result.FiscalStatus);
    }

    private static TerminalCashPaymentReadbackResponse ToReadbackResponse(TerminalCashPaymentReadback readback)
    {
        return new TerminalCashPaymentReadbackResponse(
            readback.TerminalCashTenderId,
            readback.CashCustodySessionId,
            readback.ParkingSessionId,
            readback.TariffSnapshotId,
            readback.TerminalId,
            readback.SiteId,
            readback.SiteGroupId,
            readback.PosServerId,
            readback.CashierId,
            readback.CashierShiftId,
            readback.Currency,
            readback.AmountDueMinorUnits,
            readback.AmountTenderedMinorUnits,
            readback.ChangeDueMinorUnits,
            readback.CanonicalPaymentStatus,
            readback.PaymentConfirmationId,
            readback.ResultClassification,
            readback.IdempotencyScope,
            readback.SemanticHashSourceVersion,
            readback.CreatedAt,
            readback.ConfirmedAt,
            readback.LastUpdatedAt,
            readback.CorrelationId,
            readback.FiscalStatus);
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId)
    {
        return new ErrorResponse
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };
    }
}
