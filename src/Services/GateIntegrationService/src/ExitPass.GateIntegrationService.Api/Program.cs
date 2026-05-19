// BRD requirement implemented: Platform operability baseline for service availability and health visibility.
// SDD section correspondence: Runtime services, deployment topology, and observability baseline.
// System invariant enforced: A service must expose machine-readable liveness and readiness endpoints.

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ExitPass.GateIntegrationService.Api.Endpoints;
using ExitPass.GateIntegrationService.Application.GateExit;
using ExitPass.GateIntegrationService.Infrastructure.CentralPms;
using ExitPass.GateIntegrationService.Infrastructure.GateExit;
using ExitPass.GateIntegrationService.Infrastructure.GateHardware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
});

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Gate Integration Service is alive."));

builder.Services.AddScoped<IConsumeGateExitAuthorizationUseCase, ConsumeGateExitAuthorizationHandler>();
builder.Services.AddScoped<ICentralPmsExitAuthorizationClient>(_ =>
{
    var baseUrl = builder.Configuration["Integrations:CentralPms:BaseUrl"] ?? "http://localhost:8080";
    return new HttpCentralPmsExitAuthorizationClient(new HttpClient
    {
        BaseAddress = new Uri(baseUrl, UriKind.Absolute),
        Timeout = TimeSpan.FromSeconds(10)
    });
});
builder.Services.AddSingleton<IGateHardwareController, NoOpGateHardwareController>();
builder.Services.AddSingleton<IGateExitAttemptRecorder, InMemoryGateExitAttemptRecorder>();

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "SecureDevelopment")
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ExitPass Gate Integration Service API v1");
    });
}

app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapGateExitAuthorizationEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = _ => true
});

app.MapGet("/", () => Results.Ok(new
{
    service = "ExitPass Gate Integration Service API",
    status = "running"
}));
app.Run();

/// <summary>
/// Entry point marker used by ASP.NET Core integration tests.
/// </summary>
public partial class Program
{
}
