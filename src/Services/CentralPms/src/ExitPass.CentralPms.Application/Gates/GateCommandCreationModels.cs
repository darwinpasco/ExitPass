using ExitPass.CentralPms.Application.Eventing;

namespace ExitPass.CentralPms.Application.Gates;

/// <summary>
/// Application service boundary for creating vendor-neutral gate command intent from a consumed authorization event.
/// </summary>
public interface IGateCommandCreationService
{
    /// <summary>
    /// Creates or reuses the canonical consumed-processing row and gate command for one consumed authorization event.
    /// </summary>
    Task<GateCommandCreationResult> CreateFromConsumedEventAsync(
        IntegrationEventEnvelope envelope,
        CancellationToken cancellationToken);
}

/// <summary>
/// Persistence boundary for atomic consumed-event-to-command creation.
/// </summary>
public interface IGateCommandCreationRepository
{
    /// <summary>
    /// Atomically creates or reuses one consumed-processing row and one vendor-neutral gate command.
    /// </summary>
    Task<GateCommandCreationResult> CreateOrReuseAsync(
        GateCommandCreationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Normalized command creation request derived from the existing GateAuthorizationConsumed event contract.
/// </summary>
public sealed record GateCommandCreationRequest(
    Guid EventId,
    string EventType,
    string EventRef,
    Guid ProcessingKey,
    Guid GateAuthorizationConsumptionId,
    Guid ExitAuthorizationId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid? GateDeviceId,
    Guid? ServiceIdentityId,
    Guid? LaneId,
    Guid? SiteId,
    Guid? VendorSystemId,
    DateTimeOffset ConsumedAt,
    Guid CorrelationId,
    string CommandType,
    DateTimeOffset RequestedAt);

/// <summary>
/// Result of a deterministic gate command creation request.
/// </summary>
public sealed record GateCommandCreationResult(
    Guid ProcessingId,
    Guid ProcessingKey,
    Guid CommandId,
    string CommandType,
    GateCommandCreationOutcome Outcome);

/// <summary>
/// Distinguishes first creation from deterministic replay.
/// </summary>
public enum GateCommandCreationOutcome
{
    /// <summary>
    /// The operation inserted a new processing row and command.
    /// </summary>
    Created,

    /// <summary>
    /// The operation found the existing semantically matching processing row and command.
    /// </summary>
    IdempotentReplay
}

/// <summary>
/// Controlled rejection for consumed-event-to-command creation.
/// </summary>
public sealed class GateCommandCreationRejectedException : Exception
{
    /// <summary>
    /// Creates a controlled gate command creation rejection.
    /// </summary>
    public GateCommandCreationRejectedException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = !string.IsNullOrWhiteSpace(errorCode)
            ? errorCode
            : throw new ArgumentException("Error code is required.", nameof(errorCode));
    }

    /// <summary>
    /// Controlled error code.
    /// </summary>
    public string ErrorCode { get; }
}
