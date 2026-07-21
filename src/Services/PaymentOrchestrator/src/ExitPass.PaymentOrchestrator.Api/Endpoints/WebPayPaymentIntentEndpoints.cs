using ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
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

        app.MapPost("/v1/webpay/parking-session", async (
            WebPayParkingSessionResolveRequest request,
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

            var result = await handler.ResolveAsync(request, cancellationToken);
            if (result.Succeeded && result.Response is not null)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = result.Response.CorrelationId.ToString();
                return Results.Ok(result.Response);
            }

            var error = result.Error ?? new WebPayPaymentIntentError(
                StatusCodes.Status502BadGateway,
                "WEBPAY_PARKING_SESSION_RESOLVE_FAILED",
                "WebPay parking session could not be resolved.",
                true);

            if (error.CorrelationId.HasValue)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = error.CorrelationId.Value.ToString();
            }

            return Results.Json(BuildErrorResponse(error), statusCode: error.StatusCode);
        })
        .WithName("ResolveWebPayParkingSession")
        .Produces<WebPayParkingSessionResolveResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

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

            if (error.CorrelationId.HasValue)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = error.CorrelationId.Value.ToString();
            }

            return Results.Json(BuildErrorResponse(error), statusCode: error.StatusCode);
        })
        .WithName("CreateWebPayPaymentIntent")
        .Produces<WebPayPaymentIntentResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/v1/webpay/payment-attempts/{paymentAttemptId:guid}/receipt-presentation", async (
            Guid paymentAttemptId,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            var result = await centralPmsClient.GetReceiptPresentationAsync(
                    paymentAttemptId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded && result.Value is not null)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString();
                return Results.Ok(ToReceiptPresentationResponse(result.Value));
            }

            var error = result.Error ?? new CentralPmsWebPayError(
                StatusCodes.Status502BadGateway,
                "WEBPAY_RECEIPT_PRESENTATION_READ_FAILED",
                "Sales Invoice presentation could not be retrieved.",
                true,
                correlationId);

            if (error.CorrelationId.HasValue)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = error.CorrelationId.Value.ToString();
            }
            else
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
            }

            return Results.Json(BuildErrorResponse(error, correlationId), statusCode: error.StatusCode);
        })
        .WithName("GetWebPayReceiptPresentation")
        .Produces<WebPayReceiptPresentationResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static Guid ReadOrCreateCorrelationId(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
            Guid.TryParse(headerValue.ToString(), out var headerCorrelationId))
        {
            return headerCorrelationId;
        }

        return Guid.NewGuid();
    }

    private static WebPayReceiptPresentationResponse ToReceiptPresentationResponse(
        CentralPmsWebPayReceiptPresentation result) =>
        new(
            result.PaymentAttemptId,
            result.PaymentConfirmationId,
            result.FiscalIssuanceReferenceId,
            result.FiscalIssuanceState,
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

    private static object BuildErrorResponse(WebPayPaymentIntentError error)
    {
        var response = new Dictionary<string, object?>
        {
            ["errorCode"] = error.ErrorCode,
            ["message"] = error.Message,
            ["retryable"] = error.Retryable,
            ["correlationId"] = error.CorrelationId,
            ["parkingSessionId"] = error.ParkingSessionId,
            ["paymentAttemptId"] = error.PaymentAttemptId,
            ["status"] = error.Status,
            ["handoff"] = error.Handoff,
            ["handoffUrl"] = error.Handoff?.HandoffUrl,
            ["resumePaymentUrl"] = error.Handoff?.HandoffUrl,
            ["paymentMethod"] = error.PaymentMethod,
            ["amountMinorUnits"] = error.AmountMinorUnits,
            ["currency"] = error.Currency,
            ["siteName"] = error.SiteName,
            ["ticketReference"] = error.TicketReference,
            ["plateNumber"] = error.PlateNumber
        };

        AddIfNotBlank(response, "selectedProviderCode", error.SelectedProviderCode);
        AddIfNotBlank(response, "fallbackProviderCode", error.FallbackProviderCode);
        AddIfNotBlank(response, "providerProduct", error.ProviderProduct);

        return response;
    }

    private static object BuildErrorResponse(CentralPmsWebPayError error, Guid fallbackCorrelationId)
    {
        return new Dictionary<string, object?>
        {
            ["errorCode"] = error.ErrorCode,
            ["message"] = error.Message,
            ["retryable"] = error.Retryable,
            ["correlationId"] = error.CorrelationId ?? fallbackCorrelationId,
            ["paymentAttemptId"] = error.PaymentAttemptId
        };
    }

    private static void AddIfNotBlank(Dictionary<string, object?> response, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            response[key] = value;
        }
    }
}
