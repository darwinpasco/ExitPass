using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ExitPass.AuditEventService.Application.AuditEvents;
using ExitPass.AuditEventService.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.AuditEventService.ContractTests;

public sealed class AuditEventContractTests
{
    [Fact]
    public async Task AuditRoutes_RequireServiceAuthentication()
    {
        await using var factory = new AuditEventApiFactory();
        using var client = factory.CreateClient();

        using var append = await client.PostAsJsonAsync("/v1/audit/events", new { });
        using var query = await client.GetAsync("/v1/audit/events?correlationId=10000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.Unauthorized, append.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, query.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedAppendAndQuery_UseTheAppendOnlyContract()
    {
        await using var factory = new AuditEventApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-ExitPass-Service-Identity", AuditEventApiFactory.ServiceIdentityId.ToString("D"));
        client.DefaultRequestHeaders.Add("X-ExitPass-Audit-Key", AuditEventApiFactory.ApiKey);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", AuditEventApiFactory.CorrelationId.ToString("D"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var request = new AppendAuditEventRequest(
            AuditEventApiFactory.EventId,
            "IST_CONTRACT_SENTINEL",
            "SYSTEM",
            "SUCCESS",
            null,
            AuditEventApiFactory.SiteId,
            AuditEventApiFactory.TerminalId,
            "IST",
            "Non-financial contract sentinel.",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            AuditEventApiFactory.CorrelationId,
            null);

        using var append = await client.PostAsJsonAsync("/v1/audit/events", request);
        using var query = await client.GetAsync(
            $"/v1/audit/events?correlationId={AuditEventApiFactory.CorrelationId:D}&siteId={AuditEventApiFactory.SiteId:D}");

        Assert.Equal(HttpStatusCode.Created, append.StatusCode);
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        var result = await query.Content.ReadFromJsonAsync<AuditEventQueryResponse>();
        var item = Assert.Single(result!.Items);
        Assert.Equal(AuditEventApiFactory.EventId, item.AuditEventId);
        Assert.Equal(AuditEventApiFactory.SiteId, item.SiteId);
        Assert.Equal(AuditEventApiFactory.TerminalId, item.TerminalId);
        Assert.Equal(AuditEventApiFactory.ServiceIdentityId, item.ActorServiceIdentityId);
    }

    private sealed class AuditEventApiFactory : WebApplicationFactory<Program>
    {
        public static readonly Guid ServiceIdentityId = Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");
        public static readonly Guid CorrelationId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid EventId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        public static readonly Guid SiteId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        public static readonly Guid TerminalId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        public const string ApiKey = "contract-test-key";
        private readonly string secretRoot = Path.Combine(Path.GetTempPath(), $"exitpass-audit-contract-{Guid.NewGuid():N}");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(secretRoot);
            var keyFile = Path.Combine(secretRoot, "api-key");
            File.WriteAllText(keyFile, ApiKey);
            builder.UseSetting("ConnectionStrings:MainDatabase", "Host=127.0.0.1;Database=unused");
            builder.UseSetting("AuditEventService:ServiceIdentityId", ServiceIdentityId.ToString("D"));
            builder.UseSetting("AuditEventService:SourceServiceName", "IST_CONTRACT_CALLER");
            builder.UseSetting("AuditEventService:SecretMountRoot", secretRoot);
            builder.UseSetting("AuditEventService:ApiKeyFile", keyFile);
            builder.UseSetting("AuditEventService:AllowedOperations:0", "AUDIT_EVENT_APPEND");
            builder.UseSetting("AuditEventService:AllowedOperations:1", "AUDIT_EVENT_READ");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuditEventRepository>();
                services.AddSingleton<IAuditEventRepository, InMemoryAuditEventRepository>();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(secretRoot)) Directory.Delete(secretRoot, recursive: true);
        }
    }

    private sealed class InMemoryAuditEventRepository : IAuditEventRepository
    {
        private readonly Dictionary<Guid, AuditEventRecord> records = [];

        public Task<(AuditEventRecord Record, bool Created)> AppendAsync(
            AuditEventRecord record,
            CancellationToken cancellationToken)
        {
            if (records.TryGetValue(record.AuditEventId, out var existing))
                return Task.FromResult((existing, false));
            var persisted = record with { RecordedAt = DateTimeOffset.UtcNow };
            records.Add(persisted.AuditEventId, persisted);
            return Task.FromResult((persisted, true));
        }

        public Task<IReadOnlyList<AuditEventRecord>> QueryAsync(
            Guid correlationId,
            Guid? siteId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AuditEventRecord>>(
                records.Values.Where(record => record.CorrelationId == correlationId &&
                    (siteId is null || record.SiteId == siteId)).ToArray());
    }
}
