using System.Diagnostics;

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
    /// <summary>
    /// Activity source for Gate Integration Service consume/open evidence.
    /// </summary>
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.GateIntegrationService.Application.GateExit");

    private readonly ICentralPmsExitAuthorizationClient _centralPmsClient;
    private readonly IGateHardwareController _gateHardwareController;
    private readonly IGateExitAttemptRecorder _attemptRecorder;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsumeGateExitAuthorizationHandler"/> class.
    /// </summary>
    /// <param name="centralPmsClient">Central PMS consume client that remains authoritative for authorization state.</param>
    /// <param name="gateHardwareController">Gate hardware boundary invoked only after Central PMS returns <c>CONSUMED</c>.</param>
    /// <param name="attemptRecorder">Recorder for reportable gate exit attempt outcomes.</param>
    public ConsumeGateExitAuthorizationHandler(
        ICentralPmsExitAuthorizationClient centralPmsClient,
        IGateHardwareController gateHardwareController,
        IGateExitAttemptRecorder attemptRecorder)
    {
        _centralPmsClient = centralPmsClient ?? throw new ArgumentNullException(nameof(centralPmsClient));
        _gateHardwareController = gateHardwareController ?? throw new ArgumentNullException(nameof(gateHardwareController));
        _attemptRecorder = attemptRecorder ?? throw new ArgumentNullException(nameof(attemptRecorder));
    }

    /// <inheritdoc />
    public async Task<ConsumeGateExitAuthorizationResult> ExecuteAsync(
        ConsumeGateExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("ConsumeGateExitAuthorization", ActivityKind.Internal);

        activity?.SetTag("operation", "consume_gate_exit_authorization");
        activity?.SetTag("correlation_id", command.CorrelationId);
        activity?.SetTag("exit_authorization_id", command.ExitAuthorizationId);
        activity?.SetTag("gate_device_id", command.GateDeviceId);
        activity?.SetTag("service_identity_id", command.ServiceIdentityId);
        activity?.SetTag("gate_open_attempted", false);
        activity?.SetTag("gate_open_succeeded", false);

        try
        {
            Validate(command);
        }
        catch (ArgumentException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("central_pms_consume_result", "NOT_CALLED");
            activity?.SetTag("result_code", "INVALID_GATE_CONSUME_REQUEST");
            activity?.SetTag("rejection_reason", ex.Message);
            activity?.SetTag("gate_open_attempted", false);
            activity?.SetTag("gate_open_succeeded", false);
            throw;
        }

        var consumeResult = await _centralPmsClient.ConsumeAsync(
            command.ExitAuthorizationId,
            command.ServiceIdentityId,
            command.CorrelationId,
            cancellationToken);

        if (consumeResult.Status == CentralPmsConsumeAuthorizationStatus.Consumed &&
            string.Equals(consumeResult.AuthorizationStatus, "CONSUMED", StringComparison.OrdinalIgnoreCase) &&
            consumeResult.ConsumedAt.HasValue)
        {
            activity?.SetTag("central_pms_consume_result", consumeResult.Status.ToString().ToUpperInvariant());
            activity?.SetTag("authorization_status", consumeResult.AuthorizationStatus);
            activity?.SetTag("consumed_at", consumeResult.ConsumedAt);
            activity?.SetTag("gate_open_attempted", true);

            await _gateHardwareController.OpenBarrierAsync(
                command.GateDeviceId,
                command.ExitAuthorizationId,
                command.CorrelationId,
                cancellationToken);

            await RecordAsync(command, "GATE_OPENED", gateOpened: true, cancellationToken);

            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("gate_open_succeeded", true);
            activity?.SetTag("result_code", "GATE_OPENED");

            return new ConsumeGateExitAuthorizationResult(
                GateOpened: true,
                ResultCode: "GATE_OPENED",
                AuthorizationStatus: consumeResult.AuthorizationStatus,
                ExitAuthorizationId: command.ExitAuthorizationId,
                ConsumedAt: consumeResult.ConsumedAt);
        }

        var resultCode = MapResultCode(consumeResult);

        await RecordAsync(command, resultCode, gateOpened: false, cancellationToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
        activity?.SetTag("central_pms_consume_result", consumeResult.Status.ToString().ToUpperInvariant());
        activity?.SetTag("authorization_status", consumeResult.AuthorizationStatus);
        activity?.SetTag("result_code", resultCode);
        activity?.SetTag("gate_open_attempted", false);
        activity?.SetTag("gate_open_succeeded", false);

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
