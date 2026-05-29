using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Application.PaymentAttempts;
using ExitPass.CentralPms.Application.PaymentAttempts.Commands;
using ExitPass.CentralPms.Application.PaymentAttempts.Results;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Contracts.OperatorConsole;
using ExitPass.CentralPms.Domain.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Verifies the Operator Console access evaluation route.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Persistence is delegated to the Operator Console evaluation writer.
/// - The evaluator does not create payment attempts, issue or consume exit authorizations,
///   record provider outcomes, record payment confirmations, or validate gate devices.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationApiIntegrationTests
{
    private static readonly DateTimeOffset EvaluatedAt = DateTimeOffset.Parse("2026-05-29T08:00:00Z");
    private static readonly Guid UserId = Guid.Parse("41000000-0000-0000-0000-000000000001");
    private static readonly Guid OperatorDeviceBindingId = Guid.Parse("41000000-0000-0000-0000-000000000002");
    private static readonly Guid SiteId = Guid.Parse("41000000-0000-0000-0000-000000000003");
    private static readonly Guid SiteGroupId = Guid.Parse("41000000-0000-0000-0000-000000000004");
    private static readonly Guid OperatorShiftId = Guid.Parse("41000000-0000-0000-0000-000000000005");
    private static readonly Guid ParkingSessionId = Guid.Parse("41000000-0000-0000-0000-000000000006");
    private static readonly Guid CorrelationId = Guid.Parse("41000000-0000-0000-0000-000000000007");

    /// <summary>
    /// Verifies the route is mapped at the documented operator-console ops path.
    /// </summary>
    [Fact]
    public void EndpointRouteExists()
    {
        using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText == "/v1/ops/operator-console/access/evaluate")
            .ToArray();

        endpoints.Should().ContainSingle();
        endpoints[0].Metadata.GetMetadata<HttpMethodMetadata>()!
            .HttpMethods.Should().ContainSingle().Which.Should().Be(HttpMethod.Post.Method);
    }

    /// <summary>
    /// Verifies request binding and the allowed evaluator response shape.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenFullRequestPostedAndReadContextIsValid_ReturnsAllowedEvaluatorResult()
    {
        var boundaryTracker = new PaymentAndGateBoundaryTracker();
        using var factory = CreateFactory(boundaryTracker);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/operator-console/access/evaluate",
            CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<OperatorConsoleAccessEvaluationResponse>();
        body.Should().NotBeNull();
        body!.EvaluationId.Should().Be(FakePersistingWriter.EvaluationId);
        body.Allowed.Should().BeTrue();
        body.Decision.Should().Be("ALLOWED");
        body.DenialReasons.Should().BeEmpty();
        body.EffectiveRole.Should().Be("OPERATOR");
        body.DeviceTrust.OperatorDeviceBindingId.Should().Be(OperatorDeviceBindingId);
        body.DeviceTrust.Status.Should().Be("ACTIVE");
        body.DeviceTrust.TrustLevel.Should().Be("BROWSER_KEY_AND_MTLS");
        body.DeviceTrust.Trusted.Should().BeTrue();
        body.ShiftContext.OperatorShiftId.Should().Be(OperatorShiftId);
        body.ShiftContext.Status.Should().Be("ACTIVE");
        body.ShiftContext.Active.Should().BeTrue();
        body.SiteContext.SiteId.Should().Be(SiteId);
        body.SiteContext.SiteGroupId.Should().Be(SiteGroupId);
        body.SiteContext.Assigned.Should().BeTrue();
        body.EvaluatedAt.Should().Be(EvaluatedAt);
        body.Persisted.Should().BeTrue();
        body.CorrelationId.Should().Be(CorrelationId);

        boundaryTracker.TotalCalls.Should().Be(0);
    }

    /// <summary>
    /// Verifies the placeholder never invokes payment or physical-control boundaries.
    /// </summary>
    [Fact]
    public async Task Evaluate_WhenCalled_DoesNotCallPaymentGateProviderOrFinalityBoundaries()
    {
        var boundaryTracker = new PaymentAndGateBoundaryTracker();
        using var factory = CreateFactory(boundaryTracker);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/v1/ops/operator-console/access/evaluate",
            CreateRequest());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        boundaryTracker.CreatePaymentAttemptCalls.Should().Be(0);
        boundaryTracker.RecordPaymentConfirmationCalls.Should().Be(0);
        boundaryTracker.ReportVerifiedProviderOutcomeCalls.Should().Be(0);
        boundaryTracker.FinalizePaymentAttemptCalls.Should().Be(0);
        boundaryTracker.IssueExitAuthorizationCalls.Should().Be(0);
        boundaryTracker.ConsumeExitAuthorizationCalls.Should().Be(0);
        boundaryTracker.GateDeviceValidationCalls.Should().Be(0);
    }

    private static OperatorConsoleAccessEvaluationRequest CreateRequest() =>
        new(
            UserId,
            OperatorDeviceBindingId,
            SiteId,
            SiteGroupId,
            OperatorShiftId,
            "STATUTORY_DISCOUNT_VALIDATION",
            "START_WORKFLOW",
            ParkingSessionId,
            "VIEW_EVIDENCE",
            "operator-console-access-evaluation-test",
            CorrelationId);

    private static CustomWebApplicationFactory CreateFactory(PaymentAndGateBoundaryTracker boundaryTracker)
    {
        return new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<ICreateOrReusePaymentAttemptUseCase>();
                services.RemoveAll<IRecordPaymentConfirmationGateway>();
                services.RemoveAll<IReportVerifiedPaymentOutcomeUseCase>();
                services.RemoveAll<IFinalizePaymentAttemptUseCase>();
                services.RemoveAll<IIssueExitAuthorizationUseCase>();
                services.RemoveAll<IConsumeExitAuthorizationUseCase>();
                services.RemoveAll<IGateDeviceIdentityValidator>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationReadRepository>();
                services.RemoveAll<IOperatorConsoleAccessEvaluationWriter>();
                services.RemoveAll<ISystemClock>();

                services.AddSingleton<ICreateOrReusePaymentAttemptUseCase>(boundaryTracker);
                services.AddSingleton<IRecordPaymentConfirmationGateway>(boundaryTracker);
                services.AddSingleton<IReportVerifiedPaymentOutcomeUseCase>(boundaryTracker);
                services.AddSingleton<IFinalizePaymentAttemptUseCase>(boundaryTracker);
                services.AddSingleton<IIssueExitAuthorizationUseCase>(boundaryTracker);
                services.AddSingleton<IConsumeExitAuthorizationUseCase>(boundaryTracker);
                services.AddSingleton<IGateDeviceIdentityValidator>(boundaryTracker);
                services.AddSingleton<IOperatorConsoleAccessEvaluationReadRepository>(new ValidOperatorConsoleReadRepository());
                services.AddSingleton<IOperatorConsoleAccessEvaluationWriter>(new FakePersistingWriter());
                services.AddSingleton<ISystemClock>(new FixedClock(EvaluatedAt));
            });
    }

    private sealed class FakePersistingWriter : IOperatorConsoleAccessEvaluationWriter
    {
        public static readonly Guid EvaluationId = Guid.Parse("41000000-0000-0000-0000-000000000010");

        public Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
            OperatorConsoleAccessEvaluationResult result,
            CancellationToken cancellationToken) =>
            Task.FromResult(result with
            {
                EvaluationId = EvaluationId,
                Persisted = true
            });
    }

    private sealed class ValidOperatorConsoleReadRepository : IOperatorConsoleAccessEvaluationReadRepository
    {
        public Task<OperatorConsoleAccessEvaluationReadContext> LoadAsync(
            OperatorConsoleAccessEvaluationReadRequest request,
            CancellationToken cancellationToken)
        {
            var context = new OperatorConsoleAccessEvaluationReadContext(
                request,
                new OperatorHrIdentityMappingReadModel(
                    Guid.Parse("41000000-0000-0000-0000-000000000008"),
                    UserId,
                    "MOCK_HR",
                    "ACTIVE",
                    EvaluatedAt.AddHours(-8),
                    EvaluatedAt.AddHours(8),
                    RevokedAt: null,
                    RevocationReasonCode: null),
                new OperatorDeviceBindingReadModel(
                    OperatorDeviceBindingId,
                    "OC-DEVICE-001",
                    "Operator Console Device",
                    SiteGroupId,
                    SiteId,
                    ServiceIdentityId: null,
                    "ACTIVE",
                    "BROWSER_KEY_AND_MTLS",
                    "TEST",
                    LastSeenAt: EvaluatedAt,
                    RevokedAt: null,
                    RevocationReasonCode: null),
                new OperatorDeviceAssignmentReadModel(
                    Guid.Parse("41000000-0000-0000-0000-000000000009"),
                    OperatorDeviceBindingId,
                    SiteGroupId,
                    SiteId,
                    "ACTIVE",
                    "TEST",
                    EvaluatedAt.AddHours(-8),
                    EvaluatedAt.AddHours(8),
                    EndedAt: null),
                new OperatorShiftReadModel(
                    OperatorShiftId,
                    Guid.Parse("41000000-0000-0000-0000-000000000008"),
                    UserId,
                    SiteGroupId,
                    SiteId,
                    "MOCK_HR",
                    "ACTIVE",
                    EvaluatedAt.AddHours(-1),
                    EvaluatedAt.AddHours(7),
                    EvaluatedAt.AddHours(-1),
                    EvaluatedAt.AddHours(7),
                    RevokedAt: null,
                    RevocationReasonCode: null,
                    CurrentTakeoverId: null),
                LatestShiftVersion: null,
                LatestShiftRevocation: null,
                ActiveShiftTakeover: null,
                StatutoryEntitlementFingerprint: null);

            return Task.FromResult(context);
        }
    }

    private sealed class FixedClock : ISystemClock
    {
        public FixedClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class PaymentAndGateBoundaryTracker :
        ICreateOrReusePaymentAttemptUseCase,
        IRecordPaymentConfirmationGateway,
        IReportVerifiedPaymentOutcomeUseCase,
        IFinalizePaymentAttemptUseCase,
        IIssueExitAuthorizationUseCase,
        IConsumeExitAuthorizationUseCase,
        IGateDeviceIdentityValidator
    {
        public int CreatePaymentAttemptCalls { get; private set; }

        public int RecordPaymentConfirmationCalls { get; private set; }

        public int ReportVerifiedProviderOutcomeCalls { get; private set; }

        public int FinalizePaymentAttemptCalls { get; private set; }

        public int IssueExitAuthorizationCalls { get; private set; }

        public int ConsumeExitAuthorizationCalls { get; private set; }

        public int GateDeviceValidationCalls { get; private set; }

        public int TotalCalls =>
            CreatePaymentAttemptCalls +
            RecordPaymentConfirmationCalls +
            ReportVerifiedProviderOutcomeCalls +
            FinalizePaymentAttemptCalls +
            IssueExitAuthorizationCalls +
            ConsumeExitAuthorizationCalls +
            GateDeviceValidationCalls;

        public Task<CreateOrReusePaymentAttemptResult> ExecuteAsync(
            CreateOrReusePaymentAttemptCommand command,
            CancellationToken cancellationToken)
        {
            CreatePaymentAttemptCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not create payment attempts.");
        }

        public Task<RecordPaymentConfirmationResult> RecordAsync(
            RecordPaymentConfirmationCommand command,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            RecordPaymentConfirmationCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not record payment confirmations.");
        }

        public Task<ReportVerifiedPaymentOutcomeResult> ExecuteAsync(
            ReportVerifiedPaymentOutcomeCommand command,
            CancellationToken cancellationToken)
        {
            ReportVerifiedProviderOutcomeCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not report provider outcomes.");
        }

        public Task<FinalizePaymentAttemptResult> ExecuteAsync(
            FinalizePaymentAttemptCommand command,
            CancellationToken cancellationToken)
        {
            FinalizePaymentAttemptCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not finalize payment attempts.");
        }

        public Task<IssueExitAuthorizationResult> ExecuteAsync(
            IssueExitAuthorizationCommand command,
            CancellationToken cancellationToken)
        {
            IssueExitAuthorizationCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not issue exit authorizations.");
        }

        public Task<ConsumeExitAuthorizationResult> ExecuteAsync(
            ConsumeExitAuthorizationCommand command,
            CancellationToken cancellationToken)
        {
            ConsumeExitAuthorizationCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not consume exit authorizations.");
        }

        public Task<GateDeviceIdentityValidationResult> ValidateConsumeAsync(
            GateDeviceIdentityValidationRequest request,
            CancellationToken cancellationToken)
        {
            GateDeviceValidationCalls++;
            throw new InvalidOperationException("Operator Console access evaluation must not validate gate-device consume requests.");
        }
    }
}
