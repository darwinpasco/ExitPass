using System.Text.Json.Serialization;

namespace ExitPass.CentralPms.Contracts.OperatorConsole;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveOperatorConsoleInvoiceCustomerInformationRequest(
    string? CustomerName,
    string? Address,
    string? Tin,
    string? BusinessStyle,
    long? ExpectedVersion);

public sealed record OperatorConsoleInvoiceCustomerInformationResponse(
    Guid ParkingSessionId,
    bool HasCustomerInformation,
    string? CustomerName,
    string? Address,
    string? Tin,
    string? BusinessStyle,
    long? RowVersion,
    DateTimeOffset? UpdatedAt,
    Guid CorrelationId);
