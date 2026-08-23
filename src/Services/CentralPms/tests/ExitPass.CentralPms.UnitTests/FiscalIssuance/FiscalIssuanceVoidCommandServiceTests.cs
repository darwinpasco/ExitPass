using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceVoidCommandServiceTests
{
    private static readonly Guid ReferenceId = Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf");
    private static readonly Guid PosDocumentId = Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395");

    [Theory]
    [InlineData("idempotencyKey", "idempotency_key_required")]
    [InlineData("reasonCode", "reason_code_required")]
    [InlineData("requestedByRef", "requested_by_ref_required")]
    public async Task VoidAsync_WhenRequiredFieldMissing_RejectsBeforePosServerCall(
        string field,
        string expectedError)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var request = field switch
        {
            "idempotencyKey" => Request() with { IdempotencyKey = null },
            "reasonCode" => Request() with { ReasonCode = null },
            "requestedByRef" => Request() with { RequestedByRef = null },
            _ => Request()
        };

        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, request, CancellationToken.None);

        response.HttpStatusCode.Should().Be(400);
        response.Status.Should().Be("fiscal_void_request_rejected");
        response.Errors.Should().Contain(expectedError);
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenReferenceMissing_ReturnsSafeNotFoundBeforePosServerCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(ReferenceId, Arg.Any<CancellationToken>())
            .Returns((FiscalIssuanceReferenceRecord?)null);

        var response = await CreateSut(repository: repository, client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(404);
        response.Status.Should().Be("fiscal_issuance_reference_not_found");
        response.Errors.Should().Contain("fiscal_issuance_reference_not_found");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenRecordedReferenceHasNoPosServerFiscalDocumentId_FailsClosed()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var repository = RepositoryReturning(RecordedReference() with { PosServerFiscalDocumentId = null });

        var response = await CreateSut(repository: repository, client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Status.Should().Be("fiscal_void_reference_rejected");
        response.Errors.Should().Contain("pos_server_fiscal_document_id_required");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenReferenceIsNotRecorded_FailsClosed()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var repository = RepositoryReturning(RecordedReference() with
        {
            FiscalIssuanceState = FiscalIssuanceIntegrationState.FiscalIssuanceFailedService
        });

        var response = await CreateSut(repository: repository, client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("fiscal_reference_not_recorded");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task VoidAsync_WhenPosServerReturnsNewlyVoided_MapsToRecordedAndPassesCallerIdempotency()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(PosServerFiscalDocumentVoidOutcome.NewlyVoided, "newly_voided"));

        var request = Request();
        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, request, CancellationToken.None);

        response.Accepted.Should().BeTrue();
        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be("pos_server_void_recorded");
        response.PosServerResultClassification.Should().Be("newly_voided");
        response.FiscalDocumentNumber.Should().Be("SI-00000002-UAT");
        response.FiscalSequenceValue.Should().Be(2);
        response.NewFiscalNumberAllocated.Should().BeFalse();
        response.PaymentFinalityChanged.Should().BeFalse();
        response.ExitAuthorizationIssued.Should().BeFalse();
        response.GateBehaviorTriggered.Should().BeFalse();
        response.RefundOrReversalCreated.Should().BeFalse();
        response.HikCentralCalled.Should().BeFalse();
        response.PaymentProviderCalled.Should().BeFalse();
        response.RenderingGenerated.Should().BeFalse();
        response.ReplacementFiscalDocumentCreated.Should().BeFalse();
        response.FiscalSequenceChangedByCentralPms.Should().BeFalse();

        await client.Received(1).VoidFiscalDocumentAsync(
            PosDocumentId,
            Arg.Is<PosServerFiscalDocumentVoidRequest>(posRequest =>
                posRequest.IdempotencyKey == request.IdempotencyKey &&
                posRequest.ReasonCode == request.ReasonCode &&
                posRequest.ReasonText == request.ReasonText &&
                posRequest.RequestedByRef == request.RequestedByRef &&
                posRequest.RequestedAt == request.RequestedAt &&
                posRequest.CorrelationId == request.CorrelationId &&
                posRequest.SourceSystemRef == FiscalIssuanceVoidCommandService.SourceSystemRef &&
                posRequest.BusinessDayDate == null),
            Arg.Any<PosServerRoutingContext>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PosServerFiscalDocumentVoidOutcome.IdempotentReplay, "idempotent_replay", "pos_server_void_idempotent_replay", true)]
    [InlineData(PosServerFiscalDocumentVoidOutcome.AlreadyVoided, "already_voided", "pos_server_already_voided", false)]
    public async Task VoidAsync_WhenPosServerReturnsSafeVoidSuccess_MapsDeterministically(
        PosServerFiscalDocumentVoidOutcome outcome,
        string classification,
        string expectedStatus,
        bool expectedReplay)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(outcome, classification));

        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be(expectedStatus);
        response.PosServerResultClassification.Should().Be(classification);
        response.IdempotentReplay.Should().Be(expectedReplay);
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Fact]
    public async Task VoidAsync_WhenPosServerConflict_MapsToConflictHttp409()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(
                PosServerFiscalDocumentVoidOutcome.Conflict,
                "conflict",
                succeeded: false,
                httpStatusCode: 409,
                code: "fiscal_document_void_idempotency_conflict"));

        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.Accepted.Should().BeFalse();
        response.HttpStatusCode.Should().Be(409);
        response.Status.Should().Be("pos_server_void_conflict");
        response.PosServerResultClassification.Should().Be("conflict");
        response.Errors.Should().Contain("fiscal_document_void_idempotency_conflict");
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Theory]
    [InlineData(PosServerFiscalDocumentVoidOutcome.Rejected, 400, "pos_server_void_rejected")]
    [InlineData(PosServerFiscalDocumentVoidOutcome.NotFound, 404, "pos_server_void_rejected")]
    [InlineData(PosServerFiscalDocumentVoidOutcome.FailedService, 503, "pos_server_void_failed")]
    public async Task VoidAsync_WhenPosServerFailure_FailsClosed(
        PosServerFiscalDocumentVoidOutcome outcome,
        int httpStatusCode,
        string expectedStatus)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(outcome, "rejected", succeeded: false, httpStatusCode: httpStatusCode));

        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.Accepted.Should().BeFalse();
        response.HttpStatusCode.Should().Be(httpStatusCode);
        response.Status.Should().Be(expectedStatus);
        response.NewFiscalNumberAllocated.Should().BeFalse();
        response.ReplacementFiscalDocumentCreated.Should().BeFalse();
    }

    [Fact]
    public async Task VoidAsync_WhenPosServerUnavailable_FailsClosed()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<PosServerRoutingContext>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<PosServerFiscalDocumentVoidResult>>(_ => throw new HttpRequestException("POS unavailable"));

        var response = await CreateSut(client: client)
            .VoidAsync(ReferenceId, Request(), CancellationToken.None);

        response.Accepted.Should().BeFalse();
        response.HttpStatusCode.Should().Be(503);
        response.Status.Should().Be("pos_server_void_failed");
        response.Errors.Should().Contain("pos_server_void_failed");
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Fact]
    public void Constructor_DoesNotWireForbiddenOperationalDependencies()
    {
        var constructorTypes = typeof(FiscalIssuanceVoidCommandService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorTypes.Should().Contain(typeof(IFiscalIssuanceReferenceRepository));
        constructorTypes.Should().Contain(typeof(IPosServerFiscalDocumentClient));
        constructorTypes.Should().NotContain(type => type.Name.Contains("PaymentProvider", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Gate", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("HikCentral", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Refund", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Render", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalIssuanceVoidCommandService CreateSut(
        IFiscalIssuanceReferenceRepository? repository = null,
        IPosServerFiscalDocumentClient? client = null)
    {
        return new FiscalIssuanceVoidCommandService(
            repository ?? RepositoryReturning(RecordedReference()),
            client ?? Substitute.For<IPosServerFiscalDocumentClient>(),
            Substitute.For<ILogger<FiscalIssuanceVoidCommandService>>());
    }

    private static IFiscalIssuanceReferenceRepository RepositoryReturning(FiscalIssuanceReferenceRecord? reference)
    {
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(reference);
        return repository;
    }

    private static FiscalIssuanceVoidCommandRequest Request() =>
        new(
            IdempotencyKey: "central-pms-fiscal-void:test-key",
            ReasonCode: "operator_error",
            ReasonText: "Operator requested fiscal void.",
            RequestedByRef: "central-pms-internal-test",
            RequestedAt: DateTimeOffset.Parse("2026-07-10T01:02:03+00:00"),
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df");

    private static PosServerFiscalDocumentVoidResult PosVoidResult(
        PosServerFiscalDocumentVoidOutcome outcome,
        string classification,
        bool succeeded = true,
        int httpStatusCode = 200,
        string? code = null) =>
        new(
            Outcome: outcome,
            Succeeded: succeeded,
            HttpStatusCode: httpStatusCode,
            Code: code ?? (succeeded ? "accepted" : "pos_server_void_failure"),
            Message: succeeded ? "Fiscal document void was recorded." : "POS Server void failed.",
            FiscalDocumentId: PosDocumentId,
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSequenceValue: 2,
            FiscalDocumentStatus: succeeded ? "voided" : null,
            VoidStatus: succeeded ? "recorded" : null,
            VoidedAt: succeeded ? DateTimeOffset.Parse("2026-07-10T00:06:07+08:00") : null,
            VoidReasonCode: succeeded ? "operator_error" : null,
            VoidReasonText: succeeded ? "Operator requested fiscal void." : null,
            RequestedByRef: succeeded ? "central-pms-internal-test" : null,
            IdempotencyKey: succeeded ? "central-pms-fiscal-void:test-key" : null,
            ResultClassification: classification,
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
            ErrorPosture: succeeded ? null : "do_not_retry_without_request_change");

    private static FiscalIssuanceReferenceRecord RecordedReference() =>
        new(
            FiscalIssuanceReferenceId: ReferenceId,
            PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
            PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
            ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
            TariffSnapshotId: null,
            SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
            SitePosServerId: Guid.Parse("10000000-0000-4000-8000-000000000201"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            PosServerFiscalDocumentId: PosDocumentId,
            FiscalIdentityId: Guid.Parse("10000000-0000-4000-8000-000000000301"),
            FiscalSequencePolicyId: Guid.Parse("10000000-0000-4000-8000-000000000401"),
            FiscalSequenceValue: 2,
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSeries: "central-pms-uat-si-sequence-policy",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-09T17:33:57+08:00"),
            FiscalNumberAssignedByRef: "pos-server:system",
            FiscalDocumentStatusCodeId: Guid.Parse("10000000-0000-4000-8000-000000000107"),
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIssuanceState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            LatestExceptionReason: null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"),
            PosServerResponseTimestamp: null,
            FirstRecordedAt: DateTimeOffset.UtcNow,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            RecordedByServiceIdentityId: null);
}
