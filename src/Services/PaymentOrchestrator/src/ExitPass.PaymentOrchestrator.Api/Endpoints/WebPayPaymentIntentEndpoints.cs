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

        app.MapPost("/v1/webpay/statutory-discounts/availability", async (
            WebPayStatutoryDiscountAvailabilityRequest request,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            if (!TryBuildCentralPmsStatutoryAvailabilityRequest(request, correlationId, out var centralPmsRequest, out var validationError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await centralPmsClient.ResolveStatutoryDiscountAvailabilityAsync(
                    centralPmsRequest,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToStatutoryAvailabilityResult(result, httpContext, correlationId);
        })
        .WithName("ResolveWebPayStatutoryDiscountAvailability")
        .Produces<WebPayStatutoryDiscountAvailabilityResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/v1/webpay/statutory-discounts/pending-lifecycle/rediscover", async (
            WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest request,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            if (!TryBuildCentralPmsStatutoryPendingLifecycleRediscoveryRequest(
                    request,
                    correlationId,
                    out var centralPmsRequest,
                    out var validationError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await centralPmsClient.RediscoverStatutoryDiscountPendingLifecycleAsync(
                    centralPmsRequest,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToStatutoryPendingLifecycleRediscoveryResult(result, httpContext, correlationId);
        })
        .WithName("RediscoverWebPayStatutoryDiscountPendingLifecycle")
        .Produces<WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/v1/webpay/statutory-discounts/decisions", async (
            WebPayStatutoryDiscountDecisionRequest request,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            if (!TryReadIdempotencyKey(httpContext, correlationId, out var idempotencyKey, out var idempotencyError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(idempotencyError, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryBuildCentralPmsStatutoryRequest(request, correlationId, out var centralPmsRequest, out var validationError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var availability = await centralPmsClient.ResolveStatutoryDiscountAvailabilityAsync(
                    new CentralPmsStatutoryDiscountAvailabilityRequest(
                        centralPmsRequest.RequestReference,
                        centralPmsRequest.ParkingSessionId,
                        centralPmsRequest.EntitlementType,
                        BeneficiaryResidencySatisfied: null),
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!availability.Succeeded || availability.Value is null)
            {
                return ToStatutoryAvailabilityGateFailure(availability, httpContext, correlationId);
            }

            if (!availability.Value.Covers(centralPmsRequest.EntitlementType))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = availability.Value.CorrelationId.ToString();
                return Results.Json(
                    BuildStatutoryErrorResponse(
                        "WEBPAY_STATUTORY_PRIVILEGE_NOT_AVAILABLE",
                        "Parking privilege requests are not available for this parking session. You may continue with the regular parking amount.",
                        retryable: availability.Value.Retryable,
                        availability.Value.CorrelationId),
                    statusCode: StatusCodes.Status409Conflict);
            }

            var result = await centralPmsClient.SubmitStatutoryDiscountDecisionAsync(
                    centralPmsRequest,
                    idempotencyKey,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToStatutoryDecisionResult(result, httpContext, correlationId);
        })
        .WithName("SubmitWebPayStatutoryDiscountDecision")
        .Produces<WebPayStatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
        .Produces<WebPayStatutoryDiscountDecisionResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/v1/webpay/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId:guid}", async (
            Guid statutoryDiscountDecisionCommandId,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            var result = await centralPmsClient.GetStatutoryDiscountDecisionAsync(
                    statutoryDiscountDecisionCommandId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            return ToStatutoryDecisionResult(result, httpContext, correlationId);
        })
        .WithName("GetWebPayStatutoryDiscountDecision")
        .Produces<WebPayStatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapPost("/v1/webpay/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId:guid}/apply-payable-basis", async (
            Guid statutoryDiscountDecisionCommandId,
            WebPayStatutoryDiscountDecisionRequest request,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(centralPmsClient);
            ArgumentNullException.ThrowIfNull(httpContext);

            var correlationId = ReadOrCreateCorrelationId(httpContext);
            if (!TryReadIdempotencyKey(httpContext, correlationId, out var idempotencyKey, out var idempotencyError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(idempotencyError, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!TryBuildCentralPmsStatutoryRequest(request, correlationId, out var centralPmsRequest, out var validationError))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var readback = await centralPmsClient.GetStatutoryDiscountDecisionAsync(
                    statutoryDiscountDecisionCommandId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!readback.Succeeded || readback.Value is null)
            {
                return ToStatutoryDecisionResult(readback, httpContext, correlationId);
            }

            if (readback.Value.ParkingSessionId != centralPmsRequest.ParkingSessionId ||
                !string.Equals(readback.Value.EntitlementType, centralPmsRequest.EntitlementType, StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(
                    BuildStatutoryErrorResponse(
                        "STATUTORY_DISCOUNT_DECISION_REQUEST_MISMATCH",
                        "The application request does not match the canonical statutory-discount decision.",
                        retryable: false,
                        correlationId),
                    statusCode: StatusCodes.Status409Conflict);
            }

            var result = await centralPmsClient.ApplyStatutoryDiscountPayableBasisAsync(
                    centralPmsRequest,
                    idempotencyKey,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded &&
                result.Value is not null &&
                result.Value.StatutoryDiscountDecisionCommandId != statutoryDiscountDecisionCommandId)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = correlationId.ToString();
                return Results.Json(
                    BuildStatutoryErrorResponse(
                        "STATUTORY_DISCOUNT_DECISION_COMMAND_MISMATCH",
                        "Central PMS returned a different statutory-discount decision.",
                        retryable: false,
                        correlationId),
                    statusCode: StatusCodes.Status409Conflict);
            }

            return ToStatutoryDecisionResult(result, httpContext, correlationId);
        })
        .WithName("ApplyWebPayStatutoryDiscountPayableBasis")
        .Produces<WebPayStatutoryDiscountDecisionResponse>(StatusCodes.Status200OK)
        .Produces<WebPayStatutoryDiscountDecisionResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .Produces(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status502BadGateway)
        .Produces(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/v1/webpay/payment-attempts/{paymentAttemptId:guid}/status", async (
            Guid paymentAttemptId,
            ICentralPmsWebPayClient centralPmsClient,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var correlationId = ReadOrCreateCorrelationId(httpContext);
            var result = await centralPmsClient.GetPaymentAttemptStatusAsync(
                    paymentAttemptId,
                    correlationId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.Succeeded && result.Value is not null)
            {
                httpContext.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString();
                var value = result.Value;
                return Results.Ok(new WebPayPaymentAttemptStatusResponse(
                    value.PaymentAttemptId,
                    value.ParkingSessionId,
                    value.TariffSnapshotId,
                    value.SiteGroupId,
                    value.SiteId,
                    value.SiteGroupName,
                    value.SiteName,
                    value.TicketReference,
                    value.PlateNumber,
                    value.AmountMinorUnits,
                    value.Currency,
                    value.PaymentMethod,
                    value.PaymentProvider,
                    value.PaymentReference,
                    value.EntryTime,
                    value.PaymentTime,
                    value.PaymentStatus,
                    value.ParkingStatus,
                    value.ExitAuthorizationId,
                    value.ExitAuthorizationStatus,
                    value.ExitAuthorizationExpiresAt,
                    value.CorrelationId));
            }

            var error = result.Error ?? new CentralPmsWebPayError(
                StatusCodes.Status502BadGateway,
                "WEBPAY_PAYMENT_ATTEMPT_STATUS_READ_FAILED",
                "Payment status could not be retrieved.",
                true,
                correlationId);
            httpContext.Response.Headers["X-Correlation-Id"] = (error.CorrelationId ?? correlationId).ToString();
            return Results.Json(BuildErrorResponse(error, correlationId), statusCode: error.StatusCode);
        })
        .WithName("GetWebPayPaymentAttemptStatus")
        .Produces<WebPayPaymentAttemptStatusResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
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

    private static bool TryReadIdempotencyKey(
        HttpContext httpContext,
        Guid correlationId,
        out string idempotencyKey,
        out object error)
    {
        idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
            idempotencyKey.Length <= 128 &&
            !idempotencyKey.Contains('\r') &&
            !idempotencyKey.Contains('\n'))
        {
            error = new { };
            return true;
        }

        error = BuildStatutoryErrorResponse(
            "WEBPAY_STATUTORY_DISCOUNT_IDEMPOTENCY_KEY_REQUIRED",
            "A valid Idempotency-Key header is required.",
            retryable: false,
            correlationId);
        return false;
    }

    private static bool TryBuildCentralPmsStatutoryAvailabilityRequest(
        WebPayStatutoryDiscountAvailabilityRequest request,
        Guid correlationId,
        out CentralPmsStatutoryDiscountAvailabilityRequest centralPmsRequest,
        out object error)
    {
        var errors = new List<string>();
        if (request.RequestReference == Guid.Empty)
        {
            errors.Add("requestReference is required.");
        }

        if (request.ParkingSessionId == Guid.Empty)
        {
            errors.Add("parkingSessionId is required.");
        }

        var entitlementType = Normalize(request.RequestedEntitlementType);
        if (entitlementType.Length > 0 && entitlementType is not "SENIOR_CITIZEN" and not "PWD")
        {
            errors.Add("requestedEntitlementType must be SENIOR_CITIZEN or PWD.");
        }

        if (errors.Count > 0)
        {
            centralPmsRequest = null!;
            error = BuildStatutoryErrorResponse(
                "WEBPAY_STATUTORY_AVAILABILITY_REQUEST_INVALID",
                $"The statutory availability request is invalid. errors={string.Join(" ", errors)}",
                retryable: false,
                correlationId);
            return false;
        }

        centralPmsRequest = new CentralPmsStatutoryDiscountAvailabilityRequest(
            request.RequestReference,
            request.ParkingSessionId,
            entitlementType.Length == 0 ? null : entitlementType,
            BeneficiaryResidencySatisfied: null);
        error = new { };
        return true;
    }

    private static bool TryBuildCentralPmsStatutoryPendingLifecycleRediscoveryRequest(
        WebPayStatutoryDiscountPendingLifecycleRediscoveryRequest request,
        Guid correlationId,
        out CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest centralPmsRequest,
        out object error)
    {
        var errors = new List<string>();
        var lookupMode = Normalize(request.LookupMode);
        var ticketReference = BlankToNull(request.TicketReference);
        var plateNumber = BlankToNull(request.PlateNumber)?.ToUpperInvariant();
        var entitlementType = Normalize(request.EntitlementType);

        if (lookupMode is not "PARKING_SESSION_ID" and not "TICKET_REFERENCE" and not "PLATE_NUMBER")
        {
            errors.Add("lookupMode must be PARKING_SESSION_ID, TICKET_REFERENCE, or PLATE_NUMBER.");
        }

        if (request.SiteId is null || request.SiteId == Guid.Empty)
        {
            errors.Add("siteId is required.");
        }

        if (request.SiteGroupId is null || request.SiteGroupId == Guid.Empty)
        {
            errors.Add("siteGroupId is required.");
        }

        if (entitlementType.Length > 0 && entitlementType is not "SENIOR_CITIZEN" and not "PWD")
        {
            errors.Add("entitlementType must be SENIOR_CITIZEN or PWD.");
        }

        if (lookupMode == "PARKING_SESSION_ID")
        {
            if (request.ParkingSessionId is null || request.ParkingSessionId == Guid.Empty)
            {
                errors.Add("parkingSessionId is required for PARKING_SESSION_ID lookup.");
            }

            if (!string.IsNullOrWhiteSpace(ticketReference) || !string.IsNullOrWhiteSpace(plateNumber))
            {
                errors.Add("PARKING_SESSION_ID lookup must not include ticketReference or plateNumber.");
            }
        }
        else if (lookupMode == "TICKET_REFERENCE")
        {
            if (string.IsNullOrWhiteSpace(ticketReference))
            {
                errors.Add("ticketReference is required for TICKET_REFERENCE lookup.");
            }

            if (request.ParkingSessionId is not null || !string.IsNullOrWhiteSpace(plateNumber))
            {
                errors.Add("TICKET_REFERENCE lookup must not include parkingSessionId or plateNumber.");
            }
        }
        else if (lookupMode == "PLATE_NUMBER")
        {
            if (string.IsNullOrWhiteSpace(plateNumber))
            {
                errors.Add("plateNumber is required for PLATE_NUMBER lookup.");
            }

            if (request.ParkingSessionId is not null || !string.IsNullOrWhiteSpace(ticketReference))
            {
                errors.Add("PLATE_NUMBER lookup must not include parkingSessionId or ticketReference.");
            }
        }

        if (errors.Count > 0)
        {
            centralPmsRequest = null!;
            error = BuildStatutoryErrorResponse(
                "WEBPAY_STATUTORY_PENDING_LIFECYCLE_REDISCOVERY_REQUEST_INVALID",
                $"The statutory pending-lifecycle rediscovery request is invalid. errors={string.Join(" ", errors)}",
                retryable: false,
                correlationId);
            return false;
        }

        centralPmsRequest = new CentralPmsStatutoryDiscountPendingLifecycleRediscoveryRequest(
            lookupMode,
            request.ParkingSessionId,
            request.SiteId!.Value,
            request.SiteGroupId!.Value,
            ticketReference,
            plateNumber,
            BlankToNull(request.VendorSystemId),
            entitlementType.Length == 0 ? null : entitlementType);
        error = new { };
        return true;
    }

    private static bool TryBuildCentralPmsStatutoryRequest(
        WebPayStatutoryDiscountDecisionRequest request,
        Guid correlationId,
        out CentralPmsStatutoryDiscountDecisionRequest centralPmsRequest,
        out object error)
    {
        var errors = new List<string>();
        if (request.RequestReference == Guid.Empty)
        {
            errors.Add("requestReference is required.");
        }

        if (request.ParkingSessionId == Guid.Empty)
        {
            errors.Add("parkingSessionId is required.");
        }

        var entitlementType = Normalize(request.EntitlementType);
        if (entitlementType is not "SENIOR_CITIZEN" and not "PWD")
        {
            errors.Add("entitlementType must be SENIOR_CITIZEN or PWD.");
        }

        if (string.IsNullOrWhiteSpace(request.IdDocumentType))
        {
            errors.Add("idDocumentType is required.");
        }

        if (string.IsNullOrWhiteSpace(request.IssuingAuthority))
        {
            errors.Add("issuingAuthority is required.");
        }

        if (string.IsNullOrWhiteSpace(request.MaskedIdReference))
        {
            errors.Add("maskedIdReference is required.");
        }
        else if (!request.MaskedIdReference.Contains('*'))
        {
            errors.Add("maskedIdReference must be masked.");
        }

        if (!request.RequesterAttestation)
        {
            errors.Add("requesterAttestation is required.");
        }

        if (request.EvidenceReferences is not null)
        {
            foreach (var evidence in request.EvidenceReferences)
            {
                if (string.IsNullOrWhiteSpace(evidence.EvidenceType) ||
                    string.IsNullOrWhiteSpace(evidence.CaptureMethod))
                {
                    errors.Add("evidenceReferences require evidenceType and captureMethod.");
                    break;
                }

                if (!string.IsNullOrWhiteSpace(evidence.ReferenceNumberMasked) &&
                    !evidence.ReferenceNumberMasked.Contains('*'))
                {
                    errors.Add("evidenceReferences referenceNumberMasked must be masked.");
                    break;
                }
            }
        }

        if (errors.Count > 0)
        {
            centralPmsRequest = null!;
            error = BuildStatutoryErrorResponse(
                "WEBPAY_STATUTORY_DISCOUNT_REQUEST_INVALID",
                $"The statutory-discount request is invalid. errors={string.Join(" ", errors)}",
                retryable: false,
                correlationId);
            return false;
        }

        centralPmsRequest = new CentralPmsStatutoryDiscountDecisionRequest(
            request.RequestReference,
            request.ParkingSessionId,
            request.SiteId,
            request.SiteGroupId,
            BlankToNull(request.TicketReference),
            BlankToNull(request.PlateNumber),
            entitlementType,
            request.IdDocumentType!.Trim(),
            request.IssuingAuthority!.Trim(),
            request.ExpiryDate,
            request.MaskedIdReference!.Trim(),
            request.EvidenceCaptureRequested,
            request.EvidenceReferences?.Select(static evidence => new CentralPmsStatutoryDiscountEvidenceReference(
                evidence.EvidenceType!.Trim(),
                evidence.CaptureMethod!.Trim(),
                BlankToNull(evidence.FileName),
                BlankToNull(evidence.ContentType),
                evidence.SizeBytes,
                BlankToNull(evidence.StorageReference),
                BlankToNull(evidence.ReferenceNumberMasked),
                BlankToNull(evidence.VerificationStatus))).ToArray(),
            request.RequesterAttestation,
            BlankToNull(request.AttestationNotes),
            BlankToNull(request.ReasonCode),
            request.OriginalTariffSnapshotId);
        error = new { };
        return true;
    }

    private static IResult ToStatutoryAvailabilityResult(
        CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability> result,
        HttpContext httpContext,
        Guid fallbackCorrelationId)
    {
        if (result.Succeeded && result.Value is not null)
        {
            httpContext.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString();
            return Results.Ok(ToStatutoryAvailabilityResponse(result.Value));
        }

        return ToStatutoryAvailabilityGateFailure(result, httpContext, fallbackCorrelationId);
    }

    private static IResult ToStatutoryPendingLifecycleRediscoveryResult(
        CentralPmsWebPayResult<CentralPmsStatutoryDiscountPendingLifecycleRediscovery> result,
        HttpContext httpContext,
        Guid fallbackCorrelationId)
    {
        if (result.Succeeded && result.Value is not null)
        {
            httpContext.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString();
            return Results.Ok(ToStatutoryPendingLifecycleRediscoveryResponse(result.Value));
        }

        var error = result.Error ?? new CentralPmsWebPayError(
            StatusCodes.Status502BadGateway,
            "WEBPAY_STATUTORY_PENDING_LIFECYCLE_REDISCOVERY_FAILED",
            "The parking privilege request could not be checked right now. Please try again.",
            true,
            fallbackCorrelationId);
        var browserSafeError = ToBrowserSafeStatutoryError(error, fallbackCorrelationId);
        httpContext.Response.Headers["X-Correlation-Id"] = browserSafeError.CorrelationId.ToString();
        return Results.Json(
            BuildStatutoryErrorResponse(
                browserSafeError.ErrorCode,
                browserSafeError.Message,
                browserSafeError.Retryable,
                browserSafeError.CorrelationId),
            statusCode: browserSafeError.StatusCode);
    }

    private static IResult ToStatutoryAvailabilityGateFailure(
        CentralPmsWebPayResult<CentralPmsStatutoryDiscountAvailability> result,
        HttpContext httpContext,
        Guid fallbackCorrelationId)
    {
        var error = result.Error ?? new CentralPmsWebPayError(
            StatusCodes.Status502BadGateway,
            "WEBPAY_STATUTORY_AVAILABILITY_FAILED",
            "Parking privilege availability could not be resolved.",
            true,
            fallbackCorrelationId);
        var browserSafeError = ToBrowserSafeStatutoryAvailabilityError(error, fallbackCorrelationId);
        httpContext.Response.Headers["X-Correlation-Id"] = browserSafeError.CorrelationId.ToString();
        return Results.Json(
            BuildStatutoryErrorResponse(
                browserSafeError.ErrorCode,
                browserSafeError.Message,
                browserSafeError.Retryable,
                browserSafeError.CorrelationId),
            statusCode: browserSafeError.StatusCode);
    }

    private static IResult ToStatutoryDecisionResult(
        CentralPmsWebPayResult<CentralPmsStatutoryDiscountDecision> result,
        HttpContext httpContext,
        Guid fallbackCorrelationId)
    {
        if (result.Succeeded && result.Value is not null)
        {
            httpContext.Response.Headers["X-Correlation-Id"] = result.Value.CorrelationId.ToString();
            return Results.Ok(ToStatutoryDiscountResponse(result.Value));
        }

        var error = result.Error ?? new CentralPmsWebPayError(
            StatusCodes.Status502BadGateway,
            "WEBPAY_STATUTORY_DISCOUNT_READBACK_FAILED",
            "Statutory discount state could not be resolved.",
            true,
            fallbackCorrelationId);
        var browserSafeError = ToBrowserSafeStatutoryError(error, fallbackCorrelationId);
        httpContext.Response.Headers["X-Correlation-Id"] = browserSafeError.CorrelationId.ToString();
        return Results.Json(
            BuildStatutoryErrorResponse(
                browserSafeError.ErrorCode,
                browserSafeError.Message,
                browserSafeError.Retryable,
                browserSafeError.CorrelationId),
            statusCode: browserSafeError.StatusCode);
    }

    private static BrowserSafeStatutoryError ToBrowserSafeStatutoryAvailabilityError(
        CentralPmsWebPayError error,
        Guid fallbackCorrelationId)
    {
        var correlationId = error.CorrelationId ?? fallbackCorrelationId;
        if (IsCentralPmsAuthOrTrustFailure(error))
        {
            return new BrowserSafeStatutoryError(
                StatusCodes.Status503ServiceUnavailable,
                "WEBPAY_STATUTORY_SERVICE_UNAVAILABLE",
                "Parking privilege availability is temporarily unavailable. You may continue with the regular parking amount or try again shortly.",
                true,
                correlationId);
        }

        if (IsCentralPmsTransientFailure(error) || ContainsInternalErrorText(error.Message))
        {
            return new BrowserSafeStatutoryError(
                StatusCodes.Status503ServiceUnavailable,
                "WEBPAY_STATUTORY_AVAILABILITY_TEMPORARILY_UNAVAILABLE",
                "Parking privilege availability is temporarily unavailable. You may continue with the regular parking amount or try again shortly.",
                true,
                correlationId);
        }

        return new BrowserSafeStatutoryError(
            error.StatusCode,
            error.ErrorCode,
            error.Message,
            error.Retryable,
            correlationId);
    }

    private static BrowserSafeStatutoryError ToBrowserSafeStatutoryError(
        CentralPmsWebPayError error,
        Guid fallbackCorrelationId)
    {
        var correlationId = error.CorrelationId ?? fallbackCorrelationId;
        if (IsCentralPmsAuthOrTrustFailure(error))
        {
            return new BrowserSafeStatutoryError(
                StatusCodes.Status503ServiceUnavailable,
                "WEBPAY_STATUTORY_SERVICE_UNAVAILABLE",
                "Parking-privilege requests are temporarily unavailable. Please try again later or ask a parking attendant for assistance.",
                true,
                correlationId);
        }

        if (IsCentralPmsTransientFailure(error))
        {
            return new BrowserSafeStatutoryError(
                StatusCodes.Status503ServiceUnavailable,
                "WEBPAY_STATUTORY_REQUEST_TEMPORARILY_UNAVAILABLE",
                "We could not process the parking-privilege request right now. Please try again.",
                true,
                correlationId);
        }

        if (ContainsInternalErrorText(error.Message))
        {
            return new BrowserSafeStatutoryError(
                StatusCodes.Status502BadGateway,
                "WEBPAY_STATUTORY_REQUEST_FAILED",
                "We could not process the parking-privilege request right now. Please try again.",
                true,
                correlationId);
        }

        return new BrowserSafeStatutoryError(
            error.StatusCode,
            error.ErrorCode,
            error.Message,
            error.Retryable,
            correlationId);
    }

    private static bool IsCentralPmsAuthOrTrustFailure(CentralPmsWebPayError error) =>
        error.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden ||
        error.ErrorCode.Contains("AUTHENTICAT", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("AUTHORIZ", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("FORBIDDEN", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("PERMISSION", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("SERVICE_IDENTITY", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("SOURCE_CHANNEL_MISMATCH", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("SOURCE_CHANNEL_AMBIGUOUS", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("AUTH_CONFIGURATION", StringComparison.OrdinalIgnoreCase);

    private static bool IsCentralPmsTransientFailure(CentralPmsWebPayError error) =>
        error.StatusCode is StatusCodes.Status408RequestTimeout or StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable or StatusCodes.Status504GatewayTimeout ||
        error.ErrorCode.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase) ||
        error.ErrorCode.Contains("TIMEOUT", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsInternalErrorText(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var internalTerms = new[]
        {
            "authenticated",
            "authentication",
            "authorization",
            "permission",
            "policy",
            "service identity",
            "x-exitpass",
            "central pms",
            "stack trace",
            "exception",
            "database",
            "connection string",
            "npgsql",
            "http://",
            "https://"
        };

        return internalTerms.Any(term => message.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static WebPayStatutoryDiscountDecisionResponse ToStatutoryDiscountResponse(
        CentralPmsStatutoryDiscountDecision decision) =>
        new()
        {
            StatutoryDiscountDecisionCommandId = decision.StatutoryDiscountDecisionCommandId,
            RequestReference = decision.RequestReference,
            StatutoryDiscountPayableBasisApplicationCommandId = decision.StatutoryDiscountPayableBasisApplicationCommandId,
            StatutoryDiscountValidationId = decision.StatutoryDiscountValidationId,
            ParkingSessionId = decision.ParkingSessionId,
            SiteId = decision.SiteId,
            SiteGroupId = decision.SiteGroupId,
            EntitlementType = decision.EntitlementType,
            DecisionCommandStatus = decision.DecisionCommandStatus,
            DecisionResultStatus = decision.DecisionResultStatus,
            ApplicationCommandStatus = decision.ApplicationCommandStatus,
            ApplicationResultClassification = decision.ApplicationResultClassification,
            PayableBasisReady = decision.PayableBasisReady,
            PayableBasisReadinessStatus = decision.PayableBasisReadinessStatus,
            PayableBasisReadinessAction = decision.PayableBasisReadinessAction,
            OriginalTariffSnapshotId = decision.OriginalTariffSnapshotId,
            AppliedTariffSnapshotId = decision.AppliedTariffSnapshotId,
            OriginalAmountMinorUnits = decision.GrossAmountMinorUnits,
            VatExclusiveBasisAmountMinorUnits = decision.VatExclusiveBasisAmountMinorUnits,
            VatAmountMinorUnits = decision.VatAmountMinorUnits,
            VatTreatment = decision.VatTreatment,
            StatutoryDiscountAmountMinorUnits = decision.StatutoryDiscountAmountMinorUnits,
            FinalPayableAmountMinorUnits = decision.NetPayableAmountMinorUnits,
            Currency = decision.Currency,
            Retryable = decision.Retryable || decision.ApplicationRetryable || decision.DecisionRetryable,
            RecoveryClassification = decision.RecoveryClassification,
            RecoveryAction = decision.RecoveryAction ?? decision.ApplicationRecoveryAction ?? decision.DecisionRecoveryAction,
            SafeErrorCode = decision.SafeErrorCode ?? decision.ErrorCode,
            OverallResultClassification = decision.OverallResultClassification,
            OneShotComplete = decision.OneShotComplete,
            CorrelationId = decision.CorrelationId,
            CreatedAt = decision.CreatedAt,
            DecidedAt = decision.DecidedAt,
            AppliedAt = decision.AppliedAt
        };

    private static WebPayStatutoryDiscountAvailabilityResponse ToStatutoryAvailabilityResponse(
        CentralPmsStatutoryDiscountAvailability availability) =>
        new()
        {
            RequestReference = availability.RequestReference,
            ParkingSessionId = availability.ParkingSessionId,
            SiteId = availability.SiteId,
            SiteGroupId = availability.SiteGroupId,
            AvailabilityStatus = availability.AvailabilityStatus,
            StatutoryParkingBenefitAvailable = availability.StatutoryParkingBenefitAvailable,
            CoveredEntitlementTypes = availability.CoveredEntitlementTypes
                .Where(static entitlement => entitlement is "SENIOR_CITIZEN" or "PWD")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            RequestedEntitlementType = availability.RequestedEntitlementType,
            SafeReasonCode = availability.SafeReasonCode,
            Retryable = availability.Retryable,
            RemediationAction = availability.RemediationAction,
            RequiredEvidenceTypes = availability.RequiredEvidenceTypes.Select(static requirement =>
                    new WebPayStatutoryDiscountAvailabilityEvidenceRequirement
                    {
                        EvidenceType = requirement.EvidenceType,
                        RequirementStatus = requirement.RequirementStatus,
                        SafeRequirementLabel = requirement.SafeRequirementLabel,
                        SafeRequirementNotes = requirement.SafeRequirementNotes
                    })
                .ToArray(),
            CorrelationId = availability.CorrelationId
        };

    private static WebPayStatutoryDiscountPendingLifecycleRediscoveryResponse ToStatutoryPendingLifecycleRediscoveryResponse(
        CentralPmsStatutoryDiscountPendingLifecycleRediscovery rediscovery) =>
        new()
        {
            Classification = rediscovery.Classification,
            StatutoryDecisionId = rediscovery.StatutoryDecisionId,
            StatutoryDecisionCommandId = rediscovery.StatutoryDecisionCommandId,
            RequestReference = rediscovery.RequestReference,
            EntitlementType = rediscovery.EntitlementType,
            DecisionStatus = rediscovery.DecisionStatus,
            PayableBasisStatus = rediscovery.PayableBasisStatus,
            ParkingSessionId = rediscovery.ParkingSessionId,
            SiteId = rediscovery.SiteId,
            SiteGroupId = rediscovery.SiteGroupId,
            OpaqueContinuationReference = rediscovery.OpaqueContinuationReference,
            OpaqueContinuationUrl = rediscovery.OpaqueContinuationUrl,
            LifecycleState = rediscovery.LifecycleState,
            Retryable = rediscovery.Retryable,
            CorrelationId = rediscovery.CorrelationId,
            CreatedAt = rediscovery.CreatedAt,
            UpdatedAt = rediscovery.UpdatedAt,
            SubmittedAt = rediscovery.SubmittedAt,
            DecidedAt = rediscovery.DecidedAt,
            ReviewedAt = rediscovery.ReviewedAt
        };

    private static object BuildStatutoryErrorResponse(
        string errorCode,
        string message,
        bool retryable,
        Guid correlationId) =>
        new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["message"] = message,
            ["retryable"] = retryable,
            ["correlationId"] = correlationId == Guid.Empty ? null : correlationId
        };

    private sealed record BrowserSafeStatutoryError(
        int StatusCode,
        string ErrorCode,
        string Message,
        bool Retryable,
        Guid CorrelationId);

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

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
