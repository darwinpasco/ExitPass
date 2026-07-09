using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuanceControlledUatVoidSmokeService
{
    Task<ControlledUatFiscalVoidSmokeResponse> RunAsync(
        ControlledUatFiscalVoidSmokeRequest request,
        CancellationToken cancellationToken);
}

public interface IControlledUatFiscalVoidSmokeStore
{
    Task<ControlledUatFiscalVoidSmokeStoreResult> RecordApprovedVoidPostureAsync(
        ControlledUatFiscalVoidSmokeStoreRequest request,
        CancellationToken cancellationToken);
}

public interface IControlledUatFiscalVoidSafetyGuard
{
    Task<ControlledUatFiscalVoidSafetyGuardResult> ValidateAsync(
        ControlledUatFiscalVoidSafetyGuardRequest request,
        CancellationToken cancellationToken);
}

public sealed class PersistenceNotConfiguredControlledUatFiscalVoidSafetyGuard : IControlledUatFiscalVoidSafetyGuard
{
    public Task<ControlledUatFiscalVoidSafetyGuardResult> ValidateAsync(
        ControlledUatFiscalVoidSafetyGuardRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ControlledUatFiscalVoidSafetyGuardResult.Rejected(
            "controlled_uat_real_void_pos_persistence_not_configured",
            ["pos_server_connection_string_missing"]));
}

public sealed class PersistenceNotConfiguredControlledUatFiscalVoidSmokeStore : IControlledUatFiscalVoidSmokeStore
{
    public Task<ControlledUatFiscalVoidSmokeStoreResult> RecordApprovedVoidPostureAsync(
        ControlledUatFiscalVoidSmokeStoreRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ControlledUatFiscalVoidSmokeStoreResult.Rejected(
            "controlled_uat_void_smoke_pos_persistence_not_configured",
            ["pos_server_connection_string_missing"]));
}

public sealed class FiscalIssuanceControlledUatVoidSmokeService : IFiscalIssuanceControlledUatVoidSmokeService
{
    public static readonly Guid ApprovedFiscalIssuanceReferenceId =
        Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf");
    public static readonly Guid ApprovedPosServerFiscalDocumentId =
        Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395");
    public const string ApprovedFiscalDocumentNumber = "SI-00000002-UAT";
    public const string ApprovedReasonCode = "CONTROLLED_UAT_REAL_VOID";
    public const string ApprovedIdempotencyKey =
        "central-pms-controlled-uat-real-void:CPS-POS-UAT-20260709-DEV-ATC-001:9bdf2948-dadd-450b-8776-be688b579395";
    public const string RequestedByRef = "central-pms-controlled-uat";
    public const string SourceSystemRef = "central-pms";

    private readonly IFiscalIssuanceReferenceRepository _referenceRepository;
    private readonly IControlledUatFiscalVoidSafetyGuard _safetyGuard;
    private readonly IPosServerFiscalDocumentClient _posServerClient;
    private readonly FiscalIssuancePosServerIntegrationOptions _posServerOptions;
    private readonly FiscalIssuanceExitAuthorizationGatingOptions _gatingOptions;
    private readonly ILogger<FiscalIssuanceControlledUatVoidSmokeService> _logger;

    public FiscalIssuanceControlledUatVoidSmokeService(
        IFiscalIssuanceReferenceRepository referenceRepository,
        IControlledUatFiscalVoidSafetyGuard safetyGuard,
        IPosServerFiscalDocumentClient posServerClient,
        IOptions<FiscalIssuancePosServerIntegrationOptions> posServerOptions,
        IOptions<FiscalIssuanceExitAuthorizationGatingOptions> gatingOptions,
        ILogger<FiscalIssuanceControlledUatVoidSmokeService> logger)
    {
        _referenceRepository = referenceRepository;
        _safetyGuard = safetyGuard;
        _posServerClient = posServerClient;
        _posServerOptions = posServerOptions.Value;
        _gatingOptions = gatingOptions.Value;
        _logger = logger;
    }

    public async Task<ControlledUatFiscalVoidSmokeResponse> RunAsync(
        ControlledUatFiscalVoidSmokeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = ValidateRequest(request);
        errors.AddRange(ValidateConfiguration());
        if (errors.Count > 0)
        {
            return Rejected(request, "controlled_uat_void_smoke_rejected", 400, errors);
        }

        var reference = await _referenceRepository.FindByFiscalIssuanceReferenceIdAsync(
                request.FiscalIssuanceReferenceId!.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (reference is null)
        {
            return Rejected(request, "controlled_uat_void_smoke_reference_not_found", 409, ["fiscal_issuance_reference_not_found"]);
        }

        var referenceErrors = ValidateReference(request, reference);
        if (referenceErrors.Count > 0)
        {
            return Rejected(request, "controlled_uat_void_smoke_reference_rejected", 409, referenceErrors);
        }

        var guardResult = await _safetyGuard.ValidateAsync(
                new ControlledUatFiscalVoidSafetyGuardRequest(
                    ProfileId: request.ProfileId!.Trim(),
                    FiscalIssuanceReferenceId: request.FiscalIssuanceReferenceId.Value,
                    PosServerFiscalDocumentId: request.PosServerFiscalDocumentId!.Value,
                    FiscalDocumentNumber: request.FiscalDocumentNumber!.Trim(),
                    PaymentFinalityRef: reference.UpstreamFinalityReference,
                    FiscalSequenceValue: reference.FiscalSequenceValue,
                    ReasonCode: request.ReasonCode!.Trim(),
                    CorrelationId: Guid.Parse(request.CorrelationId!.Trim())),
                cancellationToken)
            .ConfigureAwait(false);
        if (!guardResult.Succeeded)
        {
            return Rejected(request, guardResult.Status, 409, guardResult.Errors);
        }

        PosServerFiscalDocumentVoidResult posResult;
        try
        {
            posResult = await _posServerClient.VoidFiscalDocumentAsync(
                    request.PosServerFiscalDocumentId.Value,
                    new PosServerFiscalDocumentVoidRequest(
                        IdempotencyKey: ApprovedIdempotencyKey,
                        ReasonCode: request.ReasonCode.Trim(),
                        ReasonText: "Controlled non-production UAT fiscal void integration smoke.",
                        RequestedByRef: RequestedByRef,
                        RequestedAt: null,
                        CorrelationId: request.CorrelationId.Trim(),
                        SourceSystemRef: SourceSystemRef,
                        BusinessDayDate: FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.BusinessDayDate),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Controlled UAT fiscal void smoke failed safely for approved document {FiscalDocumentId}.",
                request.PosServerFiscalDocumentId);

            return Rejected(
                request,
                "pos_server_void_failed",
                409,
                ["pos_server_void_failed"]);
        }

        return posResult.Succeeded
            ? new ControlledUatFiscalVoidSmokeResponse(
                Accepted: true,
                Status: MapSuccessfulStatus(posResult.Outcome),
                HttpStatusCode: 200,
                Errors: Array.Empty<string>(),
                ProfileId: request.ProfileId,
                CorrelationId: request.CorrelationId,
                FiscalIssuanceReferenceId: request.FiscalIssuanceReferenceId,
                PosServerFiscalDocumentId: request.PosServerFiscalDocumentId,
                FiscalDocumentNumber: posResult.FiscalDocumentNumber,
                FiscalDocumentStatusPosture: posResult.FiscalDocumentStatus,
                FiscalSequenceValue: posResult.FiscalSequenceValue,
                NewFiscalNumberAllocated: false,
                PaymentFinalityChanged: false,
                ExitAuthorizationIssued: false,
                GateBehaviorTriggered: false,
                RefundOrReversalCreated: false,
                HikCentralCalled: false,
                PaymentProviderCalled: false,
                RenderingGenerated: false,
                StatusHistoryRecorded: false,
                IdempotentReplay: posResult.Outcome == PosServerFiscalDocumentVoidOutcome.IdempotentReplay,
                PosServerResultClassification: posResult.ResultClassification,
                VoidStatus: posResult.VoidStatus)
            : Rejected(
                request,
                MapFailedStatus(posResult.Outcome),
                posResult.HttpStatusCode is 400 or 404 or 409 ? posResult.HttpStatusCode : 409,
                [posResult.Code]);
    }

    private static List<string> ValidateRequest(ControlledUatFiscalVoidSmokeRequest request)
    {
        var errors = new List<string>();
        var profileErrors = new List<string>();
        var profileId = string.IsNullOrWhiteSpace(request.ProfileId)
            ? request.ProfileId
            : request.ProfileId.Trim();
        if (string.IsNullOrWhiteSpace(profileId))
        {
            errors.Add("profile_id_required");
        }
        else if (ControlledUatFiscalSmokeProfileCatalog.TryResolve(
                profileId,
                FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile,
                Array.Empty<ControlledUatFiscalSmokeProfileOptions>(),
                profileErrors) is null)
        {
            errors.AddRange(profileErrors);
        }

        Require(request.ExplicitExecutionApproval == true, "explicit_execution_approval_required", errors);
        RequireEqual(request.FiscalIssuanceReferenceId, ApprovedFiscalIssuanceReferenceId, "fiscal_issuance_reference_id_not_approved", errors);
        RequireEqual(request.PosServerFiscalDocumentId, ApprovedPosServerFiscalDocumentId, "pos_server_fiscal_document_id_not_approved", errors);
        RequireEqual(request.FiscalDocumentNumber, ApprovedFiscalDocumentNumber, "fiscal_document_number_not_approved", errors);
        RequireEqual(request.ReasonCode, ApprovedReasonCode, "reason_code_not_approved", errors);
        RequireEqual(request.CorrelationId, FiscalIssuanceControlledUatInvocationService.DefaultSmokeProfile.CorrelationId, "correlation_id_not_approved_for_profile", errors);
        Require(!string.IsNullOrWhiteSpace(request.ApprovedBy), "approved_by_required", errors);

        return errors;
    }

    private List<string> ValidateConfiguration()
    {
        var errors = new List<string>();
        if (!_posServerOptions.EnableControlledUatDiagnosticPath)
        {
            errors.Add("controlled_diagnostic_flag_disabled");
        }

        if (!_posServerOptions.EnablePosServerFiscalIssuanceLiveCall)
        {
            errors.Add("pos_server_live_call_flag_disabled");
        }

        if (string.IsNullOrWhiteSpace(_posServerOptions.PosServerBaseUrl))
        {
            errors.Add("pos_server_base_url_required");
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

    private static string MapSuccessfulStatus(PosServerFiscalDocumentVoidOutcome outcome) =>
        outcome switch
        {
            PosServerFiscalDocumentVoidOutcome.NewlyVoided => "pos_server_void_recorded",
            PosServerFiscalDocumentVoidOutcome.IdempotentReplay => "pos_server_void_idempotent_replay",
            PosServerFiscalDocumentVoidOutcome.AlreadyVoided => "pos_server_already_voided",
            _ => "pos_server_void_failed"
        };

    private static string MapFailedStatus(PosServerFiscalDocumentVoidOutcome outcome) =>
        outcome switch
        {
            PosServerFiscalDocumentVoidOutcome.Conflict => "pos_server_void_conflict",
            PosServerFiscalDocumentVoidOutcome.Rejected or
            PosServerFiscalDocumentVoidOutcome.NotFound => "pos_server_void_rejected",
            _ => "pos_server_void_failed"
        };

    private static List<string> ValidateReference(
        ControlledUatFiscalVoidSmokeRequest request,
        FiscalIssuanceReferenceRecord reference)
    {
        var errors = new List<string>();
        Require(reference.FiscalIssuanceReferenceId == ApprovedFiscalIssuanceReferenceId, "fiscal_issuance_reference_id_not_approved", errors);
        Require(reference.PosServerFiscalDocumentId == ApprovedPosServerFiscalDocumentId, "pos_server_fiscal_document_id_mismatch", errors);
        Require(string.Equals(reference.FiscalDocumentNumber, request.FiscalDocumentNumber, StringComparison.Ordinal), "fiscal_document_number_mismatch", errors);
        Require(reference.FiscalIssuanceState == FiscalIssuanceIntegrationState.FiscalIssuanceRecorded, "fiscal_reference_not_recorded", errors);
        Require(reference.FiscalNumberAssignmentState == FiscalNumberAssignmentState.Assigned, "fiscal_number_not_assigned", errors);
        Require(reference.FiscalSequenceValue == 2, "fiscal_sequence_value_not_approved", errors);
        return errors;
    }

    private static ControlledUatFiscalVoidSmokeResponse Rejected(
        ControlledUatFiscalVoidSmokeRequest request,
        string status,
        int httpStatusCode,
        IReadOnlyList<string> errors) =>
        new(
            Accepted: false,
            Status: status,
            HttpStatusCode: httpStatusCode,
            Errors: errors,
            ProfileId: request.ProfileId,
            CorrelationId: request.CorrelationId,
            FiscalIssuanceReferenceId: request.FiscalIssuanceReferenceId,
            PosServerFiscalDocumentId: request.PosServerFiscalDocumentId,
            FiscalDocumentNumber: request.FiscalDocumentNumber,
            FiscalDocumentStatusPosture: null,
            FiscalSequenceValue: null,
            NewFiscalNumberAllocated: false,
            PaymentFinalityChanged: false,
            ExitAuthorizationIssued: false,
            GateBehaviorTriggered: false,
            RefundOrReversalCreated: false,
            HikCentralCalled: false,
            PaymentProviderCalled: false,
            RenderingGenerated: false,
            StatusHistoryRecorded: false,
            IdempotentReplay: false,
            PosServerResultClassification: null,
            VoidStatus: null);

    private static void Require(bool condition, string error, List<string> errors)
    {
        if (!condition)
        {
            errors.Add(error);
        }
    }

    private static void RequireEqual(Guid? actual, Guid expected, string error, List<string> errors) =>
        Require(actual == expected, error, errors);

    private static void RequireEqual(string? actual, string expected, string error, List<string> errors) =>
        Require(string.Equals(actual?.Trim(), expected, StringComparison.Ordinal), error, errors);
}

public sealed record ControlledUatFiscalVoidSmokeRequest(
    string? ProfileId,
    Guid? FiscalIssuanceReferenceId,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    string? ReasonCode,
    string? CorrelationId,
    string? ApprovedBy,
    bool? ExplicitExecutionApproval);

public sealed record ControlledUatFiscalVoidSmokeResponse(
    bool Accepted,
    string Status,
    int HttpStatusCode,
    IReadOnlyList<string> Errors,
    string? ProfileId,
    string? CorrelationId,
    Guid? FiscalIssuanceReferenceId,
    Guid? PosServerFiscalDocumentId,
    string? FiscalDocumentNumber,
    string? FiscalDocumentStatusPosture,
    long? FiscalSequenceValue,
    bool NewFiscalNumberAllocated,
    bool PaymentFinalityChanged,
    bool ExitAuthorizationIssued,
    bool GateBehaviorTriggered,
    bool RefundOrReversalCreated,
    bool HikCentralCalled,
    bool PaymentProviderCalled,
    bool RenderingGenerated,
    bool StatusHistoryRecorded,
    bool IdempotentReplay,
    string? PosServerResultClassification = null,
    string? VoidStatus = null);

public sealed record ControlledUatFiscalVoidSafetyGuardRequest(
    string ProfileId,
    Guid FiscalIssuanceReferenceId,
    Guid PosServerFiscalDocumentId,
    string FiscalDocumentNumber,
    string PaymentFinalityRef,
    long? FiscalSequenceValue,
    string ReasonCode,
    Guid CorrelationId);

public sealed record ControlledUatFiscalVoidSafetyGuardResult(
    bool Succeeded,
    string Status,
    IReadOnlyList<string> Errors)
{
    public static ControlledUatFiscalVoidSafetyGuardResult Accepted() =>
        new(true, "controlled_uat_real_void_safety_guard_accepted", Array.Empty<string>());

    public static ControlledUatFiscalVoidSafetyGuardResult Rejected(string status, IReadOnlyList<string> errors) =>
        new(false, status, errors);
}

public sealed record ControlledUatFiscalVoidSmokeStoreRequest(
    string ProfileId,
    Guid FiscalIssuanceReferenceId,
    Guid PosServerFiscalDocumentId,
    string FiscalDocumentNumber,
    string PaymentFinalityRef,
    string ReasonCode,
    Guid CorrelationId,
    string ApprovedBy);

public sealed record ControlledUatFiscalVoidSmokeStoreResult(
    bool Succeeded,
    string Status,
    IReadOnlyList<string> Errors,
    string? FiscalDocumentNumber,
    long? FiscalSequenceValue,
    string? FiscalDocumentStatusPosture,
    bool StatusHistoryRecorded,
    bool AlreadyRecorded)
{
    public static ControlledUatFiscalVoidSmokeStoreResult Rejected(string status, IReadOnlyList<string> errors) =>
        new(false, status, errors, null, null, null, false, false);
}
