namespace ExitPass.GateIntegrationService.Application.GateExit;

/// <summary>
/// Consumes an ExitAuthorization through Central PMS and opens the gate only after successful consume.
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
/// - Central PMS remains the sole consume authority.
/// - Gate hardware is never commanded before successful Central PMS consume.
/// - Gate Integration Service never invents payment or financial finality.
/// </summary>
public sealed class ConsumeGateExitAuthorizationHandler : IConsumeGateExitAuthorizationUseCase
{
    private readonly ICentralPmsExitAuthorizationClient _centralPmsClient;
    private readonly IGateHardwareController _gateHardwareController;
    private readonly IGateExitAttemptRecorder _attemptRecorder;

    public ConsumeGateExitAuthorizationHandler(
        ICentralPmsExitAuthorizationClient centralPmsClient,
        IGateHardwareController gateHardwareController,
        IGateExitAttemptRecorder attemptRecorder)
    {
        _centralPmsClient = centralPmsClient ?? throw new ArgumentNullException(nameof(centralPmsClient));
        _gateHardwareController = gateHardwareController ?? throw new ArgumentNullException(nameof(gateHardwareController));
        _attemptRecorder = attemptRecorder ?? throw new ArgumentNullException(nameof(attemptRecorder));
    }

    public async Task<ConsumeGateExitAuthorizationResult> ExecuteAsync(
        ConsumeGateExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Validate(command);

        var consumeResult = await _centralPmsClient.ConsumeAsync(
            command.ExitAuthorizationId,
            command.ServiceIdentityId,
            command.CorrelationId,
            cancellationToken);

        if (consumeResult.Status == CentralPmsConsumeAuthorizationStatus.Consumed &&
            string.Equals(consumeResult.AuthorizationStatus, "CONSUMED", StringComparison.OrdinalIgnoreCase) &&
            consumeResult.ConsumedAt.HasValue)
        {
            await _gateHardwareController.OpenBarrierAsync(
                command.GateDeviceId,
                command.ExitAuthorizationId,
                command.CorrelationId,
                cancellationToken);

            await RecordAsync(command, "GATE_OPENED", gateOpened: true, cancellationToken);

            return new ConsumeGateExitAuthorizationResult(
                GateOpened: true,
                ResultCode: "GATE_OPENED",
                AuthorizationStatus: consumeResult.AuthorizationStatus,
                ExitAuthorizationId: command.ExitAuthorizationId,
                ConsumedAt: consumeResult.ConsumedAt);
        }

        var resultCode = MapResultCode(consumeResult);

        await RecordAsync(command, resultCode, gateOpened: false, cancellationToken);

        return new ConsumeGateExitAuthorizationResult(
            GateOpened: false,
            ResultCode: resultCode,
            AuthorizationStatus: consumeResult.AuthorizationStatus,
            ExitAuthorizationId: command.ExitAuthorizationId,
            ConsumedAt: consumeResult.ConsumedAt);
    }

    private static void Validate(ConsumeGateExitAuthorizationCommand command)
    {
        if (command.ExitAuthorizationId == Guid.Empty)
        {
            throw new ArgumentException("ExitAuthorizationId is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.GateDeviceId))
        {
            throw new ArgumentException("GateDeviceId is required.", nameof(command));
        }

        if (command.ServiceIdentityId == Guid.Empty)
        {
            throw new ArgumentException("ServiceIdentityId is required.", nameof(command));
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId is required.", nameof(command));
        }
    }

    private static string MapResultCode(CentralPmsConsumeAuthorizationResult result)
    {
        return result.Status switch
        {
            CentralPmsConsumeAuthorizationStatus.NotFound => "EXIT_AUTHORIZATION_NOT_FOUND",
            CentralPmsConsumeAuthorizationStatus.AlreadyConsumed => "EXIT_AUTHORIZATION_ALREADY_CONSUMED",
            CentralPmsConsumeAuthorizationStatus.Expired => "EXIT_AUTHORIZATION_EXPIRED",
            CentralPmsConsumeAuthorizationStatus.Unavailable => "CENTRAL_PMS_UNAVAILABLE",
            _ => string.IsNullOrWhiteSpace(result.ErrorCode) ? "EXIT_AUTHORIZATION_REJECTED" : result.ErrorCode
        };
    }

    private Task RecordAsync(
        ConsumeGateExitAuthorizationCommand command,
        string resultCode,
        bool gateOpened,
        CancellationToken cancellationToken)
    {
        return _attemptRecorder.RecordAsync(
            new GateExitAttemptRecord(
                command.ExitAuthorizationId,
                command.GateDeviceId,
                command.ServiceIdentityId,
                command.CorrelationId,
                resultCode,
                gateOpened,
                DateTimeOffset.UtcNow),
            cancellationToken);
    }
}
