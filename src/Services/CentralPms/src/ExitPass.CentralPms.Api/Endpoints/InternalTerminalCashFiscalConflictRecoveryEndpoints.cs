using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.TerminalCashPayments;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Internal, single-record recovery for an unchanged terminal-cash fiscal obligation whose
/// persisted configuration failure is explicitly approved for guarded retry.
/// </summary>
public static class InternalTerminalCashFiscalConfigurationRecoveryEndpoints
{
    public static IEndpointRouteBuilder MapInternalTerminalCashFiscalConfigurationRecoveryEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/v1/terminal-cash-fiscal-recovery")
            .WithTags("InternalTerminalCashFiscalRecovery")
            .RequireInternalServiceMtls();

        group.MapPost("/{terminalCashTenderId:guid}/configuration-failure", RecoverAsync)
            .WithName("RecoverTerminalCashFiscalConfigurationFailure")
            .Produces<TerminalCashFiscalConflictRecoveryResult>(StatusCodes.Status200OK)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status400BadRequest)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status404NotFound)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status409Conflict);

        group.MapPost("/{terminalCashTenderId:guid}/reporting-period-conflict", RecoverAsync)
            .WithName("RecoverTerminalCashFiscalReportingPeriodConflict")
            .Produces<TerminalCashFiscalConflictRecoveryResult>(StatusCodes.Status200OK)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status400BadRequest)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status404NotFound)
            .Produces<InternalTerminalCashFiscalRecoveryError>(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> RecoverAsync(
        Guid terminalCashTenderId,
        InternalTerminalCashFiscalRecoveryRequest? body,
        HttpRequest request,
        ITerminalCashFiscalIssuanceService service,
        CancellationToken cancellationToken)
    {
        var deliveryIdempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault();
        var correlationRaw = request.Headers["X-Correlation-Id"].FirstOrDefault();
        if (body is null ||
            string.IsNullOrWhiteSpace(deliveryIdempotencyKey) ||
            !Guid.TryParse(correlationRaw, out var recoveryCorrelationId) ||
            recoveryCorrelationId == Guid.Empty)
        {
            return Results.BadRequest(new InternalTerminalCashFiscalRecoveryError(
                "TERMINAL_CASH_FISCAL_RECOVERY_REQUEST_INVALID",
                "A request body, Idempotency-Key, and X-Correlation-Id are required."));
        }

        try
        {
            var result = await service.RecoverConfigurationFailureAsync(
                    new TerminalCashFiscalConflictRecoveryCommand(
                        terminalCashTenderId,
                        body.FiscalIssuanceReferenceId,
                        body.ExpectedPaymentAttemptId,
                        body.ExpectedPaymentConfirmationId,
                        body.ExpectedParkingSessionId,
                        body.ExpectedTariffSnapshotId,
                        body.ExpectedAmountMinorUnits,
                        body.ExpectedCurrency,
                        body.ExpectedDeliveryRequestHash,
                        body.ExpectedFiscalSemanticRequestHash,
                        body.ExpectedUpstreamFinalityReference,
                        deliveryIdempotencyKey.Trim(),
                        body.ExpectedTransactionCorrelationId,
                        body.ExpectedFiscalCorrelationId,
                        recoveryCorrelationId,
                        body.ActorServiceIdentityId,
                        body.ApprovalReference,
                        body.ReasonCode,
                        body.SafeJustification),
                    cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (TerminalCashFiscalIssuanceRejectedException ex) when (ex.IsNotFound)
        {
            return Results.NotFound(new InternalTerminalCashFiscalRecoveryError(ex.ErrorCode, ex.Message));
        }
        catch (TerminalCashFiscalIssuanceRejectedException ex)
        {
            return Results.Conflict(new InternalTerminalCashFiscalRecoveryError(ex.ErrorCode, ex.Message));
        }
    }
}

public sealed record InternalTerminalCashFiscalRecoveryRequest(
    Guid FiscalIssuanceReferenceId,
    Guid ExpectedPaymentAttemptId,
    Guid ExpectedPaymentConfirmationId,
    Guid ExpectedParkingSessionId,
    Guid ExpectedTariffSnapshotId,
    long ExpectedAmountMinorUnits,
    string ExpectedCurrency,
    string ExpectedDeliveryRequestHash,
    string ExpectedFiscalSemanticRequestHash,
    string ExpectedUpstreamFinalityReference,
    Guid ExpectedTransactionCorrelationId,
    Guid ExpectedFiscalCorrelationId,
    Guid ActorServiceIdentityId,
    string ApprovalReference,
    string ReasonCode,
    string SafeJustification);

public sealed record InternalTerminalCashFiscalRecoveryError(string ErrorCode, string Message);
