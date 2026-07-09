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
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceControlledUatInvocationService : IFiscalIssuanceControlledUatInvocationService
{
    private const string ApprovedEnvironmentName = "DEV-CONTROLLED-UAT-LOCAL";
    private const string ApprovedSiteRef = "DEV-SITE-ATC-001";
    private const string ApprovedSitePosServerRef = "DEV-POS-SERVER-ATC-001";
    private const string ApprovedFiscalDocumentType = "sales_invoice";
    private const string ApprovedRunId = "CPS-POS-UAT-20260709-DEV-ATC-001";
    private const string ApprovedCorrelationId = "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df";
    private const string ApprovedUpstreamFinalityRef = "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001";
    private const string ApprovedParkingSessionRef = "DEV-PARKING-SESSION-ATC-001";
    private const string ApprovedPaymentAttemptRef = "DEV-PAYMENT-ATTEMPT-ATC-001";
    private const string ApprovedPaymentConfirmationRef = "DEV-PAYMENT-FINALITY-ATC-001";
    private const string ApprovedPayableBasisRef = "DEV-PAYABLE-BASIS-ATC-001";
    private const string ApprovedCurrency = "PHP";
    private const string ApprovedApprovalReference = "DEV-UAT-CPS-POS-001";
    private const string ApprovedExpectedRunType = "newly_created";
    private const long ApprovedAmountMinorUnits = 10000;
    private const long ApprovedTaxAmountMinorUnits = 0;
    private static readonly DateOnly ApprovedBusinessDayDate = new(2026, 7, 9);
    private static readonly Guid ControlledUatPaymentConfirmationId =
        Guid.Parse("00000000-0000-4000-8000-000000000301");
    private static readonly Guid ControlledUatPaymentAttemptId =
        Guid.Parse("00000000-0000-4000-8000-000000000302");
    private static readonly Guid ControlledUatParkingSessionId =
        Guid.Parse("00000000-0000-4000-8000-000000000303");
    private static readonly Guid ControlledUatSiteGroupId =
        Guid.Parse("00000000-0000-4000-8000-000000000401");
    private static readonly Guid ControlledUatSiteId =
        Guid.Parse("00000000-0000-4000-8000-000000000402");
    private static readonly Guid ControlledUatVendorSystemId =
        Guid.Parse("00000000-0000-4000-8000-000000000501");
    private static readonly Guid ControlledUatTariffSnapshotId =
        Guid.Parse("00000000-0000-4000-8000-000000000601");
    private static readonly Guid ControlledUatServiceIdentityId =
        Guid.Parse("00000000-0000-4000-8000-000000000901");
    private static readonly Guid ControlledUatSitePosServerId =
        Guid.Parse("10000000-0000-4000-8000-000000000201");
    private static readonly Guid ControlledUatFiscalDocumentTypeCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000103");
    private static readonly Guid ControlledUatFiscalDocumentStatusCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000107");
    private static readonly Guid ControlledUatLineTypeCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000108");
    private static readonly Guid ControlledUatTenderTypeCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000109");
    private static readonly Guid ControlledUatTaxTypeCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000110");
    private static readonly Guid ControlledUatTaxClassificationCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000111");
    private static readonly Guid ControlledUatTotalTypeCodeId =
        Guid.Parse("10000000-0000-4000-8000-000000000112");

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

        var errors = ValidateRequest(request);
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

        var errors = ValidateRequest(request);
        errors.AddRange(ValidateConfiguration());
        if (errors.Count > 0)
        {
            return BuildRejectedResponse(request, "run_rejected", errors);
        }

        var fixturePreparation = await EnsureControlledUatFixtureAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (fixturePreparation is not null)
        {
            return fixturePreparation;
        }

        var referencePreparation = await PrepareFiscalIssuanceReferenceAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (referencePreparation.ErrorResponse is not null)
        {
            return referencePreparation.ErrorResponse;
        }

        var harnessRequest = BuildHarnessRequest(request, referencePreparation.Reference!.FiscalIssuanceReferenceId);
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
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _referenceRepository.FindByUpstreamFinalityReferenceAsync(
                    request.UpstreamFinalityRef!.Trim(),
                    request.SitePosServerId,
                    request.FiscalDocumentTypeCodeId,
                    cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                return CanStartDiagnostic(existing)
                    ? FiscalReferencePreparationResult.Prepared(existing)
                    : FiscalReferencePreparationResult.Rejected(BuildRejectedResponse(
                        request,
                        "fiscal_reference_prepare_rejected",
                        new[] { "fiscal_reference_not_startable_state", existing.FiscalIssuanceState.ToString() }));
            }

            var prepared = await _orchestrationService.PreparePendingAsync(
                    BuildPrepareCommand(request),
                    cancellationToken)
                .ConfigureAwait(false);

            return FiscalReferencePreparationResult.Prepared(prepared);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
        CancellationToken cancellationToken)
    {
        try
        {
            await _fixtureStore.EnsureApprovedFirstRunFixtureAsync(BuildFixture(request), cancellationToken)
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
            or "fiscal_reference_not_startable_state";

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

    private static List<string> ValidateRequest(ControlledUatFiscalIssuanceInvocationRequest request)
    {
        var errors = new List<string>();

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

        if (!string.Equals(request.FiscalDocumentType, ApprovedFiscalDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("wrong_fiscal_document_type");
        }

        if (!string.Equals(request.ExpectedRunType, ApprovedExpectedRunType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("expected_run_type_must_be_newly_created");
        }

        if (request.ReplayIncluded)
        {
            errors.Add("replay_not_allowed_for_first_run");
        }

        if (request.ConflictIncluded)
        {
            errors.Add("conflict_not_allowed_for_first_run");
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
        ValidateApprovedFirstRunValues(request, errors);

        if (EnumerateStrings(request).Where(value => !string.IsNullOrWhiteSpace(value)).Any(ContainsSensitiveTerm))
        {
            errors.Add("sensitive_marker_detected");
        }

        return errors;
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

    private static void ValidateApprovedFirstRunValues(
        ControlledUatFiscalIssuanceInvocationRequest request,
        List<string> errors)
    {
        CheckEquals(request.EnvironmentName, ApprovedEnvironmentName, "environment_not_approved_for_first_run", errors);
        CheckEquals(request.SiteRef, ApprovedSiteRef, "site_ref_not_approved_for_first_run", errors);
        CheckEquals(request.SitePosServerRef, ApprovedSitePosServerRef, "site_pos_server_ref_not_approved_for_first_run", errors);
        CheckEquals(request.RunId, ApprovedRunId, "run_id_not_approved_for_first_run", errors);
        CheckEquals(request.CorrelationId, ApprovedCorrelationId, "correlation_id_not_approved_for_first_run", errors);
        CheckEquals(request.UpstreamFinalityRef, ApprovedUpstreamFinalityRef, "upstream_finality_ref_not_approved_for_first_run", errors);
        CheckEquals(request.ParkingSessionRef, ApprovedParkingSessionRef, "parking_session_ref_not_approved_for_first_run", errors);
        CheckEquals(request.PaymentAttemptRef, ApprovedPaymentAttemptRef, "payment_attempt_ref_not_approved_for_first_run", errors);
        CheckEquals(request.PaymentConfirmationRef, ApprovedPaymentConfirmationRef, "payment_confirmation_ref_not_approved_for_first_run", errors);
        CheckEquals(request.PayableBasisRef, ApprovedPayableBasisRef, "payable_basis_ref_not_approved_for_first_run", errors);
        CheckEquals(request.Currency, ApprovedCurrency, "currency_not_approved_for_first_run", errors);
        CheckEquals(request.ApprovalReference, ApprovedApprovalReference, "approval_reference_not_approved_for_first_run", errors);

        if (request.BusinessDayDate != ApprovedBusinessDayDate)
        {
            errors.Add("business_day_date_not_approved_for_first_run");
        }

        if (request.AmountMinorUnits != ApprovedAmountMinorUnits ||
            request.LineAmountTotal != ApprovedAmountMinorUnits ||
            request.TenderAmountTotal != ApprovedAmountMinorUnits ||
            request.GrandTotal != ApprovedAmountMinorUnits)
        {
            errors.Add("amounts_not_approved_for_first_run");
        }

        if (request.TaxAmountTotal != ApprovedTaxAmountMinorUnits)
        {
            errors.Add("tax_amount_not_approved_for_first_run");
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
        ControlledUatFiscalIssuanceInvocationRequest request) =>
        new(
            PaymentConfirmationId: ControlledUatPaymentConfirmationId,
            PaymentAttemptId: ControlledUatPaymentAttemptId,
            ParkingSessionId: ControlledUatParkingSessionId,
            TariffSnapshotId: ControlledUatTariffSnapshotId,
            SiteId: ControlledUatSiteId,
            SitePosServerId: ResolveSitePosServerId(request),
            SitePosServerRef: request.SitePosServerRef?.Trim(),
            FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request),
            FiscalDocumentTypeCodeKey: request.FiscalDocumentType?.Trim(),
            PayableBasisRef: request.PayableBasisRef?.Trim(),
            UpstreamFinalityReference: request.UpstreamFinalityRef!.Trim(),
            CorrelationId: Guid.Parse(request.CorrelationId!.Trim()),
            ServiceIdentityId: ControlledUatServiceIdentityId);

    private static ControlledUatFiscalIssuanceFixture BuildFixture(
        ControlledUatFiscalIssuanceInvocationRequest request) =>
        new(
            PaymentConfirmationId: ControlledUatPaymentConfirmationId,
            PaymentAttemptId: ControlledUatPaymentAttemptId,
            ParkingSessionId: ControlledUatParkingSessionId,
            TariffSnapshotId: ControlledUatTariffSnapshotId,
            ServiceIdentityId: ControlledUatServiceIdentityId,
            SiteGroupId: ControlledUatSiteGroupId,
            SiteId: ControlledUatSiteId,
            VendorSystemId: ControlledUatVendorSystemId,
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

    private static FiscalIssuanceControlledUatHarnessRequest BuildHarnessRequest(
        ControlledUatFiscalIssuanceInvocationRequest request,
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
            FiscalContext: BuildFiscalContext(request),
            RecordingContext: new PosServerCreateResultRecordingContext(
                UpstreamFinalityReference: request.UpstreamFinalityRef.Trim(),
                SitePosServerId: ResolveSitePosServerId(request),
                FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request),
                CorrelationId: correlationId,
                PosServerResponseTimestamp: null,
                ServiceIdentityId: null),
            ExpectedRunType: FiscalIssuanceControlledUatExpectedRunType.NewlyCreated,
            CorrelationId: request.CorrelationId.Trim());
    }

    private static CentralPmsFiscalDocumentMappingContext BuildFiscalContext(
        ControlledUatFiscalIssuanceInvocationRequest request) =>
        new(
            SitePosServerId: ResolveSitePosServerId(request),
            SitePosServerRef: request.SitePosServerRef.Trim(),
            FiscalDocumentTypeCodeId: ResolveFiscalDocumentTypeCodeId(request),
            FiscalDocumentTypeCodeKey: request.FiscalDocumentType.Trim(),
            FiscalDocumentStatusCodeId: ResolveFiscalDocumentStatusCodeId(request),
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
                    LineTypeCodeId: ControlledUatLineTypeCodeId,
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
                    TenderTypeCodeId: ControlledUatTenderTypeCodeId,
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
                    TaxTypeCodeId: ControlledUatTaxTypeCodeId,
                    TaxClassificationCodeId: ControlledUatTaxClassificationCodeId,
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
                    TotalTypeCodeId: ControlledUatTotalTypeCodeId,
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

    private static Guid ResolveSitePosServerId(ControlledUatFiscalIssuanceInvocationRequest request) =>
        request.SitePosServerId.GetValueOrDefault(ControlledUatSitePosServerId);

    private static Guid ResolveFiscalDocumentTypeCodeId(ControlledUatFiscalIssuanceInvocationRequest request) =>
        request.FiscalDocumentTypeCodeId.GetValueOrDefault(ControlledUatFiscalDocumentTypeCodeId);

    private static Guid ResolveFiscalDocumentStatusCodeId(ControlledUatFiscalIssuanceInvocationRequest request) =>
        request.FiscalDocumentStatusCodeId.GetValueOrDefault(ControlledUatFiscalDocumentStatusCodeId);

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
    string? EvidenceOwner);

public sealed record ControlledUatFiscalIssuanceFixture(
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
