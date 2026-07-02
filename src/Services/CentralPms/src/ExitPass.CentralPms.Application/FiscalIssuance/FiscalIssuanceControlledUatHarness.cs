using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuanceControlledUatHarness
{
    Task<FiscalIssuanceControlledUatHarnessResult> ExecuteAsync(
        FiscalIssuanceControlledUatHarnessRequest request,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuanceControlledUatHarness : IFiscalIssuanceControlledUatHarness
{
    private static readonly string[] SensitiveTerms =
    [
        "pan",
        "cvv",
        "token",
        "secret",
        "credential",
        "password",
        "provider callback",
        "provider_callback",
        "raw payload",
        "raw_payload",
        "callback_payload",
        "entitlement image",
        "entitlement_image",
        "base64 image",
        "base64_image",
        "unmanaged customer pii",
        "customer_pii"
    ];

    private readonly FiscalIssuancePosServerIntegrationOptions _posServerOptions;
    private readonly FiscalIssuanceExitAuthorizationGatingOptions _gatingOptions;
    private readonly IFiscalIssuancePosServerLiveIntegrationService _liveIntegrationService;

    public FiscalIssuanceControlledUatHarness(
        FiscalIssuancePosServerIntegrationOptions posServerOptions,
        IFiscalIssuancePosServerLiveIntegrationService liveIntegrationService,
        FiscalIssuanceExitAuthorizationGatingOptions? gatingOptions = null)
    {
        _posServerOptions = posServerOptions ?? new FiscalIssuancePosServerIntegrationOptions();
        _liveIntegrationService = liveIntegrationService;
        _gatingOptions = gatingOptions ?? new FiscalIssuanceExitAuthorizationGatingOptions();
    }

    public async Task<FiscalIssuanceControlledUatHarnessResult> ExecuteAsync(
        FiscalIssuanceControlledUatHarnessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var readiness = _posServerOptions.EvaluateReadiness();

        var identityErrors = ValidateRunIdentity(request);
        if (identityErrors.Count > 0)
        {
            return FiscalIssuanceControlledUatHarnessResult.Rejected(
                request,
                FiscalIssuanceControlledUatHarnessStatuses.RejectedMissingApprovalOrRunId,
                readiness.Status,
                identityErrors);
        }

        var inputErrors = ValidateInput(request);
        if (inputErrors.Count > 0)
        {
            return FiscalIssuanceControlledUatHarnessResult.Rejected(
                request,
                FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput,
                readiness.Status,
                inputErrors);
        }

        var sensitiveErrors = ValidateNoSensitivePayloadIndicators(request);
        if (sensitiveErrors.Count > 0)
        {
            return FiscalIssuanceControlledUatHarnessResult.Rejected(
                request,
                FiscalIssuanceControlledUatHarnessStatuses.RejectedSensitivePayload,
                readiness.Status,
                sensitiveErrors);
        }

        var guardErrors = ValidateGuards(readiness);
        if (guardErrors.Count > 0)
        {
            var status = !_posServerOptions.EnableControlledUatDiagnosticPath &&
                _posServerOptions.EnablePosServerFiscalIssuanceLiveCall
                    ? FiscalIssuanceControlledUatHarnessStatuses.DiagnosticDisabled
                    : FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady;

            return FiscalIssuanceControlledUatHarnessResult.Rejected(
                request,
                status,
                readiness.Status,
                guardErrors);
        }

        var diagnosticResult = await _liveIntegrationService.RunPosServerFiscalIssuanceDiagnosticAsync(
            request.FiscalIssuanceReferenceId,
            request.FiscalContext,
            request.RecordingContext,
            cancellationToken);

        return FiscalIssuanceControlledUatHarnessResult.FromDiagnosticResult(
            request,
            diagnosticResult);
    }

    private static List<string> ValidateRunIdentity(FiscalIssuanceControlledUatHarnessRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            errors.Add("run_id_required");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovedByRef))
        {
            errors.Add("approval_reference_required");
        }

        return errors;
    }

    private static List<string> ValidateInput(FiscalIssuanceControlledUatHarnessRequest request)
    {
        var errors = new List<string>();

        if (request.FiscalIssuanceReferenceId == Guid.Empty)
        {
            errors.Add("fiscal_issuance_reference_id_required");
        }

        if (string.IsNullOrWhiteSpace(request.EnvironmentName))
        {
            errors.Add("environment_name_required");
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceReference) &&
            string.IsNullOrWhiteSpace(request.EvidenceLocation))
        {
            errors.Add("evidence_reference_or_location_required");
        }

        if (string.IsNullOrWhiteSpace(request.EvidenceOwner))
        {
            errors.Add("evidence_owner_required");
        }

        if (request.FiscalContext.SitePosServerId is null &&
            string.IsNullOrWhiteSpace(request.FiscalContext.SitePosServerRef))
        {
            errors.Add("site_pos_server_context_required");
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.CentralPmsParkingSessionRef))
        {
            errors.Add("central_pms_parking_session_ref_required");
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.CentralPmsPaymentAttemptRef))
        {
            errors.Add("central_pms_payment_attempt_ref_required");
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.CentralPmsPaymentConfirmationRef))
        {
            errors.Add("central_pms_payment_confirmation_ref_required");
        }

        ValidatePayableBasis(request, errors);
        ValidateFiscalFacts(request.FiscalContext, errors);

        return errors;
    }

    private static void ValidatePayableBasis(
        FiscalIssuanceControlledUatHarnessRequest request,
        List<string> errors)
    {
        if (request.FiscalContext.PayableBasis is null)
        {
            errors.Add("payable_basis_required");
            return;
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.PayableBasis.PayableBasisRef))
        {
            errors.Add("payable_basis_ref_required");
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.PayableBasis.UpstreamFinalityRef))
        {
            errors.Add("upstream_finality_reference_required");
        }

        if (string.IsNullOrWhiteSpace(request.RecordingContext.UpstreamFinalityReference))
        {
            errors.Add("recording_upstream_finality_reference_required");
        }

        if (!string.IsNullOrWhiteSpace(request.FiscalContext.PayableBasis.UpstreamFinalityRef) &&
            !string.IsNullOrWhiteSpace(request.RecordingContext.UpstreamFinalityReference) &&
            !string.Equals(
                request.FiscalContext.PayableBasis.UpstreamFinalityRef,
                request.RecordingContext.UpstreamFinalityReference,
                StringComparison.Ordinal))
        {
            errors.Add("upstream_finality_reference_mismatch");
        }

        if (string.IsNullOrWhiteSpace(request.FiscalContext.PayableBasis.CurrencyCode))
        {
            errors.Add("currency_code_required");
        }

        if (request.FiscalContext.PayableBasis.PayableAmountMinorUnits <= 0)
        {
            errors.Add("payable_amount_minor_units_required");
        }
    }

    private static void ValidateFiscalFacts(
        CentralPmsFiscalDocumentMappingContext context,
        List<string> errors)
    {
        if (context.DocumentLines.Count == 0)
        {
            errors.Add("document_line_required");
        }

        if (context.Tenders.Count == 0)
        {
            errors.Add("tender_required");
        }

        if (context.DocumentLines.Any(line => string.IsNullOrWhiteSpace(line.CurrencyCode)))
        {
            errors.Add("document_line_currency_code_required");
        }

        if (context.Tenders.Any(tender => string.IsNullOrWhiteSpace(tender.CurrencyCode)))
        {
            errors.Add("tender_currency_code_required");
        }

        if (context.DocumentLines.Any(line => line.GrossAmountMinorUnits <= 0 || line.NetAmountMinorUnits < 0))
        {
            errors.Add("document_line_amounts_invalid");
        }

        if (context.Tenders.Any(tender => tender.AmountMinorUnits <= 0))
        {
            errors.Add("tender_amount_minor_units_required");
        }
    }

    private List<string> ValidateGuards(FiscalIssuancePosServerIntegrationReadiness readiness)
    {
        var errors = new List<string>();

        if (!_posServerOptions.EnablePosServerFiscalIssuanceLiveCall)
        {
            errors.Add("pos_server_fiscal_issuance_live_call_must_be_enabled");
        }

        if (!_posServerOptions.EnableControlledUatDiagnosticPath)
        {
            errors.Add("controlled_uat_diagnostic_path_must_be_enabled");
        }

        if (_posServerOptions.EnableLiveFiscalIssuanceFromPaymentFlow)
        {
            errors.Add("payment_flow_live_call_guard_must_remain_disabled");
        }

        if (_posServerOptions.EnableLiveFiscalIssuanceFromExitFlow)
        {
            errors.Add("exit_flow_live_call_guard_must_remain_disabled");
        }

        if (_gatingOptions.EnableFiscalBeforeExitAuthorizationEnforcement)
        {
            errors.Add("fiscal_gating_enforcement_must_remain_disabled");
        }

        errors.AddRange(readiness.Errors);
        return errors.Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> ValidateNoSensitivePayloadIndicators(
        FiscalIssuanceControlledUatHarnessRequest request)
    {
        var values = EnumerateStrings(request).Where(value => !string.IsNullOrWhiteSpace(value));
        return values.Any(ContainsSensitiveTerm)
            ? ["sensitive_payload_indicator_rejected"]
            : [];
    }

    private static IEnumerable<string?> EnumerateStrings(FiscalIssuanceControlledUatHarnessRequest request)
    {
        yield return request.RunId;
        yield return request.EnvironmentName;
        yield return request.EvidenceReference;
        yield return request.EvidenceLocation;
        yield return request.EvidenceOwner;
        yield return request.ApprovedByRef;
        yield return request.CorrelationId;
        yield return request.FiscalContext.SitePosServerRef;
        yield return request.FiscalContext.FiscalDocumentTypeCodeKey;
        yield return request.FiscalContext.CentralPmsParkingSessionRef;
        yield return request.FiscalContext.CentralPmsPaymentAttemptRef;
        yield return request.FiscalContext.CentralPmsPaymentConfirmationRef;
        yield return request.FiscalContext.PaymentFinalityRef;
        yield return request.FiscalContext.VendorAckRef;
        yield return request.RecordingContext.UpstreamFinalityReference;

        if (request.FiscalContext.PayableBasis is not null)
        {
            yield return request.FiscalContext.PayableBasis.PayableBasisRef;
            yield return request.FiscalContext.PayableBasis.UpstreamFinalityRef;
            yield return request.FiscalContext.PayableBasis.CurrencyCode;

            foreach (var value in EnumerateDictionaryStrings(request.FiscalContext.PayableBasis.ReferenceContext))
            {
                yield return value;
            }

            foreach (var discount in request.FiscalContext.PayableBasis.DiscountReferences)
            {
                yield return discount.DiscountValidationRef;
                yield return discount.Status;
                foreach (var value in EnumerateDictionaryStrings(discount.ReferenceContext))
                {
                    yield return value;
                }
            }
        }

        foreach (var value in EnumerateDictionaryStrings(request.FiscalContext.ReferenceContext))
        {
            yield return value;
        }

        foreach (var line in request.FiscalContext.DocumentLines)
        {
            yield return line.Description;
            yield return line.CurrencyCode;
            yield return line.SourceRef;
            foreach (var value in EnumerateDictionaryStrings(line.LineContext))
            {
                yield return value;
            }
        }

        foreach (var tender in request.FiscalContext.Tenders)
        {
            yield return tender.CurrencyCode;
            yield return tender.CentralPmsPaymentAttemptRef;
            yield return tender.CentralPmsPaymentConfirmationRef;
            yield return tender.PaymentFinalityRef;
            yield return tender.ProviderRef;
            foreach (var value in EnumerateDictionaryStrings(tender.TenderContext))
            {
                yield return value;
            }
        }

        foreach (var tax in request.FiscalContext.TaxDetails)
        {
            yield return tax.CurrencyCode;
            foreach (var value in EnumerateDictionaryStrings(tax.TaxContext))
            {
                yield return value;
            }
        }

        foreach (var discount in request.FiscalContext.DiscountPrivilegeDetails)
        {
            yield return discount.CurrencyCode;
            yield return discount.BeneficiaryRef;
            yield return discount.EvidenceRef;
            yield return discount.ApprovalRef;
            foreach (var value in EnumerateDictionaryStrings(discount.DiscountPrivilegeContext))
            {
                yield return value;
            }
        }

        foreach (var total in request.FiscalContext.Totals)
        {
            yield return total.CurrencyCode;
            foreach (var value in EnumerateDictionaryStrings(total.TotalContext))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<string?> EnumerateDictionaryStrings(IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            yield return pair.Key;
            yield return pair.Value;
        }
    }

    private static bool ContainsSensitiveTerm(string? value) =>
        value is not null &&
        SensitiveTerms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}

public sealed record FiscalIssuanceControlledUatHarnessRequest(
    Guid FiscalIssuanceReferenceId,
    string RunId,
    string EnvironmentName,
    string? EvidenceReference,
    string? EvidenceLocation,
    string EvidenceOwner,
    string ApprovedByRef,
    CentralPmsFiscalDocumentMappingContext FiscalContext,
    PosServerCreateResultRecordingContext RecordingContext,
    FiscalIssuanceControlledUatExpectedRunType ExpectedRunType,
    string? CorrelationId);

public enum FiscalIssuanceControlledUatExpectedRunType
{
    NewlyCreated = 1,
    IdempotentReplay = 2,
    Conflict = 3,
    Failure = 4,
    Unknown = 5
}

public static class FiscalIssuanceControlledUatHarnessStatuses
{
    public const string RejectedInvalidInput = "rejected_invalid_input";
    public const string RejectedSensitivePayload = "rejected_sensitive_payload";
    public const string RejectedConfigNotReady = "rejected_config_not_ready";
    public const string RejectedMissingApprovalOrRunId = "rejected_missing_approval_or_run_id";
    public const string DiagnosticDisabled = "diagnostic_disabled";
    public const string DiagnosticInvoked = "diagnostic_invoked";
    public const string NewlyCreatedRecorded = "newly_created_recorded";
    public const string ReplayRecorded = "replay_recorded";
    public const string ConflictFailureMapped = "conflict_failure_mapped";
    public const string RequestFailureMapped = "request_failure_mapped";
    public const string ConfigurationFailureMapped = "configuration_failure_mapped";
    public const string ServiceFailureMapped = "service_failure_mapped";
    public const string UnknownFailClosed = "unknown_fail_closed";
}

public sealed record FiscalIssuanceControlledUatHarnessResult(
    string RunId,
    string Status,
    string ReadinessStatus,
    bool ValidationPassed,
    bool DiagnosticInvoked,
    bool PosServerCallAttempted,
    string? DiagnosticStatus,
    FiscalIssuanceResultClassification? ResultClassification,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    FiscalIssuanceIntegrationState? CentralPmsFiscalState,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    string? EvidenceReference,
    string? EvidenceLocation,
    string? CorrelationId,
    IReadOnlyList<string> Errors)
{
    public static FiscalIssuanceControlledUatHarnessResult Rejected(
        FiscalIssuanceControlledUatHarnessRequest request,
        string status,
        string readinessStatus,
        IReadOnlyList<string> errors) =>
        new(
            RunId: request.RunId,
            Status: status,
            ReadinessStatus: readinessStatus,
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
            ErrorCode: null,
            ErrorPosture: null,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            EvidenceReference: request.EvidenceReference,
            EvidenceLocation: request.EvidenceLocation,
            CorrelationId: request.CorrelationId,
            Errors: errors);

    public static FiscalIssuanceControlledUatHarnessResult FromDiagnosticResult(
        FiscalIssuanceControlledUatHarnessRequest request,
        FiscalIssuancePosServerDiagnosticResult diagnosticResult) =>
        new(
            RunId: request.RunId,
            Status: MapStatus(diagnosticResult.Status),
            ReadinessStatus: diagnosticResult.ReadinessStatus,
            ValidationPassed: true,
            DiagnosticInvoked: true,
            PosServerCallAttempted: diagnosticResult.ClientCalled,
            DiagnosticStatus: diagnosticResult.Status,
            ResultClassification: diagnosticResult.PosServerResponseClassification,
            FiscalDocumentId: diagnosticResult.FiscalDocumentId,
            FiscalDocumentNumber: diagnosticResult.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: diagnosticResult.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: diagnosticResult.FiscalNumberAssignmentState,
            CentralPmsFiscalState: diagnosticResult.FiscalIssuanceStateApplied,
            ErrorCode: diagnosticResult.ErrorCode,
            ErrorPosture: diagnosticResult.ErrorPosture,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            EvidenceReference: request.EvidenceReference,
            EvidenceLocation: request.EvidenceLocation,
            CorrelationId: request.CorrelationId ?? diagnosticResult.CorrelationId?.ToString("D"),
            Errors: diagnosticResult.Errors);

    private static string MapStatus(string diagnosticStatus) =>
        diagnosticStatus switch
        {
            FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded =>
                FiscalIssuanceControlledUatHarnessStatuses.NewlyCreatedRecorded,
            FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded =>
                FiscalIssuanceControlledUatHarnessStatuses.ReplayRecorded,
            FiscalIssuancePosServerDiagnosticStatuses.ConflictFailureMapped =>
                FiscalIssuanceControlledUatHarnessStatuses.ConflictFailureMapped,
            FiscalIssuancePosServerDiagnosticStatuses.RequestFailureMapped =>
                FiscalIssuanceControlledUatHarnessStatuses.RequestFailureMapped,
            FiscalIssuancePosServerDiagnosticStatuses.ConfigurationFailureMapped =>
                FiscalIssuanceControlledUatHarnessStatuses.ConfigurationFailureMapped,
            FiscalIssuancePosServerDiagnosticStatuses.ServiceFailureMapped =>
                FiscalIssuanceControlledUatHarnessStatuses.ServiceFailureMapped,
            FiscalIssuancePosServerDiagnosticStatuses.UnknownFailClosed =>
                FiscalIssuanceControlledUatHarnessStatuses.UnknownFailClosed,
            FiscalIssuancePosServerDiagnosticStatuses.DiagnosticDisabled =>
                FiscalIssuanceControlledUatHarnessStatuses.DiagnosticDisabled,
            FiscalIssuancePosServerDiagnosticStatuses.ConfigurationInvalid =>
                FiscalIssuanceControlledUatHarnessStatuses.RejectedConfigNotReady,
            FiscalIssuancePosServerDiagnosticStatuses.LocalContextInvalid =>
                FiscalIssuanceControlledUatHarnessStatuses.RejectedInvalidInput,
            _ => FiscalIssuanceControlledUatHarnessStatuses.DiagnosticInvoked
        };
}
