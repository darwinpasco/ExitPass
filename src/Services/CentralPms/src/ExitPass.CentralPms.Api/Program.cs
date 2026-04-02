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

using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Infrastructure.Common;
using ExitPass.CentralPms.Infrastructure.PaymentAttempts;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
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

var mainDatabaseConnectionString =
    builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");

var otlpEndpoint = builder.Configuration["Observability:Otlp:Endpoint"];

// BRD:
// - 9.9 Payment Initiation
// - 18.3 Payment Initiation
//
// SDD:
// - 6.3 Initiate Payment Attempt
// - 10.2.4 Initiate Payment Attempt
//
// Invariants Enforced:
// - Central PMS owns PaymentAttempt creation and reuse
// - PaymentAttempt creation must go through the DB-backed control path
// - TariffSnapshot eligibility must be validated before PaymentAttempt creation
builder.Services.AddScoped<ICreateOrReusePaymentAttemptUseCase, CreateOrReusePaymentAttemptHandler>();
builder.Services.AddScoped<IProviderHandoffFactory, ProviderHandoffFactory>();
builder.Services.AddScoped<IPaymentAttemptCreationPolicy, PaymentAttemptCreationPolicy>();

builder.Services.AddScoped<CreatePaymentAttemptRequestValidator>();
builder.Services.AddScoped<CreatePaymentAttemptHeadersValidator>();

builder.Services.AddScoped<IParkingSessionReadRepository, ParkingSessionReadRepository>();
builder.Services.AddScoped<ITariffSnapshotReadRepository, TariffSnapshotReadRepository>();

builder.Services.AddScoped<IPaymentAttemptDbRoutineGateway>(_ =>
    new PaymentAttemptDbRoutineGateway(mainDatabaseConnectionString));

// BRD:
// - 9.10 Payment Processing and Confirmation
// - 9.13 Timeout, Retry, and Duplicate Handling
//
// SDD:
// - 6.4 Finalize Payment
// - 10.5.3 Report Verified Payment Outcome
//
// Invariants Enforced:
// - Only Central PMS may persist canonical PaymentConfirmation records
// - Only Central PMS may finalize PaymentAttempt state
// - Payment confirmation persistence must use the canonical Central PMS database path
builder.Services.AddScoped<IRecordPaymentConfirmationGateway>(_ =>
    new RecordPaymentConfirmationGateway(mainDatabaseConnectionString));

builder.Services.AddScoped<RecordPaymentConfirmationService>();

builder.Services.AddScoped<IFinalizePaymentAttemptUseCase, FinalizePaymentAttemptHandler>();

builder.Services.AddScoped<IFinalizePaymentAttemptGateway>(_ =>
    new FinalizePaymentAttemptGateway(mainDatabaseConnectionString));

// BRD:
// - 9.12 Exit Authorization
// - 9.13 Timeout, Retry, and Duplicate Handling
//
// SDD:
// - 6.5 Issue Exit Authorization
// - 10.6 Internal Service APIs
//
// Invariants Enforced:
// - Only Central PMS may issue ExitAuthorization
// - ExitAuthorization issuance must use the canonical DB-backed control path
// - ExitAuthorization may only be issued from confirmed payment finality
builder.Services.AddScoped<IIssueExitAuthorizationUseCase, IssueExitAuthorizationHandler>();

builder.Services.AddScoped<IIssueExitAuthorizationGateway>(_ =>
    new IssueExitAuthorizationGateway(mainDatabaseConnectionString));

builder.Services.AddSingleton<ISystemClock, SystemClock>();

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Central PMS Service is alive."));

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
        serviceName: "ExitPass.CentralPms.Api",
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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ExitPass Central PMS API v1");
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

app.MapInternalPaymentConfirmationEndpoints();
app.MapInternalPaymentAttemptFinalizationEndpoints();
app.MapInternalPaymentAttemptExitAuthorizationEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "ExitPass Central PMS API",
    status = "running"
}));

app.Run();

public partial class Program
{
}
