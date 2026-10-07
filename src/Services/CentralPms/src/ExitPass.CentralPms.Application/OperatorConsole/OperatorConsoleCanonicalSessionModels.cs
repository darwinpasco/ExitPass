using ExitPass.CentralPms.Application.VendorSessions;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed record OperatorConsoleCanonicalSessionCommand(
    Guid SiteId,
    Guid SiteGroupId,
    Guid VendorSystemId,
    string LookupMode,
    string? TicketReference,
    string? PlateNumber,
    Guid CorrelationId);

public sealed record OperatorConsoleCanonicalSessionResult(
    Guid ParkingSessionId,
    bool ReusedExistingSession,
    Guid VendorSessionProjectionId,
    Guid CorrelationId);

public sealed record OperatorConsoleCanonicalSessionPersistenceCommand(
    VendorSessionProjection Projection,
    Guid CorrelationId);

public sealed record OperatorConsoleCanonicalSessionPersistenceResult(
    Guid ParkingSessionId,
    bool ReusedExistingSession);

public interface IOperatorConsoleCanonicalSessionService
{
    Task<OperatorConsoleCanonicalSessionResult> EnsureAsync(
        OperatorConsoleCanonicalSessionCommand command,
        CancellationToken cancellationToken);
}

public interface IOperatorConsoleCanonicalSessionRepository
{
    Task<OperatorConsoleCanonicalSessionPersistenceResult> EnsureAsync(
        OperatorConsoleCanonicalSessionPersistenceCommand command,
        CancellationToken cancellationToken);
}

public sealed class OperatorConsoleCanonicalSessionException(string errorCode)
    : Exception("The canonical parking session could not be established safely.")
{
    public string ErrorCode { get; } = errorCode;
}
