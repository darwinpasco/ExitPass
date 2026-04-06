using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;

namespace ExitPass.PaymentOrchestrator.Api.Endpoints;

/// <summary>
/// Maps provider-facing webhook endpoints owned by the Payment Orchestrator.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Provider webhooks must enter the system only through POA-owned ingress.
/// - Webhook requests must be handed to provider-specific verification logic before being accepted.
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

            if (!result.Accepted)
            {
                logger.LogWarning(
                    "Rejected PayMongo webhook. Code {Code}",
                    result.Code);

                return Results.Unauthorized();
            }

            if (result.Duplicate)
            {
                logger.LogInformation(
                    "Accepted duplicate PayMongo webhook. EventId {EventId}",
                    result.Code);
            }
            else
            {
                logger.LogInformation(
                    "Accepted PayMongo webhook. EventId {EventId}",
                    result.Code);
            }

            return Results.Ok();
        });

        return app;
    }
}
