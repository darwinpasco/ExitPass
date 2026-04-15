using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;

namespace ExitPass.PaymentOrchestrator.Api.Endpoints;

/// <summary>
/// Maps provider-facing webhook endpoints owned by the Payment Orchestrator.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Provider webhooks must enter the system only through POA-owned ingress.
/// - Webhook requests must be verified before they are allowed to influence platform state.
/// - Expected business rejections must map to deterministic HTTP responses, not fallback 500s.
/// - Non-authoritative provider events for a rail must be acknowledged safely without mutating business state.
/// </summary>
public static class ProviderWebhookEndpoints
{
    /// <summary>
    /// Maps provider webhook endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapProviderWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/v1/provider/paymongo/webhooks", async (
            HttpRequest httpRequest,
            VerifyProviderWebhookHandler handler,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(httpRequest);
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            var logger = loggerFactory.CreateLogger("ExitPass.PaymentOrchestrator.Api.ProviderWebhookEndpoints");

            using var reader = new StreamReader(httpRequest.Body);
            var rawBody = await reader.ReadToEndAsync(cancellationToken);

            var headers = httpRequest.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            logger.LogInformation(
                "Received PayMongo webhook request. Path {Path}, HeaderCount {HeaderCount}, BodyLength {BodyLength}",
                httpRequest.Path,
                headers.Count,
                rawBody.Length);

            var request = new ProviderWebhookRequest(headers, rawBody);
            var result = await handler.HandleAsync(request, cancellationToken);

            if (result.Ignored)
            {
                logger.LogInformation(
                    "Ignored non-authoritative PayMongo webhook. EventId {EventId}",
                    result.Code);

                return Results.Ok();
            }

            if (result.Duplicate)
            {
                logger.LogInformation(
                    "Accepted duplicate PayMongo webhook. EventId {EventId}",
                    result.Code);

                return Results.Ok();
            }

            if (result.Accepted)
            {
                logger.LogInformation(
                    "Accepted PayMongo webhook. EventId {EventId}",
                    result.Code);

                return Results.Ok();
            }

            var (statusCode, errorCode, message) = MapRejectedWebhookResult(result.Code);

            logger.LogWarning(
                "Rejected PayMongo webhook. ErrorCode {ErrorCode}, StatusCode {StatusCode}",
                errorCode,
                statusCode);

            return Results.Json(
                new
                {
                    error_code = errorCode,
                    message,
                    correlation_id = httpRequest.Headers.TryGetValue("X-Correlation-Id", out var correlationId)
                        ? correlationId.ToString()
                        : string.Empty,
                    retryable = statusCode is StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable
                },
                statusCode: statusCode);
        });

        return app;
    }

    /// <summary>
    /// Maps deterministic webhook rejection codes into deterministic HTTP responses.
    /// </summary>
    /// <param name="code">The handler rejection code.</param>
    /// <returns>The HTTP status code, canonical error code, and response message.</returns>
    private static (int StatusCode, string ErrorCode, string Message) MapRejectedWebhookResult(string code)
    {
        return code switch
        {
            "WEBHOOK_NOT_AUTHENTIC" => (
                StatusCodes.Status401Unauthorized,
                "WEBHOOK_NOT_AUTHENTIC",
                "Provider webhook authenticity verification failed."),

            "WEBHOOK_MISSING_PARKING_SESSION_ID" => (
                StatusCodes.Status400BadRequest,
                "WEBHOOK_MISSING_PARKING_SESSION_ID",
                "Required webhook metadata field 'parking_session_id' is missing or invalid."),

            "WEBHOOK_MISSING_REQUESTED_BY_USER_ID" => (
                StatusCodes.Status400BadRequest,
                "WEBHOOK_MISSING_REQUESTED_BY_USER_ID",
                "Required webhook metadata field 'requested_by_user_id' is missing or invalid."),

            "WEBHOOK_UNKNOWN_PROVIDER_SESSION" => (
                StatusCodes.Status404NotFound,
                "WEBHOOK_UNKNOWN_PROVIDER_SESSION",
                "The provider session referenced by the webhook is unknown."),

            _ => (
                StatusCodes.Status400BadRequest,
                "WEBHOOK_REJECTED",
                "The provider webhook was rejected.")
        };
    }
}
