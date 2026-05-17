using System.Net;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Infrastructure.Integrations;

/// <summary>
/// Unit tests for <see cref="CentralPmsWebPayClient"/>.
/// </summary>
public sealed class CentralPmsWebPayClientTests
{
    private static readonly Guid ParkingSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid TariffSnapshotId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CorrelationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// Verifies Central PMS payment attempt creation receives both provider rail and payment method.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_SendsProviderRailPaymentMethodAndRequiredHeaders()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent(new
            {
                paymentAttemptId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                attemptStatus = "PENDING_PROVIDER",
                paymentProvider = "AUB_QRPH",
                wasReused = false
            })
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB_QRPH",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("/v1/public/payment-attempts", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(CorrelationId.ToString(), handler.LastRequest.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("webpay:test", handler.LastRequest.Headers.GetValues("Idempotency-Key").Single());

        using var document = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("AUB_QRPH", document.RootElement.GetProperty("paymentProvider").GetString());
        Assert.Equal("QRPH", document.RootElement.GetProperty("paymentMethod").GetString());
    }

    /// <summary>
    /// Verifies Central PMS JSON problem responses are preserved as deterministic errors.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_WhenCentralPmsReturnsProblemJson_PreservesErrorBody()
    {
        const string responseBody = "{\"title\":\"Unsupported payment provider\",\"detail\":\"Unsupported payment provider: AUB\"}";
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(400, result.Error!.StatusCode);
        Assert.Equal("PAYMENT_ATTEMPT_CREATE_FAILED", result.Error.ErrorCode);
        Assert.Equal("Unsupported payment provider: AUB", result.Error.Message);
        Assert.False(result.Error.Retryable);
    }

    /// <summary>
    /// Verifies Central PMS active-attempt conflict correlation is preserved.
    /// </summary>
    [Fact]
    public async Task CreateOrReusePaymentAttemptAsync_WhenActivePaymentAttemptConflict_PreservesCorrelationId()
    {
        var handler = new CapturingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent(new
            {
                errorCode = "ACTIVE_PAYMENT_ATTEMPT_EXISTS",
                message = "An active payment attempt already exists for parking session.",
                correlationId = CorrelationId,
                retryable = false
            })
        });
        var client = CreateClient(handler);

        var result = await client.CreateOrReusePaymentAttemptAsync(
            ParkingSessionId,
            TariffSnapshotId,
            "AUB_QRPH",
            "QRPH",
            "webpay:test",
            CorrelationId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(409, result.Error!.StatusCode);
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", result.Error.ErrorCode);
        Assert.Equal(CorrelationId, result.Error.CorrelationId);
    }

    private static CentralPmsWebPayClient CreateClient(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:CentralPms:BaseUrl"] = "http://central-pms.test"
            })
            .Build();

        return new CentralPmsWebPayClient(
            new HttpClient(handler),
            configuration,
            NullLogger<CentralPmsWebPayClient>.Instance);
    }

    private static StringContent JsonContent(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public CapturingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastRequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_response);
        }
    }
}
