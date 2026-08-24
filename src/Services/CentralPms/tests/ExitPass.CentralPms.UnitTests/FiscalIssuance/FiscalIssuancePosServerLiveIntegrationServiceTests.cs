using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuancePosServerLiveIntegrationServiceTests
{
    [Fact]
    public void Options_Defaults_DisableLivePosServerFiscalIssuance()
    {
        var options = new FiscalIssuancePosServerIntegrationOptions();

        options.EnablePosServerFiscalIssuanceLiveCall.Should().BeFalse();
        options.EnableControlledUatDiagnosticPath.Should().BeFalse();
        options.EnableLiveFiscalIssuanceFromPaymentFlow.Should().BeFalse();
        options.EnableLiveFiscalIssuanceFromExitFlow.Should().BeFalse();
        options.Endpoints.Should().BeEmpty();
        options.TimeoutSeconds.Should().Be(10);
    }

    [Fact]
    public void EvaluateReadiness_WhenDefaultOptions_ReportsDisabledWithoutRequiredBaseUrl()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions().EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.Disabled);
        readiness.IsEnabled.Should().BeFalse();
        readiness.IsReady.Should().BeFalse();
        readiness.BaseUrlConfigured.Should().BeFalse();
        readiness.TimeoutConfigured.Should().BeTrue();
        readiness.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateReadiness_WhenDisabledWithBaseUrl_ReportsDisabledConfigPresent()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions()
            .AddEndpoint()
            .EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.DisabledConfigPresent);
        readiness.IsEnabled.Should().BeFalse();
        readiness.IsReady.Should().BeFalse();
        readiness.BaseUrlConfigured.Should().BeTrue();
        readiness.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateReadiness_WhenEnabledWithoutBaseUrl_ReportsMissingBaseUrl()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true
        }.EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledMissingBaseUrl);
        readiness.IsEnabled.Should().BeTrue();
        readiness.IsReady.Should().BeFalse();
        readiness.Errors.Should().Contain("site_pos_server_endpoints_required");
    }

    [Fact]
    public void EvaluateReadiness_WhenEnabledWithInvalidBaseUrl_ReportsInvalidBaseUrl()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true
        }.AddEndpoint("not-a-url").EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidBaseUrl);
        readiness.IsReady.Should().BeFalse();
        readiness.BaseUrlConfigured.Should().BeTrue();
        readiness.Errors.Should().Contain("site_pos_server_endpoint_url_invalid");
    }

    [Fact]
    public void EvaluateReadiness_WhenEnabledWithValidBaseUrl_ReportsReady()
    {
        var readiness = EnabledOptions().EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady);
        readiness.IsEnabled.Should().BeTrue();
        readiness.IsReady.Should().BeTrue();
        readiness.BaseUrlConfigured.Should().BeTrue();
        readiness.TimeoutConfigured.Should().BeTrue();
        readiness.LiveCallsAllowedFromPaymentFlow.Should().BeFalse();
        readiness.LiveCallsAllowedFromExitFlow.Should().BeFalse();
        readiness.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateReadiness_WhenPaymentLiveFlowFlagIsEnabled_ReportsReady()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableLiveFiscalIssuanceFromPaymentFlow = true
        }.AddEndpoint().EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady);
        readiness.IsReady.Should().BeTrue();
        readiness.LiveCallsAllowedFromPaymentFlow.Should().BeTrue();
        readiness.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EvaluateReadiness_WhenPaymentLiveFlowProfileIsMissing_FailsClosed()
    {
        var options = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableLiveFiscalIssuanceFromPaymentFlow = true
        }.AddEndpoint();
        options.Endpoints[0].FiscalDocumentStatusCodeId = null;

        var readiness = options.EvaluateReadiness();

        readiness.IsReady.Should().BeFalse();
        readiness.Errors.Should().Contain("site_pos_server_fiscal_profile_required_for_payment_flow");
    }

    [Fact]
    public void EvaluateReadiness_WhenExitLiveFlowFlagIsEnabled_ReportsUnsafeFlowWiring()
    {
        var readiness = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableLiveFiscalIssuanceFromExitFlow = true
        }.AddEndpoint().EvaluateReadiness();

        readiness.Status.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledUnsafeFlowWiring);
        readiness.IsReady.Should().BeFalse();
        readiness.Errors.Should().Contain("exit_live_flow_flag_must_remain_disabled");
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenDisabled_DoesNotCallClientOrMapper()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions(),
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Disabled);
        result.Code.Should().Be("pos_server_fiscal_issuance_live_call_disabled");
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await orchestration.DidNotReceiveWithAnyArgs().MarkRequestedAsync(default, default!, default);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenEnabledWithoutBaseUrl_FailsSafely()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true
            },
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid);
        result.Errors.Should().Contain("site_pos_server_endpoints_required");
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenEnabledWithInvalidBaseUrl_DoesNotCallClient()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true
            }.AddEndpoint("not-a-url"),
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid);
        result.Errors.Should().Contain("site_pos_server_endpoint_url_invalid");
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await orchestration.DidNotReceiveWithAnyArgs().MarkRequestedAsync(default, default!, default);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenExitFlowFlagIsEnabled_DoesNotCallClient()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableLiveFiscalIssuanceFromExitFlow = true
            }.AddEndpoint(),
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid);
        result.Errors.Should().Contain("exit_live_flow_flag_must_remain_disabled");
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenEnabled_MapsRequestAndCallsMockedClient()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServerResult = CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated);
        client.CreateFiscalDocumentAsync(Arg.Any<PosServerFiscalDocumentCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(posServerResult);
        orchestration.MarkRequestedAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceTransitionContext>(), Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRequested));
        orchestration.ApplyPosServerCreateResultAsync(
                FiscalIssuanceReferenceId,
                posServerResult,
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded));
        var sut = CreateSut(client: client, orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        result.MappedRequest.Should().NotBeNull();
        result.MappedRequest!.PayableBasis.UpstreamFinalityRef.Should().Be("upstream-finality-ref");
        await client.Received(1).CreateFiscalDocumentAsync(
            Arg.Is<PosServerFiscalDocumentCreateRequest>(request =>
                request.UpstreamFinalityRef == "upstream-finality-ref" &&
                request.PayableBasis.UpstreamFinalityRef == "upstream-finality-ref"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_PaymentFlowUsesServerConfiguredLocalFiscalProfile()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServerResult = CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated);
        client.CreateFiscalDocumentAsync(Arg.Any<PosServerFiscalDocumentCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(posServerResult);
        orchestration.MarkRequestedAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceTransitionContext>(), Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRequested));
        orchestration.ApplyPosServerCreateResultAsync(
                FiscalIssuanceReferenceId,
                posServerResult,
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded));
        var options = EnabledOptions();
        options.EnableLiveFiscalIssuanceFromPaymentFlow = true;
        var input = PosServerFiscalDocumentRequestMapperTests.ValidContext() with
        {
            FiscalDocumentTypeCodeId = null,
            FiscalDocumentStatusCodeId = null,
            DocumentLines = PosServerFiscalDocumentRequestMapperTests.ValidContext().DocumentLines
                .Select(line => line with { LineTypeCodeId = null })
                .ToArray(),
            Tenders = PosServerFiscalDocumentRequestMapperTests.ValidContext().Tenders
                .Select(tender => tender with { TenderTypeCodeId = null })
                .ToArray(),
            Totals = PosServerFiscalDocumentRequestMapperTests.ValidContext().Totals
                .Select(total => total with { TotalTypeCodeId = null })
                .ToArray()
        };
        var sut = CreateSut(options, client: client, orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            input,
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.Applied);
        result.MappedRequest.Should().NotBeNull();
        result.MappedRequest!.FiscalDocumentTypeCodeId.Should().Be(SitePosServerTestOptions.FiscalDocumentTypeCodeId);
        result.MappedRequest.FiscalDocumentStatusCodeId.Should().Be(SitePosServerTestOptions.FiscalDocumentStatusCodeId);
        result.MappedRequest.DocumentLines.Should().OnlyContain(line => line.LineTypeCodeId == SitePosServerTestOptions.FiscalLineTypeCodeId);
        result.MappedRequest.Tenders.Should().OnlyContain(tender => tender.TenderTypeCodeId == SitePosServerTestOptions.FiscalTenderTypeCodeId);
        result.MappedRequest.Totals.Should().OnlyContain(total => total.TotalTypeCodeId == SitePosServerTestOptions.FiscalTotalTypeCodeId);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenSemanticHashUnavailable_DoesNotCallClient()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var calculator = Substitute.For<IFiscalSemanticRequestHashCalculator>();
        calculator.Calculate(Arg.Any<PosServerFiscalDocumentCreateRequest>())
            .Returns(new FiscalSemanticRequestHashResult(
                Status: FiscalSemanticRequestHashSourceStatus.Incomplete,
                HashValue: null,
                HashAlgorithm: FiscalSemanticRequestHashCalculator.CurrentHashAlgorithm,
                HashSourceVersion: FiscalSemanticRequestHashCalculator.CurrentHashSourceVersion,
                SourceFactCount: 0,
                SafeSourceSummary: "semantic_request_hash_source_incomplete:document_line_required",
                BlockReasonCode: "document_line_required"));
        var sut = CreateSut(
            semanticRequestHashCalculator: calculator,
            client: client);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerLiveIntegrationStatus.LocalContextInvalid);
        result.Errors.Should().Contain("document_line_required");
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenNewlyCreatedReturned_AppliesRecordedState()
    {
        var result = await ExecuteWithPosServerResultAsync(
            CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated),
            FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);

        result.FiscalIssuanceReference.Should().NotBeNull();
        result.FiscalIssuanceReference!.FiscalIssuanceState.Should()
            .Be(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        result.PosServerResult!.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenReplayReturned_AppliesReplayedState()
    {
        var result = await ExecuteWithPosServerResultAsync(
            CompletePosServerCreateResult(FiscalIssuanceResultClassification.IdempotentReplay),
            FiscalIssuanceIntegrationState.FiscalIssuanceReplayed);

        result.FiscalIssuanceReference.Should().NotBeNull();
        result.FiscalIssuanceReference!.FiscalIssuanceState.Should()
            .Be(FiscalIssuanceIntegrationState.FiscalIssuanceReplayed);
        result.PosServerResult!.ResultClassification.Should().Be(FiscalIssuanceResultClassification.IdempotentReplay);
    }

    [Fact]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenConflictReturned_AppliesConflictState()
    {
        var result = await ExecuteWithPosServerResultAsync(
            FailurePosServerCreateResult(
                PosServerFiscalDocumentOutcome.Conflict,
                409,
                "fiscal_document_idempotency_conflict",
                FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange),
            FiscalIssuanceIntegrationState.FiscalIssuanceConflict);

        result.FiscalIssuanceReference!.FiscalIssuanceState.Should()
            .Be(FiscalIssuanceIntegrationState.FiscalIssuanceConflict);
    }

    [Theory]
    [InlineData(
        PosServerFiscalDocumentOutcome.FailedRequest,
        400,
        "missing_payable_basis",
        FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest)]
    [InlineData(
        PosServerFiscalDocumentOutcome.FailedConfiguration,
        400,
        "fiscal_sequence_policy_not_found",
        FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration)]
    [InlineData(
        PosServerFiscalDocumentOutcome.FailedService,
        503,
        "persistence_write_failed",
        FiscalIssuanceIntegrationState.FiscalIssuanceFailedService)]
    public async Task TryIssueFiscalDocumentViaPosServerAsync_WhenFailureReturned_AppliesFailureState(
        PosServerFiscalDocumentOutcome outcome,
        int httpStatusCode,
        string code,
        FiscalIssuanceIntegrationState expectedState)
    {
        var result = await ExecuteWithPosServerResultAsync(
            FailurePosServerCreateResult(
                outcome,
                httpStatusCode,
                code,
                outcome == PosServerFiscalDocumentOutcome.FailedConfiguration
                    ? FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection
                    : outcome == PosServerFiscalDocumentOutcome.FailedService
                        ? FiscalIssuanceErrorPosture.RetryAfterServiceRecovery
                        : FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange),
            expectedState);

        result.FiscalIssuanceReference!.FiscalIssuanceState.Should().Be(expectedState);
        result.PosServerResult!.Code.Should().Be(code);
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenLiveCallDisabled_ReturnsDisabledAndDoesNotCallMapperClientOrchestration()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions(),
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.Disabled);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.Disabled);
        result.RequestMapped.Should().BeFalse();
        result.ClientCalled.Should().BeFalse();
        result.NoPaymentFinalityChanged.Should().BeTrue();
        result.NoExitAuthorizationIssued.Should().BeTrue();
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        await orchestration.DidNotReceiveWithAnyArgs().MarkRequestedAsync(default, default!, default);
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenDiagnosticGuardDisabled_ReturnsDiagnosticDisabledAndDoesNotCallClient()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            EnabledOptions(),
            mapper,
            client: client,
            orchestration: orchestration);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.DiagnosticDisabled);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady);
        result.RequestMapped.Should().BeFalse();
        result.ClientCalled.Should().BeFalse();
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        await orchestration.DidNotReceiveWithAnyArgs().MarkRequestedAsync(default, default!, default);
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenEnabledWithMissingBaseUrl_FailsSafely()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableControlledUatDiagnosticPath = true
            },
            mapper,
            client: client);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.ConfigurationInvalid);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledMissingBaseUrl);
        result.Errors.Should().Contain("site_pos_server_endpoints_required");
        result.ClientCalled.Should().BeFalse();
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenEnabledWithInvalidBaseUrl_FailsSafely()
    {
        var mapper = Substitute.For<IPosServerFiscalDocumentRequestMapper>();
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var sut = CreateSut(
            new FiscalIssuancePosServerIntegrationOptions
            {
                EnablePosServerFiscalIssuanceLiveCall = true,
                EnableControlledUatDiagnosticPath = true
            }.AddEndpoint("not-a-url"),
            mapper,
            client: client);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.ConfigurationInvalid);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidBaseUrl);
        result.Errors.Should().Contain("site_pos_server_endpoint_url_invalid");
        result.ClientCalled.Should().BeFalse();
        mapper.DidNotReceiveWithAnyArgs().Map(default!);
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenEnabledAndGuarded_MapsRequestAndCallsMockedClient()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var posServerResult = CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated);
        client.CreateFiscalDocumentAsync(Arg.Any<PosServerFiscalDocumentCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(posServerResult);
        orchestration.MarkRequestedAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceTransitionContext>(), Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRequested));
        orchestration.ApplyPosServerCreateResultAsync(
                FiscalIssuanceReferenceId,
                posServerResult,
                Arg.Any<PosServerCreateResultRecordingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded));
        var sut = CreateSut(
            DiagnosticEnabledOptions(),
            client: client,
            orchestration: orchestration);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded);
        result.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady);
        result.RequestMapped.Should().BeTrue();
        result.ClientCalled.Should().BeTrue();
        result.PosServerResponseClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        result.FiscalIssuanceStateApplied.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        result.FiscalDocumentId.Should().Be(PosServerFiscalDocumentId);
        result.FiscalDocumentNumber.Should().Be("SI-010001");
        result.NoPaymentFinalityChanged.Should().BeTrue();
        result.NoExitAuthorizationIssued.Should().BeTrue();
        await client.Received(1).CreateFiscalDocumentAsync(
            Arg.Is<PosServerFiscalDocumentCreateRequest>(request =>
                request.PayableBasis.UpstreamFinalityRef == "upstream-finality-ref"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(DiagnosticResultCases))]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenMockedResultReturned_ReportsDiagnosticStatus(
        PosServerFiscalDocumentCreateResult posServerResult,
        FiscalIssuanceIntegrationState expectedAppliedState,
        string expectedDiagnosticStatus)
    {
        var result = await ExecuteDiagnosticWithPosServerResultAsync(posServerResult, expectedAppliedState);

        result.Status.Should().Be(expectedDiagnosticStatus);
        result.FiscalIssuanceStateApplied.Should().Be(expectedAppliedState);
        result.RequestMapped.Should().BeTrue();
        result.ClientCalled.Should().BeTrue();
        result.NoPaymentFinalityChanged.Should().BeTrue();
        result.NoExitAuthorizationIssued.Should().BeTrue();
    }

    [Fact]
    public async Task RunPosServerFiscalIssuanceDiagnosticAsync_WhenSensitiveReferenceIsPresent_FailsClosedWithoutClientCall()
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        var sut = CreateSut(
            DiagnosticEnabledOptions(),
            client: client,
            orchestration: orchestration);
        var context = PosServerFiscalDocumentRequestMapperTests.ValidContext() with
        {
            ReferenceContext = new Dictionary<string, string> { ["raw_payload"] = "not-allowed" }
        };

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            context,
            RecordingContext(),
            CancellationToken.None);

        result.Status.Should().Be(FiscalIssuancePosServerDiagnosticStatuses.LocalContextInvalid);
        result.Errors.Should().Contain(error => error.Contains("sensitive_payload_reference_rejected", StringComparison.Ordinal));
        result.RequestMapped.Should().BeFalse();
        result.ClientCalled.Should().BeFalse();
        await client.DidNotReceiveWithAnyArgs().CreateFiscalDocumentAsync(default!, default);
        await orchestration.DidNotReceiveWithAnyArgs().MarkRequestedAsync(default, default!, default);
    }

    [Fact]
    public async Task OperationalPaymentAndExitFlows_DoNotDependOnLivePosServerIntegrationSeam()
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

        constructorParameterTypes.Should().NotContain(typeof(IFiscalIssuancePosServerLiveIntegrationService));
        constructorParameterTypes.Should().NotContain(typeof(IPosServerFiscalDocumentClient));
        constructorParameterTypes.Should().NotContain(typeof(IPosServerFiscalDocumentRequestMapper));
    }

    private static FiscalIssuancePosServerLiveIntegrationService CreateSut(
        FiscalIssuancePosServerIntegrationOptions? options = null,
        IPosServerFiscalDocumentRequestMapper? mapper = null,
        IFiscalSemanticRequestHashCalculator? semanticRequestHashCalculator = null,
        IPosServerFiscalDocumentClient? client = null,
        IFiscalIssuanceOrchestrationService? orchestration = null)
    {
        var resolvedOrchestration = orchestration ?? Substitute.For<IFiscalIssuanceOrchestrationService>();
        resolvedOrchestration.RecordSemanticRequestHashAsync(
                Arg.Any<Guid>(),
                Arg.Any<FiscalSemanticRequestHashResult>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance));

        return new FiscalIssuancePosServerLiveIntegrationService(
            options ?? EnabledOptions(),
            mapper ?? new PosServerFiscalDocumentRequestMapper(),
            semanticRequestHashCalculator ?? new FiscalSemanticRequestHashCalculator(),
            client ?? Substitute.For<IPosServerFiscalDocumentClient>(),
            resolvedOrchestration);
    }

    private static async Task<FiscalIssuancePosServerLiveIntegrationResult> ExecuteWithPosServerResultAsync(
        PosServerFiscalDocumentCreateResult posServerResult,
        FiscalIssuanceIntegrationState expectedAppliedState)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        client.CreateFiscalDocumentAsync(Arg.Any<PosServerFiscalDocumentCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(posServerResult);
        orchestration.MarkRequestedAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceTransitionContext>(), Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRequested));
        orchestration.RecordSemanticRequestHashAsync(
                FiscalIssuanceReferenceId,
                Arg.Any<FiscalSemanticRequestHashResult>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance));

        if (posServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted && posServerResult.Succeeded)
        {
            orchestration.ApplyPosServerCreateResultAsync(
                    FiscalIssuanceReferenceId,
                    posServerResult,
                    Arg.Any<PosServerCreateResultRecordingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Reference(expectedAppliedState));
        }
        else
        {
            orchestration.ApplyPosServerFailureResultAsync(
                    FiscalIssuanceReferenceId,
                    posServerResult,
                    Arg.Any<PosServerCreateResultRecordingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Reference(expectedAppliedState));
        }

        var sut = CreateSut(client: client, orchestration: orchestration);

        var result = await sut.TryIssueFiscalDocumentViaPosServerAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        await client.Received(1).CreateFiscalDocumentAsync(
            Arg.Any<PosServerFiscalDocumentCreateRequest>(),
            Arg.Any<CancellationToken>());

        return result;
    }

    private static async Task<FiscalIssuancePosServerDiagnosticResult> ExecuteDiagnosticWithPosServerResultAsync(
        PosServerFiscalDocumentCreateResult posServerResult,
        FiscalIssuanceIntegrationState expectedAppliedState)
    {
        var client = Substitute.For<IPosServerFiscalDocumentClient>();
        var orchestration = Substitute.For<IFiscalIssuanceOrchestrationService>();
        client.CreateFiscalDocumentAsync(Arg.Any<PosServerFiscalDocumentCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(posServerResult);
        orchestration.MarkRequestedAsync(FiscalIssuanceReferenceId, Arg.Any<FiscalIssuanceTransitionContext>(), Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.FiscalIssuanceRequested));
        orchestration.RecordSemanticRequestHashAsync(
                FiscalIssuanceReferenceId,
                Arg.Any<FiscalSemanticRequestHashResult>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Reference(FiscalIssuanceIntegrationState.PendingFiscalIssuance));

        if (posServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted && posServerResult.Succeeded)
        {
            orchestration.ApplyPosServerCreateResultAsync(
                    FiscalIssuanceReferenceId,
                    posServerResult,
                    Arg.Any<PosServerCreateResultRecordingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Reference(expectedAppliedState));
        }
        else
        {
            orchestration.ApplyPosServerFailureResultAsync(
                    FiscalIssuanceReferenceId,
                    posServerResult,
                    Arg.Any<PosServerCreateResultRecordingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns(Reference(expectedAppliedState));
        }

        var sut = CreateSut(DiagnosticEnabledOptions(), client: client, orchestration: orchestration);

        var result = await sut.RunPosServerFiscalIssuanceDiagnosticAsync(
            FiscalIssuanceReferenceId,
            PosServerFiscalDocumentRequestMapperTests.ValidContext(),
            RecordingContext(),
            CancellationToken.None);

        await client.Received(1).CreateFiscalDocumentAsync(
            Arg.Any<PosServerFiscalDocumentCreateRequest>(),
            Arg.Any<CancellationToken>());

        return result;
    }

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            TimeoutSeconds = 10
        }.AddEndpoint();

    private static FiscalIssuancePosServerIntegrationOptions DiagnosticEnabledOptions() =>
        new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableControlledUatDiagnosticPath = true,
            TimeoutSeconds = 10
        }.AddEndpoint();

    public static TheoryData<PosServerFiscalDocumentCreateResult, FiscalIssuanceIntegrationState, string> DiagnosticResultCases() =>
        new()
        {
            {
                CompletePosServerCreateResult(FiscalIssuanceResultClassification.NewlyCreated),
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded
            },
            {
                CompletePosServerCreateResult(FiscalIssuanceResultClassification.IdempotentReplay),
                FiscalIssuanceIntegrationState.FiscalIssuanceReplayed,
                FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded
            },
            {
                FailurePosServerCreateResult(
                    PosServerFiscalDocumentOutcome.Conflict,
                    409,
                    "fiscal_document_idempotency_conflict",
                    FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange),
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
                FiscalIssuancePosServerDiagnosticStatuses.ConflictFailureMapped
            },
            {
                FailurePosServerCreateResult(
                    PosServerFiscalDocumentOutcome.FailedRequest,
                    400,
                    "missing_payable_basis",
                    FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange),
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest,
                FiscalIssuancePosServerDiagnosticStatuses.RequestFailureMapped
            },
            {
                FailurePosServerCreateResult(
                    PosServerFiscalDocumentOutcome.FailedConfiguration,
                    400,
                    "fiscal_sequence_policy_not_found",
                    FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection),
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedConfiguration,
                FiscalIssuancePosServerDiagnosticStatuses.ConfigurationFailureMapped
            },
            {
                FailurePosServerCreateResult(
                    PosServerFiscalDocumentOutcome.FailedService,
                    503,
                    "persistence_write_failed",
                    FiscalIssuanceErrorPosture.RetryAfterServiceRecovery),
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
                FiscalIssuancePosServerDiagnosticStatuses.ServiceFailureMapped
            }
        };

    private static PosServerCreateResultRecordingContext RecordingContext() =>
        new(
            UpstreamFinalityReference: "upstream-finality-ref",
            SitePosServerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            FiscalDocumentTypeCodeId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            ServiceIdentityId: Guid.NewGuid());

    private static PosServerFiscalDocumentCreateResult CompletePosServerCreateResult(
        FiscalIssuanceResultClassification resultClassification) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.Accepted,
            Succeeded: true,
            HttpStatusCode: 202,
            Code: "accepted",
            Message: "accepted",
            FiscalDocumentId: PosServerFiscalDocumentId,
            ResultClassification: resultClassification,
            FiscalIssuanceEvidenceStatus: FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.Assigned,
            FiscalIdentityId: FiscalIdentityId,
            FiscalDocumentStatusCodeId: FiscalDocumentStatusCodeId,
            FiscalSequencePolicyId: FiscalSequencePolicyId,
            FiscalSequenceValue: 10001,
            FiscalDocumentNumber: "SI-010001",
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: DateTimeOffset.Parse("2026-07-02T10:30:00+08:00"),
            FiscalNumberAssignedByRef: "pos-server-runtime",
            ErrorPosture: null);

    private static PosServerFiscalDocumentCreateResult FailurePosServerCreateResult(
        PosServerFiscalDocumentOutcome outcome,
        int httpStatusCode,
        string code,
        FiscalIssuanceErrorPosture? errorPosture) =>
        new(
            Outcome: outcome,
            Succeeded: false,
            HttpStatusCode: httpStatusCode,
            Code: code,
            Message: code,
            FiscalDocumentId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: FiscalNumberAssignmentState.NotAssigned,
            FiscalIdentityId: null,
            FiscalDocumentStatusCodeId: null,
            FiscalSequencePolicyId: null,
            FiscalSequenceValue: null,
            FiscalDocumentNumber: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            FiscalNumberAssignedByRef: null,
            ErrorPosture: errorPosture);

    private static FiscalIssuanceReferenceRecord Reference(FiscalIssuanceIntegrationState state) =>
        new(
            FiscalIssuanceReferenceId: FiscalIssuanceReferenceId,
            PaymentConfirmationId: Guid.NewGuid(),
            PaymentAttemptId: Guid.NewGuid(),
            ParkingSessionId: Guid.NewGuid(),
            TariffSnapshotId: Guid.NewGuid(),
            SiteId: Guid.NewGuid(),
            SitePosServerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SitePosServerRef: "site-pos-server-main",
            PayableBasisRef: "payable-basis-ref",
            UpstreamFinalityReference: "upstream-finality-ref",
            PosServerFiscalDocumentId: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? PosServerFiscalDocumentId
                : null,
            FiscalIdentityId: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIdentityId
                : null,
            FiscalSequencePolicyId: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalSequencePolicyId
                : null,
            FiscalSequenceValue: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? 10001
                : null,
            FiscalDocumentNumber: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? "SI-010001"
                : null,
            FiscalSeries: "SI",
            FiscalNumberPrefixText: "SI-",
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? DateTimeOffset.Parse("2026-07-02T10:30:00+08:00")
                : null,
            FiscalNumberAssignedByRef: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? "pos-server-runtime"
                : null,
            FiscalDocumentStatusCodeId: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalDocumentStatusCodeId
                : null,
            ResultClassification: state == FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIssuanceResultClassification.IdempotentReplay
                : state == FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                    ? FiscalIssuanceResultClassification.NewlyCreated
                    : null,
            FiscalIssuanceEvidenceStatus: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned
                : null,
            FiscalNumberAssignmentState: state is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
                or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed
                ? FiscalNumberAssignmentState.Assigned
                : FiscalNumberAssignmentState.NotAssigned,
            FiscalIssuanceState: state,
            LatestExceptionReason: state == FiscalIssuanceIntegrationState.FiscalIssuanceConflict
                ? FiscalIssuanceExceptionReason.FiscalDocumentIdempotencyConflict
                : null,
            LatestErrorCode: null,
            LatestErrorPosture: null,
            CorrelationId: Guid.NewGuid(),
            PosServerResponseTimestamp: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
            FirstRecordedAt: DateTimeOffset.Parse("2026-07-02T10:30:02+08:00"),
            LastUpdatedAt: DateTimeOffset.Parse("2026-07-02T10:30:03+08:00"),
            RecordedByServiceIdentityId: Guid.NewGuid());

    private static readonly Guid FiscalIssuanceReferenceId =
        Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private static readonly Guid PosServerFiscalDocumentId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid FiscalIdentityId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid FiscalSequencePolicyId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly Guid FiscalDocumentStatusCodeId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");
}
