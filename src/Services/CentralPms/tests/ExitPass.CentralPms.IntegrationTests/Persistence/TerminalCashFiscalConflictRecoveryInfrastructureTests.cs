using ExitPass.CentralPms.Application.Payments;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using ExitPass.CentralPms.Infrastructure.Common;
using ExitPass.CentralPms.Infrastructure.Payments;
using ExitPass.CentralPms.Infrastructure.TerminalCashPayments;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
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
        await EnsureTerminalCashPatchAppliedAsync(ConnectionString);
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
    public async Task GuardReader_ReturnsExactTerminalCashTenderAndCustodyEvidence()
    {
        var context = PaymentTestContext.Create(nameof(GuardReader_ReturnsExactTerminalCashTenderAndCustodyEvidence));
        await EnsureTerminalCashPatchAppliedAsync(ConnectionString);
        await PaymentTestDataHelper.ResetAndSeedAsync(ConnectionString, context, "Seed business-date recovery guard test.");
        var tenderId = Guid.NewGuid();
        var custodySessionId = Guid.NewGuid();

        try
        {
            var service = new TerminalCashPaymentService(
                new TerminalCashPaymentRepository(ConnectionString),
                new SystemClock());
            var payment = await service.CreateOrReadAsync(
                new TerminalCashPaymentCommand(
                    tenderId,
                    custodySessionId,
                    context.ParkingSessionId,
                    context.TariffSnapshotId,
                    context.RequestedByUserId.ToString("D"),
                    $"cashier-session-{Guid.NewGuid():N}",
                    "shift-business-date-recovery",
                    "apt-pitx-level-3-01",
                    context.SiteId,
                    context.SiteGroupId,
                    "3a138565-1b88-55f8-c83d-5380db6edccc",
                    "PHP",
                    10_000,
                    10_000,
                    0,
                    DateTimeOffset.UtcNow,
                    [new TerminalCashDenominationEntry("PHP100", 10_000, 1)],
                    $"cash-received-{Guid.NewGuid():N}",
                    $"terminal-cash-payment-{tenderId:N}",
                    context.CorrelationId),
                CancellationToken.None);

            var repository = new PostgresTerminalCashFiscalConflictRecoveryGuardRepository(ConnectionString);
            var facts = await repository.ReadAsync(
                payment.PaymentAttemptId,
                payment.PaymentConfirmationId,
                CancellationToken.None);

            facts.Should().NotBeNull();
            facts!.TerminalCashCommandCount.Should().Be(1);
            facts.TerminalCashTenderId.Should().Be(tenderId);
            facts.CashCustodySessionId.Should().Be(custodySessionId);
            facts.PaymentAttemptStatus.Should().Be("CONFIRMED");
            facts.PaymentConfirmationStatus.Should().Be("RECORDED");
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

    private static async Task EnsureTerminalCashPatchAppliedAsync(string connectionString)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string? patchPath = null;
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "infra",
                "db",
                "patches",
                "ExitPass_TerminalCashPaymentCommandReadback_v1.3.sql");
            if (File.Exists(candidate))
            {
                patchPath = candidate;
                break;
            }

            directory = directory.Parent;
        }

        if (patchPath is null)
        {
            throw new FileNotFoundException("Could not locate the terminal cash persistence patch.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(patchPath), connection)
        {
            CommandTimeout = 30
        };
        await command.ExecuteNonQueryAsync();
    }
}
