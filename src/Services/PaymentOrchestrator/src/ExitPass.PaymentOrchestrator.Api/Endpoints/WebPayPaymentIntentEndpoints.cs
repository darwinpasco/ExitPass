using ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;

namespace ExitPass.PaymentOrchestrator.Api.Endpoints;

/// <summary>
/// Maps WebPay-facing payment intent endpoints.
/// </summary>
public static class WebPayPaymentIntentEndpoints
{
    /// <summary>
    /// Maps the WebPay payment intent endpoint.
    /// </summary>
    /// <param name="app">Endpoint route builder.</param>
    /// <returns>The endpoint route builder.</returns>
    public static IEndpointRouteBuilder MapWebPayPaymentIntentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/v1/webpay/payment-intents", async (
            WebPayPaymentIntentRequest request,
            WebPayPaymentIntentHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(httpContext);

            if (!request.CorrelationId.HasValue &&
                httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
                Guid.TryParse(headerValue.ToString(), out var headerCorrelationId))
            {
                request.CorrelationId = headerCorrelationId;
            }

            var result = await handler.HandleAsync(request, cancellationToken);
            if (result.Succeeded && result.Response is not null)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = result.Response.CorrelationId.ToString();
                return Results.Ok(result.Response);
            }

            var error = result.Error ?? new WebPayPaymentIntentError(
                StatusCodes.Status502BadGateway,
                "WEBPAY_PAYMENT_INTENT_FAILED",
                "WebPay payment intent could not be created.",
                true);

            var response = new
            {
                errorCode = error.ErrorCode,
                message = error.Message,
                retryable = error.Retryable,
                correlationId = error.CorrelationId
            };

            if (error.CorrelationId.HasValue)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = error.CorrelationId.Value.ToString();
            }

            return Results.Json(response, statusCode: error.StatusCode);
        })
        .WithName("CreateWebPayPaymentIntent")
        .Produces<WebPayPaymentIntentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}
