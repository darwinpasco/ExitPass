using System.Net;
using System.Text;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class SitePosServerFiscalDocumentRoutingTests : IDisposable
{
    private static readonly Guid SiteAPosId = Guid.Parse("30000000-0000-0000-0000-00000000000a");
    private static readonly Guid SiteBPosId = Guid.Parse("30000000-0000-0000-0000-00000000000b");
    private readonly string _siteAKeyFile = Path.GetTempFileName();
    private readonly string _siteBKeyFile = Path.GetTempFileName();

    public SitePosServerFiscalDocumentRoutingTests()
    {
        File.WriteAllText(_siteAKeyFile, "site-a-api-key");
        File.WriteAllText(_siteBKeyFile, "site-b-api-key");
    }

    public void Dispose()
    {
        File.Delete(_siteAKeyFile);
        File.Delete(_siteBKeyFile);
    }

    [Fact]
    public async Task Create_RoutesEachSiteToItsOwnEndpointAndCredential()
    {
        var observed = new List<ObservedRequest>();
        var client = CreateClient(TwoSiteOptions(), observed);

        var resultA = await client.CreateFiscalDocumentAsync(
            CreateRequest(SiteAPosId, "SITE-A-POS"),
            CancellationToken.None);
        var resultB = await client.CreateFiscalDocumentAsync(
            CreateRequest(SiteBPosId, "SITE-B-POS"),
            CancellationToken.None);

        resultA.Succeeded.Should().BeTrue("routing result was {0}", resultA.Code);
        resultB.Succeeded.Should().BeTrue("routing result was {0}", resultB.Code);
        observed.Should().HaveCount(2);
        observed[0].Uri.Should().Be("http://site-a-pos:8080/v1/fiscal-documents/");
        observed[0].ApiKey.Should().Be("site-a-api-key");
        observed[0].Permission.Should().Be("fiscal_document.create");
        observed[1].Uri.Should().Be("http://site-b-pos:8080/v1/fiscal-documents/");
        observed[1].ApiKey.Should().Be("site-b-api-key");
        observed[1].Permission.Should().Be("fiscal_document.create");
    }

    [Fact]
    public async Task Presentation_RoutesReadToOriginalSitePosServer()
    {
        var observed = new List<ObservedRequest>();
        var client = CreateClient(TwoSiteOptions(), observed, PresentationResponse());
        var correlationId = Guid.Parse("d9f94e06-b3f4-4dcc-904a-9cb7dcab6f1e");

        var result = await client.GetFiscalDocumentPresentationAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            correlationId,
            new PosServerRoutingContext(SiteBPosId, "SITE-B-POS"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue("presentation result was {0}", result.Code);
        observed.Should().ContainSingle();
        observed[0].Uri.Should().StartWith("http://site-b-pos:8080/");
        observed[0].Permission.Should().Be("fiscal_document.read");
        observed[0].CorrelationId.Should().Be(correlationId.ToString("D"));
    }

    [Theory]
    [InlineData("unknown", "site_pos_server_endpoint_not_found")]
    [InlineData("mismatch", "site_pos_server_endpoint_identity_mismatch")]
    [InlineData("disabled", "site_pos_server_endpoint_disabled")]
    [InlineData("missing-secret", "site_pos_server_api_key_file_unavailable")]
    [InlineData("duplicate", "site_pos_server_endpoint_ambiguous")]
    [InlineData("production-http", "site_pos_server_endpoint_https_required")]
    [InlineData("credential-bearing-url", "site_pos_server_endpoint_url_invalid")]
    public async Task InvalidRoutingConfiguration_FailsBeforeNetworkCall(string scenario, string expectedCode)
    {
        var observed = new List<ObservedRequest>();
        var options = OptionsForScenario(scenario);
        var client = CreateClient(options, observed);
        var routing = scenario switch
        {
            "unknown" => new PosServerRoutingContext(Guid.NewGuid(), "UNKNOWN-POS"),
            "mismatch" => new PosServerRoutingContext(SiteAPosId, "SITE-B-POS"),
            _ => new PosServerRoutingContext(SiteAPosId, "SITE-A-POS")
        };

        var request = CreateRequest(routing.SitePosServerId, routing.SitePosServerRef);
        var result = await client.CreateFiscalDocumentAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Code.Should().Be(expectedCode);
        result.Message.ToLowerInvariant().Should().NotContain("api-key");
        observed.Should().BeEmpty();
    }

    [Fact]
    public void Readiness_RejectsGlobalEndpointAndDuplicateSiteBindings()
    {
#pragma warning disable CS0618
        var global = new FiscalIssuancePosServerIntegrationOptions
        {
            EnablePosServerFiscalIssuanceLiveCall = true,
            PosServerBaseUrl = "https://global-pos.invalid",
            RuntimeEnvironment = "Production"
        };
#pragma warning restore CS0618
        var duplicate = TwoSiteOptions();
        duplicate.EnablePosServerFiscalIssuanceLiveCall = true;
        duplicate.Endpoints.Add(duplicate.Endpoints[0]);

        global.EvaluateReadiness().Errors.Should().Contain("site_pos_server_endpoints_required");
        duplicate.EvaluateReadiness().Errors.Should().Contain("site_pos_server_endpoint_id_duplicate");
    }

    private FiscalIssuancePosServerIntegrationOptions OptionsForScenario(string scenario)
    {
        var options = TwoSiteOptions();
        switch (scenario)
        {
            case "disabled":
                options.Endpoints[0].Enabled = false;
                break;
            case "missing-secret":
                options.Endpoints[0].ApiKeyFile = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
                break;
            case "duplicate":
                options.Endpoints.Add(new SitePosServerEndpointOptions
                {
                    SitePosServerId = SiteAPosId,
                    SitePosServerRef = "SITE-A-POS",
                    BaseUrl = "http://site-a-pos-duplicate:8080",
                    ApiKeyFile = _siteAKeyFile,
                    Environment = "IntegrationTest",
                    Enabled = true
                });
                break;
            case "production-http":
                options.RuntimeEnvironment = "Production";
                options.Endpoints[0].Environment = "Production";
                break;
            case "credential-bearing-url":
                options.Endpoints[0].BaseUrl = "https://embedded:credential@site-a-pos:8443";
                break;
        }

        return options;
    }

    private FiscalIssuancePosServerIntegrationOptions TwoSiteOptions() =>
        new()
        {
            RuntimeEnvironment = "IntegrationTest",
            Endpoints =
            [
                new SitePosServerEndpointOptions
                {
                    SitePosServerId = SiteAPosId,
                    SitePosServerRef = "SITE-A-POS",
                    BaseUrl = "http://site-a-pos:8080",
                    ApiKeyFile = _siteAKeyFile,
                    Environment = "IntegrationTest",
                    Enabled = true
                },
                new SitePosServerEndpointOptions
                {
                    SitePosServerId = SiteBPosId,
                    SitePosServerRef = "SITE-B-POS",
                    BaseUrl = "http://site-b-pos:8080",
                    ApiKeyFile = _siteBKeyFile,
                    Environment = "IntegrationTest",
                    Enabled = true
                }
            ]
        };

    private static PosServerFiscalDocumentCreateRequest CreateRequest(Guid sitePosServerId, string sitePosServerRef) =>
        new PosServerFiscalDocumentRequestMapper()
            .Map(PosServerFiscalDocumentRequestMapperTests.ValidContext()) with
        {
            SitePosServerId = sitePosServerId,
            SitePosServerRef = sitePosServerRef
        };

    private static HttpPosServerFiscalDocumentClient CreateClient(
        FiscalIssuancePosServerIntegrationOptions options,
        List<ObservedRequest> observed,
        string? responseBody = null)
    {
        var handler = new CaptureHandler(request =>
        {
            observed.Add(new ObservedRequest(
                request.RequestUri!.AbsoluteUri,
                request.Headers.GetValues("X-PosServer-Admin-Key").Single(),
                request.Headers.GetValues("X-PosServer-Admin-Permission").Single(),
                request.Headers.TryGetValues("X-Correlation-Id", out var values) ? values.Single() : null));
            return new HttpResponseMessage(responseBody is null ? HttpStatusCode.Accepted : HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody ?? AcceptedResponse(), Encoding.UTF8, "application/json")
            };
        });
        return new HttpPosServerFiscalDocumentClient(new HttpClient(handler), Options.Create(options));
    }

    private static string AcceptedResponse() =>
        """
        {
          "succeeded": true,
          "code": "accepted",
          "message": "accepted",
          "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
          "resultClassification": "newly_created",
          "fiscalIssuanceEvidenceStatus": "fiscal_document_number_assigned",
          "fiscalNumberAssignmentState": "assigned",
          "fiscalIdentityId": "22222222-2222-2222-2222-222222222222",
          "fiscalDocumentStatusCodeId": "33333333-3333-3333-3333-333333333333",
          "fiscalSequencePolicyId": "44444444-4444-4444-4444-444444444444",
          "fiscalSequenceValue": 1,
          "fiscalDocumentNumber": "SI-000001",
          "fiscalSeries": "SI",
          "fiscalNumberPrefixText": "SI-",
          "fiscalNumberSuffixText": null,
          "fiscalNumberAssignedAt": "2026-07-02T02:30:00Z",
          "fiscalNumberAssignedByRef": "pos-server-runtime"
        }
        """;

    private static string PresentationResponse() =>
        """
        {
          "succeeded": true,
          "code": "fiscal_document_presentation_available",
          "message": "available",
          "fiscalDocumentId": "11111111-1111-1111-1111-111111111111",
          "fiscalDocumentNumber": "SI-000001",
          "fiscalDocumentStatus": "issued",
          "fiscalNumberAssignmentState": "assigned",
          "presentationVersion": "v1",
          "templateVersion": "v1",
          "contentType": "application/json",
          "authoritativeResponse": { "fiscalDocumentNumber": "SI-000001" }
        }
        """;

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed record ObservedRequest(
        string Uri,
        string ApiKey,
        string Permission,
        string? CorrelationId);
}
