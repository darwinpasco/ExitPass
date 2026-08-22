// BRD requirement implemented: Platform operability baseline for service availability and health visibility.
// SDD section correspondence: Runtime services, deployment topology, and observability baseline.
// System invariant enforced: A service must expose machine-readable liveness and readiness endpoints.

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ExitPass.VendorPmsAdapter.Api.Configuration;
using ExitPass.VendorPmsAdapter.Api.Endpoints;
using ExitPass.VendorPmsAdapter.Api.Health;
using ExitPass.VendorPmsAdapter.Api.Security;
using ExitPass.VendorPmsAdapter.Application.Parking;
using ExitPass.VendorPmsAdapter.Application.Routing;
using ExitPass.VendorPmsAdapter.Infrastructure.HikCentral;
using ExitPass.VendorPmsAdapter.Infrastructure.Projection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var siteAdapterOptions = builder.Configuration.GetSection(SiteAdapterRuntimeOptions.SectionName)
    .Get<SiteAdapterRuntimeOptions>() ?? new SiteAdapterRuntimeOptions();
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = siteAdapterOptions.MaxRequestBodyBytes);
builder.Services.AddSingleton(siteAdapterOptions);
builder.Services.AddSingleton(new SiteAdapterBinding(
    siteAdapterOptions.SiteId,
    siteAdapterOptions.SiteGroupId,
    siteAdapterOptions.VendorSystemId,
    siteAdapterOptions.AdapterIdentityId,
    siteAdapterOptions.AllowedCentralPmsServiceIdentityId,
    siteAdapterOptions.ParkingLotIndexCode ?? string.Empty,
    siteAdapterOptions.Environment ?? string.Empty,
    siteAdapterOptions.Activated));
builder.Services.AddSingleton<SiteAdapterBindingGuard>();
builder.Services.AddSingleton<IHikCentralRequestSigner>(_ => new HikCentralRequestSigner(
    new HikCentralCredentialOptions(
        SiteAdapterRuntimeOptions.ReadSecret(siteAdapterOptions.HikCentralAppKeyFile!, siteAdapterOptions.SecretMountRoot!),
        SiteAdapterRuntimeOptions.ReadSecret(siteAdapterOptions.HikCentralAppSecretFile!, siteAdapterOptions.SecretMountRoot!))));
builder.Services.AddSingleton<IVendorParkingDataClient>(serviceProvider => new HikCentralParkingClient(
    new HttpClient { BaseAddress = new Uri(siteAdapterOptions.HikCentralBaseUrl!, UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(siteAdapterOptions.TimeoutSeconds) },
    serviceProvider.GetRequiredService<IHikCentralRequestSigner>(),
    siteAdapterOptions.HikCentralUserId ?? string.Empty,
    confirmPaymentEnabled: siteAdapterOptions.ConfirmPaymentEnabled,
    serviceProvider.GetService<ILogger<HikCentralParkingClient>>()));
builder.Services.AddSingleton<IHikCentralPassagewayRecordClient>(serviceProvider =>
    new HikCentralPassagewayRecordClient(
        new HttpClient { BaseAddress = new Uri(siteAdapterOptions.HikCentralBaseUrl!, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(siteAdapterOptions.TimeoutSeconds) },
        serviceProvider.GetRequiredService<IHikCentralRequestSigner>(),
        siteAdapterOptions.HikCentralUserId ?? string.Empty,
        serviceProvider.GetService<ILogger<HikCentralPassagewayRecordClient>>(),
        siteAdapterOptions.RequestTimeZoneId));
builder.Services.AddScoped<IResolveVendorParkingSessionUseCase, ResolveVendorParkingSessionHandler>();
builder.Services.AddScoped<IResolveVendorTariffQuoteUseCase, ResolveVendorTariffQuoteHandler>();
builder.Services.AddScoped<IConfirmVendorParkingFeeUseCase, ConfirmVendorParkingFeeHandler>();
builder.Services.AddScoped<HikCentralPassagewaySyncUseCase>();

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Service is alive."))
    .AddCheck<SiteAdapterReadinessHealthCheck>("site_adapter_binding");

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "SecureDevelopment")
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseMiddleware<ControlledAdapterExceptionMiddleware>();
app.UseMiddleware<CentralPmsServiceAuthenticationMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapVendorAdapterEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true
});

app.Run();

public partial class Program
{
}
