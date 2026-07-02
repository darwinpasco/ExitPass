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
}
