namespace ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;

/// <summary>
/// Raised when a provider webhook references a provider session that does not exist in POA persistence.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Provider callbacks must be anchored to a known provider session before immutable evidence is persisted.
/// - Unknown provider sessions must be rejected deterministically and must not surface as unhandled 500 errors.
/// </summary>
public sealed class UnknownProviderSessionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownProviderSessionException"/> class.
    /// </summary>
    /// <param name="providerSessionRef">The unknown provider session reference.</param>
    public UnknownProviderSessionException(string providerSessionRef)
        : base($"No provider session found for provider_session_ref '{providerSessionRef}'.")
    {
        ProviderSessionRef = providerSessionRef;
    }

    /// <summary>
    /// Gets the provider session reference that could not be resolved.
    /// </summary>
    public string ProviderSessionRef { get; }
}
