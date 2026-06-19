namespace ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;

/// <summary>
/// Read-only operations monitoring service for Vendor PMS payment acknowledgments.
/// </summary>
public interface IVendorPaymentAcknowledgmentOpsService
{
    /// <summary>Searches durable Vendor PMS payment acknowledgments for ops monitoring.</summary>
    Task<VendorPaymentAcknowledgmentSearchResult> SearchAsync(
        SearchVendorPaymentAcknowledgmentsQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reads one durable Vendor PMS payment acknowledgment for ops monitoring.</summary>
    Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
        Guid vendorPaymentAcknowledgmentId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default read-only operations monitoring service for Vendor PMS payment acknowledgments.
/// </summary>
public sealed class VendorPaymentAcknowledgmentOpsService : IVendorPaymentAcknowledgmentOpsService
{
    private static readonly IReadOnlySet<string> ValidStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        VendorPaymentAcknowledgmentStatuses.Pending,
        VendorPaymentAcknowledgmentStatuses.Confirmed,
        VendorPaymentAcknowledgmentStatuses.Failed,
        VendorPaymentAcknowledgmentStatuses.SkippedDisabled,
        VendorPaymentAcknowledgmentStatuses.RetryPending,
        VendorPaymentAcknowledgmentStatuses.Cancelled
    };

    private readonly IVendorPaymentAcknowledgmentRepository _repository;

    /// <summary>
    /// Creates a read-only Vendor PMS payment acknowledgment ops service.
    /// </summary>
    public VendorPaymentAcknowledgmentOpsService(IVendorPaymentAcknowledgmentRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<VendorPaymentAcknowledgmentSearchResult> SearchAsync(
        SearchVendorPaymentAcknowledgmentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var status = NormalizeStatus(query.AcknowledgmentStatus);
        if (!string.IsNullOrWhiteSpace(status) && !ValidStatuses.Contains(status))
        {
            throw new ArgumentException(
                $"AcknowledgmentStatus must be one of: {string.Join(", ", ValidStatuses)}.",
                nameof(query));
        }

        if (query.CreatedFrom.HasValue &&
            query.CreatedTo.HasValue &&
            query.CreatedFrom.Value > query.CreatedTo.Value)
        {
            throw new ArgumentException("CreatedFrom must be earlier than or equal to CreatedTo.", nameof(query));
        }

        if (query.LastAttemptedFrom.HasValue &&
            query.LastAttemptedTo.HasValue &&
            query.LastAttemptedFrom.Value > query.LastAttemptedTo.Value)
        {
            throw new ArgumentException("LastAttemptedFrom must be earlier than or equal to LastAttemptedTo.", nameof(query));
        }

        var normalized = query with
        {
            AcknowledgmentStatus = status,
            VendorSystemCode = NormalizeString(query.VendorSystemCode),
            TicketNumber = NormalizeString(query.TicketNumber),
            CardNum = NormalizeString(query.CardNum),
            PageIndex = Math.Min(Math.Max(0, query.PageIndex), 10000),
            PageSize = query.PageSize <= 0 ? 25 : Math.Min(query.PageSize, 100),
            UtcNow = query.UtcNow.ToUniversalTime()
        };

        return _repository.SearchAsync(normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<VendorPaymentAcknowledgmentRecord?> ReadAsync(
        Guid vendorPaymentAcknowledgmentId,
        CancellationToken cancellationToken) =>
        _repository.ReadAsync(vendorPaymentAcknowledgmentId, cancellationToken);

    private static string? NormalizeStatus(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
