using ExitPass.CentralPms.Application.Eventing;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Domain.Common;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

[CollectionDefinition(nameof(ReportVerifiedPaymentOutcomeFiscalRecoveryCollection), DisableParallelization = true)]
public sealed class ReportVerifiedPaymentOutcomeFiscalRecoveryCollection;

[Collection(nameof(ReportVerifiedPaymentOutcomeFiscalRecoveryCollection))]
public sealed class ReportVerifiedPaymentOutcomeFiscalRecoveryTests
{
    private static readonly Guid AttemptId = Guid.Parse("76000000-0000-4000-8000-000000000001");
    private static readonly Guid SessionId = Guid.Parse("76000000-0000-4000-8000-000000000002");
    private static readonly Guid ConfirmationId = Guid.Parse("76000000-0000-4000-8000-000000000003");
    private static readonly Guid FiscalReferenceId = Guid.Parse("76000000-0000-4000-8000-000000000004");
    private static readonly Guid CorrelationId = Guid.Parse("76000000-0000-4000-8000-000000000005");
    private static readonly Guid ActorId = Guid.Parse("76000000-0000-4000-8000-000000000006");
    private static readonly DateTimeOffset VerifiedAt = DateTimeOffset.Parse("2026-08-24T08:00:00Z");

    [Theory]
    [InlineData("pos_server_unavailable")]
    [InlineData("pos_server_timeout")]
    public async Task InitialConfirmedOutcome_WhenFiscalServiceIsRetryable_ReturnsTypedUnavailableWithoutAuthorization(
        string safeErrorCode)
    {
        var fixture = CreateFixture();
        fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new DigitalPaymentFiscalIssuanceResult(
                FiscalReferenceId,
                false,
                true,
                safeErrorCode,
                Guid.NewGuid(),
                "IST-POS-A",
                RetryableAfterServiceRecovery: true));

        var exception = await Assert.ThrowsAsync<RetryableFiscalIssuanceUnavailableException>(() =>
            fixture.Sut.ExecuteAsync(Command(), CancellationToken.None));

        Assert.Equal(FiscalReferenceId, exception.FiscalIssuanceReferenceId);
        Assert.Equal(RetryableFiscalIssuanceUnavailableException.SafeMessage, exception.Message);
        await fixture.Confirmation.Received(1).RecordAsync(
            Arg.Any<RecordPaymentConfirmationCommand>(),
            VerifiedAt,
            Arg.Any<CancellationToken>());
        await fixture.Finalization.Received(1).ExecuteAsync(
            Arg.Any<FinalizePaymentAttemptCommand>(),
            Arg.Any<CancellationToken>());
        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task RetryableFinalPaymentReplay_ResumesOriginalFiscalContextWithoutReconfirmingPayment()
    {
        var fixture = CreateFixture(RecoveryContext(retryable: true));
        fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new DigitalPaymentFiscalIssuanceResult(
                FiscalReferenceId,
                true,
                true,
                null,
                Guid.NewGuid(),
                "IST-POS-A"));

        var result = await fixture.Sut.ExecuteAsync(Command(), CancellationToken.None);

        Assert.Equal(ConfirmationId, result.PaymentConfirmationId);
        Assert.NotNull(result.ExitAuthorizationId);
        await fixture.Confirmation.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
        await fixture.Finalization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        Received.InOrder(() =>
        {
            fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Is<DigitalPaymentFiscalIssuanceCommand>(value =>
                    value.PaymentAttemptId == AttemptId &&
                    value.PaymentConfirmationId == ConfirmationId &&
                    value.ParkingSessionId == SessionId),
                Arg.Any<CancellationToken>());
            fixture.ExitAuthorization.ExecuteAsync(
                Arg.Any<IssueExitAuthorizationCommand>(),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RetryableFinalPaymentReplay_WithCanonicalSucceededStatus_ResumesOriginalFiscalContext()
    {
        var fixture = CreateFixture(RecoveryContext(retryable: true));
        fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new DigitalPaymentFiscalIssuanceResult(
                FiscalReferenceId,
                true,
                true,
                null,
                Guid.NewGuid(),
                "IST-POS-A"));

        var result = await fixture.Sut.ExecuteAsync(
            Command(providerStatus: "SUCCEEDED"),
            CancellationToken.None);

        Assert.Equal(ConfirmationId, result.PaymentConfirmationId);
        Assert.NotNull(result.ExitAuthorizationId);
        await fixture.Confirmation.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
        await fixture.Finalization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await fixture.FiscalIssuance.Received(1).IssueOrReadAsync(
            Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletedFiscalReplay_ReturnsExistingAuthoritativePathWithoutPaymentMutation()
    {
        var fixture = CreateFixture(RecoveryContext(completed: true));
        fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new DigitalPaymentFiscalIssuanceResult(
                FiscalReferenceId,
                true,
                false,
                null,
                Guid.NewGuid(),
                "IST-POS-A"));

        await fixture.Sut.ExecuteAsync(Command(), CancellationToken.None);

        await fixture.Confirmation.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
        await fixture.Finalization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
        await fixture.ExitAuthorization.Received(1).ExecuteAsync(
            Arg.Any<IssueExitAuthorizationCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinalPaymentWithoutRetryableFiscalContext_RemainsNonRetryableConflict()
    {
        var fixture = CreateFixture(RecoveryContext());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Sut.ExecuteAsync(Command(), CancellationToken.None));

        Assert.IsNotType<RetryableFiscalIssuanceUnavailableException>(exception);
        await fixture.Confirmation.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
        await fixture.FiscalIssuance.DidNotReceiveWithAnyArgs().IssueOrReadAsync(default!, default);
        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task RetryableReplayWithIdentityDrift_FailsBeforeFiscalOrPosProcessing()
    {
        var fixture = CreateFixture(RecoveryContext(retryable: true) with
        {
            ProviderReference = "DIFFERENT-PROVIDER-REFERENCE"
        });

        await Assert.ThrowsAsync<PaymentFinalityConflictException>(() =>
            fixture.Sut.ExecuteAsync(Command(), CancellationToken.None));

        await fixture.Confirmation.DidNotReceiveWithAnyArgs().RecordAsync(default!, default, default);
        await fixture.FiscalIssuance.DidNotReceiveWithAnyArgs().IssueOrReadAsync(default!, default);
        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    [Fact]
    public async Task CallerCancellationFromFiscalProcessing_IsNotConvertedToServiceUnavailable()
    {
        var fixture = CreateFixture(RecoveryContext(retryable: true));
        fixture.FiscalIssuance.IssueOrReadAsync(
                Arg.Any<DigitalPaymentFiscalIssuanceCommand>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<DigitalPaymentFiscalIssuanceResult>>(_ =>
                throw new OperationCanceledException("caller_cancelled"));

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Sut.ExecuteAsync(Command(), CancellationToken.None));

        Assert.IsNotType<RetryableFiscalIssuanceUnavailableException>(exception);
        await fixture.ExitAuthorization.DidNotReceiveWithAnyArgs().ExecuteAsync(default!, default);
    }

    private static Fixture CreateFixture(DigitalPaymentFiscalRecoveryContext? recovery = null)
    {
        var confirmation = Substitute.For<IRecordPaymentConfirmationGateway>();
        var finalization = Substitute.For<IFinalizePaymentAttemptUseCase>();
        var exitAuthorization = Substitute.For<IIssueExitAuthorizationUseCase>();
        var events = Substitute.For<IIntegrationEventPublisher>();
        var clock = Substitute.For<ISystemClock>();
        var fiscalIssuance = Substitute.For<IDigitalPaymentFiscalIssuanceService>();
        var recoveryReader = Substitute.For<IDigitalPaymentFiscalRecoveryContextReader>();
        clock.UtcNow.Returns(VerifiedAt);
        recoveryReader.FindByPaymentAttemptIdAsync(AttemptId, Arg.Any<CancellationToken>())
            .Returns(recovery);
        confirmation.RecordAsync(Arg.Any<RecordPaymentConfirmationCommand>(), VerifiedAt, Arg.Any<CancellationToken>())
            .Returns(new RecordPaymentConfirmationResult(
                ConfirmationId,
                AttemptId,
                "IST-PROVIDER-OUTAGE",
                "SUCCESS",
                "RECORDED",
                VerifiedAt));
        finalization.ExecuteAsync(Arg.Any<FinalizePaymentAttemptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FinalizePaymentAttemptResult(AttemptId, "CONFIRMED"));
        exitAuthorization.ExecuteAsync(Arg.Any<IssueExitAuthorizationCommand>(), Arg.Any<CancellationToken>())
            .Returns(new IssueExitAuthorizationResult(
                Guid.NewGuid(),
                SessionId,
                AttemptId,
                "IST-AUTH",
                "ISSUED",
                VerifiedAt.AddSeconds(2),
                VerifiedAt.AddMinutes(15)));

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
            },
            digitalPaymentFiscalRecoveryContextReader: recoveryReader);

        return new Fixture(sut, confirmation, finalization, exitAuthorization, fiscalIssuance);
    }

    private static DigitalPaymentFiscalRecoveryContext RecoveryContext(
        bool retryable = false,
        bool completed = false) =>
        new(
            AttemptId,
            SessionId,
            "CONFIRMED",
            ConfirmationId,
            "IST-PROVIDER-OUTAGE",
            "RECORDED",
            VerifiedAt,
            FiscalReferenceId,
            retryable
                ? "FISCAL_ISSUANCE_FAILED_SERVICE"
                : completed
                    ? "FISCAL_ISSUANCE_RECORDED"
                    : "FISCAL_ISSUANCE_FAILED_REQUEST",
            retryable ? "RETRY_AFTER_SERVICE_RECOVERY" : null,
            completed);

    private static ReportVerifiedPaymentOutcomeCommand Command(string providerStatus = "SUCCESS") =>
        new(
            AttemptId,
            SessionId,
            "IST-PROVIDER-OUTAGE",
            providerStatus,
            "CONFIRMED",
            "payment-orchestrator",
            ActorId,
            CorrelationId);

    private sealed record Fixture(
        ReportVerifiedPaymentOutcomeHandler Sut,
        IRecordPaymentConfirmationGateway Confirmation,
        IFinalizePaymentAttemptUseCase Finalization,
        IIssueExitAuthorizationUseCase ExitAuthorization,
        IDigitalPaymentFiscalIssuanceService FiscalIssuance);
}
