using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Api.Security;
using ExitPass.CentralPms.Application.Reconciliation;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Reconciliation;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Central PMS MoPS transaction API surface.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - MoPS transaction imports remain reconciliation evidence only.
/// - MoPS endpoints expose RBAC policy hooks for later enforcement.
/// </summary>
public sealed class MopsTransactionApiIntegrationTests
{
    private static readonly Guid SiteId = Guid.Parse("55555555-5555-5555-5555-555555555551");
    private static readonly Guid MopsTransactionRecordId = Guid.Parse("55555555-5555-5555-5555-555555555552");
    private static readonly Guid ReconciliationRunId = Guid.Parse("55555555-5555-5555-5555-555555555553");
    private static readonly Guid ReconciliationItemId = Guid.Parse("55555555-5555-5555-5555-555555555554");
    private static readonly Guid CorrelationId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// Verifies valid MoPS imports create reconciliation evidence without payment-truth mutation.
    /// </summary>
    [Fact]
    public async Task Import_WhenValid_ReturnsCreatedAndDoesNotInvokePaymentTruth()
    {
        var fake = new FakeMopsTransactionService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, ValidImportRequest(), CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<ImportMopsTransactionResponse>();
        body.Should().NotBeNull();
        body!.MopsTransactionRecordId.Should().Be(MopsTransactionRecordId);
        body.ReconciliationItemId.Should().Be(ReconciliationItemId);
        body.WasDuplicate.Should().BeFalse();
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies replayed duplicate imports are deterministic and return existing reconciliation linkage.
    /// </summary>
    [Fact]
    public async Task Import_WhenDuplicate_ReturnsOkWithExistingLinkage()
    {
        using var factory = CreateFactory(new FakeMopsTransactionService { DuplicateImport = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, ValidImportRequest(), CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ImportMopsTransactionResponse>();
        body.Should().NotBeNull();
        body!.WasDuplicate.Should().BeTrue();
        body.ReconciliationRunId.Should().Be(ReconciliationRunId);
    }

    /// <summary>
    /// Verifies unknown site and site-group reference failures are deterministic.
    /// </summary>
    [Fact]
    public async Task Import_WhenReferenceInvalid_ReturnsDeterministicError()
    {
        using var factory = CreateFactory(new FakeMopsTransactionService { RejectImport = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, ValidImportRequest(), CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("SITE_NOT_FOUND");
    }

    /// <summary>
    /// Verifies imported MoPS records are retrievable by id and list endpoint.
    /// </summary>
    [Fact]
    public async Task Reads_WhenCalled_ReturnImportedRecords()
    {
        using var factory = CreateFactory(new FakeMopsTransactionService());
        using var client = factory.CreateClient();

        using var readResponse = await client.GetAsync($"/v1/ops/mops-transactions/{MopsTransactionRecordId}");
        using var listResponse = await client.GetAsync("/v1/ops/mops-transactions?limit=10&sourceSystemCode=MOPS");

        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await readResponse.Content.ReadFromJsonAsync<MopsTransactionSummary>())!
            .MopsTransactionRecordId.Should().Be(MopsTransactionRecordId);
        (await listResponse.Content.ReadFromJsonAsync<MopsTransactionsResponse>())!
            .Records.Should().ContainSingle(record => record.MopsTransactionRecordId == MopsTransactionRecordId);
    }

    /// <summary>
    /// Verifies unknown MoPS ids return deterministic API errors.
    /// </summary>
    [Fact]
    public async Task Read_WhenMissing_ReturnsDeterministicError()
    {
        using var factory = CreateFactory(new FakeMopsTransactionService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/mops-transactions/{MopsTransactionRecordId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("MOPS_TRANSACTION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies placeholder RBAC metadata is present for future authorization enforcement.
    /// </summary>
    [Fact]
    public void MopsEndpoints_ExposePolicyMetadata()
    {
        using var factory = CreateFactory(new FakeMopsTransactionService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/ops/mops-transactions", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "MopsTransactionImporter", "MopsTransactionViewer" });
    }

    private static CustomWebApplicationFactory CreateFactory(FakeMopsTransactionService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IMopsTransactionService>();
                services.AddSingleton<IMopsTransactionService>(fake);
            });
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        ImportMopsTransactionRequest body,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/ops/mops-transactions/import")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(request);
    }

    private static ImportMopsTransactionRequest ValidImportRequest() =>
        new(
            SiteId,
            SiteGroupId: null,
            PaymentRailId: null,
            VendorSystemId: null,
            ParkingSessionId: null,
            LaneId: null,
            SourceSystemCode: "MOPS",
            SourceTransactionRef: "MOPS-TX-001",
            SourceBatchRef: null,
            CollectionReference: null,
            CurrencyCode: "PHP",
            Amount: 100m,
            PaymentMethodLabel: "QRPH",
            ContinuityReasonCode: "MANUAL_GATE",
            CapturedAt: DateTimeOffset.UtcNow,
            EvidenceRef: "DEV-EVIDENCE",
            EvidenceHash: null,
            ActorUserId: null,
            ImportedByServiceIdentityId: null);

    private sealed class FakeMopsTransactionService : IMopsTransactionService
    {
        public bool DuplicateImport { get; init; }

        public bool RejectImport { get; init; }

        public bool ReturnMissing { get; init; }

        public bool PaymentTruthMutationRequested { get; private set; }

        public Task<MopsImportResult> ImportAsync(
            ImportMopsTransactionCommand command,
            CancellationToken cancellationToken)
        {
            if (RejectImport)
            {
                throw new MopsImportRejectedException("SITE_NOT_FOUND", "The supplied site_id does not exist.");
            }

            return Task.FromResult(new MopsImportResult(
                MopsTransactionRecordId,
                ReconciliationRunId,
                ReconciliationItemId,
                "IMPORTED",
                "MOPS-API-TEST",
                DuplicateImport,
                command.CorrelationId));
        }

        public Task<IReadOnlyList<MopsTransactionRecord>> ListAsync(
            ListMopsTransactionsQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<MopsTransactionRecord>>(new[] { Record() });
        }

        public Task<MopsTransactionRecord> ReadAsync(
            ReadMopsTransactionQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new MopsTransactionNotFoundException(query.MopsTransactionRecordId);
            }

            return Task.FromResult(Record());
        }

        private static MopsTransactionRecord Record() =>
            new(
                MopsTransactionRecordId,
                ReconciliationRunId,
                ReconciliationItemId,
                SiteId,
                SiteGroupId: null,
                PaymentRailId: null,
                VendorSystemId: null,
                ParkingSessionId: null,
                LaneId: null,
                SourceSystemCode: "MOPS",
                SourceTransactionRef: "MOPS-TX-001",
                SourceBatchRef: null,
                CollectionReference: null,
                CurrencyCode: "PHP",
                Amount: 100m,
                PaymentMethodLabel: "QRPH",
                ContinuityReasonCode: "MANUAL_GATE",
                RecordStatus: "IMPORTED",
                CapturedAt: DateTimeOffset.UtcNow,
                ImportedAt: DateTimeOffset.UtcNow,
                EvidenceRef: "DEV-EVIDENCE",
                CorrelationId);
    }
}
