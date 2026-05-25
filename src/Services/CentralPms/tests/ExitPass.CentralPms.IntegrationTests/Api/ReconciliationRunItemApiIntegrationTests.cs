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
/// Verifies the Central PMS reconciliation run and item API surface.
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
/// - Reconciliation run creation never mutates payment truth.
/// - Reconciliation run/item endpoints expose RBAC policy hooks for later enforcement.
/// </summary>
public sealed class ReconciliationRunItemApiIntegrationTests
{
    private static readonly Guid RunId = Guid.Parse("77777777-7777-7777-7777-777777777771");
    private static readonly Guid ItemId = Guid.Parse("77777777-7777-7777-7777-777777777772");
    private static readonly Guid CorrelationId = Guid.Parse("77777777-7777-7777-7777-777777777773");

    /// <summary>
    /// Verifies valid reconciliation run creation succeeds without payment-truth mutation.
    /// </summary>
    [Fact]
    public async Task CreateRun_WhenValid_ReturnsCreatedAndDoesNotInvokePaymentTruth()
    {
        var fake = new FakeReconciliationRunItemService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, ValidCreateRunRequest(), CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateReconciliationRunResponse>();
        body.Should().NotBeNull();
        body!.ReconciliationRunId.Should().Be(RunId);
        body.ItemGenerationPerformed.Should().BeFalse();
        body.ItemGenerationMessage.Should().Contain("not performed");
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies created reconciliation runs are retrievable.
    /// </summary>
    [Fact]
    public async Task ReadRun_WhenKnown_ReturnsRun()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/reconciliation/runs/{RunId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReconciliationRunDetailResponse>();
        body.Should().NotBeNull();
        body!.ReconciliationRunId.Should().Be(RunId);
    }

    /// <summary>
    /// Verifies existing run-list behavior remains available on GET /runs.
    /// </summary>
    [Fact]
    public async Task ListRuns_WhenCalled_StillUsesExistingWorkflowEndpoint()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/ops/reconciliation/runs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationRunsResponse>())!
            .Runs.Should().ContainSingle(run => run.ReconciliationRunId == RunId);
    }

    /// <summary>
    /// Verifies run-item list and item read endpoints are deterministic.
    /// </summary>
    [Fact]
    public async Task ReadItems_WhenKnown_ReturnExpectedResults()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService());
        using var client = factory.CreateClient();

        using var listResponse = await client.GetAsync($"/v1/ops/reconciliation/runs/{RunId}/items");
        using var itemResponse = await client.GetAsync($"/v1/ops/reconciliation/items/{ItemId}");

        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        itemResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await listResponse.Content.ReadFromJsonAsync<ReconciliationItemsResponse>())!
            .Items.Should().ContainSingle(item => item.ReconciliationItemId == ItemId);
        (await itemResponse.Content.ReadFromJsonAsync<ReconciliationItemSummary>())!
            .ReconciliationItemId.Should().Be(ItemId);
    }

    /// <summary>
    /// Verifies unknown identifiers return deterministic API errors.
    /// </summary>
    [Fact]
    public async Task UnknownIds_WhenServiceReportsMissing_ReturnDeterministicErrors()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var runResponse = await client.GetAsync($"/v1/ops/reconciliation/runs/{RunId}");
        using var itemResponse = await client.GetAsync($"/v1/ops/reconciliation/items/{ItemId}");

        runResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        itemResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await runResponse.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_RUN_NOT_FOUND");
        (await itemResponse.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_ITEM_NOT_FOUND");
    }

    /// <summary>
    /// Verifies invalid enum requests return deterministic validation errors.
    /// </summary>
    [Fact]
    public async Task CreateRun_WhenInvalidEnum_ReturnsBadRequest()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService { RejectInvalid = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(client, ValidCreateRunRequest() with { RunType = "NOT_A_RUN_TYPE" }, CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("INVALID_REQUEST");
    }

    /// <summary>
    /// Verifies placeholder RBAC metadata is present for future authorization enforcement.
    /// </summary>
    [Fact]
    public void ReconciliationRunItemEndpoints_ExposePolicyMetadata()
    {
        using var factory = CreateFactory(new FakeReconciliationRunItemService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint =>
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/runs", StringComparison.OrdinalIgnoreCase) == true ||
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/items/{reconciliationItemId:guid}", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "ReconciliationRunCreator", "ReconciliationRunViewer", "ReconciliationItemViewer" });
    }

    private static CustomWebApplicationFactory CreateFactory(FakeReconciliationRunItemService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationRunItemService>();
                services.AddSingleton<IReconciliationRunItemService>(fake);
                services.RemoveAll<IReconciliationWorkflowService>();
                services.AddSingleton<IReconciliationWorkflowService>(new FakeReconciliationWorkflowService());
            });
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        CreateReconciliationRunRequest body,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/ops/reconciliation/runs")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(request);
    }

    private static CreateReconciliationRunRequest ValidCreateRunRequest() =>
        new(
            "PAYMENT_PROVIDER_RECONCILIATION",
            "TIME_WINDOW",
            RunCode: null,
            RunStatus: null,
            SiteGroupId: null,
            SiteId: null,
            IncidentRecordId: null,
            PaymentRailId: null,
            VendorSystemId: null,
            SourceBatchRef: null,
            WindowStartAt: DateTimeOffset.Parse("2026-05-25T00:00:00Z"),
            WindowEndAt: DateTimeOffset.Parse("2026-05-25T23:59:59Z"),
            GenerateItems: true,
            ActorUserId: null,
            ServiceIdentityId: null);

    private sealed class FakeReconciliationRunItemService : IReconciliationRunItemService
    {
        public bool ReturnMissing { get; init; }

        public bool RejectInvalid { get; init; }

        public bool PaymentTruthMutationRequested { get; private set; }

        public Task<ReconciliationRunCreateResult> CreateRunAsync(
            CreateReconciliationRunCommand command,
            CancellationToken cancellationToken)
        {
            if (RejectInvalid)
            {
                throw new ArgumentException("RunType must be one of the supported values.", nameof(command.RunType));
            }

            return Task.FromResult(new ReconciliationRunCreateResult(
                RunId,
                "RECON-API-TEST",
                command.RunType,
                command.RunStatus,
                command.ScopeType,
                0,
                ItemGenerationPerformed: false,
                "Automatic reconciliation item generation is not performed in this slice.",
                command.CorrelationId));
        }

        public Task<ReconciliationRunDetailRecord> ReadRunAsync(
            ReadReconciliationRunQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
            }

            return Task.FromResult(RunRecord());
        }

        public Task<IReadOnlyList<ReconciliationItemRecord>> ListRunItemsAsync(
            ListReconciliationRunItemsQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
            }

            return Task.FromResult<IReadOnlyList<ReconciliationItemRecord>>(new[] { ItemRecord() });
        }

        public Task<ReconciliationItemRecord> ReadItemAsync(
            ReadReconciliationItemQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationItemNotFoundException(query.ReconciliationItemId);
            }

            return Task.FromResult(ItemRecord());
        }
    }

    private sealed class FakeReconciliationWorkflowService : IReconciliationWorkflowService
    {
        public Task<ReconciliationNoteResult> AddNoteAsync(AddReconciliationNoteCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(SubmitReconciliationResolutionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(DecideReconciliationResolutionCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(ReadReconciliationWorkflowHistoryQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(ListReconciliationRunsQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationRunRecord>>(
                new[]
                {
                    new ReconciliationRunRecord(
                        RunId,
                        "RECON-API-TEST",
                        "PAYMENT_PROVIDER_RECONCILIATION",
                        "STARTED",
                        "TIME_WINDOW",
                        null,
                        DateTimeOffset.UtcNow,
                        null,
                        0,
                        0,
                        0,
                        CorrelationId)
                });
        }

        public Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(ListReconciliationExceptionsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationExceptionRecord>>(Array.Empty<ReconciliationExceptionRecord>());
    }

    private static ReconciliationRunDetailRecord RunRecord() =>
        new(
            RunId,
            "RECON-API-TEST",
            "PAYMENT_PROVIDER_RECONCILIATION",
            "STARTED",
            "TIME_WINDOW",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            0,
            0,
            0,
            0,
            0,
            null,
            null,
            CorrelationId);

    private static ReconciliationItemRecord ItemRecord() =>
        new(
            ItemId,
            RunId,
            null,
            null,
            null,
            null,
            null,
            "DEV_TEST",
            null,
            "PROVIDER_TO_CORE",
            "PENDING",
            "NOT_EVALUATED",
            null,
            null,
            "PHP",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CorrelationId);
}
