using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Orchestrates the WebPay payment intent flow without taking over Central PMS payment finality.
/// </summary>
public sealed class WebPayPaymentIntentHandler
{
    private const string RequestedBy = "webpay-api";
    private const string PendingProviderStatus = "PENDING_PROVIDER";

    private readonly ICentralPmsWebPayClient _centralPmsClient;
    private readonly IPaymentProviderRoutingPolicyResolver _routingPolicyResolver;
    private readonly IProviderProductResolver _providerProductResolver;
    private readonly IProviderPaymentHandoffInitiator _handoffInitiator;
    private readonly IProviderSessionRepository _providerSessionRepository;
    private readonly ILogger<WebPayPaymentIntentHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPayPaymentIntentHandler"/> class.
    /// </summary>
    /// <param name="centralPmsClient">Central PMS API client.</param>
    /// <param name="routingPolicyResolver">Database-backed provider routing resolver.</param>
    /// <param name="providerProductResolver">Provider product resolver.</param>
    /// <param name="handoffInitiator">Provider handoff initiator.</param>
    /// <param name="logger">Structured logger.</param>
    public WebPayPaymentIntentHandler(
        ICentralPmsWebPayClient centralPmsClient,
        IPaymentProviderRoutingPolicyResolver routingPolicyResolver,
        IProviderProductResolver providerProductResolver,
        IProviderPaymentHandoffInitiator handoffInitiator,
        IProviderSessionRepository providerSessionRepository,
        ILogger<WebPayPaymentIntentHandler> logger)
    {
        _centralPmsClient = centralPmsClient ?? throw new ArgumentNullException(nameof(centralPmsClient));
        _routingPolicyResolver = routingPolicyResolver ?? throw new ArgumentNullException(nameof(routingPolicyResolver));
        _providerProductResolver = providerProductResolver ?? throw new ArgumentNullException(nameof(providerProductResolver));
        _handoffInitiator = handoffInitiator ?? throw new ArgumentNullException(nameof(handoffInitiator));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handles a WebPay payment intent request by composing Central PMS resolution, provider routing,
    /// Central PMS attempt creation, and provider handoff creation.
    /// </summary>
    /// <param name="request">WebPay request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Successful provider-neutral handoff response or deterministic error.</returns>
    public async Task<WebPayPaymentIntentResult> HandleAsync(
        WebPayPaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = request.CorrelationId.GetValueOrDefault(Guid.NewGuid());
        var validationError = Validate(request, correlationId);
        if (validationError is not null)
        {
            return WebPayPaymentIntentResult.Failure(validationError);
        }

        var paymentMethod = Normalize(request.PaymentMethod!);

        _logger.LogInformation(
            "Resolving WebPay parking for payment method {PaymentMethod}, correlation {CorrelationId}.",
            paymentMethod,
            correlationId);

        var parking = await _centralPmsClient.ResolveVendorParkingAsync(
            request.SiteGroupId,
            request.SiteId,
            request.VendorSystemId!,
            request.PlateNumber,
            request.TicketReference,
            correlationId,
            cancellationToken);

        if (!parking.Succeeded || parking.Value is null)
        {
            return WebPayPaymentIntentResult.Failure(MapCentralPmsError(parking.Error, correlationId));
        }

        var route = await _routingPolicyResolver.ResolveAsync(
            new ResolvePaymentProviderRouteRequest(
                request.SiteId,
                request.SiteGroupId,
                paymentMethod,
                parking.Value.NetPayableMinorUnits,
                parking.Value.Currency,
                request.PreferredProviderCode,
                correlationId),
            cancellationToken);

        if (!route.IsRouted || string.IsNullOrWhiteSpace(route.SelectedProviderCode))
        {
            return WebPayPaymentIntentResult.Failure(new WebPayPaymentIntentError(
                422,
                route.ErrorCode ?? "PAYMENT_PROVIDER_ROUTE_NOT_AVAILABLE",
                "No enabled payment provider route is available for the requested payment method.",
            false));
        }

        var centralPmsPaymentProviderRail = ResolveCentralPmsPaymentProviderRail(
            route.SelectedProviderCode,
            paymentMethod);
        if (centralPmsPaymentProviderRail is null)
        {
            return WebPayPaymentIntentResult.Failure(new WebPayPaymentIntentError(
                422,
                "PAYMENT_PROVIDER_MAPPING_NOT_SUPPORTED",
                $"No Central PMS payment provider rail mapping is configured for provider '{route.SelectedProviderCode}' and payment method '{paymentMethod}'.",
                false));
        }

        var idempotencyKey = BuildIdempotencyKey(parking.Value.ParkingSessionId, paymentMethod, correlationId);
        var attempt = await _centralPmsClient.CreateOrReusePaymentAttemptAsync(
            parking.Value.ParkingSessionId,
            parking.Value.TariffSnapshotId,
            centralPmsPaymentProviderRail,
            paymentMethod,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (!attempt.Succeeded || attempt.Value is null)
        {
            if (IsActivePaymentAttemptConflict(attempt.Error))
            {
                var activeError = await MapActivePaymentAttemptErrorAsync(
                    attempt.Error,
                    parking.Value,
                    paymentMethod,
                    correlationId,
                    cancellationToken);

                return WebPayPaymentIntentResult.Failure(activeError);
            }

            return WebPayPaymentIntentResult.Failure(MapCentralPmsError(attempt.Error, correlationId));
        }

        var providerProduct = _providerProductResolver.ResolveProviderProduct(
            route.SelectedProviderCode,
            paymentMethod);

        _logger.LogInformation(
            "WebPay provider handoff route selected. PaymentMethod {PaymentMethod}, SelectedProviderCode {SelectedProviderCode}, FallbackProviderCode {FallbackProviderCode}, CentralPmsPaymentProviderRail {CentralPmsPaymentProviderRail}, ProviderProduct {ProviderProduct}, CorrelationId {CorrelationId}",
            paymentMethod,
            route.SelectedProviderCode,
            route.FallbackProviderCode,
            centralPmsPaymentProviderRail,
            providerProduct,
            correlationId);

        var handoff = await _handoffInitiator.InitiateAsync(
            new InitiateProviderPaymentRequest(
                attempt.Value.PaymentAttemptId,
                route.SelectedProviderCode,
                providerProduct,
                parking.Value.NetPayableMinorUnits,
                parking.Value.Currency,
                $"ExitPass parking payment {parking.Value.ParkingSessionId}",
                idempotencyKey,
                "/webpay/payment/success",
                "/webpay/payment/failed",
                "/webpay/payment/cancelled",
                "/v1/provider/webhooks",
                new Dictionary<string, string>
                {
                    ["payment_attempt_id"] = attempt.Value.PaymentAttemptId.ToString(),
                    ["parking_session_id"] = parking.Value.ParkingSessionId.ToString(),
                    ["tariff_snapshot_id"] = parking.Value.TariffSnapshotId.ToString(),
                    ["payment_method"] = paymentMethod,
                    ["requested_by"] = RequestedBy,
                    ["correlation_id"] = correlationId.ToString()
                }),
            cancellationToken);

        return WebPayPaymentIntentResult.Success(new WebPayPaymentIntentResponse
        {
            PaymentAttemptId = attempt.Value.PaymentAttemptId,
            ParkingSessionId = parking.Value.ParkingSessionId,
            TariffSnapshotId = parking.Value.TariffSnapshotId,
            AmountMinorUnits = parking.Value.NetPayableMinorUnits,
            Currency = parking.Value.Currency,
            SiteName = BlankToNull(parking.Value.SiteName),
            TicketReference = BlankToNull(parking.Value.TicketReference),
            PlateNumber = BlankToNull(parking.Value.PlateNumber),
            EntryTime = parking.Value.EntryTime,
            CurrentFeeCalculationTime = parking.Value.CurrentFeeCalculationTime,
            TariffName = BlankToNull(parking.Value.TariffName),
            FeeValidUntil = parking.Value.FeeValidUntil,
            PaymentMethod = route.PaymentMethod,
            SelectedProviderCode = route.SelectedProviderCode,
            FallbackProviderCode = route.FallbackProviderCode,
            RoutingReason = route.RoutingReason,
            Status = string.IsNullOrWhiteSpace(handoff.SessionStatus) ? PendingProviderStatus : handoff.SessionStatus,
            Handoff = new WebPayPaymentHandoffDto
            {
                Type = handoff.ProviderHandoff.Type.ToString(),
                HandoffUrl = handoff.ProviderHandoff.RedirectUrl,
                QrCodeUrl = handoff.ProviderHandoff.QrPayload ?? handoff.ProviderHandoff.QrImageBase64,
                ExpiresAt = handoff.ProviderHandoff.ExpiresAtUtc ?? handoff.ExpiresAtUtc
            },
            CorrelationId = correlationId
        });
    }

    /// <summary>
    /// Resolves the parking session and payable amount without creating a PaymentAttempt or provider session.
    /// </summary>
    /// <param name="request">WebPay pre-payment resolve request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parking session summary or deterministic error.</returns>
    public async Task<WebPayParkingSessionResolveResult> ResolveAsync(
        WebPayParkingSessionResolveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var correlationId = request.CorrelationId.GetValueOrDefault(Guid.NewGuid());
        var validationError = ValidateResolve(request, correlationId);
        if (validationError is not null)
        {
            return WebPayParkingSessionResolveResult.Failure(validationError);
        }

        var parking = await _centralPmsClient.ResolveVendorParkingAsync(
            request.SiteGroupId,
            request.SiteId,
            request.VendorSystemId!,
            request.PlateNumber,
            request.TicketReference,
            correlationId,
            cancellationToken);

        if (!parking.Succeeded || parking.Value is null)
        {
            return WebPayParkingSessionResolveResult.Failure(MapCentralPmsError(parking.Error, correlationId));
        }

        return WebPayParkingSessionResolveResult.Success(BuildResolveResponse(parking.Value, correlationId));
    }

    private static WebPayPaymentIntentError? Validate(
        WebPayPaymentIntentRequest request,
        Guid correlationId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.PlateNumber) &&
            string.IsNullOrWhiteSpace(request.TicketReference))
        {
            errors.Add("Either plateNumber or ticketReference is required.");
        }

        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            errors.Add("paymentMethod is required.");
        }

        if (string.IsNullOrWhiteSpace(request.VendorSystemId))
        {
            errors.Add("vendorSystemId is required.");
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return new WebPayPaymentIntentError(
            400,
            "INVALID_REQUEST",
            $"The request is invalid. correlationId={correlationId}; errors={string.Join(" ", errors)}",
            false);
    }

    private static WebPayPaymentIntentError? ValidateResolve(
        WebPayParkingSessionResolveRequest request,
        Guid correlationId)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.PlateNumber) &&
            string.IsNullOrWhiteSpace(request.TicketReference))
        {
            errors.Add("Either plateNumber or ticketReference is required.");
        }

        if (string.IsNullOrWhiteSpace(request.VendorSystemId))
        {
            errors.Add("vendorSystemId is required.");
        }

        if (errors.Count == 0)
        {
            return null;
        }

        return new WebPayPaymentIntentError(
            400,
            "INVALID_REQUEST",
            $"The request is invalid. correlationId={correlationId}; errors={string.Join(" ", errors)}",
            false);
    }

    private static WebPayParkingSessionResolveResponse BuildResolveResponse(
        CentralPmsResolvedParking parking,
        Guid fallbackCorrelationId)
    {
        return new WebPayParkingSessionResolveResponse
        {
            ParkingSessionId = parking.ParkingSessionId,
            TariffSnapshotId = parking.TariffSnapshotId,
            AmountMinorUnits = parking.NetPayableMinorUnits,
            Currency = parking.Currency,
            SiteName = BlankToNull(parking.SiteName),
            TicketReference = BlankToNull(parking.TicketReference),
            PlateNumber = BlankToNull(parking.PlateNumber),
            EntryTime = parking.EntryTime,
            CurrentFeeCalculationTime = parking.CurrentFeeCalculationTime,
            TariffName = BlankToNull(parking.TariffName),
            ParkingStatus = BlankToNull(parking.ParkingStatus),
            FeeValidUntil = parking.FeeValidUntil,
            CorrelationId = parking.CorrelationId == Guid.Empty ? fallbackCorrelationId : parking.CorrelationId
        };
    }

    private static WebPayPaymentIntentError MapCentralPmsError(
        CentralPmsWebPayError? error,
        Guid correlationId)
    {
        if (error is null)
        {
            return new WebPayPaymentIntentError(
                502,
                "CENTRAL_PMS_ERROR",
                $"Central PMS returned an invalid response. correlationId={correlationId}",
                true);
        }

        return new WebPayPaymentIntentError(
            error.StatusCode,
            error.ErrorCode,
            error.Message,
            error.Retryable,
            error.CorrelationId ?? correlationId);
    }

    private async Task<WebPayPaymentIntentError> MapActivePaymentAttemptErrorAsync(
        CentralPmsWebPayError? error,
        CentralPmsResolvedParking parking,
        string paymentMethod,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var providerSession = await _providerSessionRepository.FindLatestActiveByParkingSessionIdAsync(
            parking.ParkingSessionId,
            cancellationToken);

        return new WebPayPaymentIntentError(
            error?.StatusCode ?? 409,
            error?.ErrorCode ?? "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
            error?.Message ?? "An active payment attempt already exists for this parking session.",
            error?.Retryable ?? false,
            error?.CorrelationId ?? correlationId,
            parking.ParkingSessionId,
            providerSession?.PaymentAttemptId,
            providerSession?.SessionStatus,
            providerSession is null || string.IsNullOrWhiteSpace(providerSession.RedirectUrl)
                ? null
                : new WebPayPaymentHandoffDto
                {
                    Type = "Redirect",
                    HandoffUrl = providerSession.RedirectUrl,
                    ExpiresAt = providerSession.ExpiresAtUtc
                },
            paymentMethod,
            parking.NetPayableMinorUnits,
            parking.Currency,
            BlankToNull(parking.SiteName),
            BlankToNull(parking.TicketReference),
            BlankToNull(parking.PlateNumber));
    }

    private static bool IsActivePaymentAttemptConflict(CentralPmsWebPayError? error)
    {
        return error?.StatusCode == 409 &&
            string.Equals(error.ErrorCode, "ACTIVE_PAYMENT_ATTEMPT_EXISTS", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIdempotencyKey(Guid parkingSessionId, string paymentMethod, Guid correlationId)
    {
        return $"webpay:{parkingSessionId:N}:{Normalize(paymentMethod)}:{correlationId:N}";
    }

    private static string? ResolveCentralPmsPaymentProviderRail(string selectedProviderCode, string paymentMethod)
    {
        var provider = Normalize(selectedProviderCode);
        var method = Normalize(paymentMethod);

        // Central PMS accepts concrete payment provider rail codes. WebPay paymentMethod remains
        // the customer-selected method and must not be sent as the provider code.
        return (provider, method) switch
        {
            (ProviderCode.Aub, PaymentMethodCode.QrPh) => "AUB_QRPH",
            (ProviderCode.Aub, PaymentMethodCode.Card) => "AUB_CARD_CASHIER",
            (ProviderCode.PayMongo, PaymentMethodCode.QrPh) => "PAYMONGO_CHECKOUT_SESSION",
            (ProviderCode.PayMongo, PaymentMethodCode.GCash) => "PAYMONGO_CHECKOUT_SESSION",
            (ProviderCode.PayMongo, PaymentMethodCode.Maya) => "PAYMONGO_CHECKOUT_SESSION",
            (ProviderCode.PayMongo, PaymentMethodCode.Card) => "PAYMONGO_CHECKOUT_SESSION",
            _ => null
        };
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
