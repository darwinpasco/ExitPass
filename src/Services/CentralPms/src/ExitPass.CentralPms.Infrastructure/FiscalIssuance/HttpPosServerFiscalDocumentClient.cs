using System.Net.Http.Json;
using ExitPass.CentralPms.Application.FiscalIssuance;

namespace ExitPass.CentralPms.Infrastructure.FiscalIssuance;

public sealed class HttpPosServerFiscalDocumentClient : IPosServerFiscalDocumentClient
{
    private const string FiscalDocumentsPath = "/v1/fiscal-documents/";

    private readonly HttpClient _httpClient;

    public HttpPosServerFiscalDocumentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PosServerFiscalDocumentCreateResult> CreateFiscalDocumentAsync(
        PosServerFiscalDocumentCreateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            FiscalDocumentsPath,
            request,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseCreateResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentReadResult> GetFiscalDocumentAsync(
        Guid fiscalDocumentId,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        using var response = await _httpClient.GetAsync(
            $"{FiscalDocumentsPath}{fiscalDocumentId:D}",
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseReadResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentPresentationReadResult> GetFiscalDocumentPresentationAsync(
        Guid fiscalDocumentId,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{FiscalDocumentsPath}{fiscalDocumentId:D}/digital-sales-invoice/presentation");
        request.Headers.Accept.ParseAdd("application/json");
        if (correlationId is not null && correlationId != Guid.Empty)
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString("D"));
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParsePresentationResponse((int)response.StatusCode, body);
    }

    public async Task<PosServerFiscalDocumentVoidResult> VoidFiscalDocumentAsync(
        Guid fiscalDocumentId,
        PosServerFiscalDocumentVoidRequest request,
        CancellationToken cancellationToken)
    {
        if (fiscalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Fiscal document id is required.", nameof(fiscalDocumentId));
        }

        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient.PostAsJsonAsync(
            $"{FiscalDocumentsPath}{fiscalDocumentId:D}/void",
            request,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return PosServerFiscalDocumentResponseParser.ParseVoidResponse((int)response.StatusCode, body);
    }
}
