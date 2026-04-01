using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Public;

public sealed class PaymentAttemptsContractTests
{
    [Fact(Skip = "Wire to Central PMS test host or WebApplicationFactory.")]
    public async Task CreatePaymentAttempt_returns_201_created_for_first_request()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
        {
            Content = JsonContent.Create(new
            {
                parkingSessionId = "11111111-1111-1111-1111-111111111111",
                tariffSnapshotId = "22222222-2222-2222-2222-222222222222",
                paymentProvider = "GCASH"
            })
        };

        request.Headers.Add("Idempotency-Key", "ctest-idem-001");
        request.Headers.Add("X-Correlation-Id", "aaaaaaaa-1111-1111-1111-111111111111");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact(Skip = "Wire to Central PMS test host or WebApplicationFactory.")]
    public async Task CreatePaymentAttempt_returns_200_ok_and_reused_true_for_same_idempotency_key()
    {
        using var client = CreateClient();

        async Task<HttpResponseMessage> SendAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
            {
                Content = JsonContent.Create(new
                {
                    parkingSessionId = "11111111-1111-1111-1111-111111111111",
                    tariffSnapshotId = "22222222-2222-2222-2222-222222222222",
                    paymentProvider = "GCASH"
                })
            };

            request.Headers.Add("Idempotency-Key", "ctest-idem-002");
            request.Headers.Add("X-Correlation-Id", "bbbbbbbb-1111-1111-1111-111111111111");

            return await client.SendAsync(request);
        }

        var first = await SendAsync();
        var second = await SendAsync();

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await second.Content.ReadFromJsonAsync<ReplayResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.WasReused);
    }

    [Fact(Skip = "Wire to Central PMS test host or WebApplicationFactory.")]
    public async Task CreatePaymentAttempt_returns_409_conflict_for_competing_active_attempt()
    {
        using var client = CreateClient();

        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
        {
            Content = JsonContent.Create(new
            {
                parkingSessionId = "11111111-1111-1111-1111-111111111111",
                tariffSnapshotId = "22222222-2222-2222-2222-222222222222",
                paymentProvider = "GCASH"
            })
        };

        firstRequest.Headers.Add("Idempotency-Key", "ctest-idem-003a");
        firstRequest.Headers.Add("X-Correlation-Id", "cccccccc-1111-1111-1111-111111111111");

        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/public/payment-attempts")
        {
            Content = JsonContent.Create(new
            {
                parkingSessionId = "11111111-1111-1111-1111-111111111111",
                tariffSnapshotId = "22222222-2222-2222-2222-222222222222",
                paymentProvider = "GCASH"
            })
        };

        secondRequest.Headers.Add("Idempotency-Key", "ctest-idem-003b");
        secondRequest.Headers.Add("X-Correlation-Id", "dddddddd-1111-1111-1111-111111111111");

        var first = await client.SendAsync(firstRequest);
        var second = await client.SendAsync(secondRequest);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var payload = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(payload);
        Assert.Equal("ACTIVE_PAYMENT_ATTEMPT_EXISTS", payload!.ErrorCode);
    }

    private static HttpClient CreateClient()
    {
        var baseUrl = Environment.GetEnvironmentVariable("EXITPASS_TEST_API_BASE_URL")
            ?? throw new InvalidOperationException("EXITPASS_TEST_API_BASE_URL environment variable is missing.");

        return new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };
    }

    private sealed class ReplayResponse
    {
        public bool WasReused { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string ErrorCode { get; set; } = string.Empty;
    }
}
