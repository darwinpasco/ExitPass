// BRD requirement implemented: Platform operability baseline for service availability, health visibility,
// and observability export to the centralized telemetry pipeline.
//
// SDD section correspondence:
// - Runtime services
// - Deployment topology
// - Observability baseline
// - 10.5.1 Initiate Provider Payment
//
// System invariant enforced:
// - A service must expose machine-readable liveness and readiness endpoints.
// - Telemetry emission must not change business behavior.
// - Service HTTP activity must be exportable to the platform observability pipeline.
// - Provider session initiation is owned by POA and must not finalize PaymentAttempt state.

using ExitPass.PaymentOrchestrator.Api.Endpoints;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// BRD:
// - 12 Payment Orchestration
//
// SDD:
// - 4.2.7 Payment Orchestrator
// - 10.5.1 Initiate Provider Payment
//
// Invariants Enforced:
// - Provider credentials and transport configuration must be externalized.
// - Provider adapter resolution must be explicit and deterministic.
builder.Services.Configure<PayMongoOptions>(
    builder.Configuration.GetSection(PayMongoOptions.SectionName));

builder.Services.AddHttpClient<PayMongoClient>();

builder.Services.AddScoped<IPaymentProviderAdapter, PayMongoCheckoutAdapter>();
builder.Services.AddScoped<IPaymentProviderRegistry, PaymentProviderRegistry>();

// Application handlers for the current MVP slice.
builder.Services.AddScoped<InitiateProviderPaymentHandler>();

// IMPORTANT:
// The initiate handler depends on IProviderSessionRepository.
// Register your real infrastructure implementation here once the repository class exists.
//
// Example:
// builder.Services.AddScoped<IProviderSessionRepository, ProviderSessionRepository>();
//
// Do not register a fake repository in production code just to satisfy DI.

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

// Minimal API endpoints for the current slice.
app.MapInternalPaymentEndpoints();

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

/// <summary>
/// Partial program class for integration testing support.
/// </summary>
public partial class Program
{
}
