namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Gate Integration Service view of the Central PMS GateAuthorizationConsumed handoff.
/// </summary>
/// <param name="EventId">Source integration event identifier.</param>
/// <param name="SourceEventRef">Source event reference, when supplied by the transport or outbox row.</param>
/// <param name="ExitAuthorizationId">Consumed exit authorization identifier.</param>
/// <param name="GateAuthorizationConsumptionId">Persisted Central PMS gate consumption identifier.</param>
/// <param name="ParkingSessionId">Parking session tied to the consumed authorization.</param>
/// <param name="PaymentAttemptId">Confirmed payment attempt tied to the consumed authorization.</param>
/// <param name="TariffSnapshotId">Paid tariff snapshot preserved from the Central PMS payment chain.</param>
/// <param name="GateDeviceId">Validated Central PMS gate device identifier, when available.</param>
/// <param name="GateDeviceIdentifier">Gate device code or external identifier, when available.</param>
/// <param name="LaneId">Validated lane scope, when available.</param>
/// <param name="SiteId">Validated site scope, when available.</param>
/// <param name="VendorSystemId">Vendor PMS system identifier, when available.</param>
/// <param name="ConsumedAtUtc">Central PMS consumption timestamp.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record GateAuthorizationConsumedHandoff(
    Guid EventId,
    string? SourceEventRef,
    Guid ExitAuthorizationId,
    Guid GateAuthorizationConsumptionId,
    Guid ParkingSessionId,
    Guid PaymentAttemptId,
    Guid TariffSnapshotId,
    Guid? GateDeviceId,
    string? GateDeviceIdentifier,
    Guid? LaneId,
    Guid? SiteId,
    Guid? VendorSystemId,
    DateTimeOffset ConsumedAtUtc,
    Guid CorrelationId);
