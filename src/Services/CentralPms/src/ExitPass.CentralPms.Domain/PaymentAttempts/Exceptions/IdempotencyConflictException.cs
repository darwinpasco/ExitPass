namespace ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;

/// <summary>
/// Indicates that an idempotency key was replayed with different payment attempt semantics.
/// </summary>
public sealed class IdempotencyConflictException : Exception
{
    /// <summary>
    /// Creates the conflict exception for an idempotency key that cannot safely reuse an existing result.
    /// </summary>
    /// <param name="idempotencyKey">Client supplied key whose replay does not match the original payment attempt request.</param>
    public IdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' conflicts with an existing semantic request.")
    {
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// Idempotency key that conflicted with the persisted payment attempt request shape.
    /// </summary>
    public string IdempotencyKey { get; }
}
