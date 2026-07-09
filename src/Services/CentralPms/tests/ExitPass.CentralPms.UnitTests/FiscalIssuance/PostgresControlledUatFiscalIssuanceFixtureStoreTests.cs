using System.Reflection;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PostgresControlledUatFiscalIssuanceFixtureStoreTests
{
    [Fact]
    public void ValidateApprovedFirstRunFixture_WhenApprovedFixture_DoesNotThrow()
    {
        var act = () => PostgresControlledUatFiscalIssuanceFixtureStore.ValidateApprovedFirstRunFixture(
            ApprovedFixture(),
            ApprovedProfile());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("run_id")]
    [InlineData("parking_session_ref")]
    [InlineData("payment_attempt_ref")]
    [InlineData("payment_confirmation_ref")]
    [InlineData("upstream_finality_ref")]
    [InlineData("business_day_date")]
    [InlineData("amount")]
    public void ValidateApprovedFirstRunFixture_WhenFixtureValueIsNotApproved_RejectsBeforeDatabaseOpen(string field)
    {
        var fixture = field switch
        {
            "run_id" => ApprovedFixture() with { RunId = "CPS-POS-UAT-OTHER" },
            "parking_session_ref" => ApprovedFixture() with { ParkingSessionRef = "DEV-PARKING-SESSION-OTHER" },
            "payment_attempt_ref" => ApprovedFixture() with { PaymentAttemptRef = "DEV-PAYMENT-ATTEMPT-OTHER" },
            "payment_confirmation_ref" => ApprovedFixture() with { PaymentConfirmationRef = "DEV-PAYMENT-FINALITY-OTHER" },
            "upstream_finality_ref" => ApprovedFixture() with { UpstreamFinalityRef = "CPS-POS-UAT:OTHER:newly_created:001" },
            "business_day_date" => ApprovedFixture() with { BusinessDayDate = new DateOnly(2026, 7, 10) },
            "amount" => ApprovedFixture() with { AmountMinorUnits = 20000 },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        var act = () => PostgresControlledUatFiscalIssuanceFixtureStore.ValidateApprovedFirstRunFixture(
            fixture,
            ApprovedProfile());

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*not_approved*");
    }

    [Fact]
    public void FixtureSql_PreservesProductionDatabaseGuard()
    {
        var sql = FixtureSql();

        sql.Should().Contain("current_database()");
        sql.Should().Contain("(prod|production|shared|live)");
        sql.Should().Contain("Refusing controlled UAT fixture preparation");
    }

    [Fact]
    public void FixtureSql_UsesSchemaNaturalConstraintsForIdempotentApprovedFixturePreparation()
    {
        var sql = FixtureSql();

        sql.Should().Contain("uq_service_identities__service_identity_code");
        sql.Should().Contain("uq_site_groups__site_group_code");
        sql.Should().Contain("uq_sites__site_group_site_code");
        sql.Should().Contain("uq_vendor_systems__vendor_code_environment");
        sql.Should().Contain("snapshot_status = 'EXPIRED'");
        sql.Should().Contain("WHERE payment_attempt_id = @payment_attempt_id");
        sql.Should().Contain("ON CONFLICT (payment_attempt_id) DO UPDATE SET");
        sql.Should().Contain("WHERE payment_confirmation_id = @payment_confirmation_id");
        sql.Should().Contain("ON CONFLICT (payment_confirmation_id) DO UPDATE SET");
    }

    [Fact]
    public void FixtureSql_RepairsStaleApprovedPaymentRowsBeforeInsert()
    {
        var sql = FixtureSql();

        sql.Should().Contain("UPDATE core.payment_attempts");
        sql.Should().Contain("idempotency_key = @payment_attempt_ref");
        sql.Should().Contain("UPDATE core.payment_confirmations");
        sql.Should().Contain("provider_transaction_ref = @upstream_finality_ref");
    }

    [Fact]
    public void FixtureSql_DeactivatesStaleControlledUatFiscalReferencesForApprovedPaymentConfirmation()
    {
        var sql = FixtureSql();

        sql.Should().Contain("UPDATE core.fiscal_issuance_references");
        sql.Should().Contain("payment_confirmation_id = @payment_confirmation_id");
        sql.Should().Contain("upstream_finality_reference <> @upstream_finality_ref");
        sql.Should().Contain("is_active = FALSE");
    }

    [Fact]
    public void AddParameters_UsesUtcTimestamptzValues()
    {
        using var command = new NpgsqlCommand();
        var method = typeof(PostgresControlledUatFiscalIssuanceFixtureStore)
            .GetMethod("AddParameters", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { command, ApprovedFixture() });

        command.Parameters["business_start_at"].Value.Should()
            .Be(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero));
        command.Parameters["business_end_at"].Value.Should()
            .Be(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("core.exit_authorizations")]
    [InlineData("gates.")]
    [InlineData("refund")]
    [InlineData("reversal")]
    [InlineData("pdf")]
    [InlineData("html")]
    [InlineData("qr")]
    public void FixtureSql_DoesNotWireForbiddenOperationalDependencies(string forbiddenToken)
    {
        var sql = FixtureSql();

        sql.Should().NotContain(forbiddenToken);
    }

    private static string FixtureSql()
    {
        var field = typeof(PostgresControlledUatFiscalIssuanceFixtureStore)
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        return field!.GetRawConstantValue().Should().BeOfType<string>().Subject;
    }

    private static ControlledUatFiscalIssuanceFixture ApprovedFixture() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
            PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
            ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
            TariffSnapshotId: Guid.Parse("00000000-0000-4000-8000-000000000601"),
            ServiceIdentityId: Guid.Parse("00000000-0000-4000-8000-000000000901"),
            SiteGroupId: Guid.Parse("00000000-0000-4000-8000-000000000401"),
            SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
            VendorSystemId: Guid.Parse("00000000-0000-4000-8000-000000000501"),
            RunId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            CorrelationId: Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"),
            SiteRef: "DEV-SITE-ATC-001",
            ParkingSessionRef: "DEV-PARKING-SESSION-ATC-001",
            PaymentAttemptRef: "DEV-PAYMENT-ATTEMPT-ATC-001",
            PaymentConfirmationRef: "DEV-PAYMENT-FINALITY-ATC-001",
            UpstreamFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            Currency: "PHP",
            AmountMinorUnits: 10000,
            BusinessDayDate: new DateOnly(2026, 7, 9));

    private static ControlledUatFiscalSmokeProfile ApprovedProfile() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            EnvironmentName: "DEV-CONTROLLED-UAT-LOCAL",
            SiteRef: "DEV-SITE-ATC-001",
            SitePosServerRef: "DEV-POS-SERVER-ATC-001",
            FiscalDocumentType: "sales_invoice",
            RunId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            CorrelationId: "b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df",
            UpstreamFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            ParkingSessionRef: "DEV-PARKING-SESSION-ATC-001",
            PaymentAttemptRef: "DEV-PAYMENT-ATTEMPT-ATC-001",
            PaymentConfirmationRef: "DEV-PAYMENT-FINALITY-ATC-001",
            PayableBasisRef: "DEV-PAYABLE-BASIS-ATC-001",
            Currency: "PHP",
            ApprovalReference: "DEV-UAT-CPS-POS-001",
            BusinessDayDate: new DateOnly(2026, 7, 9),
            AmountMinorUnits: 10000,
            ConflictAmountMinorUnits: 10001,
            TaxAmountMinorUnits: 0,
            SupportedScenarios: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "newly_created",
                "replay",
                "conflict"
            },
            PaymentConfirmationId: Guid.Parse("00000000-0000-4000-8000-000000000301"),
            PaymentAttemptId: Guid.Parse("00000000-0000-4000-8000-000000000302"),
            ParkingSessionId: Guid.Parse("00000000-0000-4000-8000-000000000303"),
            SiteGroupId: Guid.Parse("00000000-0000-4000-8000-000000000401"),
            SiteId: Guid.Parse("00000000-0000-4000-8000-000000000402"),
            VendorSystemId: Guid.Parse("00000000-0000-4000-8000-000000000501"),
            TariffSnapshotId: Guid.Parse("00000000-0000-4000-8000-000000000601"),
            ServiceIdentityId: Guid.Parse("00000000-0000-4000-8000-000000000901"),
            SitePosServerId: Guid.Parse("10000000-0000-4000-8000-000000000201"),
            FiscalDocumentTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000103"),
            FiscalDocumentStatusCodeId: Guid.Parse("10000000-0000-4000-8000-000000000107"),
            LineTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000108"),
            TenderTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000109"),
            TaxTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000110"),
            TaxClassificationCodeId: Guid.Parse("10000000-0000-4000-8000-000000000111"),
            TotalTypeCodeId: Guid.Parse("10000000-0000-4000-8000-000000000112"));
}
