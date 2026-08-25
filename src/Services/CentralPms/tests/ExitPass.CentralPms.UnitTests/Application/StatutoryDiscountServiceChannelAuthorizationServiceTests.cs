using ExitPass.CentralPms.Application.Security;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class StatutoryDiscountServiceChannelAuthorizationServiceTests
{
    private static readonly Guid ServiceIdentityId = Guid.Parse("7d000000-0000-0000-0000-000000000001");
    private static readonly Guid SiteId = Guid.Parse("7d000000-0000-0000-0000-000000000002");
    private static readonly Guid CorrelationId = Guid.Parse("7d000000-0000-0000-0000-000000000003");

    [Theory]
    [InlineData("WEBPAY", "PAYMENT_ORCHESTRATOR", "statutory-discounts.decision.submit.webpay")]
    [InlineData("ASSISTED_PAYMENT_TERMINAL", "ASSISTED_PAYMENT_TERMINAL", "statutory-discounts.decision.submit.assisted-payment-terminal")]
    public async Task AuthorizeAsync_ActiveCompatibleSiteScopedService_IsAllowed(
        string sourceChannel,
        string owningServiceName,
        string permission)
    {
        var repository = Repository(new CentralPmsServiceIdentityAuthorizationRecord(
            ServiceIdentityId,
            "INTERNAL_SERVICE",
            owningServiceName,
            Active: true,
            SiteAssigned: true));
        var sut = new StatutoryDiscountServiceChannelAuthorizationService(repository);

        var result = await sut.AuthorizeAsync(
            new StatutoryDiscountServiceChannelCallerContext(
                ServiceIdentityId,
                sourceChannel,
                sourceChannel,
                permission),
            SiteId,
            CorrelationId,
            CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        result.Decision.Should().Be("SERVICE_CHANNEL_ALLOW");
    }

    [Fact]
    public async Task AuthorizeAsync_WrongApplicationAudience_IsDeniedBeforeRepositoryRead()
    {
        var repository = Repository(null);
        var sut = new StatutoryDiscountServiceChannelAuthorizationService(repository);

        var result = await sut.AuthorizeAsync(
            new StatutoryDiscountServiceChannelCallerContext(
                ServiceIdentityId,
                StatutoryDiscountSourceChannels.WebPay,
                StatutoryDiscountSourceChannels.AssistedPaymentTerminal,
                "statutory-discounts.decision.submit.webpay"),
            SiteId,
            CorrelationId,
            CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
        await repository.DidNotReceive().GetServiceIdentityAuthorizationAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AuthorizeAsync_MissingChannelPermission_IsDenied()
    {
        var repository = Repository(null);
        var sut = new StatutoryDiscountServiceChannelAuthorizationService(repository);

        var result = await sut.AuthorizeAsync(
            new StatutoryDiscountServiceChannelCallerContext(
                ServiceIdentityId,
                StatutoryDiscountSourceChannels.WebPay,
                StatutoryDiscountSourceChannels.WebPay,
                "statutory-discounts.decision.read"),
            SiteId,
            CorrelationId,
            CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be("ACCESS_DENIED");
    }

    [Theory]
    [InlineData(false, true, "PAYMENT_ORCHESTRATOR", "ACCESS_DENIED")]
    [InlineData(true, false, "PAYMENT_ORCHESTRATOR", "STATUTORY_DISCOUNT_DECISION_NOT_FOUND")]
    [InlineData(true, true, "ASSISTED_PAYMENT_TERMINAL", "ACCESS_DENIED")]
    public async Task AuthorizeAsync_InvalidCanonicalIdentityFacts_FailClosed(
        bool active,
        bool siteAssigned,
        string owningServiceName,
        string expectedErrorCode)
    {
        var repository = Repository(new CentralPmsServiceIdentityAuthorizationRecord(
            ServiceIdentityId,
            "INTERNAL_SERVICE",
            owningServiceName,
            active,
            siteAssigned));
        var sut = new StatutoryDiscountServiceChannelAuthorizationService(repository);

        var result = await sut.AuthorizeAsync(
            new StatutoryDiscountServiceChannelCallerContext(
                ServiceIdentityId,
                StatutoryDiscountSourceChannels.WebPay,
                StatutoryDiscountSourceChannels.WebPay,
                "statutory-discounts.decision.submit.webpay"),
            SiteId,
            CorrelationId,
            CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.ErrorCode.Should().Be(expectedErrorCode);
    }

    private static ICentralPmsRbacRepository Repository(CentralPmsServiceIdentityAuthorizationRecord? record)
    {
        var repository = Substitute.For<ICentralPmsRbacRepository>();
        repository.GetServiceIdentityAuthorizationAsync(ServiceIdentityId, SiteId, Arg.Any<CancellationToken>())
            .Returns(record);
        repository.RecordAuditEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return repository;
    }
}
