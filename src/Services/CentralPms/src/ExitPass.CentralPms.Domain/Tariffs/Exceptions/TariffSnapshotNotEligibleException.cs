namespace ExitPass.CentralPms.Domain.Tariffs.Exceptions;

public sealed class TariffSnapshotNotEligibleException : Exception
{
    public TariffSnapshotNotEligibleException(
        Guid tariffSnapshotId,
        TariffSnapshotStatus status,
        DateTimeOffset expiresAt,
        Guid? consumedByPaymentAttemptId)
        : base($"Tariff snapshot '{tariffSnapshotId}' is not eligible for payment.")
    {
        TariffSnapshotId = tariffSnapshotId;
        Status = status;
        ExpiresAt = expiresAt;
        ConsumedByPaymentAttemptId = consumedByPaymentAttemptId;
    }

    public Guid TariffSnapshotId { get; }
    public TariffSnapshotStatus Status { get; }
    public DateTimeOffset ExpiresAt { get; }
    public Guid? ConsumedByPaymentAttemptId { get; }
}