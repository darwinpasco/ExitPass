using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;

/// <summary>
/// Verifies a provider webhook, persists immutable evidence, deduplicates provider events,
/// canonicalizes the provider result, and reports the verified outcome to Central PMS.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Only verified provider outcomes may enter the platform.
/// - Duplicate provider callbacks must not create duplicate control transitions.
/// - Only Central PMS may finalize PaymentAttempt state.
/// - Unknown provider sessions must be rejected deterministically and must not surface as unhandled 500 errors.
/// - Only authoritative provider events for the configured rail may mutate platform state.
/// </summary>
public sealed class VerifyProviderWebhookHandler
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.PaymentOrchestrator.Application");

    private const string PayMongoProviderCode = "PAYMONGO";
    private const string PayMongoCheckoutSessionProduct = "PAYMONGO_CHECKOUT_SESSION";
    private const string AuthoritativeCheckoutSuccessEvent = "checkout_session.payment.paid";
    private const string NonAuthoritativePaymentPaidEvent = "payment.paid";

    private readonly ILogger<VerifyProviderWebhookHandler> _logger;
    private readonly IPaymentProviderAdapter _adapter;
    private readonly IProviderWebhookEventRepository _providerWebhookEventRepository;
    private readonly ICentralPmsPaymentOutcomeReporter _centralPmsPaymentOutcomeReporter;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyProviderWebhookHandler"/> class.
    /// </summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="adapter">The provider adapter handling webhook verification.</param>
    /// <param name="providerWebhookEventRepository">The webhook event repository.</param>
    /// <param name="centralPmsPaymentOutcomeReporter">The Central PMS outcome reporter.</param>
    public VerifyProviderWebhookHandler(
        ILogger<VerifyProviderWebhookHandler> logger,
        IPaymentProviderAdapter adapter,
        IProviderWebhookEventRepository providerWebhookEventRepository,
        ICentralPmsPaymentOutcomeReporter centralPmsPaymentOutcomeReporter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _providerWebhookEventRepository = providerWebhookEventRepository ?? throw new ArgumentNullException(nameof(providerWebhookEventRepository));
        _centralPmsPaymentOutcomeReporter = centralPmsPaymentOutcomeReporter ?? throw new ArgumentNullException(nameof(centralPmsPaymentOutcomeReporter));
    }

    /// <summary>
    /// Handles verification, persistence, deduplication, and reporting for an inbound provider webhook.
    /// </summary>
    /// <param name="request">The raw provider webhook request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result of webhook handling.</returns>
    public async Task<VerifyProviderWebhookResult> HandleAsync(
        ProviderWebhookRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = ActivitySource.StartActivity("VerifyProviderWebhook");
        activity?.SetTag("provider.code", _adapter.ProviderCode);
        activity?.SetTag("provider.product", _adapter.ProviderProduct);

        var verification = await _adapter.VerifyWebhookAsync(request, cancellationToken);

        activity?.SetTag("provider_event.id", verification.EventId);
        activity?.SetTag("provider_event.type", verification.EventType);
        activity?.SetTag("payment_attempt.id", verification.PaymentAttemptId);
        activity?.SetTag("provider_session.id", verification.ProviderSessionId);
        activity?.SetTag("provider_reference", verification.ProviderReference);

        if (!verification.IsAuthentic)
        {
            _logger.LogWarning(
                "Rejected provider webhook because authenticity verification failed. ProviderCode {ProviderCode}, EventId {EventId}",
                _adapter.ProviderCode,
                verification.EventId);

            activity?.SetTag("webhook.accepted", false);
            activity?.SetTag("webhook.duplicate", false);
            activity?.SetTag("webhook.ignored", false);
            activity?.SetTag("webhook.rejection_code", "WEBHOOK_NOT_AUTHENTIC");

            return VerifyProviderWebhookResult.CreateRejected("WEBHOOK_NOT_AUTHENTIC");
        }

        if (ShouldIgnoreAsNonAuthoritativeEvent(verification.EventType))
        {
            _logger.LogInformation(
                "Ignored non-authoritative provider webhook event for rail. ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, EventId {EventId}, EventType {EventType}",
                _adapter.ProviderCode,
                _adapter.ProviderProduct,
                verification.EventId,
                verification.EventType);

            activity?.SetTag("webhook.accepted", true);
            activity?.SetTag("webhook.duplicate", false);
            activity?.SetTag("webhook.ignored", true);
            activity?.SetTag("webhook.ignore_reason", "NON_AUTHORITATIVE_EVENT_FOR_RAIL");

            return VerifyProviderWebhookResult.CreateIgnored(verification.EventId);
        }

        var isDuplicate = await _providerWebhookEventRepository.ExistsByProviderEventIdAsync(
            _adapter.ProviderCode,
            verification.EventId,
            cancellationToken);

        if (isDuplicate)
        {
            _logger.LogInformation(
                "Accepted duplicate provider webhook before persistence. ProviderCode {ProviderCode}, EventId {EventId}",
                _adapter.ProviderCode,
                verification.EventId);

            activity?.SetTag("webhook.accepted", true);
            activity?.SetTag("webhook.duplicate", true);
            activity?.SetTag("webhook.ignored", false);

            return VerifyProviderWebhookResult.CreateAcceptedDuplicate(verification.EventId);
        }

        var eventRecord = new ProviderWebhookEventRecord(
            Guid.NewGuid(),
            _adapter.ProviderCode,
            verification.EventId,
            verification.EventType,
            verification.ProviderReference,
            verification.ProviderSessionId,
            verification.PaymentAttemptId,
            JsonSerializer.Serialize(request.Headers),
            request.RawBody,
            true,
            false,
            DateTimeOffset.UtcNow);

        try
        {
            await _providerWebhookEventRepository.AddAsync(eventRecord, cancellationToken);
        }
        catch (DuplicateProviderWebhookEventException)
        {
            _logger.LogInformation(
                "Accepted duplicate provider webhook after unique-constraint detection. ProviderCode {ProviderCode}, EventId {EventId}",
                _adapter.ProviderCode,
                verification.EventId);

            activity?.SetTag("webhook.accepted", true);
            activity?.SetTag("webhook.duplicate", true);
            activity?.SetTag("webhook.ignored", false);

            return VerifyProviderWebhookResult.CreateAcceptedDuplicate(verification.EventId);
        }
        catch (UnknownProviderSessionException exception)
        {
            _logger.LogWarning(
                exception,
                "Rejected provider webhook because the provider session is unknown. ProviderCode {ProviderCode}, EventId {EventId}, ProviderSessionId {ProviderSessionId}",
                _adapter.ProviderCode,
                verification.EventId,
                verification.ProviderSessionId);

            activity?.SetTag("webhook.accepted", false);
            activity?.SetTag("webhook.duplicate", false);
            activity?.SetTag("webhook.ignored", false);
            activity?.SetTag("webhook.rejection_code", "WEBHOOK_UNKNOWN_PROVIDER_SESSION");

            return VerifyProviderWebhookResult.CreateRejected("WEBHOOK_UNKNOWN_PROVIDER_SESSION");
        }

        var report = new VerifiedPaymentOutcomeReport(
            verification.PaymentAttemptId,
            _adapter.ProviderCode,
            verification.ProviderReference,
            verification.ProviderSessionId,
            verification.CanonicalStatus.ToString().ToUpperInvariant(),
            verification.OccurredAtUtc,
            verification.AmountMinor,
            verification.Currency,
            verification.EventId,
            verification.IsTerminal,
            verification.IsSuccess,
            verification.RawAttributes);

        await _centralPmsPaymentOutcomeReporter.ReportVerifiedOutcomeAsync(report, cancellationToken);

        _logger.LogInformation(
            "Reported verified provider outcome to Central PMS. ProviderCode {ProviderCode}, EventId {EventId}, PaymentAttemptId {PaymentAttemptId}, CanonicalStatus {CanonicalStatus}",
            _adapter.ProviderCode,
            verification.EventId,
            verification.PaymentAttemptId,
            verification.CanonicalStatus);

        activity?.SetTag("webhook.accepted", true);
        activity?.SetTag("webhook.duplicate", false);
        activity?.SetTag("webhook.ignored", false);
        activity?.SetTag("payment.canonical_status", verification.CanonicalStatus.ToString());

        return VerifyProviderWebhookResult.CreateAccepted(verification.EventId);
    }

    /// <summary>
    /// Determines whether a verified webhook event should be ignored because it is non-authoritative
    /// for the configured rail.
    /// </summary>
    /// <param name="eventType">The provider event type.</param>
    /// <returns><c>true</c> when the event is valid but non-authoritative for this rail; otherwise <c>false</c>.</returns>
    private bool ShouldIgnoreAsNonAuthoritativeEvent(string eventType)
    {
        if (!string.Equals(_adapter.ProviderCode, PayMongoProviderCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(_adapter.ProviderProduct, PayMongoCheckoutSessionProduct, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(eventType, NonAuthoritativePaymentPaidEvent, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Represents the result of handling an inbound provider webhook.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Webhook handling outcomes must be explicit and deterministic.
/// </summary>
/// <param name="Accepted">Indicates whether the webhook was accepted by POA.</param>
/// <param name="Duplicate">Indicates whether the webhook was identified as a duplicate event.</param>
/// <param name="Ignored">Indicates whether the webhook was intentionally ignored as non-authoritative.</param>
/// <param name="Code">The event identifier or rejection code.</param>
public sealed record VerifyProviderWebhookResult(
    bool Accepted,
    bool Duplicate,
    bool Ignored,
    string Code)
{
    /// <summary>
    /// Creates a rejected webhook result.
    /// </summary>
    /// <param name="code">The rejection code.</param>
    /// <returns>A rejected webhook result.</returns>
    public static VerifyProviderWebhookResult CreateRejected(string code) => new(false, false, false, code);

    /// <summary>
    /// Creates an accepted webhook result for a first-seen authoritative event.
    /// </summary>
    /// <param name="eventId">The provider event identifier.</param>
    /// <returns>An accepted webhook result.</returns>
    public static VerifyProviderWebhookResult CreateAccepted(string eventId) => new(true, false, false, eventId);

    /// <summary>
    /// Creates an accepted webhook result for a duplicate event.
    /// </summary>
    /// <param name="eventId">The provider event identifier.</param>
    /// <returns>An accepted duplicate webhook result.</returns>
    public static VerifyProviderWebhookResult CreateAcceptedDuplicate(string eventId) => new(true, true, false, eventId);

    /// <summary>
    /// Creates an accepted webhook result for a non-authoritative event that is intentionally ignored.
    /// </summary>
    /// <param name="eventId">The provider event identifier.</param>
    /// <returns>An accepted ignored webhook result.</returns>
    public static VerifyProviderWebhookResult CreateIgnored(string eventId) => new(true, false, true, eventId);
}
