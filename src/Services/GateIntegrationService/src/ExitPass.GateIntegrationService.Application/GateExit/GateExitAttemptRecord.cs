namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Reportable gate exit attempt record.
/// </summary>
public sealed record GateExitAttemptRecord(
    Guid ExitAuthorizationId,
    string GateDeviceId,
    Guid ServiceIdentityId,
    Guid CorrelationId,
    string ResultCode,
    bool GateOpened,
    DateTimeOffset RecordedAtUtc);
