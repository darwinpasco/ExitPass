using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ExitPass.AuditEventService.Contracts;
using ExitPass.CentralPms.Application.Auditing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExitPass.CentralPms.Infrastructure.Auditing;

public sealed class CentralPmsAuditEventClientOptions
{
    public const string SectionName = "AuditEventClient";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public Guid ServiceIdentityId { get; set; }
    public string SecretMountRoot { get; set; } = string.Empty;
    public string ApiKeyFile { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;

    public IReadOnlyList<string> Validate()
    {
        if (!Enabled) return [];
        var errors = new List<string>();
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Host is not "audit-event" and not "localhost" and not "127.0.0.1"))
            errors.Add("AUDIT_EVENT_CLIENT_ENDPOINT_INVALID");
        if (ServiceIdentityId == Guid.Empty) errors.Add("AUDIT_EVENT_CLIENT_IDENTITY_INVALID");
        if (!ReadableSecretFile()) errors.Add("AUDIT_EVENT_CLIENT_SECRET_REFERENCE_INVALID");
        if (TimeoutSeconds is < 1 or > 60) errors.Add("AUDIT_EVENT_CLIENT_TIMEOUT_INVALID");
        return errors;
    }

    public string ReadApiKey()
    {
        if (!ReadableSecretFile()) throw new InvalidOperationException("AUDIT_EVENT_CLIENT_SECRET_REFERENCE_INVALID");
        var value = File.ReadAllText(ApiKeyFile).Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException("AUDIT_EVENT_CLIENT_SECRET_REFERENCE_INVALID")
            : value;
    }

    private bool ReadableSecretFile()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyFile) || string.IsNullOrWhiteSpace(SecretMountRoot) ||
            !Path.IsPathFullyQualified(ApiKeyFile) || !Path.IsPathFullyQualified(SecretMountRoot) ||
            !File.Exists(ApiKeyFile)) return false;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(SecretMountRoot));
        return Path.GetFullPath(ApiKeyFile).StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class HttpAuditEventPublisher(HttpClient httpClient, CentralPmsAuditEventClientOptions options)
    : IAuditEventPublisher
{
    public async Task AppendAsync(ApplicationAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/audit/events")
        {
            Content = JsonContent.Create(new AppendAuditEventRequest(
                auditEvent.AuditEventId, auditEvent.EventType, auditEvent.EventCategory, auditEvent.EventResult,
                auditEvent.EventReasonCode, auditEvent.SiteId, auditEvent.TerminalId, auditEvent.SourceChannel,
                auditEvent.Summary, auditEvent.OccurredAt, auditEvent.CorrelationId, auditEvent.CausationId))
        };
        request.Headers.TryAddWithoutValidation("X-ExitPass-Service-Identity", options.ServiceIdentityId.ToString("D"));
        request.Headers.TryAddWithoutValidation("X-ExitPass-Audit-Key", options.ReadApiKey());
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", auditEvent.CorrelationId.ToString("D"));
        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new AuditEventPublishException("AUDIT_EVENT_APPEND_REJECTED");
        }
        catch (OperationCanceledException) { throw; }
        catch (AuditEventPublishException) { throw; }
        catch (Exception) { throw new AuditEventPublishException("AUDIT_EVENT_SERVICE_UNAVAILABLE"); }
    }
}

public sealed class DisabledAuditEventPublisher : IAuditEventPublisher
{
    public Task AppendAsync(ApplicationAuditEvent auditEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

public sealed class AuditEventPublishException(string errorCode) : Exception(errorCode)
{
    public string ErrorCode { get; } = errorCode;
}

public static class CentralPmsAuditEventRegistration
{
    public static IServiceCollection AddCentralPmsAuditEventClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection(CentralPmsAuditEventClientOptions.SectionName)
            .Get<CentralPmsAuditEventClientOptions>() ?? new CentralPmsAuditEventClientOptions();
        var errors = options.Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(",", errors));
        services.AddSingleton(options);
        if (!options.Enabled)
        {
            services.AddSingleton<IAuditEventPublisher, DisabledAuditEventPublisher>();
            return services;
        }
        services.AddHttpClient<HttpAuditEventPublisher>(client =>
        {
            client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });
        services.AddSingleton<IAuditEventPublisher>(provider => provider.GetRequiredService<HttpAuditEventPublisher>());
        return services;
    }
}

public static class ProjectionAuditIdentity
{
    public static Guid For(Guid siteId, Guid correlationId)
    {
        var input = Encoding.UTF8.GetBytes($"CENTRAL_PMS|VENDOR_SESSION_PROJECTION_BATCH_RECEIVED|{siteId:D}|{correlationId:D}");
        var bytes = SHA256.HashData(input)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
