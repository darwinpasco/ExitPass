using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceControlledUatEvidenceExporterTests
{
    private static readonly Guid FiscalIssuanceReferenceId =
        Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private static readonly Guid PosServerFiscalDocumentId =
        Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222");

    private readonly FiscalIssuanceControlledUatEvidenceExporter _sut = new();

    [Fact]
    public void BuildEvidence_WhenRequestAndResultAreValid_BuildsStructuredEvidence()
    {
        var export = _sut.BuildEvidence(ExportRequest());

        export.Succeeded.Should().BeTrue();
        export.Evidence.Should().NotBeNull();
        export.Json.Should().NotBeNullOrWhiteSpace();
        export.Evidence!.SchemaVersion.Should().Be("central-pms-pos-server-controlled-uat-evidence.v1");
        export.Evidence.Run.RunId.Should().Be("uat-run-20260702-001");
        export.Evidence.Run.RunTimestamp.Should().Be(DateTimeOffset.Parse("2026-07-02T11:00:00+08:00"));
        export.Evidence.FinalOutcome.Should().Be(FiscalIssuanceControlledUatFinalOutcomes.Passed);
    }

    [Fact]
    public void BuildEvidence_IncludesRunApprovalEnvironmentAndSiteContext()
    {
        var evidence = _sut.BuildEvidence(ExportRequest()).Evidence!;

        evidence.Run.EvidenceReference.Should().Be("uat-evidence-folder/ref-001");
        evidence.Run.EvidenceOwner.Should().Be("uat-lead");
        evidence.Approval.ApprovedByRef.Should().Be("approval-ref");
        evidence.Approval.ReviewerRef.Should().Be("reviewer-ref");
        evidence.Environment.EnvironmentName.Should().Be("uat");
        evidence.SiteContext.SitePosServerId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        evidence.SiteContext.SitePosServerRef.Should().Be("site-pos-server-main");
    }

    [Fact]
    public void BuildEvidence_IncludesUpstreamFinalityAndSafeFiscalRequestFacts()
    {
        var facts = _sut.BuildEvidence(ExportRequest()).Evidence!.FiscalRequestFacts;

        facts.ParkingSessionRef.Should().Be("parking-session-ref");
        facts.PaymentAttemptRef.Should().Be("payment-attempt-ref");
        facts.PaymentConfirmationRef.Should().Be("payment-confirmation-ref");
        facts.PayableBasisRef.Should().Be("payable-basis-ref");
        facts.UpstreamFinalityRef.Should().Be("upstream-finality-ref");
        facts.BusinessDayDate.Should().Be(new DateOnly(2026, 7, 2));
        facts.CurrencyCode.Should().Be("php");
        facts.AmountMinorUnits.Should().Be(12500);
        facts.LineCount.Should().Be(1);
        facts.TenderCount.Should().Be(1);
        facts.TaxDetailCount.Should().Be(1);
        facts.TotalCount.Should().Be(1);
    }

    [Fact]
    public void BuildEvidence_IncludesConfigurationReadinessSummaryWithoutBaseUrlValue()
    {
        var readiness = _sut.BuildEvidence(ExportRequest()).Evidence!.ConfigurationReadiness;
        var json = _sut.BuildEvidence(ExportRequest()).Json!;

        readiness.ReadinessStatus.Should().Be(FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady);
        readiness.LiveCallEnabled.Should().BeTrue();
        readiness.DiagnosticPathEnabled.Should().BeTrue();
        readiness.BaseUrlConfigured.Should().BeTrue();
        readiness.PaymentFlowGuardEnabled.Should().BeFalse();
        readiness.ExitFlowGuardEnabled.Should().BeFalse();
        readiness.FiscalGatingEnforcementEnabled.Should().BeFalse();
        readiness.EnforcementWiredForBlocking.Should().BeFalse();
        json.Should().NotContain("https://pos-server.local");
    }

    [Fact]
    public void BuildEvidence_IncludesDiagnosticAndPosServerResponseFacts()
    {
        var evidence = _sut.BuildEvidence(ExportRequest()).Evidence!;

        evidence.DiagnosticInvocation.HarnessStatus.Should()
            .Be(FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded);
        evidence.DiagnosticInvocation.ValidationPassed.Should().BeTrue();
        evidence.DiagnosticInvocation.DiagnosticInvoked.Should().BeTrue();
        evidence.DiagnosticInvocation.PosServerCallAttempted.Should().BeTrue();
        evidence.PosServerResponse.ResultClassification.Should().Be(FiscalIssuanceResultClassification.NewlyCreated);
        evidence.PosServerResponse.FiscalDocumentId.Should().Be(PosServerFiscalDocumentId);
        evidence.PosServerResponse.FiscalDocumentNumber.Should().Be("SI-010001");
        evidence.PosServerResponse.FiscalIssuanceEvidenceStatus.Should().Be(FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned);
        evidence.PosServerResponse.FiscalNumberAssignmentState.Should().Be(FiscalNumberAssignmentState.Assigned);
    }

    [Fact]
    public void BuildEvidence_IncludesCentralPmsFiscalReferenceResult()
    {
        var fiscalReference = _sut.BuildEvidence(ExportRequest()).Evidence!.CentralPmsFiscalReference;

        fiscalReference.FiscalIssuanceReferenceId.Should().Be(FiscalIssuanceReferenceId);
        fiscalReference.CentralPmsFiscalState.Should().Be(FiscalIssuanceIntegrationState.FiscalIssuanceRecorded);
        fiscalReference.FiscalDocumentIdRecorded.Should().Be(PosServerFiscalDocumentId);
        fiscalReference.FiscalDocumentNumberRecorded.Should().Be("SI-010001");
        fiscalReference.FiscalIssuanceEvidenceStatusRecorded.Should()
            .Be(FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned);
        fiscalReference.FiscalNumberAssignmentStateRecorded.Should().Be(FiscalNumberAssignmentState.Assigned);
    }

    [Fact]
    public void BuildEvidence_IncludesImpactConfirmationsAsUnaffected()
    {
        var impact = _sut.BuildEvidence(ExportRequest()).Evidence!.ImpactConfirmation;

        impact.PaymentFinalityChanged.Should().BeFalse();
        impact.ExitAuthorizationIssued.Should().BeFalse();
        impact.GateBehaviorTriggered.Should().BeFalse();
        impact.PaymentFinalityUnaffected.Should().BeTrue();
        impact.ExitAuthorizationUnaffected.Should().BeTrue();
        impact.GateBehaviorUnaffected.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(OutcomeCases))]
    public void BuildEvidence_MapsHarnessOutcomeSummaries(
        string harnessStatus,
        FiscalIssuanceResultClassification? classification,
        FiscalIssuanceIntegrationState? fiscalState,
        string expectedFinalOutcome)
    {
        var evidence = _sut.BuildEvidence(
                ExportRequest(result: HarnessResult(harnessStatus, classification, fiscalState)))
            .Evidence!;

        evidence.OutcomeSummary.FinalStatus.Should().Be(harnessStatus);
        evidence.FinalOutcome.Should().Be(expectedFinalOutcome);
        evidence.OutcomeSummary.NewlyCreatedRecorded.Should()
            .Be(harnessStatus == FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded);
        evidence.OutcomeSummary.ReplayRecorded.Should()
            .Be(harnessStatus == FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded);
        evidence.OutcomeSummary.ConflictMapped.Should()
            .Be(harnessStatus == FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped);
        evidence.OutcomeSummary.UnknownFailClosed.Should()
            .Be(harnessStatus == FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed);
    }

    [Fact]
    public void BuildEvidence_WhenSensitiveMarkerIsPresentInNotes_RejectsExport()
    {
        var export = _sut.BuildEvidence(ExportRequest(notes: "contains token value"));

        export.Succeeded.Should().BeFalse();
        export.Evidence.Should().BeNull();
        export.Json.Should().BeNull();
        export.SensitiveDataExcluded.Should().BeFalse();
        export.RedactionRequired.Should().BeTrue();
        export.RedactionStatus.Should().Be(FiscalIssuanceControlledUatEvidenceRedactionStatuses.RejectedSensitiveMetadata);
        export.Errors.Should().Contain("sensitive_evidence_metadata_rejected");
    }

    [Fact]
    public void BuildEvidence_WhenSensitiveMarkerIsPresentInMetadata_RejectsExport()
    {
        var export = _sut.BuildEvidence(
            ExportRequest(metadata: new Dictionary<string, string> { ["raw provider callback"] = "not allowed" }));

        export.Succeeded.Should().BeFalse();
        export.Errors.Should().Contain("sensitive_evidence_metadata_rejected");
    }

    [Fact]
    public void SerializeEvidence_ProducesStableCamelCaseJsonWithExpectedFields()
    {
        var export = _sut.BuildEvidence(ExportRequest());

        export.Json.Should().Contain("\"schemaVersion\":");
        export.Json.Should().Contain("\"runId\": \"uat-run-20260702-001\"");
        export.Json.Should().Contain("\"readinessStatus\": \"enabled_ready\"");
        export.Json.Should().Contain("\"posServerCallAttempted\": true");
        export.Json.Should().Contain("\"fiscalDocumentNumber\": \"SI-010001\"");
        export.Json.Should().Contain("\"paymentFinalityChanged\": false");
        export.Json.Should().Contain("\"exitAuthorizationIssued\": false");
        export.Json.Should().Contain("\"gateBehaviorTriggered\": false");
    }

    [Fact]
    public void SerializeEvidence_ExcludesProhibitedSensitiveFieldNamesAndValues()
    {
        var json = _sut.BuildEvidence(ExportRequest()).Json!;
        var prohibitedTerms = new[]
        {
            "PAN",
            "CVV",
            "token",
            "secret",
            "credential",
            "raw provider callback",
            "raw_payload"
        };

        foreach (var term in prohibitedTerms)
        {
            json.Should().NotContain(term, because: "controlled UAT evidence must not expose sensitive payload fields");
        }
    }

    [Fact]
    public void BuildEvidence_DoesNotInvokeDiagnosticSeamOrPosServerClient()
    {
        typeof(FiscalIssuanceControlledUatEvidenceExporter)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Should()
            .NotContain(typeof(IFiscalIssuancePosServerLiveIntegrationService))
            .And.NotContain(typeof(IPosServerFiscalDocumentClient));
    }

    private static FiscalIssuanceControlledUatEvidenceExportRequest ExportRequest(
        FiscalIssuanceControlledUatHarnessResult? result = null,
        string? notes = "reviewed by UAT",
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            HarnessRequest: ValidRequest(),
            HarnessResult: result ?? HarnessResult(
                FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
                FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded),
            PosServerOptions: EnabledOptions(),
            GatingOptions: new FiscalIssuanceExitAuthorizationGatingOptions(),
            RunTimestamp: DateTimeOffset.Parse("2026-07-02T11:00:00+08:00"),
            ReviewerRef: "reviewer-ref",
            Notes: notes,
            SafeMetadata: metadata ?? new Dictionary<string, string> { ["uatBatch"] = "batch-001" });

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
            RecordingContext: new PosServerCreateResultRecordingContext(
                UpstreamFinalityReference: "upstream-finality-ref",
                SitePosServerId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                FiscalDocumentTypeCodeId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                CorrelationId: Guid.Parse("cccccccc-3333-3333-3333-333333333333"),
                PosServerResponseTimestamp: DateTimeOffset.Parse("2026-07-02T10:30:01+08:00"),
                ServiceIdentityId: Guid.Parse("dddddddd-4444-4444-4444-444444444444")),
            ExpectedRunType: FiscalIssuanceControlledUatExpectedRunType.NewlyCreated,
            CorrelationId: "correlation-ref");

    private static FiscalIssuanceControlledUatHarnessResult HarnessResult(
        string status,
        FiscalIssuanceResultClassification? classification,
        FiscalIssuanceIntegrationState? fiscalState)
    {
        var accepted = status is FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded
            or FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded;

        return new FiscalIssuanceControlledUatHarnessResult(
            RunId: "uat-run-20260702-001",
            Status: status,
            ReadinessStatus: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady,
            ValidationPassed: true,
            DiagnosticInvoked: true,
            PosServerCallAttempted: true,
            DiagnosticStatus: status,
            ResultClassification: classification,
            FiscalDocumentId: accepted ? PosServerFiscalDocumentId : null,
            FiscalDocumentNumber: accepted ? "SI-010001" : null,
            FiscalIssuanceEvidenceStatus: accepted ? FiscalIssuanceEvidenceStatus.FiscalDocumentNumberAssigned : null,
            FiscalNumberAssignmentState: accepted ? FiscalNumberAssignmentState.Assigned : FiscalNumberAssignmentState.NotAssigned,
            CentralPmsFiscalState: fiscalState,
            ErrorCode: accepted ? null : "pos_server_failure",
            ErrorPosture: accepted ? null : FiscalIssuanceErrorPosture.RetryAfterServiceRecovery,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            EvidenceReference: "uat-evidence-folder/ref-001",
            EvidenceLocation: null,
            CorrelationId: "correlation-ref",
            Errors: Array.Empty<string>());
    }

    private static FiscalIssuancePosServerIntegrationOptions EnabledOptions() =>
        new()
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            EnableControlledUatDiagnosticPath = true,
            PosServerBaseUrl = "https://pos-server.local",
            TimeoutSeconds = 10
        };

    public static TheoryData<string, FiscalIssuanceResultClassification?, FiscalIssuanceIntegrationState?, string> OutcomeCases() =>
        new()
        {
            {
                FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
                FiscalIssuanceResultClassification.NewlyCreated,
                FiscalIssuanceIntegrationState.FiscalIssuanceRecorded,
                FiscalIssuanceControlledUatFinalOutcomes.Passed
            },
            {
                FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded,
                FiscalIssuanceResultClassification.IdempotentReplay,
                FiscalIssuanceIntegrationState.FiscalIssuanceReplayed,
                FiscalIssuanceControlledUatFinalOutcomes.Passed
            },
            {
                FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped,
                null,
                FiscalIssuanceIntegrationState.FiscalIssuanceConflict,
                FiscalIssuanceControlledUatFinalOutcomes.PassedWithNotes
            },
            {
                FiscalIssuanceControlledUatHarnessStatuses.RequestFailureMapped,
                null,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedRequest,
                FiscalIssuanceControlledUatFinalOutcomes.PassedWithNotes
            },
            {
                FiscalIssuanceControlledUatHarnessStatuses.ServiceFailureMapped,
                null,
                FiscalIssuanceIntegrationState.FiscalIssuanceFailedService,
                FiscalIssuanceControlledUatFinalOutcomes.PassedWithNotes
            },
            {
                FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed,
                null,
                FiscalIssuanceIntegrationState.FiscalIssuanceUnknown,
                FiscalIssuanceControlledUatFinalOutcomes.Inconclusive
            }
        };
}
