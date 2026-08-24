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
            request = _requestMapper.Map(ApplyConfiguredFiscalProfile(fiscalContext));
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

        PosServerFiscalDocumentCreateResult posServerResult;
        try
        {
            posServerResult = await _client.CreateFiscalDocumentAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            posServerResult = CreateRetryableTransportFailure(
                httpStatusCode: 504,
                code: "pos_server_timeout");
        }
        catch (HttpRequestException)
        {
            posServerResult = CreateRetryableTransportFailure(
                httpStatusCode: 503,
                code: "pos_server_unavailable");
        }

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

    private static PosServerFiscalDocumentCreateResult CreateRetryableTransportFailure(
        int httpStatusCode,
        string code) =>
        new(
            Outcome: PosServerFiscalDocumentOutcome.FailedService,
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
            ErrorPosture: FiscalIssuanceErrorPosture.RetryAfterServiceRecovery);

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

    private CentralPmsFiscalDocumentMappingContext ApplyConfiguredFiscalProfile(
        CentralPmsFiscalDocumentMappingContext context)
    {
        var endpoint = _options.Endpoints.SingleOrDefault(candidate =>
            candidate.SitePosServerId == context.SitePosServerId &&
            string.Equals(candidate.SitePosServerRef?.Trim(), context.SitePosServerRef, StringComparison.Ordinal));

        if (endpoint is null || !endpoint.HasCompleteFiscalProfile())
        {
            return context;
        }

        return context with
        {
            FiscalDocumentTypeCodeId = endpoint.FiscalDocumentTypeCodeId,
            FiscalDocumentStatusCodeId = endpoint.FiscalDocumentStatusCodeId,
            DocumentLines = context.DocumentLines
                .Select(line => line with { LineTypeCodeId = endpoint.FiscalLineTypeCodeId })
                .ToArray(),
            Tenders = context.Tenders
                .Select(tender => tender with { TenderTypeCodeId = endpoint.FiscalTenderTypeCodeId })
                .ToArray(),
            TaxDetails = context.TaxDetails
                .Select(tax => tax with
                {
                    TaxTypeCodeId = endpoint.FiscalTaxTypeCodeId!.Value,
                    TaxClassificationCodeId = endpoint.FiscalTaxClassificationCodeId!.Value
                })
                .ToArray(),
            DiscountPrivilegeDetails = context.DiscountPrivilegeDetails
                .Select(discount => discount with
                {
                    DiscountPrivilegeTypeCodeId = endpoint.FiscalDiscountPrivilegeTypeCodeId!.Value
                })
                .ToArray(),
            Totals = context.Totals
                .Select(total => total with { TotalTypeCodeId = endpoint.FiscalTotalTypeCodeId })
                .ToArray()
        };
    }
}

public sealed class FiscalIssuancePosServerIntegrationOptions
{
    public const string SectionName = "FiscalIssuance:PosServerIntegration";

    public bool EnablePosServerFiscalIssuanceLiveCall { get; set; }

    public bool EnableControlledUatDiagnosticPath { get; set; }

    [Obsolete("Use site-specific Endpoints. A global POS Server endpoint is not a supported runtime route.")]
    public string? PosServerBaseUrl { get; set; }

    public string? RuntimeEnvironment { get; set; }

    public List<SitePosServerEndpointOptions> Endpoints { get; set; } = [];

    public int TimeoutSeconds { get; set; } = 10;

    public bool EnableLiveFiscalIssuanceFromPaymentFlow { get; set; }

    public bool EnableLiveFiscalIssuanceFromExitFlow { get; set; }

    public List<ControlledUatFiscalSmokeProfileOptions> ControlledUatSmokeProfiles { get; set; } = [];

    public FiscalIssuancePosServerIntegrationReadiness EvaluateReadiness()
    {
        var baseUrlConfigured = Endpoints.Count > 0;
        var timeoutConfigured = TimeoutSeconds > 0;
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
                "site_pos_server_endpoints_required",
                baseUrlConfigured,
                timeoutConfigured);
        }

        var endpointErrors = ValidateEndpoints();
        if (endpointErrors.Count > 0)
        {
            return new FiscalIssuancePosServerIntegrationReadiness(
                Status: FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidBaseUrl,
                IsEnabled: true,
                IsReady: false,
                Reason: endpointErrors[0],
                BaseUrlConfigured: true,
                TimeoutConfigured: timeoutConfigured,
                LiveCallsAllowedFromPaymentFlow: EnableLiveFiscalIssuanceFromPaymentFlow,
                LiveCallsAllowedFromExitFlow: EnableLiveFiscalIssuanceFromExitFlow,
                Errors: endpointErrors);
        }

        if (!timeoutConfigured)
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledInvalidTimeout,
                "pos_server_timeout_seconds_must_be_positive",
                baseUrlConfigured,
                timeoutConfigured);
        }

        if (EnableLiveFiscalIssuanceFromExitFlow)
        {
            return InvalidReadiness(
                FiscalIssuancePosServerIntegrationReadinessStatuses.EnabledUnsafeFlowWiring,
                "exit_live_flow_flag_must_remain_disabled",
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
            LiveCallsAllowedFromPaymentFlow: EnableLiveFiscalIssuanceFromPaymentFlow,
            LiveCallsAllowedFromExitFlow: false,
            Errors: Array.Empty<string>());
    }

    public IReadOnlyList<string> ValidateForLiveCall()
    {
        var readiness = EvaluateReadiness();
        return readiness.IsReady ? Array.Empty<string>() : readiness.Errors;
    }

    private IReadOnlyList<string> ValidateEndpoints()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(RuntimeEnvironment))
        {
            errors.Add("pos_server_runtime_environment_required");
        }

        foreach (var duplicate in Endpoints
                     .GroupBy(endpoint => endpoint.SitePosServerId)
                     .Where(group => group.Key == Guid.Empty || group.Count() != 1))
        {
            errors.Add(duplicate.Key == Guid.Empty
                ? "site_pos_server_id_required"
                : "site_pos_server_endpoint_id_duplicate");
        }

        foreach (var duplicate in Endpoints
                     .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.SitePosServerRef))
                     .GroupBy(endpoint => endpoint.SitePosServerRef!.Trim(), StringComparer.Ordinal)
                     .Where(group => group.Count() != 1))
        {
            errors.Add("site_pos_server_endpoint_ref_duplicate");
        }

        if (EnableLiveFiscalIssuanceFromPaymentFlow)
        {
            foreach (var duplicate in Endpoints
                         .GroupBy(endpoint => endpoint.SiteId)
                         .Where(group => group.Key == Guid.Empty || group.Count() != 1))
            {
                errors.Add(duplicate.Key == Guid.Empty
                    ? "site_pos_server_site_id_required_for_payment_flow"
                    : "site_pos_server_site_id_duplicate");
            }
        }

        foreach (var endpoint in Endpoints)
        {
            if (EnableLiveFiscalIssuanceFromPaymentFlow && endpoint.SiteId == Guid.Empty)
            {
                errors.Add("site_pos_server_site_id_required_for_payment_flow");
            }

            if (EnableLiveFiscalIssuanceFromPaymentFlow && !endpoint.HasCompleteFiscalProfile())
            {
                errors.Add("site_pos_server_fiscal_profile_required_for_payment_flow");
            }

            if (string.IsNullOrWhiteSpace(endpoint.SitePosServerRef))
            {
                errors.Add("site_pos_server_ref_required");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Environment) ||
                !string.Equals(endpoint.Environment.Trim(), RuntimeEnvironment?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("site_pos_server_endpoint_environment_mismatch");
            }

            if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath != "/")
            {
                errors.Add("site_pos_server_endpoint_url_invalid");
            }
            else if (uri.Scheme != Uri.UriSchemeHttps && !IsLocalEnvironment(RuntimeEnvironment))
            {
                errors.Add("site_pos_server_endpoint_https_required");
            }

            if (string.IsNullOrWhiteSpace(endpoint.ApiKeyFile))
            {
                errors.Add("site_pos_server_api_key_file_required");
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsLocalEnvironment(string? environment) =>
        string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "SecureDevelopment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "Test", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(environment, "IntegrationTest", StringComparison.OrdinalIgnoreCase);

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

public sealed class SitePosServerEndpointOptions
{
    public Guid SiteId { get; set; }

    public Guid SitePosServerId { get; set; }

    public string? SitePosServerRef { get; set; }

    public string? BaseUrl { get; set; }

    public string? ApiKeyFile { get; set; }

    public string? Environment { get; set; }

    public bool Enabled { get; set; }

    public Guid? FiscalDocumentTypeCodeId { get; set; }

    public Guid? FiscalDocumentStatusCodeId { get; set; }

    public Guid? FiscalLineTypeCodeId { get; set; }

    public Guid? FiscalTenderTypeCodeId { get; set; }

    public Guid? FiscalTaxTypeCodeId { get; set; }

    public Guid? FiscalTaxClassificationCodeId { get; set; }

    public Guid? FiscalDiscountPrivilegeTypeCodeId { get; set; }

    public Guid? FiscalTotalTypeCodeId { get; set; }

    public bool HasCompleteFiscalProfile() =>
        IsConfigured(FiscalDocumentTypeCodeId) &&
        IsConfigured(FiscalDocumentStatusCodeId) &&
        IsConfigured(FiscalLineTypeCodeId) &&
        IsConfigured(FiscalTenderTypeCodeId) &&
        IsConfigured(FiscalTaxTypeCodeId) &&
        IsConfigured(FiscalTaxClassificationCodeId) &&
        IsConfigured(FiscalDiscountPrivilegeTypeCodeId) &&
        IsConfigured(FiscalTotalTypeCodeId);

    private static bool IsConfigured(Guid? value) => value is not null && value != Guid.Empty;
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
