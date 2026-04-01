// BRD requirement implemented: Platform operability baseline for service availability and health visibility.
// SDD section correspondence: Runtime services, deployment topology, and observability baseline.
// System invariant enforced: A service must expose machine-readable liveness and readiness endpoints.

using ExitPass.CentralPms.Api.Validation;
using ExitPass.CentralPms.Application.Abstractions.Persistence;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Domain.Common;
using ExitPass.CentralPms.Domain.PaymentAttempts.Policies;
using ExitPass.CentralPms.Infrastructure.Common;
using ExitPass.CentralPms.Infrastructure.PaymentAttempts;
using ExitPass.CentralPms.Infrastructure.Persistence.Routines;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
});

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
    new PaymentAttemptDbRoutineGateway(
        builder.Configuration.GetConnectionString("MainDatabase")
        ?? throw new InvalidOperationException("Connection string 'MainDatabase' is missing.")
    ));

builder.Services.AddSingleton<ISystemClock, SystemClock>();

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Central PMS Service is alive."));

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

app.MapGet("/", () => Results.Ok(new
{
    service = "ExitPass Central PMS API",
    status = "running"
}));

app.Run();

public partial class Program
{
}
