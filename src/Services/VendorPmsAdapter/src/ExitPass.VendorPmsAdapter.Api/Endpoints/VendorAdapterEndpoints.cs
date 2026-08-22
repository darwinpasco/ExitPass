using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Application.Routing;
using ExitPass.VendorPmsAdapter.Api.Configuration;
using ExitPass.VendorPmsAdapter.Contracts.Operations;
using ExitPass.VendorPmsAdapter.Contracts.Parking;
using ExitPass.VendorPmsAdapter.Contracts.Projection;
using ExitPass.VendorPmsAdapter.Infrastructure.Projection;

namespace ExitPass.VendorPmsAdapter.Api.Endpoints;

public static class VendorAdapterEndpoints
{
    public static IEndpointRouteBuilder MapVendorAdapterEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/vendor");
        group.MapGet("/identity", (SiteAdapterBinding binding, SiteAdapterRuntimeOptions options,
            IWebHostEnvironment environment) =>
        {
            var errors = options.Validate(environment.EnvironmentName);
            var capabilities = new List<string>
            {
                "SESSION_RESOLUTION", "TARIFF_CALCULATION", "PASSAGEWAY_SYNCHRONIZATION"
            };
            if (options.ConfirmPaymentEnabled) capabilities.Add("PAYMENT_CONFIRMATION");
            return Results.Ok(new VendorAdapterIdentityResponse(
                binding.SiteId, binding.SiteGroupId, binding.VendorSystemId, binding.AdapterIdentityId,
                binding.ParkingLotIndexCode, "HIKCENTRAL", binding.Environment, binding.Activated,
                errors.Count == 0, capabilities, errors.FirstOrDefault()));
        });
        group.MapPost("/sessions/resolve", async (VendorParkingSessionLookupRequest request,
            SiteAdapterBindingGuard guard, SiteAdapterBinding binding, IResolveVendorParkingSessionUseCase useCase,
            CancellationToken token) => { guard.EnsureCompatible(request.Context); return Results.Ok(
                (await useCase.ExecuteAsync(request, token)) with { AdapterContext = binding.ToResponseContext() }); });
        group.MapPost("/tariffs/calculate", async (VendorTariffQuoteRequest request,
            SiteAdapterBindingGuard guard, SiteAdapterBinding binding, IResolveVendorTariffQuoteUseCase useCase,
            CancellationToken token) => { guard.EnsureCompatible(request.Context); return Results.Ok(
                (await useCase.ExecuteAsync(request, token)) with { AdapterContext = binding.ToResponseContext() }); });
        group.MapPost("/parking-fees/confirm", async (VendorParkingFeeConfirmationRequest request,
            SiteAdapterBindingGuard guard, SiteAdapterBinding binding, IConfirmVendorParkingFeeUseCase useCase,
            CancellationToken token) => { guard.EnsureCompatible(request.Context);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return Results.BadRequest(
                    new { code = "VENDOR_CONFIRMATION_IDEMPOTENCY_KEY_REQUIRED", request.CorrelationId });
                return Results.Ok((await useCase.ExecuteAsync(request, token)) with
                    { AdapterContext = binding.ToResponseContext() }); });
        group.MapPost("/passageway-records/synchronize", async (VendorPassagewaySyncRequest request,
            HikCentralPassagewaySyncUseCase useCase, CancellationToken token) =>
            Results.Ok(await useCase.ExecuteAsync(request, token)));
        return endpoints;
    }
}
