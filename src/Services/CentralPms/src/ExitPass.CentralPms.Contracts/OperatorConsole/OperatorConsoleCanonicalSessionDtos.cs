namespace ExitPass.CentralPms.Contracts.OperatorConsole;

public sealed record OperatorConsoleCanonicalSessionRequest(
    Guid VendorSystemId,
    string LookupMode,
    string? TicketReference,
    string? PlateNumber,
    Guid CorrelationId);

public sealed record OperatorConsoleCanonicalSessionResponse(
    Guid ParkingSessionId,
    bool ReusedExistingSession,
    Guid VendorSessionProjectionId,
    Guid CorrelationId);
