namespace ExitPass.CentralPms.Application.Abstractions.Persistence;

/// <summary>
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.4 Finalize Payment
/// - 9.6 Integrity Constraints and Concurrency Rules
///
/// Invariants Enforced:
/// - PaymentAttempt finalization must be delegated to the authoritative DB-backed path
/// - Application code must not bypass storage-backed finalization rules
/// </summary>
public interface IFinalizePaymentAttemptGateway
{
    /// <summary>
    /// Finalizes a payment attempt through the canonical Central PMS database routine.
    /// </summary>
    /// <param name="request">Finalization request carrying the terminal status and trace metadata.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative finalized payment attempt state.</returns>
    Task<FinalizePaymentAttemptDbResult> FinalizeAsync(
        FinalizePaymentAttemptDbRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Database routine request for applying verified provider finality to a payment attempt.
/// </summary>
public sealed class FinalizePaymentAttemptDbRequest
{
    /// <summary>
    /// Payment attempt whose final status is being applied by Central PMS.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// Terminal status derived from the verified provider outcome.
    /// </summary>
    public string FinalAttemptStatus { get; init; } = string.Empty;

    /// <summary>
    /// Logical actor requesting finalization.
    /// </summary>
    public string RequestedBy { get; init; } = string.Empty;

    /// <summary>
    /// Correlation identifier for the payment-to-exit control chain.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Timestamp supplied to the authoritative finalization routine.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }
}

/// <summary>
/// Database routine result after Central PMS finalizes a payment attempt.
/// </summary>
public sealed class FinalizePaymentAttemptDbResult
{
    /// <summary>
    /// Payment attempt that was finalized.
    /// </summary>
    public Guid PaymentAttemptId { get; init; }

    /// <summary>
    /// DB-authoritative payment attempt status after finalization.
    /// </summary>
    public string AttemptStatus { get; init; } = string.Empty;
}
