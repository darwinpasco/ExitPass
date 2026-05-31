namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// DB-backed gateway for consuming exit authorizations through the canonical routine.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 9.7 Recommended Database Functions
///
/// Invariants Enforced:
/// - ExitAuthorization consumption is delegated to the canonical DB routine
/// - Application code must not mutate authorization state outside the DB control path
/// </summary>
public interface IConsumeExitAuthorizationGateway
{
    /// <summary>
    /// Consumes an issued exit authorization through the canonical database routine.
    /// </summary>
    /// <param name="request">Consumption request metadata and identifiers.</param>
    /// <param name="cancellationToken">Cancellation token for the asynchronous operation.</param>
    /// <returns>The DB-authoritative consume result.</returns>
    Task<ConsumeExitAuthorizationDbResult> ConsumeAsync(
        ConsumeExitAuthorizationDbRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// DB request for consuming an exit authorization.
/// </summary>
public sealed record ConsumeExitAuthorizationDbRequest
{
    /// <summary>
    /// Canonical identifier of the exit authorization to consume.
    /// </summary>
    public Guid ExitAuthorizationId { get; init; }

    /// <summary>
    /// User or actor identifier requesting the consume operation.
    /// </summary>
    public Guid RequestedByUserId { get; init; }

    /// <summary>
    /// Correlation identifier for end-to-end traceability.
    /// </summary>
    public Guid CorrelationId { get; init; }

    /// <summary>
    /// Timestamp at which the consume request is issued.
    /// </summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>
    /// Validated gate device identifier for the consuming device, when known.
    /// </summary>
    public Guid? GateDeviceId { get; init; }

    /// <summary>
    /// Gate device identifier supplied at the HTTP boundary, when known.
    /// </summary>
    public string? GateDeviceIdentifier { get; init; }

    /// <summary>
    /// Validated exit lane for the consuming gate device, when known.
    /// </summary>
    public Guid? LaneId { get; init; }

    /// <summary>
    /// Validated site scope for the consuming gate device, when known.
    /// </summary>
    public Guid? SiteId { get; init; }
}

/// <summary>
/// DB-authoritative result returned after consuming an exit authorization.
/// </summary>
/// <param name="ExitAuthorizationId">Canonical identifier of the consumed authorization.</param>
/// <param name="AuthorizationStatus">Authorization status after consumption.</param>
/// <param name="ConsumedAt">Timestamp at which the authorization was consumed.</param>
/// <param name="GateAuthorizationConsumptionId">Persisted gate authorization consumption identifier.</param>
/// <param name="ParkingSessionId">Parking session tied to the consumed authorization.</param>
/// <param name="PaymentAttemptId">Confirmed payment attempt tied to the consumed authorization.</param>
/// <param name="TariffSnapshotId">Paid tariff snapshot stored on the confirmed payment attempt.</param>
/// <param name="GateDeviceId">Validated consuming gate device identifier.</param>
/// <param name="GateDeviceIdentifier">Gate device identifier supplied by the caller.</param>
/// <param name="LaneId">Validated lane scope, when present.</param>
/// <param name="SiteId">Validated site scope.</param>
/// <param name="VendorSystemId">Vendor PMS system for the parking session, when present.</param>
public sealed record ConsumeExitAuthorizationDbResult(
    Guid ExitAuthorizationId,
    string AuthorizationStatus,
    DateTimeOffset ConsumedAt,
    Guid? GateAuthorizationConsumptionId = null,
    Guid? ParkingSessionId = null,
    Guid? PaymentAttemptId = null,
    Guid? TariffSnapshotId = null,
    Guid? GateDeviceId = null,
    string? GateDeviceIdentifier = null,
    Guid? LaneId = null,
    Guid? SiteId = null,
    Guid? VendorSystemId = null);
