namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Command issued by a gate device when a vehicle presents an exit authorization.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 6.6 Consume Exit Authorization
/// - 10.4.2 Consume Exit Authorization
///
/// Invariants Enforced:
/// - Gate Integration Service must carry device and service identity before Central PMS consume.
/// - Gate Integration Service must not decide payment finality.
/// </summary>
/// <param name="ExitAuthorizationId">Exit authorization identifier issued by Central PMS.</param>
/// <param name="GateDeviceId">Gate device identifier.</param>
/// <param name="ServiceIdentityId">Service identity used as the Central PMS consume actor.</param>
/// <param name="CorrelationId">End-to-end correlation identifier.</param>
public sealed record ConsumeGateExitAuthorizationCommand(
    Guid ExitAuthorizationId,
    string GateDeviceId,
    Guid ServiceIdentityId,
    Guid CorrelationId);
