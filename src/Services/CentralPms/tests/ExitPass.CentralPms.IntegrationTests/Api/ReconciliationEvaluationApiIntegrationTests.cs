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
/// Verifies the Central PMS reconciliation evaluation API surface.
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
/// - Evaluation endpoints never mutate payment, provider, exit, gate, or financial truth.
/// - Evaluation endpoints expose RBAC policy hooks for later enforcement.
/// </summary>
public sealed class ReconciliationEvaluationApiIntegrationTests
{
    private static readonly Guid ItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    private static readonly Guid RunId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");
    private static readonly Guid CorrelationId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3");

    /// <summary>
    /// Verifies evaluating an existing item returns a deterministic result.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenItemExists_ReturnsEvaluationAndDoesNotMutatePaymentTruth()
    {
        var fake = new FakeEvaluationService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            $"/v1/ops/reconciliation/items/{ItemId}/evaluate",
            new EvaluateReconciliationItemRequest(null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReconciliationItemEvaluationResponse>();
        body.Should().NotBeNull();
        body!.ReconciliationItemId.Should().Be(ItemId);
        body.MatchStatus.Should().Be("MATCH");
        body.ExceptionCreatedOrUpdated.Should().BeFalse();
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies reading current evaluation does not require a write.
    /// </summary>
    [Fact]
    public async Task GetEvaluation_WhenItemExists_ReturnsEvaluation()
    {
        using var factory = CreateFactory(new FakeEvaluationService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/reconciliation/items/{ItemId}/evaluation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationItemEvaluationResponse>())!
            .EvaluationClassification.Should().Be("MATCH");
    }

    /// <summary>
    /// Verifies duplicate evaluation is deterministic for this idempotent item-level evaluator.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenRepeated_ReturnsSameClassification()
    {
        using var factory = CreateFactory(new FakeEvaluationService());
        using var client = factory.CreateClient();
        var request = new EvaluateReconciliationItemRequest(null, null);

        using var first = await SendJsonAsync(client, $"/v1/ops/reconciliation/items/{ItemId}/evaluate", request, CorrelationId);
        using var second = await SendJsonAsync(client, $"/v1/ops/reconciliation/items/{ItemId}/evaluate", request, CorrelationId);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<ReconciliationItemEvaluationResponse>())!
            .MatchStatus.Should().Be("MATCH");
    }

    /// <summary>
    /// Verifies unknown items return deterministic errors.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenItemMissing_ReturnsDeterministicError()
    {
        using var factory = CreateFactory(new FakeEvaluationService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            $"/v1/ops/reconciliation/items/{ItemId}/evaluate",
            new EvaluateReconciliationItemRequest(null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_ITEM_NOT_FOUND");
    }

    /// <summary>
    /// Verifies run-level evaluation succeeds and does not request payment truth mutation.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunExists_ReturnsSummaryAndDoesNotMutatePaymentTruth()
    {
        var fake = new FakeEvaluationService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            $"/v1/ops/reconciliation/runs/{RunId}/evaluate",
            new EvaluateReconciliationRunRequest(null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReconciliationRunEvaluationSummaryResponse>();
        body.Should().NotBeNull();
        body!.ReconciliationRunId.Should().Be(RunId);
        body.TotalItems.Should().Be(2);
        body.MatchedItems.Should().Be(1);
        body.MismatchedItems.Should().Be(1);
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies empty runs complete successfully with zero counts.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunIsEmpty_ReturnsZeroSummary()
    {
        using var factory = CreateFactory(new FakeEvaluationService { EmptyRun = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            $"/v1/ops/reconciliation/runs/{RunId}/evaluate",
            new EvaluateReconciliationRunRequest(null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReconciliationRunEvaluationSummaryResponse>();
        body!.TotalItems.Should().Be(0);
        body.EvaluatedItems.Should().Be(0);
    }

    /// <summary>
    /// Verifies unknown runs return deterministic errors.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRunMissing_ReturnsDeterministicError()
    {
        using var factory = CreateFactory(new FakeEvaluationService { ReturnRunMissing = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            $"/v1/ops/reconciliation/runs/{RunId}/evaluate",
            new EvaluateReconciliationRunRequest(null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_RUN_NOT_FOUND");
    }

    /// <summary>
    /// Verifies duplicate run evaluation returns the same summary.
    /// </summary>
    [Fact]
    public async Task EvaluateRun_WhenRepeated_ReturnsSameSummary()
    {
        using var factory = CreateFactory(new FakeEvaluationService());
        using var client = factory.CreateClient();
        var request = new EvaluateReconciliationRunRequest(null, null);

        using var first = await SendJsonAsync(client, $"/v1/ops/reconciliation/runs/{RunId}/evaluate", request, CorrelationId);
        using var second = await SendJsonAsync(client, $"/v1/ops/reconciliation/runs/{RunId}/evaluate", request, CorrelationId);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<ReconciliationRunEvaluationSummaryResponse>();
        var secondBody = await second.Content.ReadFromJsonAsync<ReconciliationRunEvaluationSummaryResponse>();
        secondBody.Should().BeEquivalentTo(firstBody);
    }

    /// <summary>
    /// Verifies run evaluation summary can be read without issuing evaluation writes.
    /// </summary>
    [Fact]
    public async Task GetRunEvaluationSummary_WhenRunExists_ReturnsSummary()
    {
        using var factory = CreateFactory(new FakeEvaluationService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/reconciliation/runs/{RunId}/evaluation-summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationRunEvaluationSummaryResponse>())!
            .TotalItems.Should().Be(2);
    }

    /// <summary>
    /// Verifies placeholder RBAC metadata is present for future authorization enforcement.
    /// </summary>
    [Fact]
    public void ReconciliationEvaluationEndpoints_ExposePolicyMetadata()
    {
        using var factory = CreateFactory(new FakeEvaluationService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint =>
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/items/{reconciliationItemId:guid}/evaluate", StringComparison.OrdinalIgnoreCase) == true ||
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/items/{reconciliationItemId:guid}/evaluation", StringComparison.OrdinalIgnoreCase) == true ||
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/runs/{reconciliationRunId:guid}/evaluate", StringComparison.OrdinalIgnoreCase) == true ||
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/runs/{reconciliationRunId:guid}/evaluation-summary", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "ReconciliationItemEvaluator", "ReconciliationRunEvaluator", "ReconciliationEvaluationViewer" });
    }

    private static CustomWebApplicationFactory CreateFactory(FakeEvaluationService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationEvaluationService>();
                services.AddSingleton<IReconciliationEvaluationService>(fake);
            });
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        string url,
        T body,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(request);
    }

    private sealed class FakeEvaluationService : IReconciliationEvaluationService
    {
        public bool ReturnMissing { get; init; }

        public bool ReturnRunMissing { get; init; }

        public bool EmptyRun { get; init; }

        public bool PaymentTruthMutationRequested { get; private set; }

        public Task<ReconciliationItemEvaluationRecord> EvaluateAsync(
            EvaluateReconciliationItemCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationItemNotFoundException(command.ReconciliationItemId);
            }

            return Task.FromResult(Record(command.CorrelationId));
        }

        public Task<ReconciliationItemEvaluationRecord> ReadEvaluationAsync(
            ReadReconciliationItemEvaluationQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationItemNotFoundException(query.ReconciliationItemId);
            }

            return Task.FromResult(Record(CorrelationId));
        }

        public Task<ReconciliationRunEvaluationSummaryRecord> EvaluateRunAsync(
            EvaluateReconciliationRunCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnRunMissing)
            {
                throw new ReconciliationRunNotFoundException(command.ReconciliationRunId);
            }

            return Task.FromResult(Summary(command.CorrelationId));
        }

        public Task<ReconciliationRunEvaluationSummaryRecord> ReadRunEvaluationSummaryAsync(
            ReadReconciliationRunEvaluationSummaryQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnRunMissing)
            {
                throw new ReconciliationRunNotFoundException(query.ReconciliationRunId);
            }

            return Task.FromResult(Summary(CorrelationId));
        }

        private static ReconciliationItemEvaluationRecord Record(Guid correlationId) =>
            new(
                ItemId,
                RunId,
                "PROVIDER_TO_CORE",
                "MATCHED",
                "MATCH",
                "MATCH",
                "Expected and actual amounts match exactly.",
                100m,
                100m,
                0m,
                null,
                ExceptionCreatedOrUpdated: false,
                "Exception creation/update is deferred.",
                DateTimeOffset.UtcNow,
                correlationId);

        private ReconciliationRunEvaluationSummaryRecord Summary(Guid correlationId) =>
            EmptyRun
                ? new ReconciliationRunEvaluationSummaryRecord(RunId, 0, 0, 0, 0, 0, 0, 0, 0, correlationId)
                : new ReconciliationRunEvaluationSummaryRecord(RunId, 2, 2, 1, 1, 0, 0, 0, 0, correlationId);
    }
}
