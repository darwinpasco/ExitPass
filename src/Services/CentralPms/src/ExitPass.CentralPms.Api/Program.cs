// BRD requirement implemented:
// - 9.10 Payment Processing and Confirmation
// - 9.12 Exit Authorization
// - 9.16 Monitoring and Administration
// - 9.21 Audit and Traceability
//
// SDD sections:
// - 4 Runtime Services
// - 6.3 Initiate Payment Attempt
// - 6.4 Finalize Payment
// - 6.5 Issue Exit Authorization
// - 6.6 Consume Exit Authorization
// - 14 Observability
//
// System invariants enforced:
// - Every request must be correlation-aware
// - Telemetry must never affect business logic
// - Service must expose liveness and readiness endpoints
// - Only Central PMS may finalize PaymentAttempt state
// - ExitAuthorization issuance and consumption must remain DB-backed and deterministic

using System.Diagnostics;
using ExitPass.CentralPms.Api.Endpoints;
using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Observability;
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
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

const string ServiceName = "ExitPass.CentralPms.Api";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var mainDatabaseConnectionString =
    builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.");

var otlpEndpoint = builder.Configuration["Observability:Otlp:Endpoint"];
var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

ConfigureLogging(builder, otlpEndpoint, serviceVersion);
ConfigureOpenTelemetry(builder, otlpEndpoint, serviceVersion);
ConfigureHealthChecks(builder);
ConfigureApplicationServices(builder, mainDatabaseConnectionString);

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "SecureDevelopment")
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ExitPass Central PMS API v1");
    });
}

app.Use(CorrelationMiddleware);

app.UseRouting();
app.UseAuthorization();

app.UseHttpMetrics();

app.MapMetrics("/metrics");
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
app.MapInternalPaymentOutcomeEndpoints();
app.MapInternalPaymentAttemptFinalizationEndpoints();
app.MapInternalPaymentAttemptExitAuthorizationEndpoints();
app.MapGateExitAuthorizationConsumeEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "ExitPass Central PMS API",
    status = "running"
}));

app.Run();

/// <summary>
/// Configures application logging, including OpenTelemetry log export.
/// </summary>
/// <param name="builder">The web application builder.</param>
/// <param name="otlpEndpoint">The optional OTLP endpoint.</param>
/// <param name="serviceVersion">The service version string.</param>
static void ConfigureLogging(
    WebApplicationBuilder builder,
    string? otlpEndpoint,
    string serviceVersion)
{
    builder.Logging.ClearProviders();

    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);

    builder.Logging.AddConsole(options =>
    {
        options.IncludeScopes = true;
    });

    builder.Logging.Configure(options =>
    {
        options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId |
            ActivityTrackingOptions.SpanId |
            ActivityTrackingOptions.ParentId |
            ActivityTrackingOptions.Tags |
            ActivityTrackingOptions.Baggage;
    });

    builder.Logging.AddOpenTelemetry(options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
        options.ParseStateValues = true;

        options.SetResourceBuilder(
            ResourceBuilder.CreateDefault().AddService(
                serviceName: ServiceName,
                serviceVersion: serviceVersion));

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            options.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = new Uri(otlpEndpoint);
            });
        }
    });
}

/// <summary>
/// Configures OpenTelemetry tracing and metrics for Central PMS.
/// </summary>
/// <param name="builder">The web application builder.</param>
/// <param name="otlpEndpoint">The optional OTLP endpoint.</param>
/// <param name="serviceVersion">The service version string.</param>
static void ConfigureOpenTelemetry(
    WebApplicationBuilder builder,
    string? otlpEndpoint,
    string serviceVersion)
{
    builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            serviceName: ServiceName,
            serviceVersion: serviceVersion))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("ExitPass.CentralPms.Api")
                .AddSource("ExitPass.CentralPms.Api.PaymentAttempts")
                .AddSource("ExitPass.CentralPms.Api.InternalPaymentAttempts")
                .AddSource("ExitPass.CentralPms.Application.PaymentAttempts")
                .AddSource("ExitPass.CentralPms.Application.Payments")
                .AddSource("ExitPass.CentralPms.Infrastructure.Payments")
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequest = (activity, request) =>
                    {
                        if (request.Headers.TryGetValue("X-Correlation-Id", out var correlationId))
                        {
                            activity.SetTag("correlation_id", correlationId.ToString());
                        }

                        activity.SetTag("http.request.method", request.Method);
                        activity.SetTag("url.path", request.Path.Value);
                    };

                    options.EnrichWithHttpResponse = (activity, response) =>
                    {
                        activity.SetTag("http.response.status_code", response.StatusCode);
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                });

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
                .AddMeter("ExitPass.CentralPms.Application.PaymentAttempts")
                .AddMeter("ExitPass.CentralPms.Application.Payments")
                .AddMeter(CentralPmsMetrics.MeterName)
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
}

/// <summary>
/// Configures liveness and readiness health checks.
/// </summary>
/// <param name="builder">The web application builder.</param>
static void ConfigureHealthChecks(WebApplicationBuilder builder)
{
    builder.Services
        .AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Central PMS Service is alive."));
}

/// <summary>
/// Registers application, infrastructure, validation, and observability services.
/// </summary>
/// <param name="builder">The web application builder.</param>
/// <param name="mainDatabaseConnectionString">The main database connection string.</param>
static void ConfigureApplicationServices(
    WebApplicationBuilder builder,
    string mainDatabaseConnectionString)
{
    builder.Services.AddScoped<ICreateOrReusePaymentAttemptUseCase, CreateOrReusePaymentAttemptHandler>();
    builder.Services.AddScoped<IProviderHandoffFactory, ProviderHandoffFactory>();
    builder.Services.AddScoped<IPaymentAttemptCreationPolicy, PaymentAttemptCreationPolicy>();

    builder.Services.AddScoped<CreatePaymentAttemptRequestValidator>();
    builder.Services.AddScoped<CreatePaymentAttemptHeadersValidator>();

    builder.Services.AddScoped<IParkingSessionReadRepository, ParkingSessionReadRepository>();
    builder.Services.AddScoped<ITariffSnapshotReadRepository, TariffSnapshotReadRepository>();

    builder.Services.AddScoped<IPaymentAttemptDbRoutineGateway>(_ =>
        new PaymentAttemptDbRoutineGateway(mainDatabaseConnectionString));

    builder.Services.AddScoped<IRecordPaymentConfirmationGateway>(_ =>
        new RecordPaymentConfirmationGateway(mainDatabaseConnectionString));

    builder.Services.AddScoped<RecordPaymentConfirmationService>();

    builder.Services.AddScoped<IReportVerifiedPaymentOutcomeUseCase, ReportVerifiedPaymentOutcomeHandler>();

    builder.Services.AddScoped<IFinalizePaymentAttemptUseCase, FinalizePaymentAttemptHandler>();
    builder.Services.AddScoped<IFinalizePaymentAttemptGateway>(_ =>
        new FinalizePaymentAttemptGateway(mainDatabaseConnectionString));

    builder.Services.AddScoped<IIssueExitAuthorizationUseCase, IssueExitAuthorizationHandler>();
    builder.Services.AddScoped<IIssueExitAuthorizationGateway>(serviceProvider =>
        new IssueExitAuthorizationGateway(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<IssueExitAuthorizationGateway>>()));

    builder.Services.AddScoped<IConsumeExitAuthorizationUseCase, ConsumeExitAuthorizationHandler>();
    builder.Services.AddScoped<IConsumeExitAuthorizationGateway>(serviceProvider =>
        new ConsumeExitAuthorizationGateway(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<ConsumeExitAuthorizationGateway>>()));

    builder.Services.AddSingleton<CentralPmsMetrics>();
    builder.Services.AddSingleton<ISystemClock, SystemClock>();
}

/// <summary>
/// Correlation-aware middleware that enriches logs and activities with a stable request correlation identifier.
/// </summary>
/// <param name="context">The HTTP context.</param>
/// <param name="next">The next middleware delegate.</param>
/// <returns>A task representing the asynchronous middleware execution.</returns>
static async Task CorrelationMiddleware(HttpContext context, Func<Task> next)
{
    var path = context.Request.Path.Value;

    var isInfrastructureNoisePath =
        string.Equals(path, "/metrics", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "/health/live", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(path, "/health/ready", StringComparison.OrdinalIgnoreCase);

    if (isInfrastructureNoisePath)
    {
        await next();
        return;
    }

    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    var correlationId =
        context.Request.Headers.TryGetValue("X-Correlation-Id", out var headerValue) &&
        !string.IsNullOrWhiteSpace(headerValue)
            ? headerValue.ToString()
            : Guid.NewGuid().ToString();

    context.Response.Headers["X-Correlation-Id"] = correlationId;

    if (Activity.Current is not null)
    {
        Activity.Current.SetTag("correlation_id", correlationId);
        Activity.Current.AddBaggage("correlation_id", correlationId);
    }

    using var scope = logger.BeginScope(new Dictionary<string, object?>
    {
        ["correlation_id"] = correlationId,
        ["service_name"] = ServiceName,
        ["request_method"] = context.Request.Method,
        ["request_path"] = context.Request.Path.Value,
        ["request_host"] = context.Request.Host.Value,
        ["trace_id"] = Activity.Current?.TraceId.ToString(),
        ["span_id"] = Activity.Current?.SpanId.ToString()
    });

    try
    {
        logger.LogInformation("Central PMS request started.");

        await next();

        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "Central PMS request completed with server error status code {StatusCode}.",
                context.Response.StatusCode);
        }
        else if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            logger.LogWarning(
                "Central PMS request completed with client error status code {StatusCode}.",
                context.Response.StatusCode);
        }
        else
        {
            logger.LogInformation(
                "Central PMS request completed successfully with status code {StatusCode}.",
                context.Response.StatusCode);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception reached Central PMS API boundary.");
        throw;
    }
}

/// <summary>
/// Program entry point marker used for integration testing and web application factory discovery.
///
/// BRD:
/// - 9.16 Monitoring and Administration
///
/// SDD:
/// - Runtime services
/// - Deployment topology
///
/// Invariants Enforced:
/// - API host remains discoverable by integration test infrastructure
/// - Host composition is centralized in a single application entry point
/// </summary>
public partial class Program
{
}
