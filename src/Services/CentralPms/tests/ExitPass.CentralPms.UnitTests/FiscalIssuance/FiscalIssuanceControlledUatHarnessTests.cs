using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceControlledUatHarnessTests
{
    private static readonly Guid FiscalIssuanceReferenceId =
        Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private static readonly Guid PosServerFiscalDocumentId =
        Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    [Fact]
    public async Task ExecuteAsync_WhenRunIdMissing_RejectsWithoutInvokingDiagnosticSeam()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var sut = CreateSut(service: service);

        var result = await sut.ExecuteAsync(ValidRequest() with { RunId = "" }, CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedMissingApprovalOrRunId);
        result.Errors.Should().Contain("run_id_required");
        result.DiagnosticInvoked.Should().BeFalse();
        await service.DidNotReceiveWithAnyArgs()
            .RunPosServerFiscalIssuanceDiagnosticAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEvidenceReferenceAndLocationMissing_Rejects()
    {
        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with { EvidenceReference = "", EvidenceLocation = "" },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput);
        result.Errors.Should().Contain("evidence_reference_or_location_required");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenApprovalReferenceMissing_Rejects()
    {
        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with { ApprovedByRef = "" },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedMissingApprovalOrRunId);
        result.Errors.Should().Contain("approval_reference_required");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSitePosServerContextMissing_Rejects()
    {
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext() with
        {
            SitePosServerId = null,
            SitePosServerRef = null
        };

        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with { FiscalContext = context },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput);
        result.Errors.Should().Contain("site_pos_server_context_required");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenUpstreamFinalityReferenceMissing_Rejects()
    {
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext();

        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with
            {
                FiscalContext = context with
                {
                    PayableBasis = context.PayableBasis with { UpstreamFinalityRef = "" }
                }
            },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput);
        result.Errors.Should().Contain("upstream_finality_reference_required");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "payment-attempt-ref", "payment-confirmation-ref", "central_pms_parking_session_ref_required")]
    [InlineData("parking-session-ref", "", "payment-confirmation-ref", "central_pms_payment_attempt_ref_required")]
    [InlineData("parking-session-ref", "payment-attempt-ref", "", "central_pms_payment_confirmation_ref_required")]
    public async Task ExecuteAsync_WhenPaymentOrSessionReferenceMissing_Rejects(
        string parkingSessionRef,
        string paymentAttemptRef,
        string paymentConfirmationRef,
        string expectedError)
    {
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext() with
        {
            CentralPmsParkingSessionRef = parkingSessionRef,
            CentralPmsPaymentAttemptRef = paymentAttemptRef,
            CentralPmsPaymentConfirmationRef = paymentConfirmationRef
        };

        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with { FiscalContext = context },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput);
        result.Errors.Should().Contain(expectedError);
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenSensitivePayloadIndicatorPresent_Rejects()
    {
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext() with
        {
            ReferenceContext = new Dictionary<string, string> { ["raw payload"] = "not allowed" }
        };

        var result = await CreateSut().ExecuteAsync(
            ValidRequest() with { FiscalContext = context },
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedSensitivePayload);
        result.Errors.Should().Contain("sensitive_payload_indicator_rejected");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenLiveCallDisabled_RejectsWithoutInvokingDiagnosticSeam()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var sut = CreateSut(new FiscalIssuancePosServerIntegrationOptions(), service);

        var result = await sut.ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.Disabled);
        result.Errors.Should().Contain("pos_server_fiscal_issuance_live_call_must_be_enabled");
        result.DiagnosticInvoked.Should().BeFalse();
        await service.DidNotReceiveWithAnyArgs()
            .RunPosServerFiscalIssuanceDiagnosticAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDiagnosticGuardDisabled_RejectsWithoutInvokingDiagnosticSeam()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableControlledUatDiagnosticPath = false,
                PosServerBaseUrl = "https://pos-server.local",
                TimeoutSeconds = 10
            },
            service);

        var result = await sut.ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.DiagnosticDisabled);
        result.Errors.Should().Contain("controlled_uat_diagnostic_path_must_be_enabled");
        result.DiagnosticInvoked.Should().BeFalse();
        await service.DidNotReceiveWithAnyArgs()
            .RunPosServerFiscalIssuanceDiagnosticAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPosServerConfigInvalid_RejectsWithoutInvokingDiagnosticSeam()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableControlledUatDiagnosticPath = true,
                PosServerBaseUrl = "not-a-url",
                TimeoutSeconds = 10
            },
            service);

        var result = await sut.ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidBaseUrl);
        result.Errors.Should().Contain("pos_server_base_url_invalid");
        result.DiagnosticInvoked.Should().BeFalse();
        await service.DidNotReceiveWithAnyArgs()
            .RunPosServerFiscalIssuanceDiagnosticAsync(default, default!, default!, default);
    }

    [Theory]
    [InlineData(true, false, "payment_flow_live_call_guard_must_remain_disabled")]
    [InlineData(false, true, "exit_flow_live_call_guard_must_remain_disabled")]
    public async Task ExecuteAsync_WhenPaymentOrExitFlowGuardEnabled_Rejects(
        bool paymentFlowEnabled,
        bool exitFlowEnabled,
        string expectedError)
    {
        var options = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableControlledUatDiagnosticPath = true,
            PosServerBaseUrl = "https://pos-server.local",
            TimeoutSeconds = 10,
            EnableLiveFiscalIssuanceFromPaymentFlow = paymentFlowEnabled,
            EnableLiveFiscalIssuanceFromExitFlow = exitFlowEnabled
        };

        var result = await CreateSut(options).ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledUnsafeFlowWiring);
        result.Errors.Should().Contain(expectedError);
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenFiscalGatingEnforcementEnabled_Rejects()
    {
        var result = await CreateSut(
                gatingOptions: new FiscalIssuanceExitAuthorizationGatingOptions
                {
                    EnableFiscalBeforeExitAuthorizationEnforcement = true
                })
            .ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady);
        result.Errors.Should().Contain("fiscal_gating_enforcement_must_remain_disabled");
        result.DiagnosticInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenRequestIsValid_InvokesDiagnosticSeamOnce()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        service.RunPosServerFiscalIssuanceDiagnosticAsync(
                FiscalIssuanceReferenceId,
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(DiagnosticResult(FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded));
        var sut = CreateSut(service: service);

        var request = ValidRequest();
        var result = await sut.ExecuteAsync(request, CancellationToken.None);

        result.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded);
        result.ValidationPassed.Should().BeTrue();
        result.DiagnosticInvoked.Should().BeTrue();
        result.PosServerCallAttempted.Should().BeTrue();
        await service.Received(1).RunPosServerFiscalIssuanceDiagnosticAsync(
            request.FiscalIssuanceReferenceId,
            request.FiscalContext,
            request.RecordingContext,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(DiagnosticMappingCases))]
    public async Task ExecuteAsync_MapsDiagnosticResultToHarnessResult(
        string diagnosticStatus,
        string expectedHarnessStatus,
        FiscalIssuanceIntegrationState? fiscalState)
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        service.RunPosServerFiscalIssuanceDiagnosticAsync(
                Arg.Any<Guid>(),
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(DiagnosticResult(diagnosticStatus, fiscalState));
        var sut = CreateSut(service: service);

        var result = await sut.ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.Status.Should().Be(expectedHarnessStatus);
        result.CentralPmsFiscalState.Should().Be(fiscalState);
        result.PaymentFinalityChanged.Should().BeFalse();
        result.ExitAuthorizationIssued.Should().BeFalse();
        result.GateBehaviorTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenDiagnosticReturnsNewlyCreated_ExposesFiscalEvidenceFields()
    {
        var service = Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>();
        service.RunPosServerFiscalIssuanceDiagnosticAsync(
                Arg.Any<Guid>(),
                Arg.Any<CentralPmsFiscalDocumentMappingContext>(),
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(DiagnosticResult(FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded));
        var sut = CreateSut(service: service);

        var result = await sut.ExecuteAsync(ValidRequest(), CancellationToken.None);

        result.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        result.FiscalDocumentId.Should().Be(PosServerFiscalDocumentId);
        result.FiscalDocumentNumber.Should().Be("SI-010001");
        result.FiscalIssuanceEvidenceStatus.Should().Be(FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned);
        result.FiscalNumberAssignmentState.Should().Be(FiscalNumberAssignmentState.Assigned);
    }

    [Fact]
    public void OperationalPaymentAndExitFlows_DoNotDependOnControlledUatHarness()
    {
        var operationalTypes = new[]
        {
            typeof(RecordPaymentConfirmationService),
            typeof(ReportVerifiedPaymentOutcomeHandler),
            typeof(IssueExitAuthorizationHandler)
        };

        var constructorParameterTypes = operationalTypes
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        constructorParameterTypes.Should().NotContain(typeof(IFiscalIssuanceControlledUatHarness));
        constructorParameterTypes.Should().NotContain(typeof(FiscalIssuanceControlledUatHarness));
        constructorParameterTypes.Should().NotContain(typeof(IFiscalIssuancePosServerLiveIntegrationService));
        constructorParameterTypes.Should().NotContain(typeof(IPosServerFiscalDocumentClient));
    }

    private static FiscalIssuanceControlledUatHarness CreateSut(
        FiscalIssuancePosServerIntegrationOptions? options = null,
        IFiscalIssuancePosServerLiveIntegrationService? service = null,
        FiscalIssuanceExitAuthorizationGatingOptions? gatingOptions = null) =>
        new(
            options ?? EnabledOptions(),
            service ?? Substitute.For<IFiscalIssuancePosServerLiveIntegrationService>(),
            gatingOptions);

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new()
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableControlledUatDiagnosticPath = true,
            PosServerBaseUrl = "https://pos-server.local",
            TimeoutSeconds = 10
        };

    private static FiscalIssuanceControlledUatHarnessRequest ValidRequest() =>
        new(
            FiscalIssuanceReferenceId: FiscalIssuanceReferenceId,
            RunId: "uat-run-20260702-001",
            EnvironmentName: "uat",
            EvidenceReference: "uat-evidence-folder/ref-001",
            EvidenceLocation: null,
            EvidenceOwner: "uat-lead",
            ApprovedByRef: "approval-ref",
            FiscalContext: PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext: RecordingContext(),
            ExpectedRunType: FiscalIssuanceControlledUatExpectedRunType.NewlyCreated,
            CorrelationId: "correlation-ref");

    private static PosServerCreateResultRecordingContext RecordingContext() =>
        new(
            UpstreamFinalityReference: "upstream-finality-ref",
            SitePosServerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FiscalDocumentTypeCodeId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CorrelationId: Guid.Parse("cccccccc-3333-3333-3333-333333333333"),
            PosServerResponseTimestamp: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            ServiceIdentityId: Guid.Parse("dddddddd-4444-4444-4444-444444444444"));

    private static FiscalIssuancePosServerDiagnosticResult DiagnosticResult(
        string status,
        FiscalIssuanceIntegrationState? fiscalState = FiscalIssuanceIntegrationState.FiscalIssuanceRecorded)
    {
        var accepted = status is FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded
            or FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded;
        var replay = status == FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded;
        var failure = !accepted;

        return new FiscalIssuancePosServerDiagnosticResult(
            Status: status,
            ReadinessStatus: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady,
            RequestMapped: true,
            ClientCalled: true,
            PosServerResponseClassification: accepted
                ? replay
                    ? FiscalIssuanceResultClassification.IdempotentReplay
                    : FiscalIssuanceResultClassification.NewlyCreated
                : null,
            FiscalIssuanceStateApplied: fiscalState,
            FiscalDocumentId: accepted ? PosServerFiscalDocumentId : null,
            FiscalDocumentNumber: accepted ? "SI-010001" : null,
            FiscalIssuanceEvidenceStatus: accepted ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned : null,
            FiscalNumberAssignmentState: accepted ? FiscalNumberAssignmentState.Assigned : FiscalNumberAssignmentState.NotAssigned,
            ErrorCode: failure ? "pos_server_failure" : null,
            ErrorPosture: failure ? FiscalIssuanceErrorPosture.RetryAfterServiceRecovery : null,
            NoPaymentFinalityChanged: true,
            NoExitAuthorizationIssued: true,
            CorrelationId: Guid.Parse("cccccccc-3333-3333-3333-333333333333"),
            Errors: Array.Empty<string>());
    }

    public static TheoryData<string, string, FiscalIssuanceIntegrationState?> DiagnosticMappingCases() =>
        new()
        {
            {
                FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded,
                FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded,
                FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded,
                FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.ConflictFailureMapped,
                FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped,
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.RequestFailureMapped,
                FiscalIssuanceControlledUatHarnessStatuses.RequestFailureMapped,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.ConfigurationFailureMapped,
                FiscalIssuanceControlledUatHarnessStatuses.ConfigurationFailureMapped,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.ServiceFailureMapped,
                FiscalIssuanceControlledUatHarnessStatuses.ServiceFailureMapped,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService
            },
            {
                FiscalIssuancePosServerDiagnosticStatuses.UnknownFailClosed,
                FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed,
                FiscalIssuanceIntegrationState.FiscalIssuanceUnknown
            }
        };
}
