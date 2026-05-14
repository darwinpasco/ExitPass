namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Represents a reportable gate exit attempt record.
/// </summary>
/// <param name="ExitAuthorizationId">Exit authorization identifier presented at the gate.</param>
/// <param name="GateDeviceId">Gate device identifier that received the attempt.</param>
/// <param name="ServiceIdentityId">Service identity used for Central PMS consume.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
/// <param name="ResultCode">Provider-neutral outcome code for the attempt.</param>
/// <param name="GateOpened">Indicates whether the barrier-open command was issued.</param>
/// <param name="RecordedAtUtc">UTC timestamp when the attempt was recorded.</param>
public sealed record GateExitAttemptRecord(
    Guid ExitAuthorizationId,
    string GateDeviceId,
    Guid ServiceIdentityId,
    Guid CorrelationId,
    string ResultCode,
    bool GateOpened,
    DateTimeOffset RecordedAtUtc);
