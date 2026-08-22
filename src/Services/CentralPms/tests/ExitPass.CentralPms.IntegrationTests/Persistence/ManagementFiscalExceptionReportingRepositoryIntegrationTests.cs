using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Api;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

[Collection(OperatorConsoleManualFixtureCollection.Name)]
public sealed class ManagementFiscalExceptionReportingRepositoryIntegrationTests
{
    private static readonly DateTimeOffset PeriodStart = DateTimeOffset.Parse("2031-03-01T00:00:00Z");
    private static readonly DateTimeOffset PeriodEnd = DateTimeOffset.Parse("2031-03-02T00:00:00Z");
    private readonly StatutoryDiscountCanonicalDatabaseFixture _database;

    public ManagementFiscalExceptionReportingRepositoryIntegrationTests(
        StatutoryDiscountCanonicalDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task ReadSummary_UsesFiscalReferenceCohortExactMoneyAndHalfOpenPeriod()
    {
        await FiscalReferenceStatePatchHarness.EnsureAppliedAndValidatedAsync(_database.ConnectionString);
        var context = PaymentTestContext.Create("management-fiscal-reporting");
        await PaymentTestDataHelper.ResetAndSeedAsync(
            _database.ConnectionString,
            context,
            "Management fiscal exception reporting integration");
        var attempt = await PaymentRoutineTestHelper.CreateAttemptAsync(
            _database.ConnectionString,
            context,
            $"fiscal-report-{Guid.NewGuid():N}",
            "management-fiscal-reporting");
        var confirmation = await PaymentRoutineTestHelper.RecordPaymentConfirmationAsync(
            _database.ConnectionString,
            attempt.PaymentAttemptId,
            $"FISCAL-REPORT-{Guid.NewGuid():N}",
            "management-fiscal-reporting",
            context.CorrelationId);
        confirmation.Should().NotBeNull();
        await InsertReferenceAsync(context, attempt.PaymentAttemptId, confirmation!.PaymentConfirmationId, PeriodStart);

        var repository = new PostgresManagementFiscalExceptionReportingRepository(_database.ConnectionString);
        var scope = new ManagementDashboardScopeSnapshot(
            ManagementDashboardReportingValues.ScopeSite,
            context.SiteId,
            "Fiscal Reporting Site",
            PeriodStart,
            [new ManagementDashboardSiteSnapshot(context.SiteId, "ACTIVE", true, PeriodStart)]);

        var included = await repository.ReadSummaryAsync(scope, PeriodStart, PeriodEnd, CancellationToken.None);
        var excludedAtEnd = await repository.ReadSummaryAsync(
            scope,
            PeriodStart.AddDays(-1),
            PeriodStart,
            CancellationToken.None);

        included.Status.Should().Be(ManagementFiscalExceptionReadStatus.Resolved);
        included.Snapshot!.Records.Should().ContainSingle(row =>
            row.FiscalIssuanceState == "PENDING_FISCAL_ISSUANCE" &&
            row.CurrencyCode == "PHP" &&
            row.Count == 1 &&
            row.ExpectedIssuanceAmount == 100.00m);
        included.Snapshot.DataAsOf.Should().Be(PeriodStart);
        excludedAtEnd.Status.Should().Be(ManagementFiscalExceptionReadStatus.Resolved);
        excludedAtEnd.Snapshot!.Records.Should().BeEmpty("periodEnd is exclusive");
    }

    private async Task InsertReferenceAsync(
        PaymentTestContext context,
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        DateTimeOffset timestamp)
    {
        const string sql = """
            INSERT INTO core.fiscal_issuance_references (
                fiscal_issuance_reference_id,
                payment_confirmation_id,
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                site_id,
                payable_basis_ref,
                upstream_finality_reference,
                fiscal_number_assignment_state,
                fiscal_issuance_state,
                correlation_id,
                first_recorded_at,
                last_updated_at,
                is_active,
                is_superseded,
                is_reconciled)
            VALUES (
                gen_random_uuid(), @payment_confirmation_id, @payment_attempt_id, @parking_session_id,
                @tariff_snapshot_id, @site_id, @payable_basis_ref, @upstream_finality_reference,
                'NOT_ASSIGNED', 'PENDING_FISCAL_ISSUANCE', @correlation_id,
                @timestamp, @timestamp, true, false, false);
            """;
        await using var connection = new NpgsqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_confirmation_id", paymentConfirmationId);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("parking_session_id", context.ParkingSessionId);
        command.Parameters.AddWithValue("tariff_snapshot_id", context.TariffSnapshotId);
        command.Parameters.AddWithValue("site_id", context.SiteId);
        command.Parameters.AddWithValue("payable_basis_ref", $"PAYABLE-{context.CorrelationId:N}");
        command.Parameters.AddWithValue("upstream_finality_reference", $"FINAL-{context.CorrelationId:N}");
        command.Parameters.AddWithValue("correlation_id", context.CorrelationId);
        command.Parameters.AddWithValue("timestamp", timestamp);
        await command.ExecuteNonQueryAsync();
    }
}
