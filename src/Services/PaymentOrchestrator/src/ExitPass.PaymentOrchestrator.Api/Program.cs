using System.Diagnostics;
using System.Reflection;
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
using Microsoft.OpenApi.Models;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

const string ServiceName = "ExitPass.PaymentOrchestrator.Api";

ConfigureConfiguration(builder);
ConfigureLogging(builder);
ConfigureServices(builder);

var app = builder.Build();

ConfigureMiddleware(app);
ConfigureEndpoints(app);

app.Run();

static void ConfigureConfiguration(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    builder.Configuration.AddEnvironmentVariables();
}

static void ConfigureLogging(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    var otlpEndpoint = builder.Configuration["Observability:Otlp:Endpoint"];
    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

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

static void ConfigureServices(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    var services = builder.Services;
    var configuration = builder.Configuration;
    var otlpEndpoint = configuration["Observability:Otlp:Endpoint"];
    var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    services.AddProblemDetails();
    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(static options =>
    {
        options.SwaggerDoc(
            "v1",
            new OpenApiInfo
            {
                Title = "ExitPass Payment Orchestrator API",
                Version = "v1",
                Description = "Payment provider orchestration, webhook verification, and verified outcome reporting for ExitPass.",
            });

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    });

    services.AddHealthChecks()
        .AddCheck("self", static () => HealthCheckResult.Healthy());

    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(
            serviceName: ServiceName,
            serviceVersion: serviceVersion))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("ExitPass.PaymentOrchestrator.Api")
                .AddSource("ExitPass.PaymentOrchestrator.Application")
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

    RegisterApplicationServices(services);
    RegisterInfrastructureServices(services, configuration);
    ValidateCriticalConfiguration(configuration);
}

static void RegisterApplicationServices(IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddScoped<InitiateProviderPaymentHandler>();
    services.AddScoped<VerifyProviderWebhookHandler>();
}

static void RegisterInfrastructureServices(IServiceCollection services, IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(services);
    ArgumentNullException.ThrowIfNull(configuration);

    services.AddHttpClient<ICentralPmsPaymentOutcomeReporter, CentralPmsPaymentOutcomeReporter>(static client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    services.AddHttpClient<PayMongoClient>();

    services.AddScoped<IProviderSessionRepository, ProviderSessionRepository>();
    services.AddScoped<IProviderWebhookEventRepository, ProviderWebhookEventRepository>();

    services.AddScoped<IPaymentProviderAdapter, PayMongoCheckoutAdapter>();
    services.AddScoped<IPaymentProviderRegistry, PaymentProviderRegistry>();

    services.AddOptions<PayMongoOptions>()
        .Bind(configuration.GetSection("Payments:Providers:PayMongo"))
        .Validate(
            static options =>
                !string.IsNullOrWhiteSpace(options.SecretKey) &&
                !string.IsNullOrWhiteSpace(options.PublicKey) &&
                !string.IsNullOrWhiteSpace(options.BaseUrl),
            "Payments:Providers:PayMongo requires SecretKey, PublicKey, and BaseUrl.")
        .ValidateOnStart();
}

static void ValidateCriticalConfiguration(IConfiguration configuration)
{
    ArgumentNullException.ThrowIfNull(configuration);

    var centralPmsBaseUrl = configuration["Integrations:CentralPms:BaseUrl"];
    if (string.IsNullOrWhiteSpace(centralPmsBaseUrl))
    {
        throw new InvalidOperationException(
            "Missing required configuration value 'Integrations:CentralPms:BaseUrl'.");
    }

    var payMongoSecretKey = configuration["Payments:Providers:PayMongo:SecretKey"];
    var payMongoPublicKey = configuration["Payments:Providers:PayMongo:PublicKey"];
    var payMongoBaseUrl = configuration["Payments:Providers:PayMongo:BaseUrl"];

    if (string.IsNullOrWhiteSpace(payMongoSecretKey))
    {
        throw new InvalidOperationException(
            "Missing required configuration value 'Payments:Providers:PayMongo:SecretKey'. Supply it through environment variables or another external configuration provider.");
    }

    if (string.IsNullOrWhiteSpace(payMongoPublicKey))
    {
        throw new InvalidOperationException(
            "Missing required configuration value 'Payments:Providers:PayMongo:PublicKey'. Supply it through environment variables or another external configuration provider.");
    }

    if (string.IsNullOrWhiteSpace(payMongoBaseUrl))
    {
        throw new InvalidOperationException(
            "Missing required configuration value 'Payments:Providers:PayMongo:BaseUrl'. Supply it through environment variables or another external configuration provider.");
    }
}

static void ConfigureMiddleware(WebApplication app)
{
    ArgumentNullException.ThrowIfNull(app);

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    else
    {
        app.UseHttpsRedirection();
    }

    app.Use(CorrelationMiddleware);
    app.UseExceptionHandler();
}

static void ConfigureEndpoints(WebApplication app)
{
    ArgumentNullException.ThrowIfNull(app);

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = static _ => false,
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = static _ => true,
    });

    app.MapProviderWebhookEndpoints();
    app.MapInternalPaymentEndpoints();

    app.MapGet("/", static () => Results.Ok("ExitPass Payment Orchestrator API"));
}

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
        logger.LogInformation("Payment Orchestrator request started.");

        await next();

        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "Payment Orchestrator request completed with server error status code {StatusCode}.",
                context.Response.StatusCode);
        }
        else if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            logger.LogWarning(
                "Payment Orchestrator request completed with client error status code {StatusCode}.",
                context.Response.StatusCode);
        }
        else
        {
            logger.LogInformation(
                "Payment Orchestrator request completed successfully with status code {StatusCode}.",
                context.Response.StatusCode);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled exception reached Payment Orchestrator API boundary.");
        throw;
    }
}

/// <summary>
/// Entry-point marker for integration testing.
/// </summary>
public partial class Program
{
}
