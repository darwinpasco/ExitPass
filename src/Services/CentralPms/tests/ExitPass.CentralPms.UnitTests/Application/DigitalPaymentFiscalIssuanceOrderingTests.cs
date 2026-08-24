using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

public sealed class DigitalPaymentFiscalIssuanceOrderingTests
{
    private static readonly Guid AttemptId = Guid.Parse("74000000-0000-4000-8000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("74000000-0000-4000-8000-000000000002");
    private static readonly Guid ConfirmationId = Guid.Parse("74000000-0000-4000-8000-000000000003");
    private static readonly Guid CorrelationId = Guid.Parse("74000000-0000-4000-8000-000000000004");
    private static readonly Guid ActorId = Guid.Parse("74000000-0000-4000-8000-000000000005");

    [Fact]
    public async Task ConfirmedDigitalPayment_FiscalizesBeforeExitAuthorization()
    {
        var fixture = CreateFixture(readyForExit: true);

        await fixture.Sut.ExecuteAsync(Command(), CancellationToken.None);

        Received.InOrder(() =>
        {
            fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Is<DigitalPaymentFiscalIssuanceCommand>(command =>
                    command.PaymentAttemptId == AttemptId &&
                    command.PaymentConfirmationId == ConfirmationId &&
                    command.ParkingSessionId == SessionId),
                Arg.Any<CancellationToken>());
            fixture.ExitAuthorization.ExecuteAsync(
                Arg.Any<IssueExitAuthorizationCommand>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ConfirmedDigitalPayment_WhenFiscalIssuanceIsNotReady_DoesNotIssueExitAuthorization()
    {
        var fixture = CreateFixture(readyForExit: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.ExecuteAsync(Command(), CancellationToken.None));

        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs()
            .ExecuteAsync(default!, default);
    }

    private static Fixture CreateFixture(bool readyForExit)
    {
        var confirmation = Substitute.For<IRecordPaymentConfirmationGateway>();
        var finalization = Substitute.For<IFinalizePaymentAttemptUseCase>();
        var exitAuthorization = Substitute.For<IIssueExitAuthorizationUseCase>();
        var events = Substitute.For<IIntegrationEventPublisher>();
        var clock = Substitute.For<ISystemClock>();
        var fiscalIssuance = Substitute.For<IDigitalPaymentFiscalIssuanceService>();
        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        clock.UtcNow.Returns(now);
        confirmation.RecordAsync(Arg.Any<RecordPaymentConfirmationCommand>(), now, Arg.Any<CancellationToken>())
            .Returns(new RecordPaymentConfirmationResult(
                ConfirmationId, AttemptId, "IST-PROVIDER-001", "SUCCEEDED", "RECORDED", now));
        finalization.ExecuteAsync(Arg.Any<FinalizePaymentAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FinalizePaymentAttemptResult(AttemptId, "CONFIRMED"));
        fiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new DigitalPaymentFiscalIssuanceResult(
                Guid.NewGuid(), readyForExit, true, readyForExit ? null : "fiscal_issuance_failed", Guid.NewGuid(), "IST-POS-A"));
        exitAuthorization.ExecuteAsync(Arg.Any<IssueExitAuthorizationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationResult(
                Guid.NewGuid(), SessionId, AttemptId, "IST-AUTH", "ISSUED", now, now.AddMinutes(15)));

        var sut = new ReportVerifiedPaymentOutcomeHandler(
            confirmation,
            finalization,
            exitAuthorization,
            events,
            clock,
            NullLogger<ReportVerifiedPaymentOutcomeHandler>.Instance,
            digitalPaymentFiscalIssuanceService: fiscalIssuance,
            posServerOptions: new FiscalIssuancePosServerIntegrationOptions
            {
                EnableLiveFiscalIssuanceFromPaymentFlow = true
            });
        return new Fixture(sut, fiscalIssuance, exitAuthorization);
    }

    private static ReportVerifiedPaymentOutcomeCommand Command() =>
        new(AttemptId, SessionId, "IST-PROVIDER-001", "SUCCEEDED", "CONFIRMED", "payment-orchestrator", ActorId, CorrelationId);

    private sealed record Fixture(
        ReportVerifiedPaymentOutcomeHandler Sut,
        IDigitalPaymentFiscalIssuanceService FiscalIssuance,
        IIssueExitAuthorizationUseCase ExitAuthorization);
}
