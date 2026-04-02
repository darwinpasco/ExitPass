// BRD requirement implemented: Platform operability baseline for service availability, health visibility,
// and observability export to the centralized telemetry pipeline.
//
// SDD section correspondence:
// - Runtime services
// - Deployment topology
// - Observability baseline
//
// System invariant enforced:
// - A service must expose machine-readable liveness and readiness endpoints.
// - Telemetry emission must not change business behavior.
// - Service HTTP activity must be exportable to the platform observability pipeline.

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
});

var otlpEndpoint = builder.Configuration["Observability:Otlp:Endpoint"];

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Payment Orchestrator Service is alive."));

// BRD:
// - 9.16 Monitoring and Administration
//
// SDD:
// - 14 Observability
//
// Invariants Enforced:
// - Telemetry export is passive and must not alter domain behavior
// - Service ingress activity must remain observable at the platform level
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "ExitPass.PaymentOrchestrator.Api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation();

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "SecureDevelopment")
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Orchestrator Service API v1");
    });
}

app.UseRouting();
app.UseAuthorization();

app.MapControllers();

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
    service = "ExitPass Payment Orchestrator API",
    status = "running"
}));

app.Run();

public partial class Program
{
}
