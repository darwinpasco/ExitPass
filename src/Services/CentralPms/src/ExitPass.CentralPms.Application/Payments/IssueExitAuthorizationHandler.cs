using ExitPass.CentralPms.Domain.Common;

namespace ExitPass.CentralPms.Application.Payments;

/// <summary>
/// Issues a single-use exit authorization through the canonical DB-backed control path.
///
/// BRD:
/// - 9.12 Exit Authorization
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 10.7.2 Payment Finality Invariant
/// - 10.7.7 Exit Token Integrity Invariant
///
/// SDD:
/// - 6.5 Issue Exit Authorization
/// - 8.5 ExitAuthorization State Machine
///
/// Invariants Enforced:
/// - ExitAuthorization may only be issued after confirmed payment finality
/// - Authorization issuance remains DB-backed and deterministic
/// - Issuance requests are fully traceable through correlation metadata
/// </summary>
public sealed class IssueExitAuthorizationHandler : IIssueExitAuthorizationUseCase
{
    private readonly IIssueExitAuthorizationGateway _gateway;
    private readonly ISystemClock _systemClock;

    public IssueExitAuthorizationHandler(
        IIssueExitAuthorizationGateway gateway,
        ISystemClock systemClock)
    {
        _gateway = gateway;
        _systemClock = systemClock;
    }

    public async Task<IssueExitAuthorizationResult> ExecuteAsync(
        IssueExitAuthorizationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ParkingSessionId == Guid.Empty)
        {
            throw new ArgumentException("ParkingSessionId is required.", nameof(command));
        }

        if (command.PaymentAttemptId == Guid.Empty)
        {
            throw new ArgumentException("PaymentAttemptId is required.", nameof(command));
        }

        if (command.RequestedByUserId == Guid.Empty)
        {
            throw new ArgumentException("RequestedByUserId is required.", nameof(command));
        }

        if (command.CorrelationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId is required.", nameof(command));
        }

        var dbResult = await _gateway.IssueAsync(
            new IssueExitAuthorizationDbRequest
            {
                ParkingSessionId = command.ParkingSessionId,
                PaymentAttemptId = command.PaymentAttemptId,
                RequestedByUserId = command.RequestedByUserId,
                CorrelationId = command.CorrelationId,
                RequestedAt = _systemClock.UtcNow
            },
            cancellationToken);

        return new IssueExitAuthorizationResult(
            ExitAuthorizationId: dbResult.ExitAuthorizationId,
            ParkingSessionId: dbResult.ParkingSessionId,
            PaymentAttemptId: dbResult.PaymentAttemptId,
            AuthorizationToken: dbResult.AuthorizationToken,
            AuthorizationStatus: dbResult.AuthorizationStatus,
            IssuedAt: dbResult.IssuedAt,
            ExpirationTimestamp: dbResult.ExpirationTimestamp);
    }
}
