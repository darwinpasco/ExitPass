using ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;
using ExitPass.PaymentOrchestrator.Contracts.Internal;

namespace ExitPass.PaymentOrchestrator.Api.Endpoints;

/// <summary>
/// Maps internal payment initiation endpoints owned by the Payment Orchestrator.
///
/// BRD:
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - Provider session initiation is handled by POA, not by public clients directly.
/// </summary>
public static class InternalPaymentEndpoints
{
    /// <summary>
    /// Maps internal payment-related endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapInternalPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/v1/internal/payments/initiate", async (
            InitiateProviderPaymentRequest request,
            InitiateProviderPaymentHandler handler,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            var logger = loggerFactory.CreateLogger("ExitPass.PaymentOrchestrator.Api.InternalPaymentEndpoints");

            logger.LogInformation(
                "Received internal provider payment initiation request for PaymentAttemptId {PaymentAttemptId}, ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}",
                request.PaymentAttemptId,
                request.ProviderCode,
                request.ProviderProduct);

            var response = await handler.HandleAsync(request, cancellationToken);

            logger.LogInformation(
                "Created provider payment session for PaymentAttemptId {PaymentAttemptId}, ProviderSessionId {ProviderSessionId}",
                response.PaymentAttemptId,
                response.ProviderSessionId);

            return Results.Ok(response);
        });

        return app;
    }
}
