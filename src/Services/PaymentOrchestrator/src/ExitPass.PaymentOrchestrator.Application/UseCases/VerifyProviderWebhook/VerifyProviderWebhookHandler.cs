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
    private const string NonAuthoritativePaymentPaidEvent = "payment.paid";

    private readonly ILogger<VerifyProviderWebhookHandler> _logger;
    private readonly IPaymentProviderAdapter _adapter;
    private readonly IProviderWebhookEventRepository _providerWebhookEventRepository;
    private readonly ICentralPmsPaymentOutcomeReporter _centralPmsPaymentOutcomeReporter;

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

            TagRejected(activity, "WEBHOOK_NOT_AUTHENTIC");
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

        if (!TryBuildVerifiedOutcomeReport(verification, out var report, out var rejectionCode))
        {
            _logger.LogWarning(
                "Rejected verified provider webhook because required internal attributes are missing. ProviderCode {ProviderCode}, EventId {EventId}, RejectionCode {RejectionCode}",
                _adapter.ProviderCode,
                verification.EventId,
                rejectionCode);

            TagRejected(activity, rejectionCode!);
            return VerifyProviderWebhookResult.CreateRejected(rejectionCode!);
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

            TagRejected(activity, "WEBHOOK_UNKNOWN_PROVIDER_SESSION");
            return VerifyProviderWebhookResult.CreateRejected("WEBHOOK_UNKNOWN_PROVIDER_SESSION");
        }

        await _centralPmsPaymentOutcomeReporter.ReportVerifiedOutcomeAsync(report!, cancellationToken);

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
        activity?.SetTag("correlation_id", report!.CorrelationId);
        activity?.SetTag("parking_session_id", report.ParkingSessionId);

        return VerifyProviderWebhookResult.CreateAccepted(verification.EventId);
    }

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

    /// <summary>
    /// Builds the verified outcome report from provider verification output.
    /// Missing internal attributes must produce deterministic business rejection,
    /// not unhandled 500 errors.
    /// </summary>
    private static bool TryBuildVerifiedOutcomeReport(
        ProviderWebhookVerificationResult verification,
        out VerifiedPaymentOutcomeReport? report,
        out string? rejectionCode)
    {
        var rawAttributes = verification.RawAttributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!TryGetRequiredGuid(rawAttributes, "parking_session_id", out var parkingSessionId))
        {
            report = null;
            rejectionCode = "WEBHOOK_MISSING_PARKING_SESSION_ID";
            return false;
        }

        if (!TryGetRequiredGuid(rawAttributes, "requested_by_user_id", out var requestedByUserId))
        {
            report = null;
            rejectionCode = "WEBHOOK_MISSING_REQUESTED_BY_USER_ID";
            return false;
        }

        report = new VerifiedPaymentOutcomeReport(
            PaymentAttemptId: verification.PaymentAttemptId,
            ParkingSessionId: parkingSessionId,
            RequestedByUserId: requestedByUserId,
            CorrelationId: ResolveCorrelationId(rawAttributes),
            ProviderCode: PayMongoProviderCode,
            ProviderReference: verification.ProviderReference,
            ProviderSessionId: verification.ProviderSessionId,
            CanonicalStatus: verification.CanonicalStatus.ToString().ToUpperInvariant(),
            OccurredAtUtc: verification.OccurredAtUtc,
            AmountMinor: verification.AmountMinor,
            Currency: verification.Currency,
            EventId: verification.EventId,
            IsTerminal: verification.IsTerminal,
            IsSuccess: verification.IsSuccess,
            RawAttributes: rawAttributes);

        rejectionCode = null;
        return true;
    }

    private static bool TryGetRequiredGuid(
        IReadOnlyDictionary<string, string> rawAttributes,
        string key,
        out Guid value)
    {
        value = Guid.Empty;

        if (!rawAttributes.TryGetValue(key, out var rawValue) ||
            string.IsNullOrWhiteSpace(rawValue) ||
            !Guid.TryParse(rawValue, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static Guid ResolveCorrelationId(IReadOnlyDictionary<string, string> rawAttributes)
    {
        if (rawAttributes.TryGetValue("correlation_id", out var value) &&
            Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return Guid.NewGuid();
    }

    private static void TagRejected(Activity? activity, string rejectionCode)
    {
        activity?.SetTag("webhook.accepted", false);
        activity?.SetTag("webhook.duplicate", false);
        activity?.SetTag("webhook.ignored", false);
        activity?.SetTag("webhook.rejection_code", rejectionCode);
        activity?.SetTag("failure_class", "BUSINESS_REJECTION");
    }
}

public sealed record VerifyProviderWebhookResult(
    bool Accepted,
    bool Duplicate,
    bool Ignored,
    string Code)
{
    public static VerifyProviderWebhookResult CreateRejected(string code) => new(false, false, false, code);
    public static VerifyProviderWebhookResult CreateAccepted(string eventId) => new(true, false, false, eventId);
    public static VerifyProviderWebhookResult CreateAcceptedDuplicate(string eventId) => new(true, true, false, eventId);
    public static VerifyProviderWebhookResult CreateIgnored(string eventId) => new(true, false, true, eventId);
}
