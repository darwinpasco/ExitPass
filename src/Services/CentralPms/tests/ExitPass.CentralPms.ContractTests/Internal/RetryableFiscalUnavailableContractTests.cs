using System.Net;
using System.Net.Http.Json;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Contracts.Common;
using ExitPass.CentralPms.Contracts.Internal;
using ExitPass.CentralPms.IntegrationTests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ExitPass.CentralPms.ContractTests.Internal;

public sealed class RetryableFiscalUnavailableContractTests
{
    [Fact]
    public async Task ReportVerifiedPaymentOutcome_WhenFiscalServiceIsUnavailable_ReturnsRetryable503()
    {
        using var factory = new CustomWebApplicationFactory()
            .WithServiceOverrides(services =>
            {
                services.RemoveAll<IReportVerifiedPaymentOutcomeUseCase>();
                services.AddSingleton<IReportVerifiedPaymentOutcomeUseCase>(
                    new UnavailableOutcomeUseCase());
            });
        using var client = factory.CreateClient();
        var correlationId = Guid.NewGuid();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/internal/payments/outcome")
        {
            Content = JsonContent.Create(new ReportVerifiedPaymentOutcomeRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "IST-PROVIDER-OUTAGE",
                "SUCCESS",
                "CONFIRMED",
                "payment-orchestrator",
                Guid.NewGuid()))
        };
        request.Headers.Add("X-Correlation-Id", correlationId.ToString());
        request.Headers.Add("Idempotency-Key", $"fiscal-outage-{Guid.NewGuid():N}");

        using var response = await client.SendAsync(request);
        var payload = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        payload.Should().NotBeNull();
        payload!.ErrorCode.Should().Be("FISCAL_ISSUANCE_TEMPORARILY_UNAVAILABLE");
        payload.Retryable.Should().BeTrue();
        payload.CorrelationId.Should().Be(correlationId);
        payload.Message.Should().NotContainEquivalentOf("POS");
        payload.Message.Should().NotContainEquivalentOf("endpoint");
        payload.Message.Should().NotContainEquivalentOf("credential");
        payload.Message.Should().NotContainEquivalentOf("exception");
    }

    private sealed class UnavailableOutcomeUseCase : IReportVerifiedPaymentOutcomeUseCase
    {
        public Task<ReportVerifiedPaymentOutcomeResult> ExecuteAsync(
            ReportVerifiedPaymentOutcomeCommand command,
            CancellationToken cancellationToken) =>
            throw new RetryableFiscalIssuanceUnavailableException(Guid.NewGuid());
    }
}
