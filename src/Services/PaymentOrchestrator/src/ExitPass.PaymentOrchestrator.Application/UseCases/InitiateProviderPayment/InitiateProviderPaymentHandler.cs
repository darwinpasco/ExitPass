using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.Observability;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;

/// <summary>
/// Creates a provider payment session for an existing canonical PaymentAttempt.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 12 Payment Orchestration
///
/// SDD:
/// - 6.3 Initiate Payment Attempt
/// - 10.5.1 Initiate Provider Payment
///
/// Invariants Enforced:
/// - POA may initiate provider flows but may not finalize PaymentAttempt state.
/// - Provider session creation must remain traceable to a single PaymentAttempt.
/// </summary>
public sealed class InitiateProviderPaymentHandler
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.PaymentOrchestrator.Application");
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> PaymentAttemptHandoffLocks = new();
    private static readonly TimeSpan InitiationOwnershipFreshnessWindow = TimeSpan.FromMinutes(5);

    private readonly ILogger<InitiateProviderPaymentHandler> _logger;
    private readonly IPaymentProviderRegistry _providerRegistry;
    private readonly IProviderSessionRepository _providerSessionRepository;
    private readonly PaymentOrchestratorMetrics _metrics;

    /// <summary>
    /// Initializes a new instance of the <see cref="InitiateProviderPaymentHandler"/> class.
    /// </summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="providerRegistry">The provider adapter registry.</param>
    /// <param name="providerSessionRepository">The provider session repository.</param>
    public InitiateProviderPaymentHandler(
        ILogger<InitiateProviderPaymentHandler> logger,
        IPaymentProviderRegistry providerRegistry,
        IProviderSessionRepository providerSessionRepository,
        PaymentOrchestratorMetrics? metrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
        _metrics = metrics ?? new PaymentOrchestratorMetrics();
    }

    /// <summary>
    /// Handles provider session creation for the specified request.
    /// </summary>
    /// <param name="request">The internal provider payment initiation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The internal response describing the created provider session and handoff.</returns>
    public async Task<InitiateProviderPaymentResponse> HandleAsync(
        InitiateProviderPaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = ActivitySource.StartActivity("InitiateProviderPayment");
        activity?.SetTag("payment_attempt.id", request.PaymentAttemptId);
        activity?.SetTag("provider.code", request.ProviderCode);
        activity?.SetTag("provider.product", request.ProviderProduct);
        activity?.SetTag("payment.amount_minor", request.AmountMinor);
        activity?.SetTag("payment.currency", request.Currency);

        _logger.LogInformation(
            "Initiating provider payment session for PaymentAttemptId {PaymentAttemptId}, ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}",
            request.PaymentAttemptId,
            request.ProviderCode,
            request.ProviderProduct);

        var paymentAttemptLock = PaymentAttemptHandoffLocks.GetOrAdd(
            request.PaymentAttemptId,
            _ => new SemaphoreSlim(1, 1));

        await paymentAttemptLock.WaitAsync(cancellationToken);
        try
        {
            var adapter = _providerRegistry.GetRequired(request.ProviderCode, request.ProviderProduct);

            _logger.LogInformation(
                "Selected provider adapter for handoff. PaymentAttemptId {PaymentAttemptId}, AdapterProviderCode {AdapterProviderCode}, AdapterProviderProduct {AdapterProviderProduct}",
                request.PaymentAttemptId,
                adapter.ProviderCode,
                adapter.ProviderProduct);

            var startedAtUtc = DateTimeOffset.UtcNow;
            var requestJson = JsonSerializer.Serialize(request);
            var reservation = await _providerSessionRepository.TryReserveInitiationAsync(
                new ProviderSessionInitiationReservation(
                    Guid.NewGuid(),
                    request.PaymentAttemptId,
                    request.ProviderProduct,
                    request.IdempotencyKey,
                    TryGetCorrelationId(request.Metadata),
                    requestJson,
                    request.AmountMinor,
                    request.Currency,
                    startedAtUtc),
                cancellationToken);

            if (reservation.Outcome == ProviderSessionInitiationReservationOutcome.Existing)
            {
                return HandleExistingProviderSessionReservation(
                    request,
                    reservation.ProviderSession,
                    startedAtUtc);
            }

            var command = new CreateProviderPaymentSessionCommand(
                request.PaymentAttemptId,
                request.AmountMinor,
                request.Currency,
                request.Description,
                request.IdempotencyKey,
                request.SuccessUrl,
                request.FailureUrl,
                request.CancelUrl,
                request.WebhookUrl,
                request.Metadata,
                request.CustomerDisplayName);

            CreateProviderPaymentSessionResult result;
            try
            {
                result = await adapter.CreatePaymentSessionAsync(command, cancellationToken);
            }
            catch (Exception exception)
            {
                _metrics.ProviderCheckoutCreationFailed(
                    request.ProviderCode,
                    request.ProviderProduct,
                    exception.GetType().Name);
                throw;
            }

            var record = new ProviderSessionRecord(
                reservation.ProviderSession.ProviderSessionRecordId,
                request.PaymentAttemptId,
                request.ProviderCode,
                request.ProviderProduct,
                result.ProviderSessionId,
                result.ProviderReference,
                result.SessionStatus,
                result.Handoff.RedirectUrl,
                result.Handoff.QrPayload,
                result.ExpiresAtUtc,
                request.IdempotencyKey,
                TryGetCorrelationId(request.Metadata),
                requestJson,
                result.RawResponseJson,
                startedAtUtc);

            await _providerSessionRepository.CompleteInitiationAsync(
                reservation.ProviderSession.ProviderSessionRecordId,
                record,
                cancellationToken);

            _metrics.ProviderCheckoutSessionCreated(request.ProviderCode, request.ProviderProduct);

            _logger.LogInformation(
                "Provider payment session created for PaymentAttemptId {PaymentAttemptId}, ProviderSessionId {ProviderSessionId}, SessionStatus {SessionStatus}",
                request.PaymentAttemptId,
                result.ProviderSessionId,
                result.SessionStatus);

            activity?.SetTag("provider_session.id", result.ProviderSessionId);
            activity?.SetTag("provider_session.status", result.SessionStatus);

            return new InitiateProviderPaymentResponse(
                request.PaymentAttemptId,
                request.ProviderCode,
                request.ProviderProduct,
                result.ProviderSessionId,
                result.ProviderReference,
                result.SessionStatus,
                result.Handoff,
                result.ExpiresAtUtc);
        }
        finally
        {
            paymentAttemptLock.Release();
        }
    }

    private static Guid? TryGetCorrelationId(IReadOnlyDictionary<string, string> metadata)
    {
        return metadata.TryGetValue("correlation_id", out var value) &&
            Guid.TryParse(value, out var correlationId)
            ? correlationId
            : null;
    }

    private InitiateProviderPaymentResponse HandleExistingProviderSessionReservation(
        InitiateProviderPaymentRequest request,
        ProviderSessionRecord existing,
        DateTimeOffset observedAtUtc)
    {
        if (IsReusableProviderSession(existing))
        {
            _logger.LogInformation(
                "Reusing existing provider payment session for PaymentAttemptId {PaymentAttemptId}, ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, SessionStatus {SessionStatus}",
                request.PaymentAttemptId,
                existing.ProviderCode,
                existing.ProviderProduct,
                existing.SessionStatus);

            return BuildResponseFromRecord(existing, request);
        }

        if (IsIncompleteInitiationReservation(existing))
        {
            var age = observedAtUtc - existing.CreatedAtUtc;
            var isFresh = age <= InitiationOwnershipFreshnessWindow;
            throw new ProviderSessionInitiationPendingException(
                isFresh
                    ? "PAYMENT_PROVIDER_HANDOFF_IN_PROGRESS"
                    : "PAYMENT_PROVIDER_HANDOFF_RECONCILIATION_REQUIRED",
                isFresh
                    ? "A provider handoff is already being prepared for this payment attempt. Please retry status shortly."
                    : "A previous provider handoff initiation is incomplete and must be reconciled before another provider handoff is started.",
                retryable: true,
                existing.PaymentAttemptId,
                existing.SessionStatus);
        }

        throw new ProviderSessionInitiationPendingException(
            "PAYMENT_PROVIDER_HANDOFF_NOT_REUSABLE",
            "The existing provider handoff for this payment attempt cannot be resumed safely. Please refresh payment status before starting another provider handoff.",
            retryable: true,
            existing.PaymentAttemptId,
            existing.SessionStatus);
    }

    private static bool IsReusableProviderSession(ProviderSessionRecord providerSession)
    {
        if (string.IsNullOrWhiteSpace(providerSession.RedirectUrl) &&
            string.IsNullOrWhiteSpace(providerSession.QrPayload))
        {
            return false;
        }

        return providerSession.SessionStatus.ToUpperInvariant() is
            "CREATED" or
            "ACTIVE" or
            "HANDOFF_READY" or
            "PENDING" or
            "PENDING_PROVIDER" or
            "PAID" or
            "SUCCEEDED" or
            "SUCCESS" or
            "CONFIRMED";
    }

    private static bool IsIncompleteInitiationReservation(ProviderSessionRecord providerSession)
    {
        return string.IsNullOrWhiteSpace(providerSession.ProviderSessionId) &&
            string.IsNullOrWhiteSpace(providerSession.RedirectUrl) &&
            string.IsNullOrWhiteSpace(providerSession.QrPayload) &&
            Normalize(providerSession.SessionStatus) is "CREATED" or "PENDING" or "ACTIVE";
    }

    private static InitiateProviderPaymentResponse BuildResponseFromRecord(
        ProviderSessionRecord providerSession,
        InitiateProviderPaymentRequest request)
    {
        var handoffType = !string.IsNullOrWhiteSpace(providerSession.RedirectUrl)
            ? ProviderHandoffType.Redirect
            : ProviderHandoffType.QrDisplay;

        return new InitiateProviderPaymentResponse(
            request.PaymentAttemptId,
            string.IsNullOrWhiteSpace(providerSession.ProviderCode)
                ? request.ProviderCode
                : providerSession.ProviderCode,
            string.IsNullOrWhiteSpace(providerSession.ProviderProduct)
                ? request.ProviderProduct
                : providerSession.ProviderProduct,
            providerSession.ProviderSessionId,
            providerSession.ProviderReference,
            providerSession.SessionStatus,
            new ProviderHandoffDto(
                handoffType,
                providerSession.RedirectUrl,
                null,
                null,
                providerSession.QrPayload,
                null,
                providerSession.ExpiresAtUtc),
            providerSession.ExpiresAtUtc);
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
}

/// <summary>
/// Indicates that durable provider-session initiation ownership exists but cannot be converted into a handoff response yet.
/// </summary>
public sealed class ProviderSessionInitiationPendingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderSessionInitiationPendingException"/> class.
    /// </summary>
    /// <param name="errorCode">Safe error code.</param>
    /// <param name="message">Safe error message.</param>
    /// <param name="retryable">Whether the caller may retry status.</param>
    /// <param name="paymentAttemptId">The canonical payment attempt identifier.</param>
    /// <param name="status">The durable provider-session status.</param>
    public ProviderSessionInitiationPendingException(
        string errorCode,
        string message,
        bool retryable,
        Guid paymentAttemptId,
        string? status)
        : base(message)
    {
        ErrorCode = errorCode;
        Retryable = retryable;
        PaymentAttemptId = paymentAttemptId;
        Status = status;
    }

    /// <summary>
    /// Gets the safe error code.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Gets a value indicating whether the caller can retry safely.
    /// </summary>
    public bool Retryable { get; }

    /// <summary>
    /// Gets the canonical payment attempt identifier.
    /// </summary>
    public Guid PaymentAttemptId { get; }

    /// <summary>
    /// Gets the durable provider-session status.
    /// </summary>
    public string? Status { get; }
}
