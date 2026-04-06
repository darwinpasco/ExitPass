// BRD requirement implemented: Platform operability baseline for service availability, health visibility,
// provider payment initiation ingress, provider webhook ingress, and observability export to the centralized
// telemetry pipeline.
//
// SDD section correspondence:
// - Runtime services
// - Deployment topology
// - Observability baseline
// - 10.5.1 Initiate Provider Payment
// - 10.5.2 Payment Provider Webhook
// - 10.5.3 Report Verified Payment Outcome
//
// System invariant enforced:
// - A service must expose machine-readable liveness and readiness endpoints.
// - Telemetry emission must not change business behavior.
// - Service HTTP activity must be exportable to the platform observability pipeline.
// - Provider session initiation is owned by POA and must not finalize PaymentAttempt state.
// - Provider webhooks must enter the platform only through POA-owned ingress.
// - Verified provider outcomes must be reported to Central PMS through an internal boundary.

using ExitPass.PaymentOrchestrator.Api.Endpoints;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Integrations;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.InitiateProviderPayment;
using ExitPass.PaymentOrchestrator.Application.UseCases.VerifyProviderWebhook;
using ExitPass.PaymentOrchestrator.Infrastructure.Integrations;
using ExitPass.PaymentOrchestrator.Infrastructure.Persistence;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers;
using ExitPass.PaymentOrchestrator.Infrastructure.Providers.PayMongo;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Information);
builder.Logging.AddFilter("Microsoft.Hosting", LogLevel.Information);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);

var otlpEndpoint = builder.Configuration["Observability:Otlp:Endpoint"];

builder.Logging.AddOpenTelemetry(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
    options.ParseStateValues = true;

    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
    {
        options.AddOtlpExporter(exporterOptions =>
        {
            exporterOptions.Endpoint = new Uri(otlpEndpoint);
        });
    }
});

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
// - Telemetry export is passive and must not alter domain behavior.
// - Service ingress activity must remain observable at the platform level.
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
// - 10.5.2 Payment Provider Webhook
// - 10.5.3 Report Verified Payment Outcome
//
// Invariants Enforced:
// - Provider credentials and transport configuration must be externalized.
// - Provider adapter resolution must be explicit and deterministic.
// - Webhook event evidence and verified outcome reporting must use concrete infrastructure.
builder.Services.Configure<PayMongoOptions>(
    builder.Configuration.GetSection(PayMongoOptions.SectionName));

builder.Services.AddHttpClient<PayMongoClient>();
builder.Services.AddHttpClient<ICentralPmsPaymentOutcomeReporter, CentralPmsPaymentOutcomeReporter>();

builder.Services.AddScoped<IPaymentProviderAdapter, PayMongoCheckoutAdapter>();
builder.Services.AddScoped<IPaymentProviderRegistry, PaymentProviderRegistry>();

// Enable this when the implementation exists and matches IProviderSessionRepository exactly.
builder.Services.AddScoped<IProviderSessionRepository, ProviderSessionRepository>();

builder.Services.AddScoped<IProviderWebhookEventRepository, ProviderWebhookEventRepository>();

builder.Services.AddScoped<InitiateProviderPaymentHandler>();
builder.Services.AddScoped<VerifyProviderWebhookHandler>();

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

app.MapInternalPaymentEndpoints();
app.MapProviderWebhookEndpoints();

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
