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
    Task<FinalizePaymentAttemptDbResult> FinalizeAsync(
        FinalizePaymentAttemptDbRequest request,
        CancellationToken cancellationToken);
}

public sealed class FinalizePaymentAttemptDbRequest
{
    public Guid PaymentAttemptId { get; init; }
    public string FinalAttemptStatus { get; init; } = string.Empty;
    public string RequestedBy { get; init; } = string.Empty;
    public Guid CorrelationId { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
}

public sealed class FinalizePaymentAttemptDbResult
{
    public Guid PaymentAttemptId { get; init; }
    public string AttemptStatus { get; init; } = string.Empty;
}
