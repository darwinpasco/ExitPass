using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceControlledUatVoidSmokeServiceTests
{
    [Fact]
    public async Task RunAsync_WhenApprovedProfile_CallsPosServerVoidEndpoint()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(PosServerFiscalDocumentVoidOutcome.NewlyVoided, "newly_voided"));

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be("pos_server_void_recorded");
        response.PosServerResultClassification.Should().Be("newly_voided");
        response.VoidStatus.Should().Be("recorded");
        response.FiscalDocumentStatusPosture.Should().Be("voided");
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

        await client.Received(1).VoidFiscalDocumentAsync(
            Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            Arg.Is<PosServerFiscalDocumentVoidRequest>(request =>
                request.IdempotencyKey == FiscalIssuanceControlledUatVoidSmokeService.ApprovedIdempotencyKey &&
                request.ReasonCode == "CONTROLLED_UAT_REAL_VOID" &&
                request.RequestedByRef == "central-pms-controlled-uat" &&
                request.CorrelationId == "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df" &&
                request.SourceSystemRef == "central-pms" &&
                request.BusinessDayDate == DateOnly.Parse("2026-07-09")),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("idempotent_replay", PosServerFiscalDocumentVoidOutcome.IdempotentReplay, "pos_server_void_idempotent_replay", true)]
    [InlineData("already_voided", PosServerFiscalDocumentVoidOutcome.AlreadyVoided, "pos_server_already_voided", false)]
    public async Task RunAsync_WhenPosServerReturnsSafeSuccess_MapsDeterministically(
        string classification,
        PosServerFiscalDocumentVoidOutcome outcome,
        string expectedStatus,
        bool expectedReplay)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(outcome, classification));

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be(expectedStatus);
        response.PosServerResultClassification.Should().Be(classification);
        response.IdempotentReplay.Should().Be(expectedReplay);
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Theory]
    [InlineData(PosServerFiscalDocumentVoidOutcome.Conflict, 409, "pos_server_void_conflict")]
    [InlineData(PosServerFiscalDocumentVoidOutcome.Rejected, 400, "pos_server_void_rejected")]
    [InlineData(PosServerFiscalDocumentVoidOutcome.NotFound, 404, "pos_server_void_rejected")]
    [InlineData(PosServerFiscalDocumentVoidOutcome.FailedService, 503, "pos_server_void_failed")]
    public async Task RunAsync_WhenPosServerVoidFails_FailsClosed(
        PosServerFiscalDocumentVoidOutcome outcome,
        int httpStatusCode,
        string expectedStatus)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(PosVoidResult(outcome, "rejected", succeeded: false, httpStatusCode: httpStatusCode));

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.Accepted.Should().BeFalse();
        response.Status.Should().Be(expectedStatus);
        response.NewFiscalNumberAllocated.Should().BeFalse();
        response.PaymentFinalityChanged.Should().BeFalse();
        response.ExitAuthorizationIssued.Should().BeFalse();
        response.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenPosServerUnavailable_FailsClosed()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        client.VoidFiscalDocumentAsync(
                Arg.Any<Guid>(),
                Arg.Any<PosServerFiscalDocumentVoidRequest>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<PosServerFiscalDocumentVoidResult>>(_ => throw new HttpRequestException("POS unavailable"));

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.Accepted.Should().BeFalse();
        response.Status.Should().Be("pos_server_void_failed");
        response.Errors.Should().Equal("pos_server_void_failed");
        response.NewFiscalNumberAllocated.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenFiscalDocumentIsUnknown_RejectsBeforePosServerCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest() with { PosServerFiscalDocumentId = Guid.NewGuid() }, CancellationToken.None);

        response.HttpStatusCode.Should().Be(400);
        response.Errors.Should().Contain("pos_server_fiscal_document_id_not_approved");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenReferenceDoesNotMatchApprovedDocument_RejectsBeforePosServerCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var repository = Substitute.For<IFiscalIssuanceReferenceRepository>();
        repository.FindByFiscalIssuanceReferenceIdAsync(
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(RecordedReference() with { PosServerFiscalDocumentId = Guid.NewGuid() });

        var response = await CreateSut(repository: repository, client: client)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("pos_server_fiscal_document_id_mismatch");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenReasonCodeIsWrong_RejectsBeforePosServerCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();

        var response = await CreateSut(client: client)
            .RunAsync(ApprovedRequest() with { ReasonCode = "CONTROLLED_UAT_VOID_SMOKE" }, CancellationToken.None);

        response.HttpStatusCode.Should().Be(400);
        response.Errors.Should().Contain("reason_code_not_approved");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenSafetyGuardRejects_RejectsBeforePosServerCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var guard = Substitute.For<IControlledUatFiscalVoidSafetyGuard>();
        guard.ValidateAsync(
                Arg.Any<ControlledUatFiscalVoidSafetyGuardRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(ControlledUatFiscalVoidSafetyGuardResult.Rejected(
                "controlled_uat_real_void_unsafe_database_name",
                ["unsafe_database_name"]));

        var response = await CreateSut(client: client, safetyGuard: guard)
            .RunAsync(ApprovedRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Status.Should().Be("controlled_uat_real_void_unsafe_database_name");
        await client.DidNotReceiveWithAnyArgs().VoidFiscalDocumentAsync(default, default!, default);
    }

    [Fact]
    public void Constructor_DoesNotWireForbiddenOperationalDependencies()
    {
        var constructorTypes = typeof(FiscalIssuanceControlledUatVoidSmokeService)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorTypes.Should().Contain(typeof(IPosServerFiscalDocumentClient));
        constructorTypes.Should().NotContain(type => type.Name.Contains("PaymentProvider", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Gate", StringComparison.OrdinalIgnoreCase));
        constructorTypes.Should().NotContain(type => type.Name.Contains("Render", StringComparison.OrdinalIgnoreCase));
    }

    private static FiscalIssuanceControlledUatVoidSmokeService CreateSut(
        IFiscalIssuanceReferenceRepository? repository = null,
        IControlledUatFiscalVoidSafetyGuard? safetyGuard = null,
        IPosServerFiscalDocumentClient? client = null,
        FiscalIssuancePosServerIntegrationOptions? options = null,
        FiscalIssuanceExitAuthorizationGatingOptions? gatingOptions = null)
    {
        var resolvedRepository = repository ?? Substitute.For<IFiscalIssuanceReferenceRepository>();
        if (repository is null)
        {
            resolvedRepository.FindByFiscalIssuanceReferenceIdAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<CancellationToken>())
                .Returns(RecordedReference());
        }

        var resolvedGuard = safetyGuard ?? Substitute.For<IControlledUatFiscalVoidSafetyGuard>();
        if (safetyGuard is null)
        {
            resolvedGuard.ValidateAsync(
                    Arg.Any<ControlledUatFiscalVoidSafetyGuardRequest>(),
                    Arg.Any<CancellationToken>())
                .Returns(ControlledUatFiscalVoidSafetyGuardResult.Accepted());
        }

        return new FiscalIssuanceControlledUatVoidSmokeService(
            resolvedRepository,
            resolvedGuard,
            client ?? Substitute.For<IPosServerFiscalDocumentClient>(),
            Options.Create(options ?? EnabledOptions()),
            Options.Create(gatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions()),
            Substitute.For<ILogger<FiscalIssuanceControlledUatVoidSmokeService>>());
    }

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new()
        {
            EnableControlledUatDiagnosticPath = true,
            EnablePosServerFiscalIssuanceLiveCall = true,
            PosServerBaseUrl = "http://localhost:5000",
            EnableLiveFiscalIssuanceFromPaymentFlow = false,
            EnableLiveFiscalIssuanceFromExitFlow = false
        };

    private static ControlledUatFiscalVoidSmokeRequest ApprovedRequest() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalDocumentNumber: "SI-00000002-UAT",
            ReasonCode: "CONTROLLED_UAT_REAL_VOID",
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
            ApprovedBy: "Darwin Pasco",
            ExplicitExecutionApproval: true);

    private static PosServerFiscalDocumentVoidResult PosVoidResult(
        PosServerFiscalDocumentVoidOutcome outcome,
        string classification,
        bool succeeded = true,
        int httpStatusCode = 200) =>
        new(
            Outcome: outcome,
            Succeeded: succeeded,
            HttpStatusCode: httpStatusCode,
            Code: succeeded ? "accepted" : "pos_server_void_failure",
            Message: succeeded ? "Fiscal document void was recorded." : "POS Server void failed.",
            FiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSequenceValue: 2,
            FiscalDocumentStatus: succeeded ? "voided" : null,
            VoidStatus: succeeded ? "recorded" : null,
            VoidedAt: succeeded ? DateTimeOffset.Parse("2026-07-09T14:23:18Z") : null,
            VoidReasonCode: succeeded ? "CONTROLLED_UAT_REAL_VOID" : null,
            VoidReasonText: succeeded ? "Controlled non-production UAT fiscal void integration smoke." : null,
            RequestedByRef: succeeded ? "central-pms-controlled-uat" : null,
            IdempotencyKey: succeeded ? FiscalIssuanceControlledUatVoidSmokeService.ApprovedIdempotencyKey : null,
            ResultClassification: classification,
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
            ErrorPosture: succeeded ? null : "do_not_retry_without_request_change");

    private static FiscalIssuanceReferenceRecord RecordedReference() =>
        new(
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
            PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
            ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
            TariffSnapshotId: null,
            SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
            SitePosServerId: Guid.Parse("10000000-0000-4000-8000-000000000201"),
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityReference: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalIdentityId: Guid.Parse("10000000-0000-4000-8000-000000000301"),
            FiscalSequencePolicyId: Guid.Parse("10000000-0000-4000-8000-000000000401"),
            FiscalSequenceValue: 2,
            FiscalDocumentNumber: "SI-00000002-UAT",
            FiscalSeries: "central-pms-uat-si-sequence-policy",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: "-UAT",
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-09T09:33:57.142508+00:00"),
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
