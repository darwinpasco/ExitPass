using System.Net.Http.Json;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorParking.Routing;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Routing;

namespace ExitPass.CentralPms.Infrastructure.VendorParking.Routing;

/// <summary>Routes Central PMS provider-neutral requests to exactly one effective Site Adapter.</summary>
public sealed class SiteVendorAdapterHttpClient(
    HttpClient httpClient,
    ISiteVendorAdapterRouteRegistry routes,
    ISiteAdapterCredentialResolver credentials,
    Guid centralPmsServiceIdentityId,
    bool allowTaskOwnedHttp) : IVendorPmsParkingResolutionClient
{
    public Task<VendorParkingSessionLookupResponse> ResolveSessionAsync(VendorParkingSessionLookupRequest request,
        CancellationToken cancellationToken) => SendAsync<VendorParkingSessionLookupRequest, VendorParkingSessionLookupResponse>(request, "/v1/vendor/sessions/resolve",
            request.Context, cancellationToken);

    public Task<VendorTariffQuoteResponse> ResolveTariffAsync(VendorTariffQuoteRequest request,
        CancellationToken cancellationToken) => SendAsync<VendorTariffQuoteRequest, VendorTariffQuoteResponse>(request, "/v1/vendor/tariffs/calculate",
            request.Context, cancellationToken);

    public Task<VendorParkingFeeConfirmationResponse> ConfirmParkingFeeAsync(
        VendorParkingFeeConfirmationRequest request, CancellationToken cancellationToken) =>
        SendAsync<VendorParkingFeeConfirmationRequest, VendorParkingFeeConfirmationResponse>(request,
            "/v1/vendor/parking-fees/confirm", request.Context, cancellationToken);

    private async Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, string path,
        VendorAdapterRequestContext? requestedContext, CancellationToken cancellationToken)
    {
        if (requestedContext is null)
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_SCOPE_REQUIRED");
        var route = await routes.ResolveAsync(requestedContext.SiteId, requestedContext.SiteGroupId,
            requestedContext.VendorSystemId == Guid.Empty ? null : requestedContext.VendorSystemId, cancellationToken);
        if (requestedContext.AdapterIdentityId != Guid.Empty &&
            requestedContext.AdapterIdentityId != route.AdapterIdentityId)
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_IMMUTABLE_ROUTE_MISMATCH");
        if (route.AdapterBaseUri.Scheme != Uri.UriSchemeHttps && !allowTaskOwnedHttp)
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_PRIVATE_TLS_REQUIRED");
        var context = new VendorAdapterRequestContext(route.SiteId, route.SiteGroupId, route.VendorSystemId,
            route.AdapterIdentityId);
        var routedRequest = request switch
        {
            VendorParkingSessionLookupRequest value => (object)(value with { Context = context }),
            VendorTariffQuoteRequest value => value with { Context = context },
            VendorParkingFeeConfirmationRequest value => value with { Context = context },
            _ => throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_REQUEST_UNSUPPORTED")
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(route.AdapterBaseUri, path))
        {
            Content = JsonContent.Create(routedRequest)
        };
        message.Headers.TryAddWithoutValidation("X-ExitPass-Service-Identity", centralPmsServiceIdentityId.ToString());
        message.Headers.TryAddWithoutValidation("X-ExitPass-Adapter-Key", credentials.Resolve(route.CredentialReference));
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new SiteVendorAdapterRoutingException(response.StatusCode is System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden ? "SITE_ADAPTER_ACCESS_DENIED" : "SITE_ADAPTER_UNAVAILABLE");
        var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken)
            ?? throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_MALFORMED_RESPONSE");
        var responseContext = result switch
        {
            VendorParkingSessionLookupResponse value => value.AdapterContext,
            VendorTariffQuoteResponse value => value.AdapterContext,
            VendorParkingFeeConfirmationResponse value => value.AdapterContext,
            _ => null
        };
        if (responseContext is null || responseContext.SiteId != route.SiteId ||
            responseContext.SiteGroupId != route.SiteGroupId || responseContext.VendorSystemId != route.VendorSystemId ||
            responseContext.AdapterIdentityId != route.AdapterIdentityId)
            throw new SiteVendorAdapterRoutingException("SITE_ADAPTER_RESPONSE_BINDING_MISMATCH");
        return result;
    }
}
