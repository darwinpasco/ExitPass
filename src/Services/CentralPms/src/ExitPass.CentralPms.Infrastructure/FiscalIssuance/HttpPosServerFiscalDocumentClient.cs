using System.Net.Http.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Domain.FiscalIssuance;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class HttpPosServerFiscalDocumentClient : IPosServerFiscalDocumentClient
{
    private const string FiscalDocumentsPath = "/v1/fiscal-documents/";
    private const string ApiKeyHeader = "X-PosServer-Admin-Key";
    private const string PermissionHeader = "X-PosServer-Admin-Permission";
    private const string CorrelationHeader = "X-Correlation-Id";
    private const string CreatePermission = "fiscal_document.create";
    private const string ReadPermission = "fiscal_document.read";
    private const string VoidPermission = "fiscal_document.void";

    private readonly HttpClient _httpClient;
    private readonly SitePosServerEndpointResolver _endpointResolver;

    public HttpPosServerFiscalDocumentClient(
        HttpClient httpClient,
        IOptions<FiscalIssuancePosServerIntegrationOptions> options)
    {
        _httpClient = httpClient;
        _endpointResolver = new SitePosServerEndpointResolver(options);
    }

    public async Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
        PosServerFiscalDocumentCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        PosServerRoutingContext routingContext;
        try
        {
            routingContext = PosServerRoutingContext.Create(request.SitePosServerId, request.SitePosServerRef);
        }
        catch (ArgumentException)
        {
            return CreateConfigurationFailure("site_pos_server_routing_context_required");
        }

        var endpoint = _endpointResolver.Resolve(routingContext);
        if (!endpoint.IsSuccess)
        {
            return CreateConfigurationFailure(endpoint.Code);
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(endpoint.BaseUri!, FiscalDocumentsPath))
        {
            Content = JsonContent.Create(request)
        };
        ApplyAuthentication(httpRequest, endpoint.ApiKey!, CreatePermission, correlationId: null);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseCreateResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
        Guid fiscalDocumentId,
        PosServerRoutingContext routingContext,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        var endpoint = _endpointResolver.Resolve(routingContext);
        if (!endpoint.IsSuccess)
        {
            return ReadConfigurationFailure(endpoint.Code);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(endpoint.BaseUri!, $"{FiscalDocumentsPath}{fiscalDocumentId:D}"));
        ApplyAuthentication(request, endpoint.ApiKey!, ReadPermission, correlationId: null);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseReadResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
        Guid fiscalDocumentId,
        Guid? correlationId,
        PosServerRoutingContext routingContext,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        var endpoint = _endpointResolver.Resolve(routingContext);
        if (!endpoint.IsSuccess)
        {
            return PresentationConfigurationFailure(endpoint.Code);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(
                endpoint.BaseUri!,
                $"{FiscalDocumentsPath}{fiscalDocumentId:D}/digital-sales-invoice/presentation"));
        request.Headers.Accept.ParseAdd("application/json");
        ApplyAuthentication(request, endpoint.ApiKey!, ReadPermission, correlationId);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParsePresentationResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
        Guid fiscalDocumentId,
        PosServerFiscalDocumentVoidRequest request,
        PosServerRoutingContext routingContext,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        ArgumentNullException.ThrowIfNull(request);

        var endpoint = _endpointResolver.Resolve(routingContext);
        if (!endpoint.IsSuccess)
        {
            return VoidConfigurationFailure(endpoint.Code);
        }

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(endpoint.BaseUri!, $"{FiscalDocumentsPath}{fiscalDocumentId:D}/void"))
        {
            Content = JsonContent.Create(request)
        };
        ApplyAuthentication(httpRequest, endpoint.ApiKey!, VoidPermission, ParseCorrelationId(request.CorrelationId));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseVoidResponse((int)response.StatusCode, body);
    }

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        new(baseUri, relativePath);

    private static void ApplyAuthentication(
        HttpRequestMessage request,
        string apiKey,
        string permission,
        Guid? correlationId)
    {
        request.Headers.TryAddWithoutValidation(ApiKeyHeader, apiKey);
        request.Headers.TryAddWithoutValidation(PermissionHeader, permission);
        if (correlationId is not null && correlationId != Guid.Empty)
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeader, correlationId.Value.ToString("D"));
        }
    }

    private static Guid? ParseCorrelationId(string? value) =>
        Guid.TryParse(value, out var correlationId) && correlationId != Guid.Empty
            ? correlationId
            : null;

    private static PosServerFiscalDocumentCreateResult CreateConfigurationFailure(string code) =>
        new(
            PosServerFiscalDocumentOutcome.FailedConfiguration,
            Succeeded: false,
            HttpStatusCode: 503,
            Code: code,
            Message: "Site POS Server routing configuration is unavailable.",
            FiscalDocumentId: null,
            ResultClassification: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
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
            ErrorPosture: FiscalIssuanceErrorPosture.RetryAfterConfigurationCorrection);

    private static PosServerFiscalDocumentReadResult ReadConfigurationFailure(string code) =>
        new(
            PosServerFiscalDocumentOutcome.FailedConfiguration,
            Succeeded: false,
            HttpStatusCode: 503,
            Code: code,
            Message: "Site POS Server routing configuration is unavailable.",
            FiscalDocumentId: null,
            FiscalIssuanceEvidenceStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalDocumentStatusCodeId: null);

    private static PosServerFiscalDocumentPresentationReadResult PresentationConfigurationFailure(string code) =>
        new(
            PosServerFiscalDocumentOutcome.FailedConfiguration,
            Succeeded: false,
            HttpStatusCode: 503,
            Code: code,
            Message: "Site POS Server routing configuration is unavailable.",
            FiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalDocumentStatus: null,
            FiscalNumberAssignmentState: null,
            FiscalDocumentStatusCodeId: null,
            FiscalDocumentType: null,
            FiscalDocumentTypeCodeId: null,
            FiscalSeries: null,
            FiscalNumberPrefixText: null,
            FiscalNumberSuffixText: null,
            FiscalNumberAssignedAt: null,
            RecordedAt: null,
            VoidStatus: null,
            VoidReasonCode: null,
            VoidedAt: null,
            PresentationVersion: null,
            TemplateVersion: null,
            ContentType: null,
            AuthoritativeResponse: null);

    private static PosServerFiscalDocumentVoidResult VoidConfigurationFailure(string code) =>
        new(
            PosServerFiscalDocumentVoidOutcome.FailedService,
            Succeeded: false,
            HttpStatusCode: 503,
            Code: code,
            Message: "Site POS Server routing configuration is unavailable.",
            FiscalDocumentId: null,
            FiscalDocumentNumber: null,
            FiscalSequenceValue: null,
            FiscalDocumentStatus: null,
            VoidStatus: null,
            VoidedAt: null,
            VoidReasonCode: null,
            VoidReasonText: null,
            RequestedByRef: null,
            IdempotencyKey: null,
            ResultClassification: null,
            CorrelationId: null,
            ErrorPosture: "retry_after_configuration_correction");
}
