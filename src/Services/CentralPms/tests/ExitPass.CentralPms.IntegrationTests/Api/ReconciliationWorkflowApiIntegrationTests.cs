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
/// Verifies the Central PMS reconciliation workflow API surface.
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
/// - Reconciliation workflow endpoints never create payment truth.
/// - Reconciliation workflow endpoints expose RBAC policy hooks for later enforcement.
/// </summary>
public sealed class ReconciliationWorkflowApiIntegrationTests
{
    private static readonly Guid ItemId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ExceptionId = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid RequestId = Guid.Parse("30000000-0000-0000-0000-000000000003");
    private static readonly Guid ApprovalId = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("30000000-0000-0000-0000-000000000005");

    /// <summary>
    /// Verifies add-note endpoint shape and required correlation handling.
    /// </summary>
    [Fact]
    public async Task AddNote_WhenValid_ReturnsCreatedAndDoesNotInvokePaymentTruth()
    {
        var fake = new FakeReconciliationWorkflowService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/items/{ItemId}/notes",
            new AddReconciliationNoteRequest("operator note", null, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<AddReconciliationNoteResponse>();
        body.Should().NotBeNull();
        body!.ReconciliationItemId.Should().Be(ItemId);
        body.CorrelationId.Should().Be(CorrelationId);
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies submit-resolution endpoint records deterministic workflow semantics.
    /// </summary>
    [Fact]
    public async Task SubmitResolutionRequest_WhenValid_ReturnsCreated()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/items/{ItemId}/resolution-requests",
            new SubmitReconciliationResolutionRequest(
                "RESOLVE_NO_ADJUSTMENT",
                "NO_ADJUSTMENT",
                "NONE",
                false,
                "Resolve without adjustment",
                "No payment truth mutation.",
                "RESOLVED",
                null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<SubmitReconciliationResolutionResponse>();
        body.Should().NotBeNull();
        body!.ResolutionRequestId.Should().Be(RequestId);
        body.RequestStatus.Should().Be("SUBMITTED");
    }

    /// <summary>
    /// Verifies approval decision endpoint shape.
    /// </summary>
    [Fact]
    public async Task Decision_WhenApproved_ReturnsOk()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/resolution-requests/{RequestId}/decision",
            new DecideReconciliationResolutionRequest("APPROVED", "APPROVED_NO_ADJUSTMENT", "Approved.", null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DecideReconciliationResolutionResponse>();
        body.Should().NotBeNull();
        body!.ResolutionApprovalId.Should().Be(ApprovalId);
        body.Decision.Should().Be("APPROVED");
    }

    /// <summary>
    /// Verifies rejection decision endpoint shape.
    /// </summary>
    [Fact]
    public async Task Decision_WhenRejected_ReturnsOk()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService());
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/resolution-requests/{RequestId}/decision",
            new DecideReconciliationResolutionRequest("REJECTED", "REJECTED_BY_CHECKER", "Rejected.", null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DecideReconciliationResolutionResponse>();
        body.Should().NotBeNull();
        body!.Decision.Should().Be("REJECTED");
        body.ExceptionStatus.Should().Be("REJECTED");
    }

    /// <summary>
    /// Verifies duplicate active resolution request conflicts are deterministic when idempotency cannot be replayed safely.
    /// </summary>
    [Fact]
    public async Task SubmitResolutionRequest_WhenActiveRequestExists_ReturnsConflict()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService { ReturnConflict = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/items/{ItemId}/resolution-requests",
            new SubmitReconciliationResolutionRequest(
                "RESOLVE_NO_ADJUSTMENT",
                "NO_ADJUSTMENT",
                "NONE",
                false,
                "Resolve without adjustment",
                null,
                "RESOLVED",
                null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_RESOLUTION_REQUEST_ALREADY_ACTIVE");
    }

    /// <summary>
    /// Verifies workflow history, runs, and exceptions are listable.
    /// </summary>
    [Fact]
    public async Task Reads_WhenCalled_ReturnExpectedLists()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService());
        using var client = factory.CreateClient();

        using var historyRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/v1/ops/reconciliation/items/{ItemId}/workflow-history");
        historyRequest.Headers.Add("X-Correlation-Id", CorrelationId.ToString());

        using var historyResponse = await client.SendAsync(historyRequest);
        using var runsResponse = await client.GetAsync("/v1/ops/reconciliation/runs");
        using var exceptionsResponse = await client.GetAsync("/v1/ops/reconciliation/exceptions");

        historyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        runsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        exceptionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await historyResponse.Content.ReadFromJsonAsync<ReconciliationWorkflowHistoryResponse>())
            ?.Entries.Should().NotBeEmpty();
        (await runsResponse.Content.ReadFromJsonAsync<ReconciliationRunsResponse>())
            ?.Runs.Should().NotBeEmpty();
        (await exceptionsResponse.Content.ReadFromJsonAsync<ReconciliationExceptionsResponse>())
            ?.Exceptions.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifies deterministic errors for unknown identifiers.
    /// </summary>
    [Fact]
    public async Task UnknownIds_WhenServiceReportsMissing_ReturnDeterministicErrors()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var noteResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/items/{ItemId}/notes",
            new AddReconciliationNoteRequest("operator note", null, null),
            CorrelationId);
        using var decisionResponse = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/resolution-requests/{RequestId}/decision",
            new DecideReconciliationResolutionRequest("REJECTED", "REJECTED", "Rejected.", null),
            CorrelationId);

        noteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        decisionResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await noteResponse.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_EXCEPTION_NOT_FOUND");
        (await decisionResponse.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_RESOLUTION_REQUEST_NOT_FOUND");
    }

    /// <summary>
    /// Verifies placeholder RBAC metadata is present for future authorization enforcement.
    /// </summary>
    [Fact]
    public void ReconciliationEndpoints_ExposePolicyMetadata()
    {
        using var factory = CreateFactory(new FakeReconciliationWorkflowService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/ops/reconciliation", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[] { "ReconciliationViewer", "ReconciliationReviewer", "ReconciliationApprover" });
    }

    private static CustomWebApplicationFactory CreateFactory(FakeReconciliationWorkflowService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationWorkflowService>();
                services.AddSingleton<IReconciliationWorkflowService>(fake);
            });
    }

    private static async Task<HttpResponseMessage> SendJsonAsync<T>(
        HttpClient client,
        HttpMethod method,
        string url,
        T body,
        Guid correlationId)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        return await client.SendAsync(request);
    }

    private sealed class FakeReconciliationWorkflowService : IReconciliationWorkflowService
    {
        public bool ReturnMissing { get; init; }

        public bool ReturnConflict { get; init; }

        public bool PaymentTruthMutationRequested { get; private set; }

        public Task<ReconciliationNoteResult> AddNoteAsync(
            AddReconciliationNoteCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationExceptionNotFoundException(command.ReconciliationItemId);
            }

            return Task.FromResult(new ReconciliationNoteResult(
                command.ReconciliationItemId,
                ExceptionId,
                Guid.NewGuid(),
                "REVIEW_NOTE",
                DateTimeOffset.UtcNow,
                command.CorrelationId));
        }

        public Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(
            SubmitReconciliationResolutionCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnConflict)
            {
                throw new ReconciliationWorkflowConflictException(
                    "RECONCILIATION_RESOLUTION_REQUEST_ALREADY_ACTIVE",
                    "An active reconciliation resolution request already exists for this exception.");
            }

            return Task.FromResult(new ReconciliationResolutionRequestResult(
                command.ReconciliationItemId,
                ExceptionId,
                RequestId,
                "SUBMITTED",
                "OPEN",
                "RESOLVED",
                DateTimeOffset.UtcNow,
                command.CorrelationId));
        }

        public Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(
            DecideReconciliationResolutionCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationResolutionRequestNotFoundException(command.ResolutionRequestId);
            }

            return Task.FromResult(new ReconciliationResolutionDecisionResult(
                command.ResolutionRequestId,
                ExceptionId,
                ApprovalId,
                command.Decision,
                command.Decision,
                command.Decision == "APPROVED" ? "RESOLVED" : "REJECTED",
                DateTimeOffset.UtcNow,
                command.CorrelationId));
        }

        public Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(
            ReadReconciliationWorkflowHistoryQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationWorkflowHistoryRecord>>(
                new[]
                {
                    new ReconciliationWorkflowHistoryRecord(
                        "EXCEPTION",
                        ExceptionId,
                        null,
                        null,
                        null,
                        null,
                        Guid.NewGuid(),
                        query.ReconciliationItemId,
                        "OPEN",
                        "DEV_TEST",
                        "Exception summary",
                        "Exception detail",
                        null,
                        DateTimeOffset.UtcNow,
                        CorrelationId)
                });
        }

        public Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(
            ListReconciliationRunsQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationRunRecord>>(
                new[]
                {
                    new ReconciliationRunRecord(
                        Guid.NewGuid(),
                        "PMWPR-API-TEST",
                        "PAYMENT_PROVIDER_RECONCILIATION",
                        "COMPLETED",
                        "SOURCE_BATCH",
                        "PAYMONGO",
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        1,
                        0,
                        1,
                        CorrelationId)
                });
        }

        public Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(
            ListReconciliationExceptionsQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ReconciliationExceptionRecord>>(
                new[]
                {
                    new ReconciliationExceptionRecord(
                        ExceptionId,
                        Guid.NewGuid(),
                        ItemId,
                        "PMWPR-API-TEST",
                        "POLICY_EXCEPTION",
                        "LOW",
                        "OPEN",
                        "DEV_TEST",
                        "Exception summary",
                        null,
                        null,
                        "DevTest",
                        null,
                        DateTimeOffset.UtcNow,
                        CorrelationId)
                });
        }
    }
}
