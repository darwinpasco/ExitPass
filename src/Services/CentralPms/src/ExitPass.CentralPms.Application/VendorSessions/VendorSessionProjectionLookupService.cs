namespace ExitPass.CentralPms.Application.VendorSessions;

/// <summary>
/// Reads latest-known vendor session projection snapshots for continuity/degraded-mode visibility.
/// </summary>
public sealed class VendorSessionProjectionLookupService(
    IVendorSessionProjectionRepository repository) : IVendorSessionProjectionLookupService
{
    /// <inheritdoc />
    public async Task<VendorSessionProjectionLookupResult> LookupAsync(
        VendorSessionProjectionLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.CardNum) && string.IsNullOrWhiteSpace(query.PlateLicense))
        {
            return VendorSessionProjectionLookupResult.NotFound(query.CorrelationId);
        }

        var projection = await repository.FindLatestAsync(query, cancellationToken);
        return projection is null
            ? VendorSessionProjectionLookupResult.NotFound(query.CorrelationId)
            : VendorSessionProjectionLookupResult.FoundProjection(
                projection,
                query.RequestedAt,
                query.CorrelationId);
    }
}
