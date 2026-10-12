using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.UseCases.QueryProviderSessionStatus;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.RecoverProviderPaymentOutcome;

/// <summary>
/// Recovers a verified terminal provider result for an incomplete canonical payment attempt.
/// Central PMS remains the sole owner of payment finality and downstream completion.
/// </summary>
public sealed class RecoverProviderPaymentOutcomeHandler
{
    private static readonly Guid PaymentOrchestratorServiceIdentityId =
        Guid.Parse("d47280c6-5e0f-528d-8b22-f026e13f9cd6");

    private readonly ILogger<RecoverProviderPaymentOutcomeHandler> _logger;
    private readonly IProviderSessionRepository _providerSessionRepository;
    private readonly QueryProviderSessionStatusHandler _statusQueryHandler;
    private readonly ICentralPmsPaymentOutcomeReporter _outcomeReporter;

    public RecoverProviderPaymentOutcomeHandler(
        ILogger<RecoverProviderPaymentOutcomeHandler> logger,
        IProviderSessionRepository providerSessionRepository,
        QueryProviderSessionStatusHandler statusQueryHandler,
        ICentralPmsPaymentOutcomeReporter outcomeReporter)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
        _statusQueryHandler = statusQueryHandler ?? throw new ArgumentNullException(nameof(statusQueryHandler));
        _outcomeReporter = outcomeReporter ?? throw new ArgumentNullException(nameof(outcomeReporter));
    }

    public async Task<RecoverProviderPaymentOutcomeResult> HandleAsync(
        RecoverProviderPaymentOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PaymentAttemptId == Guid.Empty ||
            command.ParkingSessionId == Guid.Empty ||
            command.AmountMinorUnits < 0 ||
            string.IsNullOrWhiteSpace(command.Currency))
        {
            return RecoverProviderPaymentOutcomeResult.Rejected("PROVIDER_STATUS_RECOVERY_INVALID_SCOPE");
        }

        var providerSession = await _providerSessionRepository.FindLatestByPaymentAttemptIdAsync(
            command.PaymentAttemptId,
            cancellationToken);
        if (providerSession is null ||
            providerSession.PaymentAttemptId != command.PaymentAttemptId ||
            string.IsNullOrWhiteSpace(providerSession.ProviderCode) ||
            string.IsNullOrWhiteSpace(providerSession.ProviderProduct) ||
            string.IsNullOrWhiteSpace(providerSession.ProviderSessionId))
        {
            return RecoverProviderPaymentOutcomeResult.Rejected("PROVIDER_SESSION_NOT_FOUND");
        }

        if (providerSession.AmountMinorUnits is not long persistedAmount ||
            persistedAmount != command.AmountMinorUnits ||
            string.IsNullOrWhiteSpace(providerSession.CurrencyCode) ||
            !string.Equals(providerSession.CurrencyCode, command.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return RecoverProviderPaymentOutcomeResult.Rejected("PROVIDER_STATUS_RECOVERY_PAYABLE_BASIS_MISMATCH");
        }

        var evidence = await _statusQueryHandler.HandleAsync(
            new QueryProviderSessionStatusCommand(
                providerSession.ProviderCode,
                providerSession.ProviderProduct,
                providerSession.ProviderSessionId,
                command.CorrelationId),
            cancellationToken);

        if (!evidence.ReportableToCentralPms || !evidence.IsTerminal || !evidence.IsSuccess)
        {
            return new RecoverProviderPaymentOutcomeResult(
                Queried: true,
                Reported: false,
                evidence.ErrorCode);
        }

        if (evidence.AmountMinor is not long verifiedAmount ||
            verifiedAmount != command.AmountMinorUnits ||
            string.IsNullOrWhiteSpace(evidence.CurrencyCode) ||
            !string.Equals(evidence.CurrencyCode, command.Currency, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evidence.ProviderReference))
        {
            return RecoverProviderPaymentOutcomeResult.Rejected("PROVIDER_STATUS_RECOVERY_EVIDENCE_MISMATCH", queried: true);
        }

        var correlationId = evidence.CorrelationId ?? command.CorrelationId;
        var rawAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = evidence.SourceStatus ?? evidence.NormalizedStatus.ToString().ToUpperInvariant(),
            ["evidence_source"] = "PROVIDER_STATUS_QUERY"
        };

        var report = new VerifiedPaymentOutcomeReport(
            command.PaymentAttemptId,
            command.ParkingSessionId,
            PaymentOrchestratorServiceIdentityId,
            correlationId,
            evidence.ProviderCode,
            evidence.ProviderReference,
            evidence.ProviderSessionId,
            evidence.NormalizedStatus.ToString().ToUpperInvariant(),
            evidence.ProviderObservedAtUtc ?? DateTimeOffset.UtcNow,
            verifiedAmount,
            evidence.CurrencyCode,
            BuildIdempotencyKey(evidence),
            evidence.IsTerminal,
            evidence.IsSuccess,
            rawAttributes);

        await _outcomeReporter.ReportVerifiedOutcomeAsync(report, cancellationToken);

        _logger.LogInformation(
            "Reported verified provider status-query outcome to Central PMS. PaymentAttemptId {PaymentAttemptId}, ProviderSessionId {ProviderSessionId}, CorrelationId {CorrelationId}",
            command.PaymentAttemptId,
            evidence.ProviderSessionId,
            correlationId);

        return new RecoverProviderPaymentOutcomeResult(Queried: true, Reported: true, ErrorCode: null);
    }

    private static string BuildIdempotencyKey(Abstractions.Providers.ProviderStatusQueryResult evidence) =>
        $"status-query:{evidence.ProviderCode}:{evidence.ProviderSessionId}:{evidence.ProviderReference}:{evidence.NormalizedStatus}";
}

public sealed record RecoverProviderPaymentOutcomeCommand(
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    long AmountMinorUnits,
    string Currency,
    Guid CorrelationId);

public sealed record RecoverProviderPaymentOutcomeResult(
    bool Queried,
    bool Reported,
    string? ErrorCode)
{
    public static RecoverProviderPaymentOutcomeResult Rejected(string errorCode, bool queried = false) =>
        new(queried, Reported: false, errorCode);
}
