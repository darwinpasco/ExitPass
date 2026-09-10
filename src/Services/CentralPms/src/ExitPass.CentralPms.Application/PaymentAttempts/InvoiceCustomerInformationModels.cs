namespace ExitPass.CentralPms.Application.PaymentAttempts;

/// <summary>
/// Immutable Sales Invoice customer information bound to a canonical payment attempt.
/// </summary>
/// <remarks>
/// TODO: Confirm exact BRD v1.2 and SDD v1.2 section references before merging.
/// Authority boundary: customer information is fiscal presentation data only and cannot change tariff,
/// payment finality, statutory entitlement, or ExitAuthorization decisions.
/// </remarks>
public sealed record InvoiceCustomerInformation(
    string? CustomerName,
    string? Address,
    string? Tin,
    string? BusinessStyle,
    string? StatutoryIdNumber);

public interface IInvoiceCustomerInformationStore
{
    Task SaveImmutableAsync(
        Guid paymentAttemptId,
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        InvoiceCustomerInformation information,
        CancellationToken cancellationToken);
}
