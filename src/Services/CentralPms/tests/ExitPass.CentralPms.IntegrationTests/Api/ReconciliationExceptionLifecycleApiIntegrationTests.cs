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
/// Verifies the Central PMS reconciliation exception lifecycle API surface.
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
/// - Reconciliation exception lifecycle endpoints never mutate payment or financial truth.
/// - Reconciliation exception lifecycle endpoints expose RBAC policy hooks for later enforcement.
/// </summary>
public sealed class ReconciliationExceptionLifecycleApiIntegrationTests
{
    private static readonly Guid ExceptionId = Guid.Parse("99999999-9999-9999-9999-999999999991");
    private static readonly Guid RunId = Guid.Parse("99999999-9999-9999-9999-999999999992");
    private static readonly Guid UserId = Guid.Parse("99999999-9999-9999-9999-999999999993");
    private static readonly Guid CorrelationId = Guid.Parse("99999999-9999-9999-9999-999999999994");

    /// <summary>
    /// Verifies exception detail readback.
    /// </summary>
    [Fact]
    public async Task ReadException_WhenKnown_ReturnsDetail()
    {
        using var factory = CreateFactory(new FakeLifecycleService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/reconciliation/exceptions/{ExceptionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationExceptionDetailResponse>())!
            .ReconciliationExceptionId.Should().Be(ExceptionId);
    }

    /// <summary>
    /// Verifies assignment is recorded where schema supports assignment fields.
    /// </summary>
    [Fact]
    public async Task Assign_WhenValid_ReturnsLifecycleResultAndDoesNotMutatePaymentTruth()
    {
        var fake = new FakeLifecycleService();
        using var factory = CreateFactory(fake);
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/exceptions/{ExceptionId}/assign",
            new AssignReconciliationExceptionRequest(UserId, null, "ASSIGN", "Assign reviewer.", UserId, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationExceptionLifecycleResponse>())!
            .CurrentStatus.Should().Be("ASSIGNED");
        fake.PaymentTruthMutationRequested.Should().BeFalse();
    }

    /// <summary>
    /// Verifies valid lifecycle transitions succeed.
    /// </summary>
    [Theory]
    [InlineData("status", "UNDER_REVIEW")]
    [InlineData("resolve", "RESOLVED")]
    [InlineData("reject", "REJECTED")]
    [InlineData("escalate", "ESCALATED")]
    public async Task LifecycleAction_WhenValid_ReturnsOk(string action, string expectedStatus)
    {
        using var factory = CreateFactory(new FakeLifecycleService());
        using var client = factory.CreateClient();
        var body = action == "status"
            ? new UpdateReconciliationExceptionStatusRequest(expectedStatus, "REASON", "detail", UserId, null)
            : (object)new ReconciliationExceptionLifecycleRequest("REASON", "detail", UserId, null);

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/exceptions/{ExceptionId}/{action}",
            body,
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationExceptionLifecycleResponse>())!
            .CurrentStatus.Should().Be(expectedStatus);
    }

    /// <summary>
    /// Verifies close succeeds for a resolved exception.
    /// </summary>
    [Fact]
    public async Task Close_WhenResolved_ReturnsOk()
    {
        using var factory = CreateFactory(new FakeLifecycleService { CurrentStatus = "RESOLVED" });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/exceptions/{ExceptionId}/close",
            new ReconciliationExceptionLifecycleRequest("CLOSE", "close reviewed exception", UserId, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationExceptionLifecycleResponse>())!
            .CurrentStatus.Should().Be("CLOSED");
    }

    /// <summary>
    /// Verifies invalid lifecycle transitions fail deterministically.
    /// </summary>
    [Fact]
    public async Task LifecycleAction_WhenInvalidTransition_ReturnsConflict()
    {
        using var factory = CreateFactory(new FakeLifecycleService { ReturnConflict = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/exceptions/{ExceptionId}/close",
            new ReconciliationExceptionLifecycleRequest("CLOSE", "invalid close", UserId, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_EXCEPTION_INVALID_TRANSITION");
    }

    /// <summary>
    /// Verifies terminal exceptions cannot be casually mutated.
    /// </summary>
    [Fact]
    public async Task LifecycleAction_WhenTerminal_ReturnsConflict()
    {
        using var factory = CreateFactory(new FakeLifecycleService { ReturnTerminal = true });
        using var client = factory.CreateClient();

        using var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/v1/ops/reconciliation/exceptions/{ExceptionId}/resolve",
            new ReconciliationExceptionLifecycleRequest("RESOLVE", "terminal", UserId, null),
            CorrelationId);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_EXCEPTION_TERMINAL");
    }

    /// <summary>
    /// Verifies unknown exception ids return deterministic errors.
    /// </summary>
    [Fact]
    public async Task ReadException_WhenMissing_ReturnsDeterministicError()
    {
        using var factory = CreateFactory(new FakeLifecycleService { ReturnMissing = true });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/v1/ops/reconciliation/exceptions/{ExceptionId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadFromJsonAsync<ErrorResponse>())!
            .ErrorCode.Should().Be("RECONCILIATION_EXCEPTION_NOT_FOUND");
    }

    /// <summary>
    /// Verifies existing exception list behavior remains available on GET /exceptions.
    /// </summary>
    [Fact]
    public async Task ListExceptions_WhenCalled_StillUsesExistingWorkflowEndpoint()
    {
        using var factory = CreateFactory(new FakeLifecycleService());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/ops/reconciliation/exceptions");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ReconciliationExceptionsResponse>())!
            .Exceptions.Should().ContainSingle(summary => summary.ReconciliationExceptionId == ExceptionId);
    }

    /// <summary>
    /// Verifies placeholder RBAC metadata is present for future authorization enforcement.
    /// </summary>
    [Fact]
    public void ReconciliationExceptionLifecycleEndpoints_ExposePolicyMetadata()
    {
        using var factory = CreateFactory(new FakeLifecycleService());
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .Where(endpoint => endpoint.DisplayName?.Contains("/v1/ops/reconciliation/exceptions/{reconciliationExceptionId:guid}", StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();

        endpoints.Should().NotBeEmpty();
        endpoints.Select(endpoint => endpoint.Metadata.GetMetadata<ReconciliationPolicyMetadata>()?.PolicyName)
            .Should()
            .Contain(new[]
            {
                "ReconciliationExceptionViewer",
                "ReconciliationExceptionAssignment",
                "ReconciliationExceptionStatusUpdate",
                "ReconciliationExceptionResolution",
                "ReconciliationExceptionRejection",
                "ReconciliationExceptionEscalation",
                "ReconciliationExceptionClosure"
            });
    }

    private static CustomWebApplicationFactory CreateFactory(FakeLifecycleService fake)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReconciliationExceptionLifecycleService>();
                services.AddSingleton<IReconciliationExceptionLifecycleService>(fake);
                services.RemoveAll<IReconciliationWorkflowService>();
                services.AddSingleton<IReconciliationWorkflowService>(new FakeWorkflowService());
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

    private sealed class FakeLifecycleService : IReconciliationExceptionLifecycleService
    {
        public string CurrentStatus { get; init; } = "UNDER_REVIEW";

        public bool ReturnMissing { get; init; }

        public bool ReturnConflict { get; init; }

        public bool ReturnTerminal { get; init; }

        public bool PaymentTruthMutationRequested { get; private set; }

        public Task<ReconciliationExceptionDetailRecord> ReadAsync(
            ReadReconciliationExceptionQuery query,
            CancellationToken cancellationToken)
        {
            if (ReturnMissing)
            {
                throw new ReconciliationExceptionNotFoundException(query.ReconciliationExceptionId);
            }

            return Task.FromResult(ExceptionRecord(CurrentStatus));
        }

        public Task<ReconciliationExceptionLifecycleResult> AssignAsync(
            AssignReconciliationExceptionCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ReconciliationExceptionLifecycleResult(ExceptionId, "OPEN", "ASSIGNED", "ASSIGN", DateTimeOffset.UtcNow, command.CorrelationId));

        public Task<ReconciliationExceptionLifecycleResult> UpdateStatusAsync(
            UpdateReconciliationExceptionStatusCommand command,
            CancellationToken cancellationToken)
        {
            if (ReturnTerminal)
            {
                throw new ReconciliationWorkflowConflictException("RECONCILIATION_EXCEPTION_TERMINAL", "Terminal exception.");
            }

            if (ReturnConflict)
            {
                throw new ReconciliationWorkflowConflictException("RECONCILIATION_EXCEPTION_INVALID_TRANSITION", "Invalid transition.");
            }

            return Task.FromResult(new ReconciliationExceptionLifecycleResult(ExceptionId, CurrentStatus, command.NewStatus, command.Action, DateTimeOffset.UtcNow, command.CorrelationId));
        }
    }

    private sealed class FakeWorkflowService : IReconciliationWorkflowService
    {
        public Task<ReconciliationNoteResult> AddNoteAsync(AddReconciliationNoteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReconciliationResolutionRequestResult> SubmitResolutionRequestAsync(SubmitReconciliationResolutionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ReconciliationResolutionDecisionResult> DecideResolutionRequestAsync(DecideReconciliationResolutionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationWorkflowHistoryRecord>> ReadWorkflowHistoryAsync(ReadReconciliationWorkflowHistoryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReconciliationRunRecord>> ListRunsAsync(ListReconciliationRunsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationRunRecord>>(Array.Empty<ReconciliationRunRecord>());

        public Task<IReadOnlyList<ReconciliationExceptionRecord>> ListExceptionsAsync(ListReconciliationExceptionsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReconciliationExceptionRecord>>(new[]
            {
                new ReconciliationExceptionRecord(
                    ExceptionId,
                    RunId,
                    null,
                    "RECON-API-TEST",
                    "POLICY_EXCEPTION",
                    "LOW",
                    "OPEN",
                    "DEV_TEST",
                    "Exception summary",
                    null,
                    null,
                    "DEV_TEST",
                    null,
                    DateTimeOffset.UtcNow,
                    CorrelationId)
            });
    }

    private static ReconciliationExceptionDetailRecord ExceptionRecord(string status) =>
        new(
            ExceptionId,
            RunId,
            null,
            null,
            "POLICY_EXCEPTION",
            "LOW",
            status,
            "DEV_TEST",
            "Exception summary",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CorrelationId);
}
