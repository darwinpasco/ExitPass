using ExitPass.CentralPms.Domain.FiscalIssuance;

namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuancePosServerLiveIntegrationService
{
    Task<FiscalIssuancePosServerLiveIntegrationResult> TryIssueFiscalDocumentViaPosServerAsync(
        Guid fiscalIssuanceReferenceId,
        CentralPmsFiscalDocumentMappingContext fiscalContext,
        PosServerCreateResultRecordingContext recordingContext,
        CancellationToken cancellationToken);

    Task<FiscalIssuancePosServerDiagnosticResult> RunPosServerFiscalIssuanceDiagnosticAsync(
        Guid fiscalIssuanceReferenceId,
        CentralPmsFiscalDocumentMappingContext fiscalContext,
        PosServerCreateResultRecordingContext recordingContext,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuancePosServerLiveIntegrationService : IFiscalIssuancePosServerLiveIntegrationService
{
    private readonly FiscalIssuancePosServerIntegrationOptions _options;
    private readonly IPosServerFiscalDocumentRequestMapper _requestMapper;
    private readonly IFiscalSemanticRequestHashCalculator _semanticRequestHashCalculator;
    private readonly IPosServerFiscalDocumentClient _client;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;

    public FiscalIssuancePosServerLiveIntegrationService(
        FiscalIssuancePosServerIntegrationOptions options,
        IPosServerFiscalDocumentRequestMapper requestMapper,
        IFiscalSemanticRequestHashCalculator semanticRequestHashCalculator,
        IPosServerFiscalDocumentClient client,
        IFiscalIssuanceOrchestrationService orchestrationService)
    {
        _options = options ?? new FiscalIssuancePosServerIntegrationOptions();
        _requestMapper = requestMapper;
        _semanticRequestHashCalculator = semanticRequestHashCalculator;
        _client = client;
        _orchestrationService = orchestrationService;
    }

    public async Task<FiscalIssuancePosServerLiveIntegrationResult> TryIssueFiscalDocumentViaPosServerAsync(
        Guid fiscalIssuanceReferenceId,
        CentralPmsFiscalDocumentMappingContext fiscalContext,
        PosServerCreateResultRecordingContext recordingContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fiscalContext);
        ArgumentNullException.ThrowIfNull(recordingContext);

        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(fiscalIssuanceReferenceId));
        }

        if (!_options.EnablePosServerFiscalIssuanceLiveCall)
        {
            return FiscalIssuancePosServerLiveIntegrationResult.Disabled();
        }

        var readiness = _options.EvaluateReadiness();
        if (!readiness.IsReady)
        {
            return FiscalIssuancePosServerLiveIntegrationResult.ConfigurationInvalid(readiness.Errors);
        }

        PosServerFiscalDocumentCreateRequest request;
        try
        {
            request = _requestMapper.Map(fiscalContext);
        }
        catch (ArgumentException ex)
        {
            return FiscalIssuancePosServerLiveIntegrationResult.LocalContextInvalid(ex.Message);
        }

        var semanticRequestHash = _semanticRequestHashCalculator.Calculate(request);
        if (semanticRequestHash.Status != FiscalSemanticRequestHashSourceStatus.Available ||
            string.IsNullOrWhiteSpace(semanticRequestHash.HashValue))
        {
            return FiscalIssuancePosServerLiveIntegrationResult.LocalContextInvalid(
                semanticRequestHash.BlockReasonCode ?? "semantic_request_hash_source_unavailable");
        }

        try
        {
            await _orchestrationService.RecordSemanticRequestHashAsync(
                fiscalIssuanceReferenceId,
                semanticRequestHash,
                recordingContext.ServiceIdentityId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return FiscalIssuancePosServerLiveIntegrationResult.LocalContextInvalid(
                $"semantic_request_hash_persistence_failed:{ex.Message}");
        }

        await _orchestrationService.MarkRequestedAsync(
            fiscalIssuanceReferenceId,
            new FiscalIssuanceTransitionContext(
                CorrelationId: recordingContext.CorrelationId,
                ServiceIdentityId: recordingContext.ServiceIdentityId),
            cancellationToken);

        var posServerResult = await _client.CreateFiscalDocumentAsync(request, cancellationToken);

        var appliedReference = posServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted &&
            posServerResult.Succeeded
            ? await _orchestrationService.ApplyPosServerCreateResultAsync(
                fiscalIssuanceReferenceId,
                posServerResult,
                recordingContext,
                cancellationToken)
            : await _orchestrationService.ApplyPosServerFailureResultAsync(
                fiscalIssuanceReferenceId,
                posServerResult,
                recordingContext,
                cancellationToken);

        return FiscalIssuancePosServerLiveIntegrationResult.Applied(
            request,
            posServerResult,
            appliedReference);
    }

    public async Task<FiscalIssuancePosServerDiagnosticResult> RunPosServerFiscalIssuanceDiagnosticAsync(
        Guid fiscalIssuanceReferenceId,
        CentralPmsFiscalDocumentMappingContext fiscalContext,
        PosServerCreateResultRecordingContext recordingContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fiscalContext);
        ArgumentNullException.ThrowIfNull(recordingContext);

        if (fiscalIssuanceReferenceId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal issuance reference id is required.", nameof(fiscalIssuanceReferenceId));
        }

        var readiness = _options.EvaluateReadiness();

        if (!_options.EnablePosServerFiscalIssuanceLiveCall)
        {
            return FiscalIssuancePosServerDiagnosticResult.NotAttempted(
                FiscalIssuancePosServerDiagnosticStatuses.Disabled,
                readiness,
                recordingContext.CorrelationId);
        }

        if (!_options.EnableControlledUatDiagnosticPath)
        {
            return FiscalIssuancePosServerDiagnosticResult.NotAttempted(
                FiscalIssuancePosServerDiagnosticStatuses.DiagnosticDisabled,
                readiness,
                recordingContext.CorrelationId);
        }

        if (!readiness.IsReady)
        {
            return FiscalIssuancePosServerDiagnosticResult.NotAttempted(
                FiscalIssuancePosServerDiagnosticStatuses.ConfigurationInvalid,
                readiness,
                recordingContext.CorrelationId);
        }

        var result = await TryIssueFiscalDocumentViaPosServerAsync(
            fiscalIssuanceReferenceId,
            fiscalContext,
            recordingContext,
            cancellationToken);

        return FiscalIssuancePosServerDiagnosticResult.FromLiveIntegrationResult(
            readiness,
            result,
            recordingContext.CorrelationId);
    }
}

public sealed class FiscalIssuancePosServerIntegrationOptions
{
    public const string SectionName = "FiscalIssuance:PosServerIntegration";

    public bool EnablePosServerFiscalIssuanceLiveCall { get; set; }

    public bool EnableControlledUatDiagnosticPath { get; set; }

    public string? PosServerBaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public bool EnableLiveFiscalIssuanceFromPaymentFlow { get; set; }

    public bool EnableLiveFiscalIssuanceFromExitFlow { get; set; }

    public FiscalIssuancePosServerIntegrationReadiness EvaluateReadiness()
    {
        var baseUrlConfigured = !string.IsNullOrWhiteSpace(PosServerBaseUrl);
        var timeoutConfigured = TimeoutSeconds > 0;
        var liveFlowConfigured = EnableLiveFiscalIssuanceFromPaymentFlow || EnableLiveFiscalIssuanceFromExitFlow;

        if (!EnablePosServerFiscalIssuanceLiveCall)
        {
            return new FiscalIssuancePosServerIntegrationReadiness(
                Status: baseUrlConfigured
                    ? FiscalIssuancePosServerIntegrationReadinessStatuses.DisabledConfigPresent
                    : FiscalIssuancePosServerIntegrationReadinessStatuses.Disabled,
                IsEnabled: false,
                IsReady: false,
                Reason: baseUrlConfigured
                    ? "pos_server_fiscal_issuance_live_call_disabled_config_present"
                    : "pos_server_fiscal_issuance_live_call_disabled",
                BaseUrlConfigured: baseUrlConfigured,
                TimeoutConfigured: timeoutConfigured,
                LiveCallsAllowedFromPaymentFlow: EnableLiveFiscalIssuanceFromPaymentFlow,
                LiveCallsAllowedFromExitFlow: EnableLiveFiscalIssuanceFromExitFlow,
                Errors: Array.Empty<string>());
        }

        if (!baseUrlConfigured)
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledMissingBaseUrl,
                "pos_server_base_url_required",
                baseUrlConfigured,
                timeoutConfigured);
        }

        if (!Uri.TryCreate(PosServerBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidBaseUrl,
                "pos_server_base_url_invalid",
                baseUrlConfigured,
                timeoutConfigured);
        }

        if (!timeoutConfigured)
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidTimeout,
                "pos_server_timeout_seconds_must_be_positive",
                baseUrlConfigured,
                timeoutConfigured);
        }

        if (liveFlowConfigured)
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledUnsafeFlowWiring,
                "payment_exit_live_flow_flags_must_remain_disabled",
                baseUrlConfigured,
                timeoutConfigured);
        }

        return new FiscalIssuancePosServerIntegrationReadiness(
            Status: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledReady,
            IsEnabled: true,
            IsReady: true,
            Reason: "pos_server_fiscal_issuance_live_call_configuration_ready",
            BaseUrlConfigured: true,
            TimeoutConfigured: true,
            LiveCallsAllowedFromPaymentFlow: false,
            LiveCallsAllowedFromExitFlow: false,
            Errors: Array.Empty<string>());
    }

    public IReadOnlyList<string> ValidateForLiveCall()
    {
        var readiness = EvaluateReadiness();
        return readiness.IsReady ? Array.Empty<string>() : readiness.Errors;
    }

    private FiscalIssuancePosServerIntegrationReadiness InvalidReadiness(
        string status,
        string error,
        bool baseUrlConfigured,
        bool timeoutConfigured) =>
        new(
            Status: status,
            IsEnabled: true,
            IsReady: false,
            Reason: error,
            BaseUrlConfigured: baseUrlConfigured,
            TimeoutConfigured: timeoutConfigured,
            LiveCallsAllowedFromPaymentFlow: EnableLiveFiscalIssuanceFromPaymentFlow,
            LiveCallsAllowedFromExitFlow: EnableLiveFiscalIssuanceFromExitFlow,
            Errors: [error]);
}

public static class FiscalIssuancePosServerIntegrationReadinessStatuses
{
    public const string Disabled = "disabled";
    public const string DisabledConfigPresent = "disabled_config_present";
    public const string EnabledMissingBaseUrl = "enabled_missing_base_url";
    public const string EnabledInvalidBaseUrl = "enabled_invalid_base_url";
    public const string EnabledInvalidTimeout = "enabled_invalid_timeout";
    public const string EnabledReady = "enabled_ready";
    public const string EnabledUnsafeFlowWiring = "enabled_unsafe_flow_wiring";
}

public sealed record FiscalIssuancePosServerIntegrationReadiness(
    string Status,
    bool IsEnabled,
    bool IsReady,
    string Reason,
    bool BaseUrlConfigured,
    bool TimeoutConfigured,
    bool LiveCallsAllowedFromPaymentFlow,
    bool LiveCallsAllowedFromExitFlow,
    IReadOnlyList<string> Errors);

public enum FiscalIssuancePosServerLiveIntegrationStatus
{
    Disabled = 1,
    ConfigurationInvalid = 2,
    LocalContextInvalid = 3,
    Applied = 4
}

public sealed record FiscalIssuancePosServerLiveIntegrationResult(
    FiscalIssuancePosServerLiveIntegrationStatus Status,
    string Code,
    IReadOnlyList<string> Errors,
    PosServerFiscalDocumentCreateRequest? MappedRequest,
    PosServerFiscalDocumentCreateResult? PosServerResult,
    FiscalIssuanceReferenceRecord? FiscalIssuanceReference)
{
    public bool Succeeded => Status == FiscalIssuancePosServerLiveIntegrationStatus.Applied;

    public static FiscalIssuancePosServerLiveIntegrationResult Disabled() =>
        new(
            FiscalIssuancePosServerLiveIntegrationStatus.Disabled,
            "pos_server_fiscal_issuance_live_call_disabled",
            Array.Empty<string>(),
            MappedRequest: null,
            PosServerResult: null,
            FiscalIssuanceReference: null);

    public static FiscalIssuancePosServerLiveIntegrationResult ConfigurationInvalid(
        IReadOnlyList<string> errors) =>
        new(
            FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid,
            "pos_server_fiscal_issuance_live_call_configuration_invalid",
            errors,
            MappedRequest: null,
            PosServerResult: null,
            FiscalIssuanceReference: null);

    public static FiscalIssuancePosServerLiveIntegrationResult LocalContextInvalid(string error) =>
        new(
            FiscalIssuancePosServerLiveIntegrationStatus.LocalContextInvalid,
            "pos_server_fiscal_issuance_live_call_context_invalid",
            [error],
            MappedRequest: null,
            PosServerResult: null,
            FiscalIssuanceReference: null);

    public static FiscalIssuancePosServerLiveIntegrationResult Applied(
        PosServerFiscalDocumentCreateRequest mappedRequest,
        PosServerFiscalDocumentCreateResult posServerResult,
        FiscalIssuanceReferenceRecord fiscalIssuanceReference) =>
        new(
            FiscalIssuancePosServerLiveIntegrationStatus.Applied,
            "pos_server_fiscal_issuance_live_call_applied",
            Array.Empty<string>(),
            mappedRequest,
            posServerResult,
            fiscalIssuanceReference);
}

public static class FiscalIssuancePosServerDiagnosticStatuses
{
    public const string Disabled = "disabled";
    public const string DiagnosticDisabled = "diagnostic_disabled";
    public const string ConfigurationInvalid = "config_invalid";
    public const string LocalContextInvalid = "local_context_invalid";
    public const string NewlyCreatedRecorded = "newly_created_recorded";
    public const string ReplayRecorded = "replay_recorded";
    public const string ConflictFailureMapped = "conflict_failure_mapped";
    public const string RequestFailureMapped = "request_failure_mapped";
    public const string ConfigurationFailureMapped = "configuration_failure_mapped";
    public const string ServiceFailureMapped = "service_failure_mapped";
    public const string UnknownFailClosed = "unknown_fail_closed";
}

public sealed record FiscalIssuancePosServerDiagnosticResult(
    string Status,
    string ReadinessStatus,
    bool RequestMapped,
    bool ClientCalled,
    FiscalIssuanceResultClassification? PosServerResponseClassification,
    FiscalIssuanceIntegrationState? FiscalIssuanceStateApplied,
    Guid? FiscalDocumentId,
    string? FiscalDocumentNumber,
    FiscalIssuanceEvidenceStatus? FiscalIssuanceEvidenceStatus,
    FiscalNumberAssignmentState? FiscalNumberAssignmentState,
    string? ErrorCode,
    FiscalIssuanceErrorPosture? ErrorPosture,
    bool NoPaymentFinalityChanged,
    bool NoExitAuthorizationIssued,
    Guid? CorrelationId,
    IReadOnlyList<string> Errors)
{
    public static FiscalIssuancePosServerDiagnosticResult NotAttempted(
        string status,
        FiscalIssuancePosServerIntegrationReadiness readiness,
        Guid? correlationId) =>
        new(
            Status: status,
            ReadinessStatus: readiness.Status,
            RequestMapped: false,
            ClientCalled: false,
            PosServerResponseClassification: null,
            FiscalIssuanceStateApplied: null,
            FiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            ErrorCode: null,
            ErrorPosture: null,
            NoPaymentFinalityChanged: true,
            NoExitAuthorizationIssued: true,
            CorrelationId: correlationId,
            Errors: readiness.Errors);

    public static FiscalIssuancePosServerDiagnosticResult FromLiveIntegrationResult(
        FiscalIssuancePosServerIntegrationReadiness readiness,
        FiscalIssuancePosServerLiveIntegrationResult result,
        Guid? correlationId) =>
        new(
            Status: ResolveStatus(result),
            ReadinessStatus: readiness.Status,
            RequestMapped: result.MappedRequest is not null,
            ClientCalled: result.MappedRequest is not null && result.PosServerResult is not null,
            PosServerResponseClassification: result.PosServerResult?.ResultClassification,
            FiscalIssuanceStateApplied: result.FiscalIssuanceReference?.FiscalIssuanceState,
            FiscalDocumentId: result.PosServerResult?.FiscalDocumentId ?? result.FiscalIssuanceReference?.PosServerFiscalDocumentId,
            FiscalDocumentNumber: result.PosServerResult?.FiscalDocumentNumber ?? result.FiscalIssuanceReference?.FiscalDocumentNumber,
            FiscalIssuanceEvidenceStatus: result.PosServerResult?.FiscalIssuanceEvidenceStatus ?? result.FiscalIssuanceReference?.FiscalIssuanceEvidenceStatus,
            FiscalNumberAssignmentState: result.PosServerResult?.FiscalNumberAssignmentState ?? result.FiscalIssuanceReference?.FiscalNumberAssignmentState,
            ErrorCode: result.PosServerResult?.Succeeded == false
                ? result.PosServerResult.Code
                : null,
            ErrorPosture: result.PosServerResult?.ErrorPosture,
            NoPaymentFinalityChanged: true,
            NoExitAuthorizationIssued: true,
            CorrelationId: correlationId,
            Errors: result.Errors);

    private static string ResolveStatus(FiscalIssuancePosServerLiveIntegrationResult result)
    {
        if (result.Status == FiscalIssuancePosServerLiveIntegrationStatus.ConfigurationInvalid)
        {
            return FiscalIssuancePosServerDiagnosticStatuses.ConfigurationInvalid;
        }

        if (result.Status == FiscalIssuancePosServerLiveIntegrationStatus.LocalContextInvalid)
        {
            return FiscalIssuancePosServerDiagnosticStatuses.LocalContextInvalid;
        }

        if (result.PosServerResult is null)
        {
            return result.Status == FiscalIssuancePosServerLiveIntegrationStatus.Disabled
                ? FiscalIssuancePosServerDiagnosticStatuses.Disabled
                : FiscalIssuancePosServerDiagnosticStatuses.UnknownFailClosed;
        }

        if (result.PosServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted &&
            result.PosServerResult.ResultClassification == FiscalIssuanceResultClassification.NewlyCreated)
        {
            return FiscalIssuancePosServerDiagnosticStatuses.NewlyCreatedRecorded;
        }

        if (result.PosServerResult.Outcome == PosServerFiscalDocumentOutcome.Accepted &&
            result.PosServerResult.ResultClassification == FiscalIssuanceResultClassification.IdempotentReplay)
        {
            return FiscalIssuancePosServerDiagnosticStatuses.ReplayRecorded;
        }

        return result.PosServerResult.Outcome switch
        {
            PosServerFiscalDocumentOutcome.Conflict => FiscalIssuancePosServerDiagnosticStatuses.ConflictFailureMapped,
            PosServerFiscalDocumentOutcome.FailedRequest => FiscalIssuancePosServerDiagnosticStatuses.RequestFailureMapped,
            PosServerFiscalDocumentOutcome.FailedConfiguration => FiscalIssuancePosServerDiagnosticStatuses.ConfigurationFailureMapped,
            PosServerFiscalDocumentOutcome.FailedService => FiscalIssuancePosServerDiagnosticStatuses.ServiceFailureMapped,
            _ => FiscalIssuancePosServerDiagnosticStatuses.UnknownFailClosed
        };
    }
}
