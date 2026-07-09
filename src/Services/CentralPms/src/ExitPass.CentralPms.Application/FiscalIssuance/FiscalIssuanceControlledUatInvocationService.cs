using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuanceControlledUatInvocationService
{
    Task<ControlledUatFiscalIssuanceInvocationResponse> PreflightAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        CancellationToken cancellationToken);

    Task<ControlledUatFiscalIssuanceInvocationResponse> RunAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        CancellationToken cancellationToken);
}

public interface IControlledUatFiscalIssuanceFixtureStore
{
    Task EnsureApprovedFirstRunFixtureAsync(
        ControlledUatFiscalIssuanceFixture fixture,
        ControlledUatFiscalSmokeProfile profile,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceControlledUatInvocationService : IFiscalIssuanceControlledUatInvocationService
{
    private const string NewlyCreatedScenario = "newly_created";
    private const string ReplayScenario = "replay";
    private const string ConflictScenario = "conflict";

    private static readonly string[] SensitiveTerms =
    [
        "pan",
        "cvv",
        "token",
        "secret",
        "credential",
        "password",
        "raw provider callback",
        "raw_provider_callback",
        "provider callback",
        "provider_callback",
        "raw payload",
        "raw_payload",
        "unmanaged customer pii",
        "unmanaged_customer_pii",
        "customer_pii",
        "raw entitlement evidence",
        "raw_entitlement_evidence",
        "entitlement image",
        "entitlement_image",
        "base64 image",
        "base64_image",
        "uncontrolled image",
        "uncontrolled_image",
        "file blob",
        "file_blob"
    ];

    private static readonly ControlledUatFiscalSmokeProfile DefaultSmokeProfile = new(
        ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
        EnvironmentName: "DEV-CONTROLLED-UAT-LOCAL",
        SiteRef: "DEV-SITE-ATC-001",
        SitePosServerRef: "DEV-POS-SERVER-ATC-001",
        FiscalDocumentType: "sales_invoice",
        RunId: "CPS-POS-UAT-20260709-DEV-ATC-001",
        CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
        UpstreamFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
        ParkingSessionRef: "DEV-PARKING-SESSION-ATC-001",
        PaymentAttemptRef: "DEV-PAYMENT-ATTEMPT-ATC-001",
        PaymentConfirmationRef: "DEV-PAYMENT-FINALITY-ATC-001",
        PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
        Currency: "PHP",
        ApprovalReference: "DEV-UAT-CPS-POS-001",
        BusinessDayDate: new DateOnly(2026, 7, 9),
        AmountMinorUnits: 10000,
        ConflictAmountMinorUnits: 10001,
        TaxAmountMinorUnits: 0,
        SupportedScenarios: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            NewlyCreatedScenario,
            ReplayScenario,
            ConflictScenario
        },
        PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
        PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
        ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
        SiteGroupId: Guid.Parse("00000000-0000-4000-8000-000000000401"),
        SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
        VendorSystemId: Guid.Parse("00000000-0000-4000-8000-000000000501"),
        TariffSnapshotId: Guid.Parse("00000000-0000-4000-8000-000000000601"),
        ServiceIdentityId: Guid.Parse("00000000-0000-4000-8000-000000000901"),
        SitePosServerId: Guid.Parse("10000000-0000-4000-8000-000000000201"),
        FiscalDocumentTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000103"),
        FiscalDocumentStatusCodeId: Guid.Parse("10000000-0000-4000-8000-000000000107"),
        LineTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000108"),
        TenderTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000109"),
        TaxTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000110"),
        TaxClassificationCodeId: Guid.Parse("10000000-0000-4000-8000-000000000111"),
        TotalTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000112"));

    private readonly IFiscalIssuanceControlledUatHarness _harness;
    private readonly IFiscalIssuanceControlledUatEvidenceExporter _evidenceExporter;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;
    private readonly IFiscalIssuanceReferenceRepository _referenceRepository;
    private readonly IControlledUatFiscalIssuanceFixtureStore _fixtureStore;
    private readonly FiscalIssuancePosServerIntegrationOptions _posServerOptions;
    private readonly FiscalIssuanceExitAuthorizationGatingOptions _gatingOptions;
    private readonly ILogger<FiscalIssuanceControlledUatInvocationService>? _logger;

    public FiscalIssuanceControlledUatInvocationService(
        IFiscalIssuanceControlledUatHarness harness,
        IFiscalIssuanceControlledUatEvidenceExporter evidenceExporter,
        IFiscalIssuanceOrchestrationService orchestrationService,
        IFiscalIssuanceReferenceRepository referenceRepository,
        IControlledUatFiscalIssuanceFixtureStore fixtureStore,
        IOptions<FiscalIssuancePosServerIntegrationOptions> posServerOptions,
        IOptions<FiscalIssuanceExitAuthorizationGatingOptions> gatingOptions,
        ILogger<FiscalIssuanceControlledUatInvocationService>? logger = null)
    {
        _harness = harness;
        _evidenceExporter = evidenceExporter;
        _orchestrationService = orchestrationService;
        _referenceRepository = referenceRepository;
        _fixtureStore = fixtureStore;
        _posServerOptions = posServerOptions.Value;
        _gatingOptions = gatingOptions.Value;
        _logger = logger;
    }

    public Task<ControlledUatFiscalIssuanceInvocationResponse> PreflightAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = ValidateRequest(request, out _);
        errors.AddRange(ValidateConfiguration());

        return Task.FromResult(errors.Count == 0
            ? BuildPreflightPassedResponse(request)
            : BuildRejectedResponse(request, "preflight_rejected", errors));
    }

    public async Task<ControlledUatFiscalIssuanceInvocationResponse> RunAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ValidateRequest(request, out var profile);
        errors.AddRange(ValidateConfiguration());
        if (errors.Count > 0)
        {
            return BuildRejectedResponse(request, "run_rejected", errors);
        }

        var replayRequested = IsReplayRequest(request);
        var conflictRequested = IsConflictRequest(request);

        if (!replayRequested && !conflictRequested)
        {
            var fixturePreparation = await EnsureControlledUatFixtureAsync(request, profile!, cancellationToken)
                .ConfigureAwait(false);
            if (fixturePreparation is not null)
            {
                return fixturePreparation;
            }
        }

        var referencePreparation = await PrepareFiscalIssuanceReferenceAsync(
                request,
                profile!,
                replayRequested,
                conflictRequested,
                cancellationToken)
            .ConfigureAwait(false);
        if (referencePreparation.ErrorResponse is not null)
        {
            return referencePreparation.ErrorResponse;
        }

        if (replayRequested)
        {
            return BuildReplayResponse(request, referencePreparation.Reference!);
        }

        if (conflictRequested)
        {
            return BuildConflictResponse(request, referencePreparation.Reference!);
        }

        var harnessRequest = BuildHarnessRequest(request, profile!, referencePreparation.Reference!.FiscalIssuanceReferenceId);
        var harnessResult = await _harness.ExecuteAsync(harnessRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!harnessResult.ValidationPassed)
        {
            return BuildHarnessRejectedResponse(request, harnessResult);
        }

        var export = _evidenceExporter.BuildEvidence(new FiscalIssuanceControlledUatEvidenceExportRequest(
            HarnessRequest: harnessRequest,
            HarnessResult: harnessResult,
            PosServerOptions: _posServerOptions,
            GatingOptions: _gatingOptions,
            RunTimestamp: DateTimeOffset.UtcNow,
            ReviewerRef: request.ApprovedBy,
            Notes: null,
            SafeMetadata: new Dictionary<string, string>
            {
                ["invocation_surface"] = "internal_controlled_uat_endpoint",
                ["approval_reference"] = request.ApprovalReference.Trim()
            }));

        if (!export.Succeeded)
        {
            return BuildRejectedResponse(
                request,
                "evidence_export_rejected",
                export.Errors);
        }

        return new ControlledUatFiscalIssuanceInvocationResponse(
            Accepted: harnessResult.ValidationPassed,
            Status: harnessResult.Status,
            HttpStatusCode: 200,
            Errors: harnessResult.Errors,
            RunId: harnessResult.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: harnessResult.ReadinessStatus,
            ValidationPassed: harnessResult.ValidationPassed,
            DiagnosticInvoked: harnessResult.DiagnosticInvoked,
            PosServerCallAttempted: harnessResult.PosServerCallAttempted,
            DiagnosticStatus: harnessResult.DiagnosticStatus,
            ResultClassification: harnessResult.ResultClassification?.ToString(),
            FiscalDocumentId: harnessResult.FiscalDocumentId,
            FiscalDocumentNumber: harnessResult.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: harnessResult.FiscalIssuanceEvidenceStatus?.ToString(),
            FiscalNumberAssignmentState: harnessResult.FiscalNumberAssignmentState?.ToString(),
            CentralPmsFiscalState: harnessResult.CentralPmsFiscalState?.ToString(),
            ErrorCode: harnessResult.ErrorCode,
            ErrorPosture: harnessResult.ErrorPosture?.ToString(),
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: export.Json,
            EvidenceRedactionStatus: export.RedactionStatus,
            SensitiveDataExcluded: export.SensitiveDataExcluded);
    }

    private async Task<FiscalReferencePreparationResult> PrepareFiscalIssuanceReferenceAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile,
        bool replayRequested,
        bool conflictRequested,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _referenceRepository.FindByUpstreamFinalityReferenceAsync(
                    request.UpstreamFinalityRef!.Trim(),
                    ResolveSitePosServerId(request, profile),
                    ResolveFiscalDocumentTypeCodeId(request, profile),
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                if (replayRequested)
                {
                    return CanReportReplay(existing)
                        ? FiscalReferencePreparationResult.Prepared(existing)
                        : FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                            request,
                            "fiscal_reference_replay_rejected",
                            new[] { "fiscal_reference_replay_not_recorded_state", existing.FiscalIssuanceState.ToString() }));
                }

                if (conflictRequested)
                {
                    return CanReportExistingRecordedEvidence(existing)
                        ? FiscalReferencePreparationResult.Prepared(existing)
                        : FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                            request,
                            "fiscal_reference_conflict_rejected",
                            new[] { "fiscal_reference_conflict_not_recorded_state", existing.FiscalIssuanceState.ToString() }));
                }

                return CanStartDiagnostic(existing)
                    ? FiscalReferencePreparationResult.Prepared(existing)
                    : FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                        request,
                        "fiscal_reference_prepare_rejected",
                        new[] { "fiscal_reference_not_startable_state", existing.FiscalIssuanceState.ToString() }));
            }

            if (replayRequested)
            {
                return FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                    request,
                    "fiscal_reference_replay_rejected",
                    new[] { "fiscal_reference_replay_not_found" }));
            }

            if (conflictRequested)
            {
                return FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                    request,
                    "fiscal_reference_conflict_rejected",
                    new[] { "fiscal_reference_conflict_not_found" }));
            }

            var prepared = await _orchestrationService.PreparePendingAsync(
                    BuildPrepareCommand(request, profile),
                    cancellationToken)
                .ConfigureAwait(false);

            return FiscalReferencePreparationResult.Prepared(prepared);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(
                exception,
                "Controlled UAT fiscal reference preparation failed. profile_id={ProfileId} run_id={RunId} correlation_id={CorrelationId}",
                profile.ProfileId,
                request.RunId,
                request.CorrelationId);

            return FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                request,
                "fiscal_reference_prepare_failed",
                new[] { "fiscal_reference_prepare_failed" }));
        }
    }

    private static ControlledUatFiscalIssuanceInvocationResponse BuildPreflightPassedResponse(
        ControlledUatFiscalIssuanceInvocationRequest request) =>
        new(
            Accepted: true,
            Status: "preflight_passed",
            HttpStatusCode: 200,
            Errors: Array.Empty<string>(),
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady,
            ValidationPassed: true,
            DiagnosticInvoked: false,
            PosServerCallAttempted: false,
            DiagnosticStatus: null,
            ResultClassification: null,
            FiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            CentralPmsFiscalState: null,
            ErrorCode: null,
            ErrorPosture: null,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: null,
            EvidenceRedactionStatus: null,
            SensitiveDataExcluded: true);

    private ControlledUatFiscalIssuanceInvocationResponse BuildReplayResponse(
        ControlledUatFiscalIssuanceInvocationRequest request,
        FiscalIssuanceReferenceRecord reference) =>
        new(
            Accepted: true,
            Status: FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded,
            HttpStatusCode: 200,
            Errors: Array.Empty<string>(),
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: _posServerOptions.EvaluateReadiness().Status,
            ValidationPassed: true,
            DiagnosticInvoked: false,
            PosServerCallAttempted: false,
            DiagnosticStatus: FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded,
            ResultClassification: FiscalIssuanceResultClassification.IdempotentReplay.ToString(),
            FiscalDocumentId: reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: reference.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: reference.FiscalIssuanceEvidenceStatus?.ToString(),
            FiscalNumberAssignmentState: reference.FiscalNumberAssignmentState.ToString(),
            CentralPmsFiscalState: reference.FiscalIssuanceState.ToString(),
            ErrorCode: null,
            ErrorPosture: null,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: null,
            EvidenceRedactionStatus: null,
            SensitiveDataExcluded: true);

    private ControlledUatFiscalIssuanceInvocationResponse BuildConflictResponse(
        ControlledUatFiscalIssuanceInvocationRequest request,
        FiscalIssuanceReferenceRecord reference) =>
        new(
            Accepted: false,
            Status: FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped,
            HttpStatusCode: 409,
            Errors: ["controlled_semantic_conflict_detected", "amount_minor_units_conflict"],
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: _posServerOptions.EvaluateReadiness().Status,
            ValidationPassed: false,
            DiagnosticInvoked: false,
            PosServerCallAttempted: false,
            DiagnosticStatus: FiscalIssuancePosServerDiagnosticStatuses.ConflictFailureMapped,
            ResultClassification: null,
            FiscalDocumentId: reference.PosServerFiscalDocumentId,
            FiscalDocumentNumber: reference.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: reference.FiscalIssuanceEvidenceStatus?.ToString(),
            FiscalNumberAssignmentState: reference.FiscalNumberAssignmentState.ToString(),
            CentralPmsFiscalState: reference.FiscalIssuanceState.ToString(),
            ErrorCode: "controlled_semantic_conflict_detected",
            ErrorPosture: FiscalIssuanceErrorPosture.DoNotRetryWithoutRequestChange.ToString(),
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: null,
            EvidenceRedactionStatus: null,
            SensitiveDataExcluded: true);

    private ControlledUatFiscalIssuanceInvocationResponse BuildRejectedResponse(
        ControlledUatFiscalIssuanceInvocationRequest request,
        string status,
        IReadOnlyList<string> errors)
    {
        var statusCode = errors.Any(IsConflictError) ? 409 : 400;
        return new ControlledUatFiscalIssuanceInvocationResponse(
            Accepted: false,
            Status: status,
            HttpStatusCode: statusCode,
            Errors: errors.Distinct(StringComparer.Ordinal).ToArray(),
            RunId: request.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: _posServerOptions.EvaluateReadiness().Status,
            ValidationPassed: false,
            DiagnosticInvoked: false,
            PosServerCallAttempted: false,
            DiagnosticStatus: null,
            ResultClassification: null,
            FiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            CentralPmsFiscalState: null,
            ErrorCode: errors.FirstOrDefault(),
            ErrorPosture: null,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: null,
            EvidenceRedactionStatus: null,
            SensitiveDataExcluded: !errors.Contains("sensitive_marker_detected", StringComparer.Ordinal));
    }

    private static ControlledUatFiscalIssuanceInvocationResponse BuildHarnessRejectedResponse(
        ControlledUatFiscalIssuanceInvocationRequest request,
        FiscalIssuanceControlledUatHarnessResult harnessResult)
    {
        var statusCode = harnessResult.Status is FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady
            or FiscalIssuanceControlledUatHarnessStatuses.DiagnosticDisabled
            ? 409
            : 400;

        return new ControlledUatFiscalIssuanceInvocationResponse(
            Accepted: false,
            Status: harnessResult.Status,
            HttpStatusCode: statusCode,
            Errors: harnessResult.Errors,
            RunId: harnessResult.RunId,
            CorrelationId: request.CorrelationId,
            ReadinessStatus: harnessResult.ReadinessStatus,
            ValidationPassed: false,
            DiagnosticInvoked: harnessResult.DiagnosticInvoked,
            PosServerCallAttempted: harnessResult.PosServerCallAttempted,
            DiagnosticStatus: harnessResult.DiagnosticStatus,
            ResultClassification: harnessResult.ResultClassification?.ToString(),
            FiscalDocumentId: harnessResult.FiscalDocumentId,
            FiscalDocumentNumber: harnessResult.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: harnessResult.FiscalIssuanceEvidenceStatus?.ToString(),
            FiscalNumberAssignmentState: harnessResult.FiscalNumberAssignmentState?.ToString(),
            CentralPmsFiscalState: harnessResult.CentralPmsFiscalState?.ToString(),
            ErrorCode: harnessResult.ErrorCode ?? harnessResult.Errors.FirstOrDefault(),
            ErrorPosture: harnessResult.ErrorPosture?.ToString(),
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            FiscalGatingEnforcementEnabled: false,
            EvidenceFileWritten: false,
            EvidenceJson: null,
            EvidenceRedactionStatus: null,
            SensitiveDataExcluded: harnessResult.Status != FiscalIssuanceControlledUatHarnessStatuses.RejectedSensitivePayload);
    }

    private async Task<ControlledUatFiscalIssuanceInvocationResponse?> EnsureControlledUatFixtureAsync(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            await _fixtureStore.EnsureApprovedFirstRunFixtureAsync(BuildFixture(request, profile), profile, cancellationToken)
                .ConfigureAwait(false);

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(
                exception,
                "Controlled UAT fixture preparation failed before fiscal reference preparation. run_id={RunId} correlation_id={CorrelationId}",
                request.RunId,
                request.CorrelationId);

            return BuildRejectedResponse(
                request,
                "controlled_uat_fixture_prepare_failed",
                new[] { "controlled_uat_fixture_prepare_failed" });
        }
    }

    private static bool IsConflictError(string error) =>
        error is "controlled_diagnostic_flag_disabled"
            or "live_call_seam_disabled"
            or "pos_server_base_url_missing"
            or "pos_server_base_url_invalid"
            or "payment_flow_guard_enabled"
            or "exit_flow_guard_enabled"
            or "fiscal_gating_enforcement_enabled"
            or "controlled_uat_fixture_prepare_failed"
            or "fiscal_reference_prepare_failed"
            or "fiscal_reference_not_startable_state"
            or "fiscal_reference_replay_not_found"
            or "fiscal_reference_replay_not_recorded_state"
            or "fiscal_reference_conflict_not_found"
            or "fiscal_reference_conflict_not_recorded_state"
            or "controlled_semantic_conflict_detected";

    private IReadOnlyList<string> ValidateConfiguration()
    {
        var errors = new List<string>();

        if (!_posServerOptions.EnableControlledUatDiagnosticPath)
        {
            errors.Add("controlled_diagnostic_flag_disabled");
        }

        if (!_posServerOptions.EnablePosServerFiscalIssuanceLiveCall)
        {
            errors.Add("live_call_seam_disabled");
        }

        if (string.IsNullOrWhiteSpace(_posServerOptions.PosServerBaseUrl))
        {
            errors.Add("pos_server_base_url_missing");
        }
        else if (!Uri.TryCreate(_posServerOptions.PosServerBaseUrl, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("pos_server_base_url_invalid");
        }

        if (_posServerOptions.EnableLiveFiscalIssuanceFromPaymentFlow)
        {
            errors.Add("payment_flow_guard_enabled");
        }

        if (_posServerOptions.EnableLiveFiscalIssuanceFromExitFlow)
        {
            errors.Add("exit_flow_guard_enabled");
        }

        if (_gatingOptions.EnableFiscalBeforeExitAuthorizationEnforcement)
        {
            errors.Add("fiscal_gating_enforcement_enabled");
        }

        return errors;
    }

    private List<string> ValidateRequest(
        ControlledUatFiscalIssuanceInvocationRequest request,
        out ControlledUatFiscalSmokeProfile? profile)
    {
        var errors = new List<string>();
        profile = ResolveProfile(request, errors);

        Require(request.RunId, "run_id_required", errors);
        Require(request.ApprovalReference, "approval_reference_required", errors);
        Require(request.ApprovedBy, "approved_by_required", errors);
        Require(request.CorrelationId, "correlation_id_required", errors);
        Require(request.SiteRef, "site_ref_required", errors);
        Require(request.SitePosServerRef, "site_pos_server_ref_required", errors);
        Require(request.ParkingSessionRef, "parking_session_ref_required", errors);
        Require(request.PaymentAttemptRef, "payment_attempt_ref_required", errors);
        Require(request.PaymentConfirmationRef, "payment_confirmation_ref_required", errors);
        Require(request.PayableBasisRef, "payable_basis_ref_required", errors);
        Require(request.UpstreamFinalityRef, "upstream_finality_ref_required", errors);
        Require(request.FiscalDocumentType, "fiscal_document_type_required", errors);
        Require(request.Currency, "currency_required", errors);
        Require(request.LineSummary, "line_summary_required", errors);
        Require(request.TenderSummary, "tender_summary_required", errors);
        Require(request.TaxDetailSummary, "tax_detail_summary_required", errors);
        Require(request.EvidenceOwner, "evidence_owner_required", errors);
        Require(request.EvidenceLocation, "evidence_location_required", errors);

        if (request.ExplicitExecutionApproval != true)
        {
            errors.Add("explicit_execution_approval_required");
        }

        if (profile is not null &&
            !string.Equals(request.FiscalDocumentType, profile.FiscalDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("wrong_fiscal_document_type");
        }

        var replayRequested = IsReplayRequest(request);
        var conflictRequested = IsConflictRequest(request);

        if (!IsSupportedScenarioName(request.ExpectedRunType))
        {
            errors.Add("expected_run_type_must_be_newly_created_replay_or_conflict");
        }
        else if (profile is not null && !profile.SupportsScenario(request.ExpectedRunType))
        {
            errors.Add("scenario_not_allowlisted_for_profile");
        }

        if (!replayRequested && request.ReplayIncluded)
        {
            errors.Add("replay_not_allowed_for_first_run");
        }

        if (replayRequested && !request.ReplayIncluded)
        {
            errors.Add("replay_flag_required_for_replay_run");
        }

        if (!conflictRequested && request.ConflictIncluded)
        {
            errors.Add("conflict_not_allowed_for_first_run");
        }

        if (conflictRequested && !request.ConflictIncluded)
        {
            errors.Add("conflict_flag_required_for_conflict_run");
        }

        if (conflictRequested && request.ReplayIncluded)
        {
            errors.Add("replay_not_allowed_for_conflict_run");
        }

        if (request.FailureIncluded)
        {
            errors.Add("failure_not_allowed_for_first_run");
        }

        if (request.UnknownIncluded)
        {
            errors.Add("unknown_not_allowed_for_first_run");
        }

        if (request.TotalsMatchPayableBasis != true)
        {
            errors.Add("totals_match_payable_basis_required");
        }

        if (!request.TaxDetailPresent)
        {
            errors.Add("tax_detail_present_required");
        }

        if (!request.TotalsPresent)
        {
            errors.Add("totals_present_required");
        }

        ValidateTotals(request, errors);
        if (profile is not null)
        {
            ValidateApprovedProfileValues(request, profile, errors);
        }

        if (EnumerateStrings(request).Where(value => !string.IsNullOrWhiteSpace(value)).Any(ContainsSensitiveTerm))
        {
            errors.Add("sensitive_marker_detected");
        }

        return errors;
    }

    private ControlledUatFiscalSmokeProfile? ResolveProfile(
        ControlledUatFiscalIssuanceInvocationRequest request,
        List<string> errors)
    {
        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? request.RunId?.Trim()
            : request.ProfileId.Trim();

        if (string.IsNullOrWhiteSpace(profileId))
        {
            errors.Add("profile_id_required");
            return null;
        }

        var configuredProfiles = _posServerOptions.ControlledUatSmokeProfiles ?? [];
        return ControlledUatFiscalSmokeProfileCatalog.TryResolve(
            profileId,
            DefaultSmokeProfile,
            configuredProfiles,
            errors);
    }

    private static void ValidateTotals(
        ControlledUatFiscalIssuanceInvocationRequest request,
        List<string> errors)
    {
        if (request.AmountMinorUnits <= 0)
        {
            errors.Add("amount_minor_units_required");
        }

        if (request.LineCount != 1)
        {
            errors.Add("line_count_must_be_one_for_first_run");
        }

        if (request.TenderCount != 1)
        {
            errors.Add("tender_count_must_be_one_for_first_run");
        }

        if (request.LineAmountTotal != request.AmountMinorUnits ||
            request.TenderAmountTotal != request.AmountMinorUnits ||
            request.GrandTotal != request.AmountMinorUnits)
        {
            errors.Add("totals_mismatch");
        }

        if (request.TaxAmountTotal < 0)
        {
            errors.Add("tax_amount_total_invalid");
        }
    }

    private static void ValidateApprovedProfileValues(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile,
        List<string> errors)
    {
        CheckEquals(request.EnvironmentName, profile.EnvironmentName, "environment_not_approved_for_profile", errors);
        CheckEquals(request.SiteRef, profile.SiteRef, "site_ref_not_approved_for_profile", errors);
        CheckEquals(request.SitePosServerRef, profile.SitePosServerRef, "site_pos_server_ref_not_approved_for_profile", errors);
        CheckEquals(request.RunId, profile.RunId, "run_id_not_approved_for_profile", errors);
        CheckEquals(request.CorrelationId, profile.CorrelationId, "correlation_id_not_approved_for_profile", errors);
        CheckEquals(request.UpstreamFinalityRef, profile.UpstreamFinalityRef, "upstream_finality_ref_not_approved_for_profile", errors);
        CheckEquals(request.ParkingSessionRef, profile.ParkingSessionRef, "parking_session_ref_not_approved_for_profile", errors);
        CheckEquals(request.PaymentAttemptRef, profile.PaymentAttemptRef, "payment_attempt_ref_not_approved_for_profile", errors);
        CheckEquals(request.PaymentConfirmationRef, profile.PaymentConfirmationRef, "payment_confirmation_ref_not_approved_for_profile", errors);
        CheckEquals(request.PayableBasisRef, profile.PayableBasisRef, "payable_basis_ref_not_approved_for_profile", errors);
        CheckEquals(request.Currency, profile.Currency, "currency_not_approved_for_profile", errors);
        CheckEquals(request.ApprovalReference, profile.ApprovalReference, "approval_reference_not_approved_for_profile", errors);

        if (request.BusinessDayDate != profile.BusinessDayDate)
        {
            errors.Add("business_day_date_not_approved_for_profile");
        }

        var approvedAmount = IsConflictRequest(request)
            ? profile.ConflictAmountMinorUnits
            : profile.AmountMinorUnits;
        var amountError = IsConflictRequest(request)
            ? "amount_conflict_value_not_approved_for_profile"
            : "amounts_not_approved_for_profile";

        if (request.AmountMinorUnits != approvedAmount ||
            request.LineAmountTotal != approvedAmount ||
            request.TenderAmountTotal != approvedAmount ||
            request.GrandTotal != approvedAmount)
        {
            errors.Add(amountError);
        }

        if (request.TaxAmountTotal != profile.TaxAmountMinorUnits)
        {
            errors.Add("tax_amount_not_approved_for_profile");
        }
    }

    private static void Require(string? value, string error, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(error);
        }
    }

    private static void CheckEquals(
        string? actual,
        string expected,
        string error,
        List<string> errors)
    {
        if (!string.Equals(actual?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(error);
        }
    }

    private static PrepareFiscalIssuanceCommand BuildPrepareCommand(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        new(
            PaymentConfirmationId: profile.PaymentConfirmationId,
            PaymentAttemptId: profile.PaymentAttemptId,
            ParkingSessionId: profile.ParkingSessionId,
            TariffSnapshotId: profile.TariffSnapshotId,
            SiteId: profile.SiteId,
            SitePosServerId: ResolveSitePosServerId(request, profile),
            SitePosServerRef: request.SitePosServerRef?.Trim(),
            FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request, profile),
            FiscalDocumentTypeCodeKey: request.FiscalDocumentType?.Trim(),
            PayableBasisRef: request.PayableBasisRef?.Trim(),
            UpstreamFinalityReference: request.UpstreamFinalityRef!.Trim(),
            CorrelationId: Guid.Parse(request.CorrelationId!.Trim()),
            ServiceIdentityId: profile.ServiceIdentityId);

    private static ControlledUatFiscalIssuanceFixture BuildFixture(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        new(
            ProfileId: profile.ProfileId,
            PaymentConfirmationId: profile.PaymentConfirmationId,
            PaymentAttemptId: profile.PaymentAttemptId,
            ParkingSessionId: profile.ParkingSessionId,
            TariffSnapshotId: profile.TariffSnapshotId,
            ServiceIdentityId: profile.ServiceIdentityId,
            SiteGroupId: profile.SiteGroupId,
            SiteId: profile.SiteId,
            VendorSystemId: profile.VendorSystemId,
            RunId: request.RunId!.Trim(),
            CorrelationId: Guid.Parse(request.CorrelationId!.Trim()),
            SiteRef: request.SiteRef!.Trim(),
            ParkingSessionRef: request.ParkingSessionRef!.Trim(),
            PaymentAttemptRef: request.PaymentAttemptRef!.Trim(),
            PaymentConfirmationRef: request.PaymentConfirmationRef!.Trim(),
            UpstreamFinalityRef: request.UpstreamFinalityRef!.Trim(),
            Currency: request.Currency!.Trim().ToUpperInvariant(),
            AmountMinorUnits: request.AmountMinorUnits,
            BusinessDayDate: request.BusinessDayDate!.Value);

    private static bool CanStartDiagnostic(FiscalIssuanceReferenceRecord reference) =>
        reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.PendingFiscalIssuance
            or FiscalIssuanceIntegrationState.FiscalIssuanceRequested;

    private static bool CanReportReplay(FiscalIssuanceReferenceRecord reference) =>
        CanReportExistingRecordedEvidence(reference);

    private static bool CanReportExistingRecordedEvidence(FiscalIssuanceReferenceRecord reference) =>
        (reference.FiscalIssuanceState is FiscalIssuanceIntegrationState.FiscalIssuanceRecorded
            or FiscalIssuanceIntegrationState.FiscalIssuanceReplayed) &&
            reference.PosServerFiscalDocumentId is not null &&
            !string.IsNullOrWhiteSpace(reference.FiscalDocumentNumber) &&
            reference.FiscalSequenceValue is > 0;

    private static bool IsReplayRequest(ControlledUatFiscalIssuanceInvocationRequest request) =>
        string.Equals(request.ExpectedRunType, ReplayScenario, StringComparison.OrdinalIgnoreCase);

    private static bool IsConflictRequest(ControlledUatFiscalIssuanceInvocationRequest request) =>
        string.Equals(request.ExpectedRunType, ConflictScenario, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSupportedScenarioName(string? scenario) =>
        string.Equals(scenario, NewlyCreatedScenario, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scenario, ReplayScenario, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(scenario, ConflictScenario, StringComparison.OrdinalIgnoreCase);

    private static FiscalIssuanceControlledUatHarnessRequest BuildHarnessRequest(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile,
        Guid fiscalIssuanceReferenceId)
    {
        var correlationId = Guid.Parse(request.CorrelationId.Trim());
        return new FiscalIssuanceControlledUatHarnessRequest(
            FiscalIssuanceReferenceId: fiscalIssuanceReferenceId,
            RunId: request.RunId.Trim(),
            EnvironmentName: request.EnvironmentName.Trim(),
            EvidenceReference: request.EvidenceReference,
            EvidenceLocation: request.EvidenceLocation,
            EvidenceOwner: request.EvidenceOwner.Trim(),
            ApprovedByRef: request.ApprovalReference.Trim(),
            FiscalContext: BuildFiscalContext(request, profile),
            RecordingContext: new PosServerCreateResultRecordingContext(
                UpstreamFinalityReference: request.UpstreamFinalityRef.Trim(),
                SitePosServerId: ResolveSitePosServerId(request, profile),
                FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request, profile),
                CorrelationId: correlationId,
                PosServerResponseTimestamp: null,
                ServiceIdentityId: null),
            ExpectedRunType: FiscalIssuanceControlledUatExpectedRunType.NewlyCreated,
            CorrelationId: request.CorrelationId.Trim());
    }

    private static CentralPmsFiscalDocumentMappingContext BuildFiscalContext(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        new(
            SitePosServerId: ResolveSitePosServerId(request, profile),
            SitePosServerRef: request.SitePosServerRef.Trim(),
            FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request, profile),
            FiscalDocumentTypeCodeKey: request.FiscalDocumentType.Trim(),
            FiscalDocumentStatusCodeId: ResolveFiscalDocumentStatusCodeId(request, profile),
            BusinessDayDate: request.BusinessDayDate,
            CentralPmsParkingSessionRef: request.ParkingSessionRef.Trim(),
            CentralPmsPaymentAttemptRef: request.PaymentAttemptRef.Trim(),
            CentralPmsPaymentConfirmationRef: request.PaymentConfirmationRef.Trim(),
            PayableBasis: new CentralPmsPayableBasisContext(
                PayableBasisRef: request.PayableBasisRef.Trim(),
                UpstreamFinalityRef: request.UpstreamFinalityRef.Trim(),
                CurrencyCode: request.Currency.Trim().ToUpperInvariant(),
                PayableAmountMinorUnits: request.AmountMinorUnits,
                DiscountReferences: Array.Empty<CentralPmsFiscalDiscountReferenceContext>(),
                ReferenceContext: new Dictionary<string, string>
                {
                    ["runId"] = request.RunId.Trim(),
                    ["siteRef"] = request.SiteRef.Trim()
                }),
            DocumentLines:
            [
                new CentralPmsFiscalDocumentLineContext(
                    LineSequence: 1,
                    LineTypeCodeId: profile.LineTypeCodeId,
                    Description: request.LineSummary.Trim(),
                    Quantity: 1,
                    UnitAmountMinorUnits: request.LineAmountTotal,
                    GrossAmountMinorUnits: request.LineAmountTotal,
                    DiscountAmountMinorUnits: 0,
                    TaxAmountMinorUnits: request.TaxAmountTotal,
                    NetAmountMinorUnits: request.LineAmountTotal - request.TaxAmountTotal,
                    CurrencyCode: request.Currency.Trim().ToUpperInvariant(),
                    LineStatusCodeId: null,
                    SourceRef: request.RunId.Trim(),
                    LineContext: new Dictionary<string, string> { ["controlledUat"] = "true" })
            ],
            Tenders:
            [
                new CentralPmsFiscalTenderContext(
                    TenderTypeCodeId: profile.TenderTypeCodeId,
                    AmountMinorUnits: request.TenderAmountTotal,
                    CurrencyCode: request.Currency.Trim().ToUpperInvariant(),
                    CentralPmsPaymentAttemptRef: request.PaymentAttemptRef.Trim(),
                    CentralPmsPaymentConfirmationRef: request.PaymentConfirmationRef.Trim(),
                    PaymentFinalityRef: request.UpstreamFinalityRef.Trim(),
                    ProviderRef: "controlled-uat-test-tender",
                    TenderContext: new Dictionary<string, string> { ["summary"] = request.TenderSummary.Trim() })
            ],
            TaxDetails:
            [
                new CentralPmsFiscalTaxDetailContext(
                    TaxTypeCodeId: profile.TaxTypeCodeId,
                    TaxClassificationCodeId: profile.TaxClassificationCodeId,
                    TaxableAmountMinorUnits: request.LineAmountTotal,
                    TaxAmountMinorUnits: request.TaxAmountTotal,
                    CurrencyCode: request.Currency.Trim().ToUpperInvariant(),
                    LineSequence: 1,
                    TaxRate: null,
                    TaxContext: new Dictionary<string, string> { ["summary"] = request.TaxDetailSummary.Trim() })
            ],
            DiscountPrivilegeDetails: Array.Empty<CentralPmsFiscalDiscountPrivilegeDetailContext>(),
            Totals:
            [
                new CentralPmsFiscalTotalContext(
                    TotalTypeCodeId: profile.TotalTypeCodeId,
                    AmountMinorUnits: request.GrandTotal,
                    CurrencyCode: request.Currency.Trim().ToUpperInvariant(),
                    TotalContext: new Dictionary<string, string> { ["kind"] = "grand_total" })
            ],
            ReferenceContext: new Dictionary<string, string>
            {
                ["environmentName"] = request.EnvironmentName.Trim(),
                ["approvalReference"] = request.ApprovalReference.Trim(),
                ["approvedBy"] = request.ApprovedBy.Trim()
            },
            PaymentFinalityRef: request.UpstreamFinalityRef.Trim(),
            VendorAckRef: null);

    private static Guid ResolveSitePosServerId(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        request.SitePosServerId.GetValueOrDefault(profile.SitePosServerId);

    private static Guid ResolveFiscalDocumentTypeCodeId(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        request.FiscalDocumentTypeCodeId.GetValueOrDefault(profile.FiscalDocumentTypeCodeId);

    private static Guid ResolveFiscalDocumentStatusCodeId(
        ControlledUatFiscalIssuanceInvocationRequest request,
        ControlledUatFiscalSmokeProfile profile) =>
        request.FiscalDocumentStatusCodeId.GetValueOrDefault(profile.FiscalDocumentStatusCodeId);

    private static IEnumerable<string?> EnumerateStrings(ControlledUatFiscalIssuanceInvocationRequest request)
    {
        yield return request.RunId;
        yield return request.ApprovalReference;
        yield return request.ApprovedBy;
        yield return request.CorrelationId;
        yield return request.EnvironmentName;
        yield return request.SiteRef;
        yield return request.SitePosServerRef;
        yield return request.ParkingSessionRef;
        yield return request.PaymentAttemptRef;
        yield return request.PaymentConfirmationRef;
        yield return request.PayableBasisRef;
        yield return request.UpstreamFinalityRef;
        yield return request.FiscalDocumentType;
        yield return request.Currency;
        yield return request.LineSummary;
        yield return request.TenderSummary;
        yield return request.TaxDetailSummary;
        yield return request.EvidenceReference;
        yield return request.EvidenceLocation;
        yield return request.EvidenceOwner;
        yield return request.ExpectedRunType;
        yield return request.ProfileId;
    }

    private static bool ContainsSensitiveTerm(string? value) =>
        value is not null &&
        SensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

internal sealed record FiscalReferencePreparationResult(
    FiscalIssuanceReferenceRecord? Reference,
    ControlledUatFiscalIssuanceInvocationResponse? ErrorResponse)
{
    public static FiscalReferencePreparationResult Prepared(FiscalIssuanceReferenceRecord reference) =>
        new(reference, null);

    public static FiscalReferencePreparationResult Rejected(ControlledUatFiscalIssuanceInvocationResponse response) =>
        new(null, response);
}

public sealed record ControlledUatFiscalIssuanceInvocationRequest(
    string? RunId,
    string? ApprovalReference,
    string? ApprovedBy,
    bool? ExplicitExecutionApproval,
    string? CorrelationId,
    string? EnvironmentName,
    string? SiteRef,
    string? SitePosServerRef,
    Guid? SitePosServerId,
    Guid? FiscalDocumentTypeCodeId,
    Guid? FiscalDocumentStatusCodeId,
    Guid? FiscalIssuanceReferenceId,
    string? ParkingSessionRef,
    string? PaymentAttemptRef,
    string? PaymentConfirmationRef,
    string? PayableBasisRef,
    string? UpstreamFinalityRef,
    string? FiscalDocumentType,
    DateOnly? BusinessDayDate,
    string? Currency,
    long AmountMinorUnits,
    string? LineSummary,
    int LineCount,
    long LineAmountTotal,
    string? TenderSummary,
    int TenderCount,
    long TenderAmountTotal,
    bool TaxDetailPresent,
    string? TaxDetailSummary,
    long TaxAmountTotal,
    bool TotalsPresent,
    long GrandTotal,
    bool? TotalsMatchPayableBasis,
    string? ExpectedRunType,
    bool ReplayIncluded,
    bool ConflictIncluded,
    bool FailureIncluded,
    bool UnknownIncluded,
    string? EvidenceReference,
    string? EvidenceLocation,
    string? EvidenceOwner,
    string? ProfileId);

public sealed record ControlledUatFiscalIssuanceFixture(
    string ProfileId,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid TariffSnapshotId,
    Guid ServiceIdentityId,
    Guid SiteGroupId,
    Guid SiteId,
    Guid VendorSystemId,
    string RunId,
    Guid CorrelationId,
    string SiteRef,
    string ParkingSessionRef,
    string PaymentAttemptRef,
    string PaymentConfirmationRef,
    string UpstreamFinalityRef,
    string Currency,
    long AmountMinorUnits,
    DateOnly BusinessDayDate);

public sealed record ControlledUatFiscalIssuanceInvocationResponse(
    bool Accepted,
    string Status,
    int HttpStatusCode,
    IReadOnlyList<string> Errors,
    string? RunId,
    string? CorrelationId,
    string ReadinessStatus,
    bool ValidationPassed,
    bool DiagnosticInvoked,
    bool PosServerCallAttempted,
    string? DiagnosticStatus,
    string? ResultClassification,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    string? FiscalIssuanceEvidenceStatus,
    string? FiscalNumberAssignmentState,
    string? CentralPmsFiscalState,
    string? ErrorCode,
    string? ErrorPosture,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool FiscalGatingEnforcementEnabled,
    bool EvidenceFileWritten,
    string? EvidenceJson,
    string? EvidenceRedactionStatus,
    bool SensitiveDataExcluded);

public sealed record ControlledUatFiscalSmokeProfile(
    string ProfileId,
    string EnvironmentName,
    string SiteRef,
    string SitePosServerRef,
    string FiscalDocumentType,
    string RunId,
    string CorrelationId,
    string UpstreamFinalityRef,
    string ParkingSessionRef,
    string PaymentAttemptRef,
    string PaymentConfirmationRef,
    string PayableBasisRef,
    string Currency,
    string ApprovalReference,
    DateOnly BusinessDayDate,
    long AmountMinorUnits,
    long ConflictAmountMinorUnits,
    long TaxAmountMinorUnits,
    IReadOnlySet<string> SupportedScenarios,
    Guid PaymentConfirmationId,
    Guid PaymentAttemptId,
    Guid ParkingSessionId,
    Guid SiteGroupId,
    Guid SiteId,
    Guid VendorSystemId,
    Guid TariffSnapshotId,
    Guid ServiceIdentityId,
    Guid SitePosServerId,
    Guid FiscalDocumentTypeCodeId,
    Guid FiscalDocumentStatusCodeId,
    Guid LineTypeCodeId,
    Guid TenderTypeCodeId,
    Guid TaxTypeCodeId,
    Guid TaxClassificationCodeId,
    Guid TotalTypeCodeId)
{
    public bool SupportsScenario(string? scenario) =>
        !string.IsNullOrWhiteSpace(scenario) &&
        SupportedScenarios.Contains(scenario.Trim());
}

public sealed record ControlledUatFiscalSmokeProfileOptions
{
    public bool Enabled { get; set; } = true;

    public string? ProfileId { get; set; }

    public string? EnvironmentName { get; set; }

    public string? SiteRef { get; set; }

    public string? SitePosServerRef { get; set; }

    public string? FiscalDocumentType { get; set; }

    public string? RunId { get; set; }

    public string? CorrelationId { get; set; }

    public string? UpstreamFinalityRef { get; set; }

    public string? ParkingSessionRef { get; set; }

    public string? PaymentAttemptRef { get; set; }

    public string? PaymentConfirmationRef { get; set; }

    public string? PayableBasisRef { get; set; }

    public string? Currency { get; set; }

    public string? ApprovalReference { get; set; }

    public DateOnly? BusinessDayDate { get; set; }

    public long AmountMinorUnits { get; set; }

    public long ConflictAmountMinorUnits { get; set; }

    public long TaxAmountMinorUnits { get; set; }

    public List<string> SupportedScenarios { get; set; } = [];

    public Guid PaymentConfirmationId { get; set; }

    public Guid PaymentAttemptId { get; set; }

    public Guid ParkingSessionId { get; set; }

    public Guid SiteGroupId { get; set; }

    public Guid SiteId { get; set; }

    public Guid VendorSystemId { get; set; }

    public Guid TariffSnapshotId { get; set; }

    public Guid ServiceIdentityId { get; set; }

    public Guid SitePosServerId { get; set; }

    public Guid FiscalDocumentTypeCodeId { get; set; }

    public Guid FiscalDocumentStatusCodeId { get; set; }

    public Guid LineTypeCodeId { get; set; }

    public Guid TenderTypeCodeId { get; set; }

    public Guid TaxTypeCodeId { get; set; }

    public Guid TaxClassificationCodeId { get; set; }

    public Guid TotalTypeCodeId { get; set; }
}

internal static class ControlledUatFiscalSmokeProfileCatalog
{
    private static readonly string[] UnsafeProductionMarkers =
    [
        "prod",
        "production",
        "live",
        "shared"
    ];

    private static readonly string[] NonProductionMarkers =
    [
        "dev",
        "uat",
        "smoke",
        "sandbox",
        "local"
    ];

    public static ControlledUatFiscalSmokeProfile? TryResolve(
        string profileId,
        ControlledUatFiscalSmokeProfile defaultProfile,
        IReadOnlyList<ControlledUatFiscalSmokeProfileOptions> configuredProfiles,
        List<string> errors)
    {
        if (string.Equals(profileId, defaultProfile.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return defaultProfile;
        }

        var option = configuredProfiles.FirstOrDefault(candidate =>
            candidate.Enabled &&
            string.Equals(candidate.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));
        if (option is null)
        {
            errors.Add("profile_not_allowlisted");
            return null;
        }

        return TryBuildConfiguredProfile(option, errors);
    }

    private static ControlledUatFiscalSmokeProfile? TryBuildConfiguredProfile(
        ControlledUatFiscalSmokeProfileOptions option,
        List<string> errors)
    {
        var profileErrors = new List<string>();

        Require(option.ProfileId, "configured_profile_id_required", profileErrors);
        Require(option.EnvironmentName, "configured_environment_name_required", profileErrors);
        Require(option.SiteRef, "configured_site_ref_required", profileErrors);
        Require(option.SitePosServerRef, "configured_site_pos_server_ref_required", profileErrors);
        Require(option.FiscalDocumentType, "configured_fiscal_document_type_required", profileErrors);
        Require(option.RunId, "configured_run_id_required", profileErrors);
        Require(option.CorrelationId, "configured_correlation_id_required", profileErrors);
        Require(option.UpstreamFinalityRef, "configured_upstream_finality_ref_required", profileErrors);
        Require(option.ParkingSessionRef, "configured_parking_session_ref_required", profileErrors);
        Require(option.PaymentAttemptRef, "configured_payment_attempt_ref_required", profileErrors);
        Require(option.PaymentConfirmationRef, "configured_payment_confirmation_ref_required", profileErrors);
        Require(option.PayableBasisRef, "configured_payable_basis_ref_required", profileErrors);
        Require(option.Currency, "configured_currency_required", profileErrors);
        Require(option.ApprovalReference, "configured_approval_reference_required", profileErrors);

        if (option.BusinessDayDate is null)
        {
            profileErrors.Add("configured_business_day_date_required");
        }

        if (option.AmountMinorUnits <= 0)
        {
            profileErrors.Add("configured_amount_minor_units_required");
        }

        if (option.ConflictAmountMinorUnits <= 0 ||
            option.ConflictAmountMinorUnits == option.AmountMinorUnits)
        {
            profileErrors.Add("configured_conflict_amount_minor_units_required");
        }

        if (option.TaxAmountMinorUnits < 0)
        {
            profileErrors.Add("configured_tax_amount_minor_units_invalid");
        }

        var supportedScenarios = option.SupportedScenarios
            .Where(scenario => !string.IsNullOrWhiteSpace(scenario))
            .Select(scenario => scenario.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (supportedScenarios.Count == 0)
        {
            profileErrors.Add("configured_supported_scenarios_required");
        }
        else if (supportedScenarios.Any(scenario => !FiscalIssuanceControlledUatInvocationService.IsSupportedScenarioName(scenario)))
        {
            profileErrors.Add("configured_supported_scenario_invalid");
        }

        RequireNonEmpty(option.PaymentConfirmationId, "configured_payment_confirmation_id_required", profileErrors);
        RequireNonEmpty(option.PaymentAttemptId, "configured_payment_attempt_id_required", profileErrors);
        RequireNonEmpty(option.ParkingSessionId, "configured_parking_session_id_required", profileErrors);
        RequireNonEmpty(option.SiteGroupId, "configured_site_group_id_required", profileErrors);
        RequireNonEmpty(option.SiteId, "configured_site_id_required", profileErrors);
        RequireNonEmpty(option.VendorSystemId, "configured_vendor_system_id_required", profileErrors);
        RequireNonEmpty(option.TariffSnapshotId, "configured_tariff_snapshot_id_required", profileErrors);
        RequireNonEmpty(option.ServiceIdentityId, "configured_service_identity_id_required", profileErrors);
        RequireNonEmpty(option.SitePosServerId, "configured_site_pos_server_id_required", profileErrors);
        RequireNonEmpty(option.FiscalDocumentTypeCodeId, "configured_fiscal_document_type_code_id_required", profileErrors);
        RequireNonEmpty(option.FiscalDocumentStatusCodeId, "configured_fiscal_document_status_code_id_required", profileErrors);
        RequireNonEmpty(option.LineTypeCodeId, "configured_line_type_code_id_required", profileErrors);
        RequireNonEmpty(option.TenderTypeCodeId, "configured_tender_type_code_id_required", profileErrors);
        RequireNonEmpty(option.TaxTypeCodeId, "configured_tax_type_code_id_required", profileErrors);
        RequireNonEmpty(option.TaxClassificationCodeId, "configured_tax_classification_code_id_required", profileErrors);
        RequireNonEmpty(option.TotalTypeCodeId, "configured_total_type_code_id_required", profileErrors);

        if (!IsNonProduction(option))
        {
            profileErrors.Add("configured_profile_not_non_production");
        }

        if (profileErrors.Count > 0)
        {
            errors.AddRange(profileErrors);
            return null;
        }

        return new ControlledUatFiscalSmokeProfile(
            ProfileId: option.ProfileId!.Trim(),
            EnvironmentName: option.EnvironmentName!.Trim(),
            SiteRef: option.SiteRef!.Trim(),
            SitePosServerRef: option.SitePosServerRef!.Trim(),
            FiscalDocumentType: option.FiscalDocumentType!.Trim(),
            RunId: option.RunId!.Trim(),
            CorrelationId: option.CorrelationId!.Trim(),
            UpstreamFinalityRef: option.UpstreamFinalityRef!.Trim(),
            ParkingSessionRef: option.ParkingSessionRef!.Trim(),
            PaymentAttemptRef: option.PaymentAttemptRef!.Trim(),
            PaymentConfirmationRef: option.PaymentConfirmationRef!.Trim(),
            PayableBasisRef: option.PayableBasisRef!.Trim(),
            Currency: option.Currency!.Trim().ToUpperInvariant(),
            ApprovalReference: option.ApprovalReference!.Trim(),
            BusinessDayDate: option.BusinessDayDate!.Value,
            AmountMinorUnits: option.AmountMinorUnits,
            ConflictAmountMinorUnits: option.ConflictAmountMinorUnits,
            TaxAmountMinorUnits: option.TaxAmountMinorUnits,
            SupportedScenarios: supportedScenarios,
            PaymentConfirmationId: option.PaymentConfirmationId,
            PaymentAttemptId: option.PaymentAttemptId,
            ParkingSessionId: option.ParkingSessionId,
            SiteGroupId: option.SiteGroupId,
            SiteId: option.SiteId,
            VendorSystemId: option.VendorSystemId,
            TariffSnapshotId: option.TariffSnapshotId,
            ServiceIdentityId: option.ServiceIdentityId,
            SitePosServerId: option.SitePosServerId,
            FiscalDocumentTypeCodeId: option.FiscalDocumentTypeCodeId,
            FiscalDocumentStatusCodeId: option.FiscalDocumentStatusCodeId,
            LineTypeCodeId: option.LineTypeCodeId,
            TenderTypeCodeId: option.TenderTypeCodeId,
            TaxTypeCodeId: option.TaxTypeCodeId,
            TaxClassificationCodeId: option.TaxClassificationCodeId,
            TotalTypeCodeId: option.TotalTypeCodeId);
    }

    private static void Require(string? value, string error, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(error);
        }
    }

    private static void RequireNonEmpty(Guid value, string error, List<string> errors)
    {
        if (value == Guid.Empty)
        {
            errors.Add(error);
        }
    }

    private static bool IsNonProduction(ControlledUatFiscalSmokeProfileOptions option)
    {
        var values = new[]
        {
            option.ProfileId,
            option.EnvironmentName,
            option.SiteRef,
            option.SitePosServerRef,
            option.RunId,
            option.UpstreamFinalityRef,
            option.ParkingSessionRef,
            option.PaymentAttemptRef,
            option.PaymentConfirmationRef,
            option.PayableBasisRef
        };

        return values.All(value => !ContainsAny(value, UnsafeProductionMarkers)) &&
            values.Any(value => ContainsAny(value, NonProductionMarkers));
    }

    private static bool ContainsAny(string? value, IReadOnlyList<string> markers) =>
        value is not null &&
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
