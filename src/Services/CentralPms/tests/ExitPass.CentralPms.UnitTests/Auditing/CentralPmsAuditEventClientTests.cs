using System.Net;
using System.Text.Json;
using ExitPass.CentralPms.Application.Auditing;
using ExitPass.CentralPms.Infrastructure.Auditing;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Auditing;

public sealed class CentralPmsAuditEventClientTests
{
    [Fact]
    public async Task Append_SendsServerConfiguredIdentityAndSiteScopedEvent()
    {
        using var secret = new TemporarySecret();
        var handler = new RecordingHandler(HttpStatusCode.Created);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://audit-event") };
        var options = CreateOptions(secret);
        var publisher = new HttpAuditEventPublisher(http, options);
        var auditEvent = NewEvent();

        await publisher.AppendAsync(auditEvent, CancellationToken.None);

        handler.Method.Should().Be(HttpMethod.Post);
        handler.Path.Should().Be("/v1/audit/events");
        handler.ServiceIdentity.Should().Be(options.ServiceIdentityId.ToString("D"));
        handler.ApiKey.Should().Be(TemporarySecret.Value);
        using var json = JsonDocument.Parse(handler.Body!);
        json.RootElement.GetProperty("siteId").GetGuid().Should().Be(auditEvent.SiteId);
        json.RootElement.GetProperty("correlationId").GetGuid().Should().Be(auditEvent.CorrelationId);
    }

    [Fact]
    public async Task Append_WhenServiceRejects_FailsClosedWithStableCode()
    {
        using var secret = new TemporarySecret();
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.Forbidden))
        { BaseAddress = new Uri("http://audit-event") };
        var publisher = new HttpAuditEventPublisher(http, CreateOptions(secret));

        var action = () => publisher.AppendAsync(NewEvent(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AuditEventPublishException>(action);
        exception.ErrorCode.Should().Be("AUDIT_EVENT_APPEND_REJECTED");
    }

    [Fact]
    public void ProjectionAuditIdentity_IsStableForReplayAndIsolatedBySite()
    {
        var correlationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var siteA = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var siteB = Guid.Parse("20000000-0000-0000-0000-000000000002");

        ProjectionAuditIdentity.For(siteA, correlationId).Should().Be(ProjectionAuditIdentity.For(siteA, correlationId));
        ProjectionAuditIdentity.For(siteA, correlationId).Should().NotBe(ProjectionAuditIdentity.For(siteB, correlationId));
    }

    private static ApplicationAuditEvent NewEvent() => new(
        Guid.Parse("10000000-0000-0000-0000-000000000010"),
        "VENDOR_SESSION_PROJECTION_BATCH_RECEIVED", "INTEGRATION", "SUCCESS", null,
        Guid.Parse("10000000-0000-0000-0000-000000000020"), null,
        "CENTRAL_PMS_VENDOR_SESSION_PROJECTION", "Validated provider-neutral projection batch.",
        DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
        Guid.Parse("10000000-0000-0000-0000-000000000030"), null);

    private static CentralPmsAuditEventClientOptions CreateOptions(TemporarySecret secret) => new()
    {
        Enabled = true,
        BaseUrl = "http://audit-event",
        ServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2"),
        SecretMountRoot = secret.Root,
        ApiKeyFile = secret.Path,
        TimeoutSeconds = 10
    };

    private sealed class RecordingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? ServiceIdentity { get; private set; }
        public string? ApiKey { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            ServiceIdentity = request.Headers.GetValues("X-ExitPass-Service-Identity").Single();
            ApiKey = request.Headers.GetValues("X-ExitPass-Audit-Key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class TemporarySecret : IDisposable
    {
        public const string Value = "central-pms-audit-test-key";
        public TemporarySecret()
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"exitpass-central-audit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Path = System.IO.Path.Combine(Root, "api-key");
            File.WriteAllText(Path, Value);
        }
        public string Root { get; }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
