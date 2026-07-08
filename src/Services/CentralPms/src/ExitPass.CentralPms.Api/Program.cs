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
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Api.Services;
using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Observability;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.Operations;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.VendorParking;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Application.VendorPaymentAcknowledgments;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Infrastructure.Common;
using ExitPass.CentralPms.Infrastructure.Eventing;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.PaymentAttempts;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using ExitPass.CentralPms.Infrastructure.OperatorConsole;
using ExitPass.CentralPms.Infrastructure.Operations;
using ExitPass.CentralPms.Infrastructure.Reconciliation;
using ExitPass.CentralPms.Infrastructure.Security;
using ExitPass.CentralPms.Infrastructure.VendorParking;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorPaymentAcknowledgments;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

const string ServiceName = "ExitPass.CentralPms.Api";
const string OperatorConsoleLocalCorsPolicyName = "OperatorConsoleLocalDevelopmentCors";

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
ConfigureInternalSecurity(builder);
ConfigureApplicationServices(builder, mainDatabaseConnectionString);
ConfigureOperatorConsoleLocalCors(builder);

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
if (IsLocalDevelopment(app.Environment))
{
    app.UseCors(OperatorConsoleLocalCorsPolicyName);
}

app.UseMiddleware<InternalMtlsMiddleware>();
app.UseMiddleware<CentralPmsRbacMiddleware>();
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
app.MapInternalControlledUatFiscalIssuanceEndpoints();
app.MapFiscalIssuanceStatusEndpoints();
app.MapInternalFiscalExceptionQueueSemanticHashBackfillEndpoints();
app.MapInternalOutboxDispatcherEndpoints();
app.MapInternalEventRecoveryEndpoints();
app.MapInternalVendorSessionProjectionEndpoints();
app.MapGateExitAuthorizationConsumeEndpoints();
app.MapReconciliationWorkflowEndpoints();
app.MapMopsTransactionEndpoints();
app.MapReconciliationRunItemEndpoints();
app.MapReconciliationExceptionLifecycleEndpoints();
app.MapReconciliationEvaluationEndpoints();
app.MapOperatorConsoleAccessEvaluationEndpoints();
app.MapOperatorConsoleAccessReadinessEndpoints();
app.MapOperatorConsoleSessionLookupEndpoints();
app.MapOperatorConsoleFiscalIssuanceStatusEndpoints();
app.MapOperatorConsoleFiscalStatusViewAuditReportEndpoints();
app.MapTicketSessionSummaryEndpoints();
app.MapVendorPaymentAcknowledgmentOpsEndpoints();
app.MapVendorSessionProjectionHealthEndpoints();
app.MapOperatorConsoleStatutoryDiscountDraftEndpoints();
app.MapOperatorConsoleStatutoryDiscountPolicyResolutionEndpoints();
app.MapOperatorConsoleProductionPolicyImportEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "ExitPass Central PMS API",
    status = "running"
}));

app.Run();

static void ConfigureLogging(
    WebApplicationBuilder builder,
    string? otlpEndpoint,
    string serviceVersion)
{
    builder.Logging.ClearProviders();

    builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
    builder.Logging.AddFilter("Microsoft.AspNetCore.Routing.EndpointMiddleware", LogLevel.Warning);

    builder.Logging.AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.SingleLine = false;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
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
                .AddSource("ExitPass.CentralPms.Api.VendorParking")
                .AddSource("ExitPass.CentralPms.Api.InternalPaymentAttempts")
                .AddSource("ExitPass.CentralPms.Api.Reconciliation")
                .AddSource("ExitPass.CentralPms.Api.MopsTransactions")
                .AddSource("ExitPass.CentralPms.Api.ReconciliationRunItems")
                .AddSource("ExitPass.CentralPms.Api.ReconciliationExceptionLifecycle")
                .AddSource("ExitPass.CentralPms.Api.ReconciliationEvaluation")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleAccessEvaluation")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleAccessReadiness")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleSessionLookup")
                .AddSource("ExitPass.CentralPms.Api.TicketSessionSummary")
                .AddSource("ExitPass.CentralPms.Api.VendorPaymentAcknowledgments")
                .AddSource("ExitPass.CentralPms.Api.VendorSessionProjectionHealth")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDraft")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountRead")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountDecision")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountApplyPayableBasis")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleStatutoryDiscountPolicyResolution")
                .AddSource("ExitPass.CentralPms.Api.OperatorConsoleProductionPolicyImport")
                .AddSource("ExitPass.CentralPms.Api.ReconciliationOutboxDispatcher")
                .AddSource("ExitPass.CentralPms.Api.EventRecovery")
                .AddSource("ExitPass.CentralPms.Application.PaymentAttempts")
                .AddSource("ExitPass.CentralPms.Application.VendorParking")
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

static void ConfigureHealthChecks(WebApplicationBuilder builder)
{
    builder.Services
        .AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Central PMS Service is alive."));
}

static void ConfigureInternalSecurity(WebApplicationBuilder builder)
{
    builder.Services.Configure<InternalMtlsOptions>(
        builder.Configuration.GetSection("InternalSecurity:Mtls"));
    builder.Services.Configure<CentralPmsRbacOptions>(
        builder.Configuration.GetSection("CentralPms:Rbac"));
    builder.Services.AddSingleton<IInternalClientCertificateAccessor, HttpContextInternalClientCertificateAccessor>();
}

static void ConfigureApplicationServices(
    WebApplicationBuilder builder,
    string mainDatabaseConnectionString)
{
    builder.Services.AddScoped<ICreateOrReusePaymentAttemptUseCase, CreateOrReusePaymentAttemptHandler>();
    builder.Services.AddScoped<IResolveVendorParkingUseCase, ResolveVendorParkingHandler>();
    builder.Services.AddCentralPmsVendorPmsAdapter(builder.Configuration);
    builder.Services.Configure<VendorSessionProjectionOptions>(
        builder.Configuration.GetSection(VendorSessionProjectionOptions.SectionName));
    builder.Services.AddScoped<IVendorParkingResolutionPersistence>(_ =>
        new VendorParkingResolutionPersistence(mainDatabaseConnectionString));
    builder.Services.AddScoped<IVendorSessionProjectionRepository>(_ =>
        new PostgresVendorSessionProjectionRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IVendorSessionProjectionSyncTargetRepository>(_ =>
        new PostgresVendorSessionProjectionSyncTargetRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IVendorSessionProjectionHealthReadRepository>(_ =>
        new PostgresVendorSessionProjectionHealthReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IVendorSessionProjectionLookupService, VendorSessionProjectionLookupService>();
    builder.Services.AddScoped<IVendorSessionProjectionHealthService, VendorSessionProjectionHealthService>();
    builder.Services.AddScoped<IVendorSessionProjectionSyncOrchestrator, VendorSessionProjectionSyncOrchestrator>();
    builder.Services.AddHostedService<VendorSessionProjectionSchedulerHostedService>();
    builder.Services.AddScoped<IProviderHandoffFactory, ProviderHandoffFactory>();
    builder.Services.AddScoped<IPaymentAttemptCreationPolicy, PaymentAttemptCreationPolicy>();
    builder.Services.AddCentralPmsEventPublishing(builder.Configuration, mainDatabaseConnectionString);
    builder.Services.AddCentralPmsReconciliationOutboxPublisher(builder.Configuration);
    builder.Services.AddScoped<IReconciliationOutboxDispatcherService, ReconciliationOutboxDispatcherService>();
    builder.Services.AddScoped<IReconciliationOutboxDispatcherRepository>(serviceProvider =>
        new ReconciliationOutboxDispatcherRepository(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<ReconciliationOutboxDispatcherRepository>>()));
    builder.Services.AddScoped<IEventRecoveryService, EventRecoveryService>();
    builder.Services.AddScoped<IEventRecoveryRepository>(_ =>
        new EventRecoveryRepository(mainDatabaseConnectionString));

    builder.Services.AddScoped<CreatePaymentAttemptRequestValidator>();
    builder.Services.AddScoped<CreatePaymentAttemptHeadersValidator>();
    builder.Services.AddScoped<ResolveVendorParkingRequestValidator>();

    builder.Services.AddScoped<IParkingSessionReadRepository, ParkingSessionReadRepository>();
    builder.Services.AddScoped<ITariffSnapshotReadRepository, TariffSnapshotReadRepository>();
    builder.Services.AddScoped<IPaymentAttemptReplayReadRepository, PaymentAttemptReplayReadRepository>();

    builder.Services.AddScoped<IPaymentAttemptDbRoutineGateway>(_ =>
        new PaymentAttemptDbRoutineGateway(mainDatabaseConnectionString));

    builder.Services.AddScoped<IRecordPaymentConfirmationGateway>(_ =>
        new RecordPaymentConfirmationGateway(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalIssuanceReferenceRepository>(_ =>
        new PostgresFiscalIssuanceReferenceRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalIssuanceStatusReadService, FiscalIssuanceStatusReadService>();
    builder.Services.AddScoped<IFiscalExceptionQueueReferenceReader>(_ =>
        new PostgresFiscalIssuanceReferenceRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionReadbackAttemptRepository>(_ =>
        new PostgresFiscalExceptionReadbackAttemptRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionRetryCommandPreparationAuditRepository>(_ =>
        new PostgresFiscalExceptionRetryCommandPreparationAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionRetrySchedulingPreparationAuditRepository>(_ =>
        new PostgresFiscalExceptionRetrySchedulingPreparationAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionControlledRetryExecutionAuditRepository>(_ =>
        new PostgresFiscalExceptionControlledRetryExecutionAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository>(_ =>
        new PostgresFiscalExceptionSemanticHashRecalculationPreviewAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository>(_ =>
        new PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository>(_ =>
        new PostgresFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IFiscalExceptionRetryCommandPreparationService, FiscalExceptionRetryCommandPreparationService>();
    builder.Services.Configure<FiscalExceptionRetrySchedulingPreparationOptions>(
        builder.Configuration.GetSection(FiscalExceptionRetrySchedulingPreparationOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionRetrySchedulingPreparationService>(serviceProvider =>
        new FiscalExceptionRetrySchedulingPreparationService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionRetrySchedulingPreparationOptions>>().Value,
            serviceProvider.GetService<IFiscalExceptionRetrySchedulingPreparationAuditRepository>()));
    builder.Services.Configure<FiscalExceptionRetryExecutionPreparationOptions>(
        builder.Configuration.GetSection(FiscalExceptionRetryExecutionPreparationOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionRetryExecutionPreparationService>(serviceProvider =>
        new FiscalExceptionRetryExecutionPreparationService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionRetryExecutionPreparationOptions>>().Value));
    builder.Services.AddScoped<
        IFiscalExceptionPosServerRetryContractReadinessService,
        FiscalExceptionPosServerRetryContractReadinessService>();
    builder.Services.AddScoped<IFiscalIssuanceOrchestrationService, FiscalIssuanceOrchestrationService>();
    builder.Services.AddScoped<IFiscalExceptionRetryEligibilityEvaluator, FiscalExceptionRetryEligibilityEvaluator>();
    builder.Services.AddScoped<IFiscalExceptionQueueService, FiscalExceptionQueueService>();
    builder.Services.AddScoped<IFiscalExceptionReadbackClient, PosServerFiscalExceptionReadbackClient>();
    builder.Services.AddScoped<IFiscalExceptionReadbackWorker, FiscalExceptionReadbackWorker>();
    builder.Services.AddScoped<IFiscalSemanticRequestHashCalculator, FiscalSemanticRequestHashCalculator>();
    builder.Services.AddScoped<
        IFiscalExceptionSemanticHashRecalculationPreviewService,
        FiscalExceptionSemanticHashRecalculationPreviewService>();
    builder.Services.Configure<FiscalExceptionSemanticHashControlledBackfillApprovalOptions>(
        builder.Configuration.GetSection(FiscalExceptionSemanticHashControlledBackfillApprovalOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashControlledBackfillApprovalService>(serviceProvider =>
        new FiscalExceptionSemanticHashControlledBackfillApprovalService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionSemanticHashControlledBackfillApprovalOptions>>().Value));
    builder.Services.Configure<FiscalExceptionSemanticHashControlledBackfillMutationOptions>(
        builder.Configuration.GetSection(FiscalExceptionSemanticHashControlledBackfillMutationOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashControlledBackfillMutationPreparationService>(serviceProvider =>
        new FiscalExceptionSemanticHashControlledBackfillMutationPreparationService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionSemanticHashControlledBackfillMutationOptions>>().Value,
            serviceProvider.GetService<IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository>()));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashGuardedBackfillMutationRepository>(serviceProvider =>
        (PostgresFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository)serviceProvider
            .GetRequiredService<IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository>());
    builder.Services.AddScoped<IFiscalExceptionSemanticHashGuardedBackfillMutationService>(serviceProvider =>
        new FiscalExceptionSemanticHashGuardedBackfillMutationService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionSemanticHashControlledBackfillMutationOptions>>().Value,
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashGuardedBackfillMutationRepository>()));
    builder.Services.Configure<FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions>(
        builder.Configuration.GetSection(FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashBackfillOperatorWorkflowService>(serviceProvider =>
        new FiscalExceptionSemanticHashBackfillOperatorWorkflowService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionSemanticHashBackfillOperatorWorkflowOptions>>().Value,
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashControlledBackfillApprovalService>(),
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashGuardedBackfillMutationService>(),
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashBackfillOperatorWorkflowAuditRepository>()));
    builder.Services.Configure<FiscalExceptionSemanticHashBackfillInternalApiOptions>(
        builder.Configuration.GetSection(FiscalExceptionSemanticHashBackfillInternalApiOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionSemanticHashBackfillInternalApiHandler>(serviceProvider =>
        new FiscalExceptionSemanticHashBackfillInternalApiHandler(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionSemanticHashBackfillInternalApiOptions>>().Value,
            serviceProvider.GetRequiredService<IFiscalExceptionQueueService>(),
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashRecalculationPreviewAuditRepository>(),
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashControlledBackfillMutationAuditRepository>(),
            serviceProvider.GetRequiredService<IFiscalExceptionSemanticHashBackfillOperatorWorkflowService>()));
    builder.Services.AddScoped<IFiscalSemanticRequestHashParityProofService, FiscalSemanticRequestHashParityProofService>();
    builder.Services.Configure<FiscalIssuancePosServerIntegrationOptions>(
        builder.Configuration.GetSection(FiscalIssuancePosServerIntegrationOptions.SectionName));
    builder.Services.AddScoped<IPosServerFiscalDocumentRequestMapper, PosServerFiscalDocumentRequestMapper>();
    builder.Services.AddScoped<IFiscalIssuancePosServerLiveIntegrationService>(serviceProvider =>
        new FiscalIssuancePosServerLiveIntegrationService(
            serviceProvider.GetRequiredService<IOptions<FiscalIssuancePosServerIntegrationOptions>>().Value,
            serviceProvider.GetRequiredService<IPosServerFiscalDocumentRequestMapper>(),
            serviceProvider.GetRequiredService<IFiscalSemanticRequestHashCalculator>(),
            serviceProvider.GetRequiredService<IPosServerFiscalDocumentClient>(),
            serviceProvider.GetRequiredService<IFiscalIssuanceOrchestrationService>()));
    builder.Services.Configure<FiscalExceptionControlledRetryExecutionOptions>(
        builder.Configuration.GetSection(FiscalExceptionControlledRetryExecutionOptions.SectionName));
    builder.Services.AddScoped<IFiscalExceptionControlledRetryExecutionService>(serviceProvider =>
        new FiscalExceptionControlledRetryExecutionService(
            serviceProvider.GetRequiredService<IOptions<FiscalExceptionControlledRetryExecutionOptions>>().Value,
            serviceProvider.GetRequiredService<IPosServerFiscalDocumentRequestMapper>(),
            serviceProvider.GetRequiredService<IFiscalSemanticRequestHashCalculator>(),
            serviceProvider.GetRequiredService<IFiscalIssuancePosServerLiveIntegrationService>(),
            serviceProvider.GetRequiredService<IFiscalExceptionControlledRetryExecutionAuditRepository>()));
    builder.Services
        .AddHttpClient<IPosServerFiscalDocumentClient, HttpPosServerFiscalDocumentClient>(
            (serviceProvider, httpClient) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<FiscalIssuancePosServerIntegrationOptions>>()
                    .Value;

                if (Uri.TryCreate(options.PosServerBaseUrl, UriKind.Absolute, out var baseUri))
                {
                    httpClient.BaseAddress = baseUri;
                }

                if (options.TimeoutSeconds > 0)
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                }
            });
    builder.Services.AddScoped<IExitAuthorizationFiscalGatingShadowEvaluator, ExitAuthorizationFiscalGatingShadowEvaluator>();
    builder.Services.Configure<FiscalIssuanceExitAuthorizationGatingOptions>(
        builder.Configuration.GetSection(FiscalIssuanceExitAuthorizationGatingOptions.SectionName));
    builder.Services.AddScoped<IFiscalIssuanceControlledUatHarness>(serviceProvider =>
        new FiscalIssuanceControlledUatHarness(
            serviceProvider.GetRequiredService<IOptions<FiscalIssuancePosServerIntegrationOptions>>().Value,
            serviceProvider.GetRequiredService<IFiscalIssuancePosServerLiveIntegrationService>(),
            serviceProvider.GetRequiredService<IOptions<FiscalIssuanceExitAuthorizationGatingOptions>>().Value));
    builder.Services.AddScoped<IFiscalIssuanceControlledUatEvidenceExporter, FiscalIssuanceControlledUatEvidenceExporter>();
    builder.Services.AddScoped<IFiscalIssuanceControlledUatInvocationService, FiscalIssuanceControlledUatInvocationService>();

    builder.Services.AddScoped<RecordPaymentConfirmationService>();
    builder.Services.AddScoped<IVendorPaymentAcknowledgmentRepository>(_ =>
        new VendorPaymentAcknowledgmentRepository(mainDatabaseConnectionString));
    builder.Services.AddSingleton<IVendorPaymentConfirmationGuard, EnvironmentVendorPaymentConfirmationGuard>();
    builder.Services.AddScoped<IVendorPaymentAcknowledgmentWorkflow, VendorPaymentAcknowledgmentWorkflow>();
    builder.Services.AddScoped<IVendorPaymentAcknowledgmentRetryDispatcherService, VendorPaymentAcknowledgmentRetryDispatcherService>();
    builder.Services.AddScoped<IVendorPaymentAcknowledgmentOpsService, VendorPaymentAcknowledgmentOpsService>();

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

    builder.Services.AddScoped<IReconciliationWorkflowService, ReconciliationWorkflowService>();
    builder.Services.AddScoped<IReconciliationWorkflowRepository>(serviceProvider =>
        new ReconciliationWorkflowRepository(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<ReconciliationWorkflowRepository>>()));
    builder.Services.AddScoped<IMopsTransactionService, MopsTransactionService>();
    builder.Services.AddScoped<IMopsTransactionRepository>(serviceProvider =>
        new MopsTransactionRepository(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<MopsTransactionRepository>>()));
    builder.Services.AddScoped<IReconciliationRunItemService, ReconciliationRunItemService>();
    builder.Services.AddScoped<IReconciliationRunItemRepository>(serviceProvider =>
        new ReconciliationRunItemRepository(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<ReconciliationRunItemRepository>>()));
    builder.Services.AddScoped<IReconciliationExceptionLifecycleService, ReconciliationExceptionLifecycleService>();
    builder.Services.AddScoped<IReconciliationExceptionLifecycleRepository>(serviceProvider =>
        new ReconciliationExceptionLifecycleRepository(
            mainDatabaseConnectionString,
            serviceProvider.GetRequiredService<ILogger<ReconciliationExceptionLifecycleRepository>>()));
    builder.Services.AddScoped<IReconciliationEvaluationService, ReconciliationEvaluationService>();
    builder.Services.AddScoped<IReconciliationEvaluationRepository>(_ =>
        new ReconciliationEvaluationRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<ICentralPmsRbacRepository>(_ =>
        new CentralPmsRbacRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IGateDeviceIdentityValidator>(_ =>
        new GateDeviceIdentityValidator(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleAccessEvaluationReadRepository>(_ =>
        new OperatorConsoleAccessEvaluationReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleAccessEvaluationService, OperatorConsoleAccessEvaluationService>();
    builder.Services.AddScoped<IOperatorConsoleAccessEvaluationWriter>(_ =>
        new OperatorConsoleAccessEvaluationWriter(mainDatabaseConnectionString));
    builder.Services.AddSingleton<OperatorConsoleActionCatalog>();
    builder.Services.AddSingleton<OperatorConsoleDenialReasonCatalog>();
    builder.Services.AddSingleton(new OperatorConsolePolicyReadinessEnvironment(builder.Environment.EnvironmentName));
    builder.Services.AddScoped<IOperatorConsoleAccessReadinessRepository>(_ =>
        new OperatorConsoleAccessReadinessRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<OperatorConsoleAccessReadinessService>();
    builder.Services.AddScoped<IOperatorConsoleSessionLookupReadRepository>(_ =>
        new OperatorConsoleSessionLookupReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleSessionLookupService, OperatorConsoleSessionLookupService>();
    builder.Services.AddScoped<IOperatorConsoleFiscalIssuanceStatusService, OperatorConsoleFiscalIssuanceStatusService>();
    builder.Services.AddScoped<IOperatorConsoleFiscalStatusViewAuditReportRepository>(_ =>
        new OperatorConsoleFiscalStatusViewAuditReportRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleFiscalStatusViewAuditReportService, OperatorConsoleFiscalStatusViewAuditReportService>();
    builder.Services.AddScoped<ITicketSessionSummaryReadRepository>(_ =>
        new TicketSessionSummaryReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<ITicketSessionSummaryService, TicketSessionSummaryService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountDraftWriter>(_ =>
        new OperatorConsoleStatutoryDiscountDraftWriter(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountDraftService, OperatorConsoleStatutoryDiscountDraftService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountReadRepository>(_ =>
        new OperatorConsoleStatutoryDiscountReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountReadService, OperatorConsoleStatutoryDiscountReadService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountDecisionWriter>(_ =>
        new OperatorConsoleStatutoryDiscountDecisionWriter(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountDecisionService, OperatorConsoleStatutoryDiscountDecisionService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountApplyPayableBasisWriter>(_ =>
        new OperatorConsoleStatutoryDiscountApplyPayableBasisWriter(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountApplyPayableBasisService, OperatorConsoleStatutoryDiscountApplyPayableBasisService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountEvidenceRepository>(_ =>
        new OperatorConsoleStatutoryDiscountEvidenceRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountEvidenceService, OperatorConsoleStatutoryDiscountEvidenceService>();
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountPolicyResolutionReadRepository>(_ =>
        new OperatorConsoleStatutoryDiscountPolicyResolutionReadRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleStatutoryDiscountPolicyResolutionService, OperatorConsoleStatutoryDiscountPolicyResolutionService>();
    builder.Services.AddScoped<IOperatorConsoleProductionPolicyImportService, OperatorConsoleProductionPolicyImportService>();
    builder.Services.AddScoped<IOperatorConsoleProductionPolicyImportReviewQueue>(_ =>
        new OperatorConsoleProductionPolicyImportReviewQueueRepository(mainDatabaseConnectionString));
    builder.Services.AddScoped<IOperatorConsoleProductionPolicyImportReviewService, OperatorConsoleProductionPolicyImportReviewService>();

    builder.Services.TryAddSingleton<CentralPmsMetrics>();
    builder.Services.AddSingleton<ISystemClock, SystemClock>();
}

static void ConfigureOperatorConsoleLocalCors(WebApplicationBuilder builder)
{
    if (!IsLocalDevelopment(builder.Environment))
    {
        return;
    }

    var section = builder.Configuration.GetSection("OperatorConsole:LocalCors");
    var allowedOrigins = section.GetSection("AllowedOrigins").Get<string[]>() ??
        [
            "http://localhost:5173",
            "http://127.0.0.1:5173",
            "http://localhost:5174",
            "http://127.0.0.1:5174",
            "http://localhost:5175",
            "http://127.0.0.1:5175",
            "http://localhost:5178",
            "http://127.0.0.1:5178"
        ];
    var allowedMethods = section.GetSection("AllowedMethods").Get<string[]>() ??
        [
            HttpMethods.Get,
            HttpMethods.Post,
            HttpMethods.Options
        ];
    var allowedHeaders = section.GetSection("AllowedHeaders").Get<string[]>() ??
        [
            "Content-Type",
            "X-Correlation-Id",
            "X-Operator-User-Id",
            "X-Operator-Device-Binding-Id",
            "X-Operator-Shift-Id"
        ];

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(OperatorConsoleLocalCorsPolicyName, policy =>
        {
            policy
                .WithOrigins(allowedOrigins)
                .WithMethods(allowedMethods)
                .WithHeaders(allowedHeaders);
        });
    });
}

static bool IsLocalDevelopment(IHostEnvironment environment) =>
    environment.IsDevelopment() ||
    string.Equals(environment.EnvironmentName, "SecureDevelopment", StringComparison.OrdinalIgnoreCase);

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
