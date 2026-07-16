using System.Diagnostics;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.TerminalCashPayments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
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

        group.MapPost("/references/{terminalCashTenderId:guid}/fiscal-issuance", IssueFiscalAsync)
            .WithName("IssueTerminalCashPaymentFiscalIssuance")
            .Produces<TerminalCashFiscalIssuanceResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet("/references/{terminalCashTenderId:guid}/fiscal-issuance", ReadFiscalAsync)
            .WithName("GetTerminalCashPaymentFiscalIssuance")
            .Produces<TerminalCashFiscalIssuanceResponse>(StatusCodes.Status200OK)
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

    private static async Task<IResult> IssueFiscalAsync(
        Guid terminalCashTenderId,
        TerminalCashFiscalIssuanceRequest? body,
        HttpRequest request,
        ITerminalCashFiscalIssuanceService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("IssueTerminalCashFiscalIssuance", ActivityKind.Server);
        activity?.SetTag("http.route", "POST /v1/terminal-cash-payments/references/{terminalCashTenderId}/fiscal-issuance");
        activity?.SetTag("terminal_cash_tender_id", terminalCashTenderId);

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
            var result = await service.IssueOrReadAsync(
                    new TerminalCashFiscalIssuanceCommand(terminalCashTenderId, idempotencyKey!, correlationId),
                    cancellationToken)
                .ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("payment_confirmation_id", result.PaymentConfirmationId);
            activity?.SetTag("fiscal_issuance_reference_id", result.FiscalIssuanceReferenceId);
            return Results.Ok(ToFiscalResponse(result));
        }
        catch (TerminalCashFiscalIssuanceRejectedException ex) when (ex.IsNotFound)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.NotFound(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (TerminalCashFiscalIssuanceRejectedException ex) when (IsBadRequest(ex.ErrorCode))
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.BadRequest(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
        catch (TerminalCashFiscalIssuanceRejectedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            return Results.Conflict(BuildError(ex.ErrorCode, ex.Message, correlationId));
        }
    }

    private static async Task<IResult> ReadFiscalAsync(
        Guid terminalCashTenderId,
        HttpRequest request,
        ITerminalCashFiscalIssuanceService service,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("GetTerminalCashFiscalIssuance", ActivityKind.Server);
        activity?.SetTag("http.route", "GET /v1/terminal-cash-payments/references/{terminalCashTenderId}/fiscal-issuance");
        activity?.SetTag("terminal_cash_tender_id", terminalCashTenderId);

        Guid? correlationId = null;
        if (request.Headers.TryGetValue("X-Correlation-Id", out var correlationHeader) &&
            Guid.TryParse(correlationHeader.ToString(), out var parsedCorrelationId))
        {
            correlationId = parsedCorrelationId;
        }

        var result = await service.GetByTerminalCashTenderIdAsync(
                terminalCashTenderId,
                correlationId,
                cancellationToken)
            .ConfigureAwait(false);
        if (result is null)
        {
            return Results.NotFound(BuildError(
                "TERMINAL_CASH_FISCAL_ISSUANCE_NOT_FOUND",
                "Fiscal issuance was not found for the terminal cash tender reference.",
                correlationId ?? Guid.Empty));
        }

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("fiscal_issuance_reference_id", result.FiscalIssuanceReferenceId);
        return Results.Ok(ToFiscalResponse(result));
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
            readback.PaymentAttemptId,
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

    private static TerminalCashFiscalIssuanceResponse ToFiscalResponse(TerminalCashFiscalIssuanceResult result)
    {
        return new TerminalCashFiscalIssuanceResponse(
            result.TerminalCashTenderId,
            result.PaymentAttemptId,
            result.PaymentConfirmationId,
            result.FiscalIssuanceReferenceId,
            ToWireValue(result.FiscalIssuanceState),
            ToWireValue(result.ResultClassification),
            result.PosFiscalDocumentId,
            result.FiscalDocumentNumber,
            result.FiscalNumberAssignedAt,
            result.SemanticHashSourceVersion,
            result.CreatedAt,
            result.UpdatedAt,
            result.CorrelationId,
            result.SafeErrorCode,
            result.SafeErrorPosture,
            result.PosServerCallAttempted,
            result.ExitAuthorizationIssued,
            result.GateBehaviorTriggered);
    }

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

    private static string? ToWireValue(FiscalIssuanceResultClassification? value) =>
        value switch
        {
            FiscalIssuanceResultClassification.NewlyCreated => "NEWLY_CREATED",
            FiscalIssuanceResultClassification.IdempotentReplay => "IDEMPOTENT_REPLAY",
            null => null,
            _ => value.ToString()
        };

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
