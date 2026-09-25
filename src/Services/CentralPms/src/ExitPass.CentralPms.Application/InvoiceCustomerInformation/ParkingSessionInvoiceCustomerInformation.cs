namespace ExitPass.CentralPms.Application.SalesInvoiceCustomerInformation;

public static class ParkingSessionInvoiceCustomerInformationRules
{
    public const int CustomerNameMaximumLength = 160;
    public const int CustomerAddressMaximumLength = 300;
    public const int CustomerTinMaximumLength = 40;
    public const int BusinessStyleMaximumLength = 160;
}

public static class InvoiceCustomerInformationSourceChannels
{
    public const string OperatorConsole = "OPERATOR_CONSOLE";
    public const string WebPay = "WEBPAY";
    public const string AssistedPaymentTerminal = "ASSISTED_PAYMENT_TERMINAL";
}

public sealed record ParkingSessionInvoiceCustomerInformationRecord(
    Guid ParkingSessionId,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerTin,
    string? BusinessStyle,
    long RowVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record InvoiceCustomerInformationActor(
    Guid? UserId,
    Guid? ServiceIdentityId,
    string SourceChannel)
{
    public bool HasExactlyOneActor => UserId.HasValue ^ ServiceIdentityId.HasValue;
}

public sealed record InvoiceCustomerInformationScope(Guid SiteId, Guid? SiteGroupId);

public sealed record SaveParkingSessionInvoiceCustomerInformationCommand(
    Guid ParkingSessionId,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerTin,
    string? BusinessStyle,
    long? ExpectedVersion,
    InvoiceCustomerInformationActor Actor,
    InvoiceCustomerInformationScope Scope,
    Guid CorrelationId);

public enum InvoiceCustomerInformationReadStatus
{
    Found,
    NotSupplied,
    ParkingSessionNotFound,
    SourceUnavailable
}

public sealed record InvoiceCustomerInformationReadResult(
    InvoiceCustomerInformationReadStatus Status,
    ParkingSessionInvoiceCustomerInformationRecord? Record);

public enum InvoiceCustomerInformationSaveStatus
{
    Created,
    Updated,
    Unchanged,
    VersionConflict,
    FiscalSnapshotLocked,
    ParkingSessionNotFound,
    Invalid,
    SourceUnavailable
}

public sealed record InvoiceCustomerInformationSaveResult(
    InvoiceCustomerInformationSaveStatus Status,
    ParkingSessionInvoiceCustomerInformationRecord? Record,
    IReadOnlyList<string> ValidationErrors);

public interface IParkingSessionInvoiceCustomerInformationRepository
{
    Task<InvoiceCustomerInformationReadResult> ReadAsync(
        Guid parkingSessionId,
        InvoiceCustomerInformationScope scope,
        CancellationToken cancellationToken);

    Task<InvoiceCustomerInformationSaveResult> SaveAsync(
        SaveParkingSessionInvoiceCustomerInformationCommand command,
        CancellationToken cancellationToken);
}

public interface IParkingSessionInvoiceCustomerInformationService
{
    Task<InvoiceCustomerInformationReadResult> ReadAsync(
        Guid parkingSessionId,
        InvoiceCustomerInformationScope scope,
        CancellationToken cancellationToken);

    Task<InvoiceCustomerInformationSaveResult> SaveAsync(
        SaveParkingSessionInvoiceCustomerInformationCommand command,
        CancellationToken cancellationToken);
}

public sealed class ParkingSessionInvoiceCustomerInformationService(
    IParkingSessionInvoiceCustomerInformationRepository repository)
    : IParkingSessionInvoiceCustomerInformationService
{
    public Task<InvoiceCustomerInformationReadResult> ReadAsync(
        Guid parkingSessionId,
        InvoiceCustomerInformationScope scope,
        CancellationToken cancellationToken)
    {
        if (parkingSessionId == Guid.Empty || scope.SiteId == Guid.Empty)
        {
            return Task.FromResult(new InvoiceCustomerInformationReadResult(
                InvoiceCustomerInformationReadStatus.ParkingSessionNotFound,
                null));
        }

        return repository.ReadAsync(parkingSessionId, scope, cancellationToken);
    }

    public Task<InvoiceCustomerInformationSaveResult> SaveAsync(
        SaveParkingSessionInvoiceCustomerInformationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalized = command with
        {
            CustomerName = Normalize(command.CustomerName),
            CustomerAddress = Normalize(command.CustomerAddress),
            CustomerTin = Normalize(command.CustomerTin),
            BusinessStyle = Normalize(command.BusinessStyle)
        };

        var errors = Validate(normalized);
        return errors.Count > 0
            ? Task.FromResult(new InvoiceCustomerInformationSaveResult(
                InvoiceCustomerInformationSaveStatus.Invalid,
                null,
                errors))
            : repository.SaveAsync(normalized, cancellationToken);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Validate(SaveParkingSessionInvoiceCustomerInformationCommand command)
    {
        var errors = new List<string>();
        if (command.ParkingSessionId == Guid.Empty) errors.Add("parking_session_id_required");
        if (command.Scope.SiteId == Guid.Empty) errors.Add("site_scope_required");
        if (!command.Actor.HasExactlyOneActor) errors.Add("server_actor_required");
        if (command.Actor.SourceChannel is not (
            InvoiceCustomerInformationSourceChannels.OperatorConsole or
            InvoiceCustomerInformationSourceChannels.WebPay or
            InvoiceCustomerInformationSourceChannels.AssistedPaymentTerminal))
        {
            errors.Add("source_channel_invalid");
        }
        if (command.CustomerName is null && command.CustomerAddress is null &&
            command.CustomerTin is null && command.BusinessStyle is null)
        {
            errors.Add("customer_information_required");
        }
        AddLengthError(errors, command.CustomerName, ParkingSessionInvoiceCustomerInformationRules.CustomerNameMaximumLength, "customer_name_too_long");
        AddLengthError(errors, command.CustomerAddress, ParkingSessionInvoiceCustomerInformationRules.CustomerAddressMaximumLength, "customer_address_too_long");
        AddLengthError(errors, command.CustomerTin, ParkingSessionInvoiceCustomerInformationRules.CustomerTinMaximumLength, "customer_tin_too_long");
        AddLengthError(errors, command.BusinessStyle, ParkingSessionInvoiceCustomerInformationRules.BusinessStyleMaximumLength, "business_style_too_long");
        if (command.ExpectedVersion is <= 0) errors.Add("expected_version_invalid");
        return errors;
    }

    private static void AddLengthError(List<string> errors, string? value, int maximum, string error)
    {
        if (value?.Length > maximum) errors.Add(error);
    }
}
