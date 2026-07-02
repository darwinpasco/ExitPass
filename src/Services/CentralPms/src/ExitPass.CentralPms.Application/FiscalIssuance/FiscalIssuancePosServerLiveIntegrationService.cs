namespace ExitPass.CentralPms.Application.FiscalIssuance;

public interface IFiscalIssuancePosServerLiveIntegrationService
{
    Task<FiscalIssuancePosServerLiveIntegrationResult> TryIssueFiscalDocumentViaPosServerAsync(
        Guid fiscalIssuanceReferenceId,
        CentralPmsFiscalDocumentMappingContext fiscalContext,
        PosServerCreateResultRecordingContext recordingContext,
        CancellationToken cancellationToken);
}

public sealed class FiscalIssuancePosServerLiveIntegrationService : IFiscalIssuancePosServerLiveIntegrationService
{
    private readonly FiscalIssuancePosServerIntegrationOptions _options;
    private readonly IPosServerFiscalDocumentRequestMapper _requestMapper;
    private readonly IPosServerFiscalDocumentClient _client;
    private readonly IFiscalIssuanceOrchestrationService _orchestrationService;

    public FiscalIssuancePosServerLiveIntegrationService(
        FiscalIssuancePosServerIntegrationOptions options,
        IPosServerFiscalDocumentRequestMapper requestMapper,
        IPosServerFiscalDocumentClient client,
        IFiscalIssuanceOrchestrationService orchestrationService)
    {
        _options = options ?? new FiscalIssuancePosServerIntegrationOptions();
        _requestMapper = requestMapper;
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

        var configurationErrors = _options.ValidateForLiveCall();
        if (configurationErrors.Count > 0)
        {
            return FiscalIssuancePosServerLiveIntegrationResult.ConfigurationInvalid(configurationErrors);
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
}

public sealed class FiscalIssuancePosServerIntegrationOptions
{
    public const string SectionName = "FiscalIssuance:PosServerIntegration";

    public bool EnablePosServerFiscalIssuanceLiveCall { get; set; }

    public string? PosServerBaseUrl { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public bool EnableLiveFiscalIssuanceFromPaymentFlow { get; set; }

    public bool EnableLiveFiscalIssuanceFromExitFlow { get; set; }

    public IReadOnlyList<string> ValidateForLiveCall()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(PosServerBaseUrl))
        {
            errors.Add("pos_server_base_url_required");
        }
        else if (!Uri.TryCreate(PosServerBaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("pos_server_base_url_invalid");
        }

        if (TimeoutSeconds <= 0)
        {
            errors.Add("pos_server_timeout_seconds_must_be_positive");
        }

        return errors;
    }
}

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
