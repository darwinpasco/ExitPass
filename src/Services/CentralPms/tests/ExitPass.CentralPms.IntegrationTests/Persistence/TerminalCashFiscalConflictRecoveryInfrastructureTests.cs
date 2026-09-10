using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Xunit;
using static ExitPass.CentralPms.IntegrationTests.Shared.PaymentRoutineTestHelper;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class TerminalCashFiscalConflictRecoveryInfrastructureTests
{
    private static string ConnectionString =>
        CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();

    [Fact]
    public async Task GuardReader_ReturnsCanonicalConfirmedPaymentFactsAndNoExitAuthorization()
    {
        var context = PaymentTestContext.Create(nameof(GuardReader_ReturnsCanonicalConfirmedPaymentFactsAndNoExitAuthorization));
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed fiscal conflict recovery guard test.");

        try
        {
            var attempt = await CreateAttemptAsync(
                ConnectionString,
                context,
                $"fiscal-recovery-{Guid.NewGuid():N}",
                "fiscal-recovery-integration-test");
            var finalized = await FinalizeAttemptAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                "CONFIRMED",
                "fiscal-recovery-integration-test",
                context.CorrelationId);
            var confirmation = await RecordPaymentConfirmationAsync(
                ConnectionString,
                attempt.PaymentAttemptId,
                $"FISCAL-RECOVERY-{Guid.NewGuid():N}",
                "fiscal-recovery-integration-test",
                context.CorrelationId);

            finalized.Should().NotBeNull();
            confirmation.Should().NotBeNull();

            var repository = new PostgresTerminalCashFiscalConflictRecoveryGuardRepository(ConnectionString);
            var facts = await repository.ReadAsync(
                attempt.PaymentAttemptId,
                confirmation!.PaymentConfirmationId,
                CancellationToken.None);

            facts.Should().NotBeNull();
            facts!.PaymentAttemptStatus.Should().Be("CONFIRMED");
            facts.PaymentConfirmationStatus.Should().Be("RECORDED");
            facts.ParkingSessionId.Should().Be(context.ParkingSessionId);
            facts.TariffSnapshotId.Should().Be(context.TariffSnapshotId);
            facts.PaymentAttemptAmountMinorUnits.Should().Be(10_000);
            facts.PaymentConfirmationAmountMinorUnits.Should().Be(10_000);
            facts.PaymentAttemptCurrency.Should().Be("PHP");
            facts.PaymentConfirmationCurrency.Should().Be("PHP");
            facts.ExitAuthorizationCount.Should().Be(0);
        }
        finally
        {
            await PaymentTestDataHelper.CleanupAsync(ConnectionString, context);
        }
    }

    [Fact]
    public async Task AdvisoryLock_AllowsOneConcurrentRecoveryLeaseAndReleasesCleanly()
    {
        var referenceId = Guid.NewGuid();
        var first = new PostgresTerminalCashFiscalConflictRecoveryLock(ConnectionString);
        var second = new PostgresTerminalCashFiscalConflictRecoveryLock(ConnectionString);

        await using var firstLease = await first.TryAcquireAsync(referenceId, CancellationToken.None);
        firstLease.Should().NotBeNull();

        var competingLease = await second.TryAcquireAsync(referenceId, CancellationToken.None);
        competingLease.Should().BeNull();

        await firstLease!.DisposeAsync();
        await using var releasedLease = await second.TryAcquireAsync(referenceId, CancellationToken.None);
        releasedLease.Should().NotBeNull();
    }
}
