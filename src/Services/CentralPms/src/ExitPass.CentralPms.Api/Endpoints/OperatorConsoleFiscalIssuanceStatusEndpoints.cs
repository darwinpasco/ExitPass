using System.Diagnostics;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Contracts.Common;
using OpenTelemetry.Trace;

namespace ExitPass.CentralPms.Api.Endpoints;

/// <summary>
/// Operator Console read-only fiscal issuance status facade.
/// </summary>
public static class OperatorConsoleFiscalIssuanceStatusEndpoints
{
    private const string StatusReadPolicy = "FiscalIssuanceStatusRead";
    private const string FiscalVoidPolicy = "FiscalIssuanceVoidCommand";
    private static readonly ActivitySource ActivitySource = new("ExitPass.CentralPms.Api.OperatorConsoleFiscalIssuanceStatus");

    /// <summary>
    /// Maps Operator Console fiscal issuance status endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapOperatorConsoleFiscalIssuanceStatusEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/ops/operator-console")
            .WithTags("OperatorConsole");

        group.MapGet("/fiscal-issuance/references/{fiscalIssuanceReferenceId:guid}", GetByReferenceIdAsync)
            .WithName("GetOperatorConsoleFiscalIssuanceStatus")
            .WithTags("OperatorConsole")
            .WithMetadata(new ReconciliationPolicyMetadata(StatusReadPolicy))
            .Produces<FiscalIssuanceStatusResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .WithSummary("View Operator Console fiscal issuance status")
            .WithDescription("Returns safe read-only fiscal issuance status after Operator Console view-audit persistence. The read may include a POS Server fiscal document status read when configured, but it does not mutate fiscal, payment, exit, gate, retry, readback, refund, reversal, or document-rendering state.");

        group.MapPost("/fiscal-issuance/references/{fiscalIssuanceReferenceId:guid}/void", VoidByReferenceIdAsync)
            .WithName("VoidOperatorConsoleFiscalIssuanceReference")
            .WithTags("OperatorConsole")
            .WithMetadata(new ReconciliationPolicyMetadata(FiscalVoidPolicy))
            .Produces<OperatorConsoleFiscalIssuanceVoidResponse>(StatusCodes.Status200OK)
            .Produces<OperatorConsoleFiscalIssuanceVoidResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<OperatorConsoleFiscalIssuanceVoidResponse>(StatusCodes.Status404NotFound)
            .Produces<OperatorConsoleFiscalIssuanceVoidResponse>(StatusCodes.Status409Conflict)
            .Produces<OperatorConsoleFiscalIssuanceVoidResponse>(StatusCodes.Status503ServiceUnavailable)
            .WithSummary("Void fiscal document through Operator Console")
            .WithDescription("Initiates a controlled fiscal void/cancellation request for a recorded fiscal issuance reference. This endpoint never refunds payment, opens gates, calls HikCentral, creates replacement fiscal documents, allocates fiscal numbers, or renders statutory documents.");

        return app;
    }

    private static async Task<IResult> GetByReferenceIdAsync(
        Guid fiscalIssuanceReferenceId,
        HttpRequest request,
        IOperatorConsoleFiscalIssuanceStatusService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP GetOperatorConsoleFiscalIssuanceStatus", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleFiscalIssuanceStatusEndpoints");
        var correlationId = ResolveCorrelationId(request);

        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("fiscal_issuance_reference_id", fiscalIssuanceReferenceId);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                request,
                fallbackCorrelationId: correlationId);

            var result = await service.GetAsync(
                new OperatorConsoleFiscalIssuanceStatusQuery(
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    fiscalIssuanceReferenceId,
                    identity.CorrelationId),
                cancellationToken);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("fiscal_status_found", result.Status is not null);

            if (!result.AccessAllowed)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                logger.LogInformation(
                    "Operator Console fiscal issuance status view denied. evaluation_id={EvaluationId} persisted={Persisted} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} correlation_id={CorrelationId}",
                    result.AccessEvaluationId,
                    result.AccessPersisted,
                    fiscalIssuanceReferenceId,
                    result.CorrelationId);

                return Results.Json(
                    BuildError(
                        "OPERATOR_CONSOLE_FISCAL_STATUS_ACCESS_DENIED",
                        "Operator Console fiscal issuance status access was denied.",
                        result.CorrelationId),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (result.Status is null)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
                logger.LogInformation(
                    "Operator Console fiscal issuance status reference not found. evaluation_id={EvaluationId} persisted={Persisted} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} correlation_id={CorrelationId}",
                    result.AccessEvaluationId,
                    result.AccessPersisted,
                    fiscalIssuanceReferenceId,
                    result.CorrelationId);

                return Results.NotFound(BuildError(
                    "FISCAL_ISSUANCE_REFERENCE_NOT_FOUND",
                    "Fiscal issuance reference was not found.",
                    result.CorrelationId));
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
            logger.LogInformation(
                "Operator Console fiscal issuance status view completed. evaluation_id={EvaluationId} persisted={Persisted} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} state={FiscalIssuanceState} correlation_id={CorrelationId}",
                result.AccessEvaluationId,
                result.AccessPersisted,
                fiscalIssuanceReferenceId,
                result.Status.FiscalIssuanceState,
                result.CorrelationId);

            return Results.Ok(FiscalIssuanceStatusResponse.FromReadModel(result.Status));
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_FISCAL_STATUS_REQUEST",
                ex.Message,
                correlationId ?? Guid.Empty));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console fiscal issuance status view failed.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_FISCAL_STATUS_VIEW_FAILED",
                    "The Operator Console fiscal issuance status view could not be completed.",
                    correlationId ?? Guid.Empty),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static Guid? ResolveCorrelationId(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Correlation-Id", out var value) &&
            Guid.TryParse(value.ToString(), out var correlationId) &&
            correlationId != Guid.Empty)
        {
            return correlationId;
        }

        return null;
    }

    private static ErrorResponse BuildError(string errorCode, string message, Guid correlationId) =>
        new()
        {
            ErrorCode = errorCode,
            Message = message,
            CorrelationId = correlationId,
            Retryable = false
        };

    private static async Task<IResult> VoidByReferenceIdAsync(
        Guid fiscalIssuanceReferenceId,
        OperatorConsoleFiscalIssuanceVoidRequest body,
        HttpRequest request,
        IOperatorConsoleFiscalIssuanceVoidService service,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("HTTP VoidOperatorConsoleFiscalIssuanceReference", ActivityKind.Server);
        var logger = loggerFactory.CreateLogger("ExitPass.CentralPms.Api.OperatorConsoleFiscalIssuanceStatusEndpoints");
        var correlationId = ResolveCorrelationId(request) ?? body.CorrelationId ?? Guid.NewGuid();

        activity?.SetTag("url.path", request.Path.Value);
        activity?.SetTag("http.request.method", request.Method);
        activity?.SetTag("correlation_id", correlationId);
        activity?.SetTag("fiscal_issuance_reference_id", fiscalIssuanceReferenceId);

        try
        {
            var identity = OperatorConsoleIdentityContext.Resolve(
                request,
                fallbackCorrelationId: correlationId);

            var result = await service.VoidAsync(
                new OperatorConsoleFiscalIssuanceVoidCommand(
                    identity.UserId,
                    identity.OperatorDeviceBindingId,
                    identity.SiteId,
                    identity.SiteGroupId,
                    identity.OperatorShiftId,
                    fiscalIssuanceReferenceId,
                    body.OperatorActionRequestId,
                    body.ReasonCode,
                    body.ReasonText,
                    body.ConfirmationText,
                    identity.CorrelationId),
                cancellationToken);

            activity?.SetTag("operator_access_evaluation_id", result.AccessEvaluationId);
            activity?.SetTag("access_evaluation_allowed", result.AccessAllowed);
            activity?.SetTag("access_evaluation_persisted", result.AccessPersisted);
            activity?.SetTag("fiscal_void_status", result.VoidResult?.Status);

            if (!result.AccessAllowed && result.VoidResult is null)
            {
                logger.LogInformation(
                    "Operator Console fiscal void denied. evaluation_id={EvaluationId} persisted={Persisted} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} correlation_id={CorrelationId}",
                    result.AccessEvaluationId,
                    result.AccessPersisted,
                    fiscalIssuanceReferenceId,
                    result.CorrelationId);

                return Results.Json(
                    BuildError(
                        "OPERATOR_CONSOLE_FISCAL_VOID_ACCESS_DENIED",
                        "Operator Console fiscal void access was denied.",
                        result.CorrelationId),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var response = OperatorConsoleFiscalIssuanceVoidResponse.FromResult(result);
            logger.LogInformation(
                "Operator Console fiscal void completed. evaluation_id={EvaluationId} persisted={Persisted} fiscal_issuance_reference_id={FiscalIssuanceReferenceId} status={Status} correlation_id={CorrelationId}",
                result.AccessEvaluationId,
                result.AccessPersisted,
                fiscalIssuanceReferenceId,
                response.Status,
                result.CorrelationId);

            return Results.Json(response, statusCode: response.HttpStatusCode);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return Results.BadRequest(BuildError(
                "INVALID_OPERATOR_CONSOLE_FISCAL_VOID_REQUEST",
                ex.Message,
                correlationId));
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            logger.LogError(ex, "Operator Console fiscal void failed safely.");
            return Results.Json(
                BuildError(
                    "OPERATOR_CONSOLE_FISCAL_VOID_FAILED",
                    "The Operator Console fiscal void request could not be completed.",
                    correlationId),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>
/// Operator Console fiscal void request body.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceVoidRequest(
    Guid OperatorActionRequestId,
    string? ReasonCode,
    string? ReasonText,
    string? ConfirmationText,
    Guid? CorrelationId);

/// <summary>
/// Safe Operator Console fiscal void response.
/// </summary>
public sealed record OperatorConsoleFiscalIssuanceVoidResponse(
    bool AccessAllowed,
    string AccessDecision,
    IReadOnlyList<string> AccessDenialReasons,
    bool AccessPersisted,
    bool Accepted,
    string Status,
    int HttpStatusCode,
    IReadOnlyList<string> Errors,
    Guid FiscalIssuanceReferenceId,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    long? FiscalSequenceValue,
    string? FiscalDocumentStatusPosture,
    string? VoidStatus,
    string? VoidReasonCode,
    DateTimeOffset? VoidedAt,
    string? PosServerResultClassification,
    string? CorrelationId,
    string? ErrorPosture,
    bool NewFiscalNumberAllocated,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool RefundOrReversalCreated,
    bool HikCentralCalled,
    bool PaymentProviderCalled,
    bool RenderingGenerated,
    bool ReplacementFiscalDocumentCreated,
    bool FiscalSequenceChangedByCentralPms,
    bool IdempotentReplay)
{
    /// <summary>
    /// Maps an application result to a safe DTO.
    /// </summary>
    public static OperatorConsoleFiscalIssuanceVoidResponse FromResult(OperatorConsoleFiscalIssuanceVoidResult result)
    {
        var voidResult = result.VoidResult ?? throw new InvalidOperationException("Fiscal void result is required.");
        return new OperatorConsoleFiscalIssuanceVoidResponse(
            result.AccessAllowed,
            result.AccessDecision,
            result.AccessDenialReasons,
            result.AccessPersisted,
            voidResult.Accepted,
            voidResult.Status,
            voidResult.HttpStatusCode,
            voidResult.Errors,
            voidResult.FiscalIssuanceReferenceId,
            voidResult.PosServerFiscalDocumentId,
            voidResult.FiscalDocumentNumber,
            voidResult.FiscalSequenceValue,
            voidResult.FiscalDocumentStatusPosture,
            voidResult.VoidStatus,
            voidResult.VoidReasonCode,
            voidResult.VoidedAt,
            voidResult.PosServerResultClassification,
            voidResult.CorrelationId,
            voidResult.ErrorPosture,
            voidResult.NewFiscalNumberAllocated,
            voidResult.PaymentFinalityChanged,
            voidResult.ExitAuthorizationIssued,
            voidResult.GateBehaviorTriggered,
            voidResult.RefundOrReversalCreated,
            voidResult.HikCentralCalled,
            voidResult.PaymentProviderCalled,
            voidResult.RenderingGenerated,
            voidResult.ReplacementFiscalDocumentCreated,
            voidResult.FiscalSequenceChangedByCentralPms,
            voidResult.IdempotentReplay);
    }
}
