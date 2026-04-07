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

var builder = WebApplication.CreateBuilder(args);

ConfigureConfiguration(builder);
ConfigureLogging(builder);
ConfigureServices(builder);

var app = builder.Build();

ConfigureMiddleware(app);
ConfigureEndpoints(app);

app.Run();

/// <summary>
/// Configures configuration providers for the Payment Orchestrator.
/// </summary>
/// <param name="builder">The web application builder.</param>
static void ConfigureConfiguration(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    builder.Configuration.AddEnvironmentVariables();
}

/// <summary>
/// Configures logging providers for the Payment Orchestrator.
/// </summary>
/// <param name="builder">The web application builder.</param>
static void ConfigureLogging(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

/// <summary>
/// Configures service registrations for the Payment Orchestrator.
/// </summary>
/// <param name="builder">The web application builder.</param>
static void ConfigureServices(WebApplicationBuilder builder)
{
    ArgumentNullException.ThrowIfNull(builder);

    var services = builder.Services;
    var configuration = builder.Configuration;

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

    RegisterApplicationServices(services);
    RegisterInfrastructureServices(services, configuration);
    ValidateCriticalConfiguration(configuration);
}

/// <summary>
/// Registers application-layer services.
///
/// BRD:
/// - 9.9 Payment Initiation
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
///
/// SDD:
/// - 10.5.1 Initiate Provider Payment
/// - 10.5.2 Payment Provider Webhook
/// - 10.5.3 Report Verified Payment Outcome
///
/// Invariants Enforced:
/// - Payment Orchestrator executes provider flows but does not declare payment finality.
/// - Duplicate provider callbacks must be handled idempotently.
/// - Only verified provider outcomes may be reported to Central PMS.
/// </summary>
/// <param name="services">The service collection.</param>
static void RegisterApplicationServices(IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddScoped<InitiateProviderPaymentHandler>();
    services.AddScoped<VerifyProviderWebhookHandler>();
}

/// <summary>
/// Registers infrastructure-layer services.
///
/// BRD:
/// - 12 Payment Orchestration
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5 Payment Orchestrator APIs and Webhooks
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Verified outcome reporting must fail closed when Central PMS integration is not configured.
/// - Webhook persistence must preserve deduplication and auditability.
/// - Provider dependencies must be explicitly registered so startup validation fails fast.
/// </summary>
/// <param name="services">The service collection.</param>
/// <param name="configuration">The application configuration.</param>
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

/// <summary>
/// Validates critical configuration required for startup.
/// </summary>
/// <param name="configuration">The application configuration.</param>
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

/// <summary>
/// Configures middleware for the Payment Orchestrator.
/// </summary>
/// <param name="app">The web application.</param>
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

    app.UseExceptionHandler();
}

/// <summary>
/// Configures HTTP endpoints for the Payment Orchestrator.
/// </summary>
/// <param name="app">The web application.</param>
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

/// <summary>
/// Entry-point marker for integration testing.
/// </summary>
public partial class Program
{
}
