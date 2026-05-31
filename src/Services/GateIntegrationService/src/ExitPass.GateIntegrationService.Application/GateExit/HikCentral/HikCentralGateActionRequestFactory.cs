namespace ExitPass.GateIntegrationService.Application.GateExit.HikCentral;

/// <summary>
/// Builds HikCentral gate action requests from vendor-neutral command lifecycle records.
/// </summary>
public static class HikCentralGateActionRequestFactory
{
    /// <summary>
    /// Creates a HikCentral door-control request from the current gate command and handoff.
    /// </summary>
    public static HikCentralGateActionRequest CreateOpenExitBarrierRequest(
        GateCommandLifecycleRecord command,
        GateAuthorizationConsumedHandoff handoff)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handoff);

        var doorIndexCode = ResolveDoorIndexCode(handoff);
        return new HikCentralGateActionRequest(
            command.CommandId,
            command.SourceProcessingId,
            command.SourceEventId,
            command.ExitAuthorizationId,
            command.GateAuthorizationConsumptionId,
            command.ParkingSessionId,
            command.PaymentAttemptId,
            command.TariffSnapshotId,
            command.GateDeviceId,
            command.GateDeviceIdentifier,
            command.LaneId,
            command.SiteId,
            command.VendorSystemId,
            command.CorrelationId,
            command.RequestedAtUtc,
            handoff.ConsumedAtUtc,
            command.AttemptCount,
            doorIndexCode,
            HikCentralDoorControlType.Open,
            HikCentralDoorControlDirection.Exit);
    }

    private static string ResolveDoorIndexCode(GateAuthorizationConsumedHandoff handoff)
    {
        if (!string.IsNullOrWhiteSpace(handoff.GateDeviceIdentifier))
        {
            return handoff.GateDeviceIdentifier;
        }

        if (handoff.GateDeviceId.HasValue && handoff.GateDeviceId.Value != Guid.Empty)
        {
            return handoff.GateDeviceId.Value.ToString("D");
        }

        throw new GateAuthorizationConsumedHandoffException(
            "HIKCENTRAL_GATE_DEVICE_IDENTIFIER_REQUIRED",
            "A HikCentral door index code requires GateDeviceIdentifier or GateDeviceId.");
    }
}
