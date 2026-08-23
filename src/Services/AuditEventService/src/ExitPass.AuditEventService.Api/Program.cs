// BRD requirement implemented: Platform operability baseline for service availability and health visibility.
// SDD section correspondence: Runtime services, deployment topology, and observability baseline.
// System invariant enforced: A service must expose machine-readable liveness and readiness endpoints.

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ExitPass.AuditEventService.Api.Configuration;
using ExitPass.AuditEventService.Api.Health;
using ExitPass.AuditEventService.Api.Security;
using ExitPass.AuditEventService.Application.AuditEvents;
using ExitPass.AuditEventService.Infrastructure.AuditEvents;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
});

var auditOptions = builder.Configuration.GetSection(AuditEventServiceOptions.SectionName)
    .Get<AuditEventServiceOptions>() ?? new AuditEventServiceOptions();
builder.Services.AddSingleton(auditOptions);
var connectionString = builder.Configuration.GetConnectionString("MainDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("AUDIT_EVENT_DATABASE_CONFIGURATION_REQUIRED");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<IAuditEventRepository, PostgresAuditEventRepository>();

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Audit Event Service is alive."))
    .AddCheck("audit_configuration", () => auditOptions.Validate().Count == 0
        ? HealthCheckResult.Healthy("Audit Event Service configuration is valid.")
        : HealthCheckResult.Unhealthy("AUDIT_EVENT_CONFIGURATION_INVALID"))
    .AddCheck<AuditDatabaseHealthCheck>("audit_database");

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "SecureDevelopment")
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "ExitPass Audit Event API v1");
    });
}

app.UseRouting();
app.UseMiddleware<AuditServiceAuthenticationMiddleware>();
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
    service = "ExitPass Audit Event API",
    status = "running"
}));
app.Run();

public partial class Program
{
}
