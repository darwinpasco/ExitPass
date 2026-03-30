namespace ExitPass.CentralPms.Domain.PaymentAttempts.Exceptions;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string idempotencyKey)
        : base($"Idempotency key '{idempotencyKey}' conflicts with an existing semantic request.")
    {
        IdempotencyKey = idempotencyKey;
    }

    public string IdempotencyKey { get; }
}