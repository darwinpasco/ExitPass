using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceControlledUatInvocationServiceTests
{
    private static readonly Guid FiscalDocumentId =
        Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    [Fact]
    public async Task PreflightAsync_WhenControlledDiagnosticPathDisabled_Rejects()
    {
        var harness = Substitute.For<IFiscalIssuanceControlledUatHarness>();
        var options = EnabledOptions();
        options.EnableControlledUatDiagnosticPath = false;
        var sut = CreateSut(
            harness: harness,
            options: options);

        var response = await sut.PreflightAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("controlled_diagnostic_flag_disabled");
        response.DiagnosticInvoked.Should().BeFalse();
        await harness.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenLiveCallSeamDisabled_Rejects()
    {
        var harness = Substitute.For<IFiscalIssuanceControlledUatHarness>();
        var options = EnabledOptions();
        options.EnablePosServerFiscalIssuanceLiveCall = false;
        var sut = CreateSut(harness: harness, options: options);

        var response = await sut.RunAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("live_call_seam_disabled");
        await harness.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_WhenPosServerBaseUrlMissing_Rejects()
    {
        var options = EnabledOptions();
        options.PosServerBaseUrl = null;

        var response = await CreateSut(options: options)
            .RunAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("pos_server_base_url_missing");
    }

    [Fact]
    public async Task RunAsync_WhenPaymentFlowGuardEnabled_Rejects()
    {
        var options = EnabledOptions();
        options.EnableLiveFiscalIssuanceFromPaymentFlow = true;

        var response = await CreateSut(options: options)
            .RunAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("payment_flow_guard_enabled");
    }

    [Fact]
    public async Task RunAsync_WhenExitFlowGuardEnabled_Rejects()
    {
        var options = EnabledOptions();
        options.EnableLiveFiscalIssuanceFromExitFlow = true;

        var response = await CreateSut(options: options)
            .RunAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("exit_flow_guard_enabled");
    }

    [Fact]
    public async Task RunAsync_WhenFiscalGatingEnforcementEnabled_Rejects()
    {
        var response = await CreateSut(gatingOptions: new FiscalIssuanceExitAuthorizationGatingOptions
            {
                EnableFiscalBeforeExitAuthorizationEnforcement = true
            })
            .RunAsync(ValidRequest(), CancellationToken.None);

        response.HttpStatusCode.Should().Be(409);
        response.Errors.Should().Contain("fiscal_gating_enforcement_enabled");
        response.FiscalGatingEnforcementEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task RunAsync_WhenExplicitExecutionApprovalIsNotTrue_Rejects(bool? explicitExecutionApproval)
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { ExplicitExecutionApproval = explicitExecutionApproval },
            CancellationToken.None);

        response.HttpStatusCode.Should().Be(400);
        response.Errors.Should().Contain("explicit_execution_approval_required");
    }

    [Fact]
    public async Task RunAsync_WhenApprovalReferenceMissing_Rejects()
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { ApprovalReference = "" },
            CancellationToken.None);

        response.Errors.Should().Contain("approval_reference_required");
    }

    [Fact]
    public async Task RunAsync_WhenApprovedByMissing_Rejects()
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { ApprovedBy = "" },
            CancellationToken.None);

        response.Errors.Should().Contain("approved_by_required");
    }

    [Fact]
    public async Task RunAsync_WhenFiscalDocumentTypeIsWrong_Rejects()
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { FiscalDocumentType = "credit_memo" },
            CancellationToken.None);

        response.Errors.Should().Contain("wrong_fiscal_document_type");
    }

    [Theory]
    [InlineData(true, false, false, false, "replay_not_allowed_for_first_run")]
    [InlineData(false, true, false, false, "conflict_not_allowed_for_first_run")]
    [InlineData(false, false, true, false, "failure_not_allowed_for_first_run")]
    [InlineData(false, false, false, true, "unknown_not_allowed_for_first_run")]
    public async Task RunAsync_WhenNonFirstRunScenarioIsIncluded_Rejects(
        bool replayIncluded,
        bool conflictIncluded,
        bool failureIncluded,
        bool unknownIncluded,
        string expectedError)
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with
            {
                ReplayIncluded = replayIncluded,
                ConflictIncluded = conflictIncluded,
                FailureIncluded = failureIncluded,
                UnknownIncluded = unknownIncluded
            },
            CancellationToken.None);

        response.Errors.Should().Contain(expectedError);
    }

    [Fact]
    public async Task RunAsync_WhenTotalsMismatch_Rejects()
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { GrandTotal = 9999 },
            CancellationToken.None);

        response.Errors.Should().Contain("totals_mismatch");
    }

    [Fact]
    public async Task RunAsync_WhenSensitiveMarkerPresent_Rejects()
    {
        var response = await CreateSut().RunAsync(
            ValidRequest() with { LineSummary = "raw provider callback payload" },
            CancellationToken.None);

        response.Errors.Should().Contain("sensitive_marker_detected");
        response.SensitiveDataExcluded.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_WhenRequestIsValid_InvokesHarnessOnceAndReturnsSafeEvidence()
    {
        var harness = Substitute.For<IFiscalIssuanceControlledUatHarness>();
        harness.ExecuteAsync(Arg.Any<FiscalIssuanceControlledUatHarnessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<FiscalIssuanceControlledUatHarnessRequest>();
                return HarnessResult(request.RunId);
            });
        var evidencePath = Path.Combine(Path.GetTempPath(), $"exitpass-controlled-uat-{Guid.NewGuid():N}");
        var sut = CreateSut(harness: harness);

        var response = await sut.RunAsync(
            ValidRequest() with { EvidenceLocation = evidencePath },
            CancellationToken.None);

        response.HttpStatusCode.Should().Be(200);
        response.Status.Should().Be(FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded);
        response.EvidenceJson.Should().NotBeNullOrWhiteSpace();
        response.EvidenceJson.Should().Contain("CPS-POS-UAT-20260703-DEV-ATC-001");
        response.PaymentFinalityChanged.Should().BeFalse();
        response.ExitAuthorizationIssued.Should().BeFalse();
        response.GateBehaviorTriggered.Should().BeFalse();
        response.FiscalGatingEnforcementEnabled.Should().BeFalse();
        response.EvidenceFileWritten.Should().BeFalse();
        Directory.Exists(evidencePath).Should().BeFalse();
        await harness.Received(1).ExecuteAsync(
            Arg.Any<FiscalIssuanceControlledUatHarnessRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreflightAsync_WhenRequestIsValid_DoesNotInvokeHarnessOrReturnEvidence()
    {
        var harness = Substitute.For<IFiscalIssuanceControlledUatHarness>();
        var response = await CreateSut(harness: harness).PreflightAsync(ValidRequest(), CancellationToken.None);

        response.Status.Should().Be("preflight_passed");
        response.DiagnosticInvoked.Should().BeFalse();
        response.PosServerCallAttempted.Should().BeFalse();
        response.EvidenceJson.Should().BeNull();
        await harness.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    private static FiscalIssuanceControlledUatInvocationService CreateSut(
        IFiscalIssuanceControlledUatHarness? harness = null,
        FiscalIssuancePosServerIntegrationOptions? options = null,
        FiscalIssuanceExitAuthorizationGatingOptions? gatingOptions = null) =>
        new(
            harness ?? Substitute.For<IFiscalIssuanceControlledUatHarness>(),
            new FiscalIssuanceControlledUatEvidenceExporter(),
            Options.Create(options ?? EnabledOptions()),
            Options.Create(gatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions()));

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new()
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableControlledUatDiagnosticPath = true,
            PosServerBaseUrl = "http://host.docker.internal:8091",
            TimeoutSeconds = 10,
            EnableLiveFiscalIssuanceFromPaymentFlow = false,
            EnableLiveFiscalIssuanceFromExitFlow = false
        };

    private static ControlledUatFiscalIssuanceInvocationRequest ValidRequest() =>
        new(
            RunId: "CPS-POS-UAT-20260703-DEV-ATC-001",
            ApprovalReference: "DEV-UAT-CPS-POS-001",
            ApprovedBy: "Darwin Pasco",
            ExplicitExecutionApproval: true,
            CorrelationId: "00000000-0000-4000-8000-000000000101",
            EnvironmentName: "DEV-CONTROLLED-UAT-LOCAL",
            SiteRef: "DEV-SITE-ATC-001",
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            SitePosServerId: null,
            FiscalDocumentTypeCodeId: null,
            FiscalDocumentStatusCodeId: null,
            FiscalIssuanceReferenceId: null,
            ParkingSessionRef: "DEV-PARKING-SESSION-ATC-001",
            PaymentAttemptRef: "DEV-PAYMENT-ATTEMPT-ATC-001",
            PaymentConfirmationRef: "DEV-PAYMENT-CONFIRMATION-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            UpstreamFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260703-DEV-ATC-001:newly_created:001",
            FiscalDocumentType: "sales_invoice",
            BusinessDayDate: new DateOnly(2026, 7, 3),
            Currency: "PHP",
            AmountMinorUnits: 10000,
            LineSummary: "Parking fee - controlled UAT development test",
            LineCount: 1,
            LineAmountTotal: 10000,
            TenderSummary: "Controlled UAT test tender - non-production",
            TenderCount: 1,
            TenderAmountTotal: 10000,
            TaxDetailPresent: true,
            TaxDetailSummary: "DEV VAT/tax facts aligned to payable basis",
            TaxAmountTotal: 0,
            TotalsPresent: true,
            GrandTotal: 10000,
            TotalsMatchPayableBasis: true,
            ExpectedRunType: "newly_created",
            ReplayIncluded: false,
            ConflictIncluded: false,
            FailureIncluded: false,
            UnknownIncluded: false,
            EvidenceReference: "DEV-UAT-CPS-POS-001",
            EvidenceLocation: @"D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001",
            EvidenceOwner: "Darwin Pasco");

    private static FiscalIssuanceControlledUatHarnessResult HarnessResult(string runId) =>
        new(
            RunId: runId,
            Status: FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
            ReadinessStatus: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady,
            ValidationPassed: true,
            DiagnosticInvoked: true,
            PosServerCallAttempted: true,
            DiagnosticStatus: FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded,
            ResultClassification: FiscalIssuanceResultClassification.NewlyCreated,
            FiscalDocumentId: FiscalDocumentId,
            FiscalDocumentNumber: "SI-010001",
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            CentralPmsFiscalState: FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
            ErrorCode: null,
            ErrorPosture: null,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            EvidenceReference: "DEV-UAT-CPS-POS-001",
            EvidenceLocation: @"D:\ExitPass-UAT-Evidence\DEV-CONTROLLED-UAT-LOCAL\DEV-SITE-ATC-001\2026-07-03\CPS-POS-UAT-20260703-DEV-ATC-001",
            CorrelationId: "00000000-0000-4000-8000-000000000101",
            Errors: Array.Empty<string>());
}
