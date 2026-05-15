namespace ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;

/// <summary>
/// Signs HikCentral Professional OpenAPI requests using AK/SK digest authentication.
/// </summary>
public interface IHikCentralRequestSigner
{
    /// <summary>
    /// Adds the HikCentral AK/SK authentication headers to the request.
    /// </summary>
    /// <param name="request">Outbound HikCentral request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SignAsync(HttpRequestMessage request, CancellationToken cancellationToken);
}
