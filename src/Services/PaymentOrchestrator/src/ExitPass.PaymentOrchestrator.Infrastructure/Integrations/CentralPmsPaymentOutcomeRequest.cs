namespace ExitPass.PaymentOrchestrator.Infrastructure.Integrations;

/// <summary>
/// Represents the request body sent from POA to Central PMS for a verified provider outcome.
/// </summary>
public sealed record CentralPmsPaymentOutcomeRequest(
    Guid PaymentAttemptId,
    string ProviderReference,
    string ProviderStatus,
    string RequestedBy);
