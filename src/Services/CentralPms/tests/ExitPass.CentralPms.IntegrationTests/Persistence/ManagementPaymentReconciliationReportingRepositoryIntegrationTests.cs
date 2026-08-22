using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ManagementPaymentReconciliationReportingRepositoryIntegrationTests
{
    private static readonly DateTimeOffset PeriodStart = DateTimeOffset.Parse("2031-02-01T00:00:00Z");
    private static readonly DateTimeOffset PeriodEnd = DateTimeOffset.Parse("2031-02-02T00:00:00Z");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public ManagementPaymentReconciliationReportingRepositoryIntegrationTests(
        StatutoryDiscountCanonicalDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ReadSummary_UsesCanonicalRowsExactMoneyAndHalfOpenPeriod()
    {
        var context = PaymentTestContext.Create("management-payment-reporting");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            _database.ConnectionString,
            context,
            "Management payment reporting integration");
        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            _database.ConnectionString,
            context,
            $"report-{Guid.NewGuid():N}",
            "management-payment-reporting");
        var confirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
            _database.ConnectionString,
            attempt.PaymentAttemptId,
            $"REPORT-{Guid.NewGuid():N}",
            "management-payment-reporting",
            context.CorrelationId);
        confirmation.Should().NotBeNull();
        await SetCanonicalTimesAsync(attempt.PaymentAttemptId, confirmation!.PaymentConfirmationId, PeriodStart);

        var repository = new PostgresManagementPaymentReconciliationReportingRepository(_database.ConnectionString);
        var scope = new ManagementDashboardScopeSnapshot(
            ManagementDashboardReportingValues.ScopeSite,
            context.SiteId,
            "Payment Reporting Site",
            PeriodStart,
            [new ManagementDashboardSiteSnapshot(context.SiteId, "ACTIVE", true, PeriodStart)]);

        var included = await repository.ReadSummaryAsync(scope, PeriodStart, PeriodEnd, CancellationToken.None);
        var excludedAtEnd = await repository.ReadSummaryAsync(
            scope,
            PeriodStart.AddDays(-1),
            PeriodStart,
            CancellationToken.None);

        included.Status.Should().Be(ManagementPaymentReconciliationReadStatus.Resolved);
        included.Snapshot!.Attempts.Should().ContainSingle(row =>
            row.CurrencyCode == "PHP" && row.Count == 1 && row.Amount == 100.00m);
        included.Snapshot.Confirmations.Should().ContainSingle(row =>
            row.CurrencyCode == "PHP" && row.Status == "RECORDED" && row.Count == 1 && row.Amount == 100.00m);
        included.Snapshot.DataAsOf.Should().NotBeNull();
        excludedAtEnd.Status.Should().Be(ManagementPaymentReconciliationReadStatus.Resolved);
        excludedAtEnd.Snapshot!.Attempts.Should().BeEmpty("the period end is exclusive");
        excludedAtEnd.Snapshot.Confirmations.Should().BeEmpty("the period end is exclusive");
    }

    private async Task SetCanonicalTimesAsync(
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        DateTimeOffset timestamp)
    {
        const string sql = """
            UPDATE core.payment_attempts
            SET requested_at = @timestamp, updated_at = @timestamp
            WHERE payment_attempt_id = @payment_attempt_id;

            UPDATE core.payment_confirmations
            SET verified_at = @timestamp, confirmed_at = @timestamp, created_at = @timestamp
            WHERE payment_confirmation_id = @payment_confirmation_id;
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("timestamp", timestamp);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        await command.ExecuteNonQueryAsync();
    }
}
