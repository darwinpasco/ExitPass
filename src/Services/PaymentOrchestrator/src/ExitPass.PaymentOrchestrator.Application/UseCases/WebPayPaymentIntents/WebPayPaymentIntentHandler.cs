using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Contracts.WebPay;
using ExitPass.PaymentOrchestrator.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.WebPayPaymentIntents;

/// <summary>
/// Orchestrates the WebPay payment intent flow without taking over Central PMS payment finality.
/// </summary>
public sealed class WebPayPaymentIntentHandler
{
    private const string RequestedBy = "webpay-api";
    private const string PendingProviderStatus = "PENDING_PROVIDER";
    private const string ActivePaymentAttemptExists = "ACTIVE_PAYMENT_ATTEMPT_EXISTS";

    private readonly ICentralPmsWebPayClient _centralPmsClient;
    private readonly IPaymentProviderRoutingPolicyResolver _routingPolicyResolver;
    private readonly IProviderProductResolver _providerProductResolver;
    private readonly IProviderPaymentHandoffInitiator _handoffInitiator;
    private readonly IProviderSessionRepository _providerSessionRepository;
    private readonly WebPayReturnUrlOptions _returnUrlOptions;
    private readonly ILogger<WebPayPaymentIntentHandler> _logger;
    private readonly PaymentOrchestratorMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPayPaymentIntentHandler"/> class.
    /// </summary>
    /// <param name="centralPmsClient">Central PMS API client.</param>
    /// <param name="routingPolicyResolver">Database-backed provider routing resolver.</param>
    /// <param name="providerProductResolver">Provider product resolver.</param>
    /// <param name="handoffInitiator">Provider handoff initiator.</param>
    /// <param name="providerSessionRepository">Provider session persistence reader.</param>
    /// <param name="returnUrlOptions">WebPay hosted checkout return URL options.</param>
    /// <param name="logger">Structured logger.</param>
    public WebPayPaymentIntentHandler(
        ICentralPmsWebPayClient centralPmsClient,
        IPaymentProviderRoutingPolicyResolver routingPolicyResolver,
        IProviderProductResolver providerProductResolver,
        IProviderPaymentHandoffInitiator handoffInitiator,
        IProviderSessionRepository providerSessionRepository,
        IOptions<WebPayReturnUrlOptions> returnUrlOptions,
        ILogger<WebPayPaymentIntentHandler> logger,
        PaymentOrchestratorMetrics? metrics = null)
    {
        _centralPmsClient = centralPmsClient ?? throw new ArgumentNullException(nameof(centralPmsClient));
        _routingPolicyResolver = routingPolicyResolver ?? throw new ArgumentNullException(nameof(routingPolicyResolver));
        _providerProductResolver = providerProductResolver ?? throw new ArgumentNullException(nameof(providerProductResolver));
        _handoffInitiator = handoffInitiator ?? throw new ArgumentNullException(nameof(handoffInitiator));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
        _returnUrlOptions = returnUrlOptions?.Value ?? throw new ArgumentNullException(nameof(returnUrlOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? new PaymentOrchestratorMetrics();
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

        var payableBasisError = ValidatePayableBasis(request, parking.Value, correlationId);
        if (payableBasisError is not null)
        {
            return WebPayPaymentIntentResult.Failure(payableBasisError);
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
                false,
                correlationId,
                PaymentMethod: paymentMethod,
                AmountMinorUnits: parking.Value.NetPayableMinorUnits,
                Currency: parking.Value.Currency,
                SiteName: BlankToNull(parking.Value.SiteName),
                TicketReference: BlankToNull(parking.Value.TicketReference),
                PlateNumber: BlankToNull(parking.Value.PlateNumber),
                SelectedProviderCode: route.SelectedProviderCode,
                FallbackProviderCode: route.FallbackProviderCode));
        }

        if (IsUnsupportedWebPayQrphRoute(route.SelectedProviderCode, paymentMethod))
        {
            return WebPayPaymentIntentResult.Failure(new WebPayPaymentIntentError(
                422,
                "WEBPAY_QRPH_PROVIDER_ROUTE_REGRESSION",
                $"WebPay QRPH/PHP must route to PAYMONGO, but routing selected '{route.SelectedProviderCode}'.",
                false,
                correlationId,
                parking.Value.ParkingSessionId,
                PaymentMethod: paymentMethod,
                AmountMinorUnits: parking.Value.NetPayableMinorUnits,
                Currency: parking.Value.Currency,
                SiteName: BlankToNull(parking.Value.SiteName),
                TicketReference: BlankToNull(parking.Value.TicketReference),
                PlateNumber: BlankToNull(parking.Value.PlateNumber),
                SelectedProviderCode: route.SelectedProviderCode,
                FallbackProviderCode: route.FallbackProviderCode));
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
                false,
                correlationId,
                parking.Value.ParkingSessionId,
                PaymentMethod: paymentMethod,
                AmountMinorUnits: parking.Value.NetPayableMinorUnits,
                Currency: parking.Value.Currency,
                SiteName: BlankToNull(parking.Value.SiteName),
                TicketReference: BlankToNull(parking.Value.TicketReference),
                PlateNumber: BlankToNull(parking.Value.PlateNumber),
                SelectedProviderCode: route.SelectedProviderCode,
                FallbackProviderCode: route.FallbackProviderCode));
        }

        var idempotencyKey = BuildIdempotencyKey(parking.Value.ParkingSessionId, paymentMethod, correlationId);
        var attemptResolution = await CreatePaymentAttemptWithOrphanRecoveryAsync(
            parking.Value,
            centralPmsPaymentProviderRail,
            paymentMethod,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (attemptResolution.Error is not null)
        {
            if (IsActivePaymentAttemptConflictCode(attemptResolution.Error.ErrorCode))
            {
                _metrics.ActivePaymentAttemptConflict(paymentMethod, route.SelectedProviderCode);
            }

            return WebPayPaymentIntentResult.Failure(attemptResolution.Error);
        }

        var attempt = attemptResolution.Attempt
            ?? throw new InvalidOperationException("Payment attempt recovery returned no attempt and no error.");

        string providerProduct;
        try
        {
            providerProduct = _providerProductResolver.ResolveProviderProduct(
                route.SelectedProviderCode,
                paymentMethod);
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "Failed to resolve WebPay provider product. PaymentMethod {PaymentMethod}, SelectedProviderCode {SelectedProviderCode}, FallbackProviderCode {FallbackProviderCode}, CorrelationId {CorrelationId}",
                paymentMethod,
                route.SelectedProviderCode,
                route.FallbackProviderCode,
                correlationId);

            return WebPayPaymentIntentResult.Failure(BuildProviderConfigurationError(
                exception,
                parking.Value,
                paymentMethod,
                route.SelectedProviderCode,
                route.FallbackProviderCode,
                null,
                correlationId));
        }

        _logger.LogInformation(
            "WebPay provider handoff route selected. PaymentMethod {PaymentMethod}, SelectedProviderCode {SelectedProviderCode}, FallbackProviderCode {FallbackProviderCode}, CentralPmsPaymentProviderRail {CentralPmsPaymentProviderRail}, ProviderProduct {ProviderProduct}, CorrelationId {CorrelationId}",
            paymentMethod,
            route.SelectedProviderCode,
            route.FallbackProviderCode,
            centralPmsPaymentProviderRail,
            providerProduct,
            correlationId);

        var customerDisplayName = BuildCustomerDisplayName(parking.Value);
        var customerDescription = BuildCustomerDescription(parking.Value);
        var metadata = BuildProviderMetadata(
            attempt.PaymentAttemptId,
            parking.Value,
            paymentMethod,
            correlationId);
        InitiateProviderPaymentResponse handoff;
        try
        {
            var successUrl = BuildReturnUrl(
                _returnUrlOptions.PublicBaseUrl,
                _returnUrlOptions.PaymentSuccessPath,
                parking.Value,
                attempt.PaymentAttemptId,
                correlationId,
                "success");
            var cancelUrl = BuildReturnUrl(
                _returnUrlOptions.PublicBaseUrl,
                _returnUrlOptions.PaymentCancelPath,
                parking.Value,
                attempt.PaymentAttemptId,
                correlationId,
                "cancelled");

            LogReturnUrlDiagnostics(successUrl, cancelUrl, correlationId);

            handoff = await _handoffInitiator.InitiateAsync(
                new InitiateProviderPaymentRequest(
                    attempt.PaymentAttemptId,
                    route.SelectedProviderCode,
                    providerProduct,
                    parking.Value.NetPayableMinorUnits,
                    parking.Value.Currency,
                    customerDescription,
                    idempotencyKey,
                    successUrl,
                    "/webpay/payment/failed",
                    cancelUrl,
                    "/v1/provider/webhooks",
                    metadata,
                    customerDisplayName),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            _metrics.ProviderCheckoutCreationFailed(route.SelectedProviderCode, providerProduct, exception.GetType().Name);

            _logger.LogError(
                exception,
                "Failed to initiate WebPay provider handoff. PaymentMethod {PaymentMethod}, SelectedProviderCode {SelectedProviderCode}, FallbackProviderCode {FallbackProviderCode}, ProviderProduct {ProviderProduct}, PaymentAttemptId {PaymentAttemptId}, CorrelationId {CorrelationId}",
                paymentMethod,
                route.SelectedProviderCode,
                route.FallbackProviderCode,
                providerProduct,
                attempt.PaymentAttemptId,
                correlationId);

            return WebPayPaymentIntentResult.Failure(BuildProviderConfigurationError(
                exception,
                parking.Value,
                paymentMethod,
                route.SelectedProviderCode,
                route.FallbackProviderCode,
                providerProduct,
                correlationId,
                attempt.PaymentAttemptId));
        }

        _metrics.WebPayPaymentIntentCreated(paymentMethod, route.SelectedProviderCode);

        return WebPayPaymentIntentResult.Success(new WebPayPaymentIntentResponse
        {
            PaymentAttemptId = attempt.PaymentAttemptId,
            ParkingSessionId = parking.Value.ParkingSessionId,
            TariffSnapshotId = parking.Value.TariffSnapshotId,
            SiteGroupId = parking.Value.SiteGroupId,
            SiteId = parking.Value.SiteId,
            VendorSystemId = BlankToNull(parking.Value.VendorSystemId),
            SiteGroupName = BlankToNull(parking.Value.SiteGroupName),
            AmountMinorUnits = parking.Value.NetPayableMinorUnits,
            Currency = parking.Value.Currency,
            SiteName = BlankToNull(parking.Value.SiteName),
            TicketReference = BlankToNull(parking.Value.TicketReference),
            PlateNumber = BlankToNull(parking.Value.PlateNumber),
            EntryTime = parking.Value.EntryTime,
            CurrentFeeCalculationTime = parking.Value.CurrentFeeCalculationTime,
            TariffName = BlankToNull(parking.Value.TariffName),
            ParkingStatus = BlankToNull(parking.Value.ParkingStatus),
            PaymentStatus = MapPaymentStatusForDisplay(handoff.SessionStatus),
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

        if (errors.Count > 0)
        {
            return new WebPayPaymentIntentError(
                400,
                "INVALID_REQUEST",
                $"The request is invalid. correlationId={correlationId}; errors={string.Join(" ", errors)}",
                false);
        }

        if (!string.Equals(Normalize(request.PaymentMethod!), PaymentMethodCode.QrPh, StringComparison.Ordinal))
        {
            /*
             * ExitPass v1.2 BRD 18.3 Payment Initiation.
             * ExitPass v1.2 SDD 10.2.4 Initiate Payment Attempt.
             * Invariant: WebPay MVP exposes QRPH/PHP only, and QRPH/PHP must remain PAYMONGO-only.
             */
            return new WebPayPaymentIntentError(
                422,
                "UNSUPPORTED_PAYMENT_METHOD",
                "WebPay MVP supports only QRPH/PHP payment initiation through PayMongo.",
                false,
                correlationId,
                PaymentMethod: Normalize(request.PaymentMethod!));
        }

        return null;
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

    /// <summary>
    /// Confirms WebPay payment initiation still targets the final approved payable basis returned by Central PMS.
    /// </summary>
    /// <param name="request">WebPay payment-intent request.</param>
    /// <param name="parking">Current server-resolved parking payable basis.</param>
    /// <param name="correlationId">End-to-end correlation identifier.</param>
    /// <returns>A deterministic payable-basis error, or <see langword="null"/> when the basis matches.</returns>
    private static WebPayPaymentIntentError? ValidatePayableBasis(
        WebPayPaymentIntentRequest request,
        CentralPmsResolvedParking parking,
        Guid correlationId)
    {
        /*
         * ExitPass v1.2 BRD 9.9 Payment Initiation.
         * ExitPass v1.2 SDD 8.2 TariffSnapshot State Machine.
         * Invariant: PaymentAttempt creation must use the final server-approved payable basis and must fail closed
         * if WebPay submits a stale pre-coupon or pre-statutory tariff snapshot.
         */
        if (request.TariffSnapshotId.HasValue &&
            request.TariffSnapshotId.Value != parking.TariffSnapshotId)
        {
            return new WebPayPaymentIntentError(
                409,
                "PAYABLE_BASIS_LOCKED",
                "The payable basis changed before payment initiation. Restart from parking lookup.",
                false,
                correlationId,
                parking.ParkingSessionId,
                PaymentMethod: Normalize(request.PaymentMethod!),
                AmountMinorUnits: parking.NetPayableMinorUnits,
                Currency: parking.Currency,
                SiteName: BlankToNull(parking.SiteName),
                TicketReference: BlankToNull(parking.TicketReference),
                PlateNumber: BlankToNull(parking.PlateNumber));
        }

        if (request.ExpectedAmountMinorUnits.HasValue &&
            request.ExpectedAmountMinorUnits.Value != parking.NetPayableMinorUnits)
        {
            return new WebPayPaymentIntentError(
                409,
                "PAYABLE_BASIS_LOCKED",
                "The payable amount changed before payment initiation. Restart from parking lookup.",
                false,
                correlationId,
                parking.ParkingSessionId,
                PaymentMethod: Normalize(request.PaymentMethod!),
                AmountMinorUnits: parking.NetPayableMinorUnits,
                Currency: parking.Currency,
                SiteName: BlankToNull(parking.SiteName),
                TicketReference: BlankToNull(parking.TicketReference),
                PlateNumber: BlankToNull(parking.PlateNumber));
        }

        return null;
    }

    private static WebPayParkingSessionResolveResponse BuildResolveResponse(
        CentralPmsResolvedParking parking,
        Guid fallbackCorrelationId)
    {
        return new WebPayParkingSessionResolveResponse
        {
            ParkingSessionId = parking.ParkingSessionId,
            TariffSnapshotId = parking.TariffSnapshotId,
            SiteGroupId = parking.SiteGroupId,
            SiteId = parking.SiteId,
            VendorSystemId = BlankToNull(parking.VendorSystemId),
            SiteGroupName = BlankToNull(parking.SiteGroupName),
            AmountMinorUnits = parking.NetPayableMinorUnits,
            Currency = parking.Currency,
            SiteName = BlankToNull(parking.SiteName),
            TicketReference = BlankToNull(parking.TicketReference),
            PlateNumber = BlankToNull(parking.PlateNumber),
            EntryTime = parking.EntryTime,
            CurrentFeeCalculationTime = parking.CurrentFeeCalculationTime,
            TariffName = BlankToNull(parking.TariffName),
            ParkingStatus = BlankToNull(parking.ParkingStatus),
            PaymentStatus = BlankToNull(parking.PaymentStatus) ?? "Not Started",
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

    private async Task<PaymentAttemptResolution> CreatePaymentAttemptWithOrphanRecoveryAsync(
        CentralPmsResolvedParking parking,
        string centralPmsPaymentProviderRail,
        string paymentMethod,
        string idempotencyKey,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var attempt = await _centralPmsClient.CreateOrReusePaymentAttemptAsync(
            parking.ParkingSessionId,
            parking.TariffSnapshotId,
            centralPmsPaymentProviderRail,
            paymentMethod,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (attempt.Succeeded && attempt.Value is not null)
        {
            return PaymentAttemptResolution.Success(attempt.Value);
        }

        if (!IsActivePaymentAttemptConflict(attempt.Error))
        {
            return PaymentAttemptResolution.Failure(MapCentralPmsError(attempt.Error, correlationId));
        }

        var activeAttemptId = attempt.Error?.PaymentAttemptId;
        var providerSession = await FindProviderSessionForActiveAttemptAsync(
            activeAttemptId,
            parking.ParkingSessionId,
            cancellationToken);

        if (providerSession is not null && !string.IsNullOrWhiteSpace(providerSession.RedirectUrl))
        {
            return PaymentAttemptResolution.Failure(BuildActivePaymentAttemptError(
                attempt.Error,
                parking,
                paymentMethod,
                correlationId,
                providerSession));
        }

        if (activeAttemptId is null || activeAttemptId == Guid.Empty)
        {
            return PaymentAttemptResolution.Failure(BuildActivePaymentAttemptError(
                attempt.Error,
                parking,
                paymentMethod,
                correlationId,
                providerSession));
        }

        /*
         * ExitPass v1.2 BRD 9.13 Timeout, Retry, and Duplicate Handling.
         * ExitPass v1.2 SDD 6.4 Finalize Payment and 9.2 Payments Domain.
         * Invariant: a PaymentAttempt with no persisted hosted checkout URL is not resumable and must not keep
         * the one-active-attempt reservation for the ParkingSession indefinitely.
         */
        var recovery = await _centralPmsClient.FinalizePaymentAttemptAsync(
            activeAttemptId.Value,
            "FAILED",
            RequestedBy,
            BuildRecoveryIdempotencyKey(activeAttemptId.Value, correlationId),
            correlationId,
            cancellationToken);

        if (!recovery.Succeeded)
        {
            return PaymentAttemptResolution.Failure(MapCentralPmsError(recovery.Error, correlationId));
        }

        var retry = await _centralPmsClient.CreateOrReusePaymentAttemptAsync(
            parking.ParkingSessionId,
            parking.TariffSnapshotId,
            centralPmsPaymentProviderRail,
            paymentMethod,
            idempotencyKey,
            correlationId,
            cancellationToken);

        if (retry.Succeeded && retry.Value is not null)
        {
            return PaymentAttemptResolution.Success(retry.Value);
        }

        return PaymentAttemptResolution.Failure(IsActivePaymentAttemptConflict(retry.Error)
            ? BuildActivePaymentAttemptError(retry.Error, parking, paymentMethod, correlationId, providerSession)
            : MapCentralPmsError(retry.Error, correlationId));
    }

    private async Task<ProviderSessionRecord?> FindProviderSessionForActiveAttemptAsync(
        Guid? activeAttemptId,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        if (activeAttemptId is not null && activeAttemptId != Guid.Empty)
        {
            return await _providerSessionRepository.FindLatestByPaymentAttemptIdAsync(
                activeAttemptId.Value,
                cancellationToken);
        }

        return await _providerSessionRepository.FindLatestActiveByParkingSessionIdAsync(
            parkingSessionId,
            cancellationToken);
    }

    private static WebPayPaymentIntentError BuildActivePaymentAttemptError(
        CentralPmsWebPayError? error,
        CentralPmsResolvedParking parking,
        string paymentMethod,
        Guid correlationId,
        ProviderSessionRecord? providerSession)
    {
        return new WebPayPaymentIntentError(
            error?.StatusCode ?? 409,
            error?.ErrorCode ?? ActivePaymentAttemptExists,
            error?.Message ?? "An active payment attempt already exists for this parking session.",
            error?.Retryable ?? false,
            error?.CorrelationId ?? correlationId,
            parking.ParkingSessionId,
            providerSession?.PaymentAttemptId ?? error?.PaymentAttemptId,
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

    private static WebPayPaymentIntentError BuildProviderConfigurationError(
        Exception exception,
        CentralPmsResolvedParking parking,
        string paymentMethod,
        string selectedProviderCode,
        string? fallbackProviderCode,
        string? providerProduct,
        Guid correlationId,
        Guid? paymentAttemptId = null)
    {
        return new WebPayPaymentIntentError(
            502,
            "PAYMENT_PROVIDER_CONFIGURATION_ERROR",
            $"Payment provider handoff could not be created. selectedProviderCode={selectedProviderCode}; providerProduct={providerProduct ?? "unresolved"}; correlationId={correlationId}; detail={exception.Message}",
            true,
            correlationId,
            parking.ParkingSessionId,
            paymentAttemptId,
            PaymentMethod: paymentMethod,
            AmountMinorUnits: parking.NetPayableMinorUnits,
            Currency: parking.Currency,
            SiteName: BlankToNull(parking.SiteName),
            TicketReference: BlankToNull(parking.TicketReference),
            PlateNumber: BlankToNull(parking.PlateNumber),
            SelectedProviderCode: selectedProviderCode,
            FallbackProviderCode: fallbackProviderCode,
            ProviderProduct: providerProduct);
    }

    private static bool IsUnsupportedWebPayQrphRoute(string selectedProviderCode, string paymentMethod)
    {
        return string.Equals(Normalize(paymentMethod), PaymentMethodCode.QrPh, StringComparison.Ordinal) &&
            !string.Equals(Normalize(selectedProviderCode), ProviderCode.PayMongo, StringComparison.Ordinal);
    }

    private static bool IsActivePaymentAttemptConflict(CentralPmsWebPayError? error)
    {
        return error?.StatusCode == 409 &&
            string.Equals(error.ErrorCode, ActivePaymentAttemptExists, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActivePaymentAttemptConflictCode(string? errorCode)
    {
        return string.Equals(errorCode, ActivePaymentAttemptExists, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildIdempotencyKey(Guid parkingSessionId, string paymentMethod, Guid correlationId)
    {
        return $"webpay:{parkingSessionId:N}:{Normalize(paymentMethod)}:{correlationId:N}";
    }

    private static string BuildRecoveryIdempotencyKey(Guid paymentAttemptId, Guid correlationId)
    {
        return $"webpay-recover-orphan:{paymentAttemptId:N}:{correlationId:N}";
    }

    private static string BuildCustomerDisplayName(CentralPmsResolvedParking parking)
    {
        var ticketReference = BlankToNull(parking.TicketReference);
        return ticketReference is null
            ? "ExitPass Parking Fee"
            : $"ExitPass Parking Fee - {ticketReference}";
    }

    private static string BuildCustomerDescription(CentralPmsResolvedParking parking)
    {
        var parts = new List<string>();

        AddDisplayPart(parts, "Site", parking.SiteName);
        AddDisplayPart(parts, "Ticket", parking.TicketReference);
        AddDisplayPart(parts, "Plate", parking.PlateNumber);

        return parts.Count == 0
            ? "ExitPass Parking Fee"
            : string.Join("  ", parts);
    }

    private static Dictionary<string, string> BuildProviderMetadata(
        Guid paymentAttemptId,
        CentralPmsResolvedParking parking,
        string paymentMethod,
        Guid correlationId)
    {
        var metadata = new Dictionary<string, string>
        {
            ["payment_attempt_id"] = paymentAttemptId.ToString(),
            ["parking_session_id"] = parking.ParkingSessionId.ToString(),
            ["tariff_snapshot_id"] = parking.TariffSnapshotId.ToString(),
            ["payment_method"] = paymentMethod,
            ["requested_by"] = RequestedBy,
            ["correlation_id"] = correlationId.ToString()
        };

        AddMetadata(metadata, "site_name", parking.SiteName);
        AddMetadata(metadata, "ticket_reference", parking.TicketReference);
        AddMetadata(metadata, "plate_number", parking.PlateNumber);

        return metadata;
    }

    private static string BuildReturnUrl(
        string publicBaseUrl,
        string configuredPath,
        CentralPmsResolvedParking parking,
        Guid paymentAttemptId,
        Guid correlationId,
        string result)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            throw new InvalidOperationException("WEBPAY_PUBLIC_BASE_URL is required for PayMongo Checkout Session return URLs.");
        }

        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? "/webpay/payment-return"
            : configuredPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var builder = new UriBuilder(new Uri(new Uri(publicBaseUrl.TrimEnd('/') + "/", UriKind.Absolute), path.TrimStart('/')));

        var query = new List<string>
        {
            $"paymentAttemptId={Uri.EscapeDataString(paymentAttemptId.ToString())}",
            $"correlationId={Uri.EscapeDataString(correlationId.ToString())}",
            $"result={Uri.EscapeDataString(result)}"
        };

        var ticketReference = BlankToNull(parking.TicketReference);
        if (ticketReference is not null)
        {
            query.Insert(0, $"ticketReference={Uri.EscapeDataString(ticketReference)}");
        }

        builder.Query = string.Join("&", query);
        return builder.Uri.ToString();
    }

    private void LogReturnUrlDiagnostics(
        string successUrl,
        string cancelUrl,
        Guid correlationId)
    {
        var success = BuildReturnUrlLogParts(successUrl);
        var cancel = BuildReturnUrlLogParts(cancelUrl);

        _logger.LogInformation(
            "WebPay hosted checkout return URLs configured. PublicBaseUrlConfigured {PublicBaseUrlConfigured}, SuccessUrlHost {SuccessUrlHost}, SuccessUrlPath {SuccessUrlPath}, SuccessUrlQuery {SuccessUrlQuery}, CancelUrlHost {CancelUrlHost}, CancelUrlPath {CancelUrlPath}, CancelUrlQuery {CancelUrlQuery}, CorrelationId {CorrelationId}",
            !string.IsNullOrWhiteSpace(_returnUrlOptions.PublicBaseUrl),
            success.Host,
            success.Path,
            success.Query,
            cancel.Host,
            cancel.Path,
            cancel.Query,
            correlationId);
    }

    private static ReturnUrlLogParts BuildReturnUrlLogParts(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return new ReturnUrlLogParts(
                absolute.Host,
                absolute.AbsolutePath,
                absolute.Query);
        }

        if (Uri.TryCreate(url, UriKind.Relative, out var relative))
        {
            var raw = relative.ToString();
            var queryStart = raw.IndexOf('?', StringComparison.Ordinal);
            return queryStart < 0
                ? new ReturnUrlLogParts("<relative>", raw, string.Empty)
                : new ReturnUrlLogParts("<relative>", raw[..queryStart], raw[queryStart..]);
        }

        return new ReturnUrlLogParts("<invalid>", "<invalid>", string.Empty);
    }

    private static void AddDisplayPart(List<string> parts, string label, string? value)
    {
        var normalized = BlankToNull(value);
        if (normalized is not null)
        {
            parts.Add($"{label}: {normalized}");
        }
    }

    private static void AddMetadata(Dictionary<string, string> metadata, string key, string? value)
    {
        var normalized = BlankToNull(value);
        if (normalized is not null)
        {
            metadata[key] = normalized;
        }
    }

    private static string? ResolveCentralPmsPaymentProviderRail(string selectedProviderCode, string paymentMethod)
    {
        var provider = Normalize(selectedProviderCode);
        var method = Normalize(paymentMethod);

        // Central PMS accepts concrete payment provider rail codes. WebPay paymentMethod remains
        // the customer-selected method and must not be sent as the provider code.
        return (provider, method) switch
        {
            (ProviderCode.PayMongo, PaymentMethodCode.QrPh) => "PAYMONGO_CHECKOUT_SESSION",
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

    private static string MapPaymentStatusForDisplay(string? status)
    {
        return Normalize(status ?? PendingProviderStatus) switch
        {
            "REQUESTED" or "PENDING" or "PENDING_PROVIDER" or "ACTIVE" => "Pending Payment",
            "CONFIRMED" or "PAID" or "FINALIZED" or "SUCCEEDED" or "SUCCESS" => "Paid",
            "FAILED" or "CANCELLED" or "DECLINED" => "Failed",
            "EXPIRED" => "Expired",
            _ => PendingProviderStatus
        };
    }

    private sealed record PaymentAttemptResolution(
        CentralPmsPaymentAttempt? Attempt,
        WebPayPaymentIntentError? Error)
    {
        public static PaymentAttemptResolution Success(CentralPmsPaymentAttempt attempt)
        {
            return new PaymentAttemptResolution(attempt, null);
        }

        public static PaymentAttemptResolution Failure(WebPayPaymentIntentError error)
        {
            return new PaymentAttemptResolution(null, error);
        }
    }

    private sealed record ReturnUrlLogParts(string Host, string Path, string Query);
}
