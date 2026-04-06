using System.Diagnostics;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Internal;
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

    private readonly ILogger<InitiateProviderPaymentHandler> _logger;
    private readonly IPaymentProviderRegistry _providerRegistry;
    private readonly IProviderSessionRepository _providerSessionRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="InitiateProviderPaymentHandler"/> class.
    /// </summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="providerRegistry">The provider adapter registry.</param>
    /// <param name="providerSessionRepository">The provider session repository.</param>
    public InitiateProviderPaymentHandler(
        ILogger<InitiateProviderPaymentHandler> logger,
        IPaymentProviderRegistry providerRegistry,
        IProviderSessionRepository providerSessionRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
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

        var adapter = _providerRegistry.GetRequired(request.ProviderCode, request.ProviderProduct);

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
            request.Metadata);

        var startedAtUtc = DateTimeOffset.UtcNow;
        var result = await adapter.CreatePaymentSessionAsync(command, cancellationToken);

        var requestJson = JsonSerializer.Serialize(request);

        var record = new ProviderSessionRecord(
            Guid.NewGuid(),
            request.PaymentAttemptId,
            request.ProviderCode,
            request.ProviderProduct,
            result.ProviderSessionId,
            result.ProviderReference,
            result.SessionStatus,
            result.Handoff.RedirectUrl,
            result.ExpiresAtUtc,
            request.IdempotencyKey,
            requestJson,
            result.RawResponseJson,
            startedAtUtc);

        await _providerSessionRepository.AddAsync(record, cancellationToken);

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
}
