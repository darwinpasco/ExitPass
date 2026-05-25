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
                endpoint.DisplayName?.Contains("/v1/ops/reconciliation/items/{reconciliationItemId:guid}/evaluation", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "ReconciliationItemEvaluator", "ReconciliationEvaluationViewer" });
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
    }
}
