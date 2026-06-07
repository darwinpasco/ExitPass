using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Application.GateExit.HikCentral;
using Microsoft.Extensions.Configuration;

namespace ExitPass.GateIntegrationService.Infrastructure.GateExit;

/// <summary>
/// Validation-only HikCentral sandbox command recorder.
/// </summary>
public sealed class PostgresHikCentralSandboxValidationCommandRecorder
    : IHikCentralSandboxValidationCommandRecorder
{
    /// <summary>
    /// Creates the recorder. The locked v1.2 schema has no standalone sandbox command table.
    /// </summary>
    public PostgresHikCentralSandboxValidationCommandRecorder(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    /// <inheritdoc />
    public Task<GateCommandLifecycleRecord> BeginValidationCommandAsync(
        HikCentralSandboxValidationCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var command = new GateCommandLifecycleRecord(
            Guid.NewGuid(),
            context.ValidationAttemptId,
            Guid.Empty,
            context.ExitAuthorizationId,
            context.GateAuthorizationConsumptionId,
            context.ParkingSessionId,
            context.PaymentAttemptId,
            context.TariffSnapshotId,
            null,
            context.DoorIndexCode,
            null,
            null,
            null,
            GateCommandStatus.InProgress,
            1,
            1,
            GateCommandRetryPolicy.Default.PolicyCode,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            context.RequestedAtUtc,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            context.CorrelationId);

        return Task.FromResult(command);
    }

    /// <inheritdoc />
    public Task CompleteValidationCommandAsync(
        Guid commandId,
        bool succeeded,
        string resultCode,
        string diagnosticMessage,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
