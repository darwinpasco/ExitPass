using System.Reflection;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PostgresControlledUatFiscalVoidSmokeStoreTests
{
    [Fact]
    public void ValidateApprovedVoidSmokeRequest_WhenApprovedRequest_DoesNotThrow()
    {
        var act = () => PostgresControlledUatFiscalVoidSmokeStore.ValidateApprovedVoidSmokeRequest(ApprovedRequest());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("profile_id")]
    [InlineData("fiscal_issuance_reference_id")]
    [InlineData("pos_server_fiscal_document_id")]
    [InlineData("fiscal_document_number")]
    [InlineData("payment_finality_ref")]
    [InlineData("reason_code")]
    [InlineData("correlation_id")]
    public void ValidateApprovedVoidSmokeRequest_WhenTargetValueIsNotApproved_RejectsBeforeDatabaseOpen(string field)
    {
        var request = field switch
        {
            "profile_id" => ApprovedRequest() with { ProfileId = "CPS-POS-UAT-OTHER" },
            "fiscal_issuance_reference_id" => ApprovedRequest() with { FiscalIssuanceReferenceId = Guid.NewGuid() },
            "pos_server_fiscal_document_id" => ApprovedRequest() with { PosServerFiscalDocumentId = Guid.NewGuid() },
            "fiscal_document_number" => ApprovedRequest() with { FiscalDocumentNumber = "SI-00000003-UAT" },
            "payment_finality_ref" => ApprovedRequest() with { PaymentFinalityRef = "CPS-POS-UAT:OTHER:newly_created:001" },
            "reason_code" => ApprovedRequest() with { ReasonCode = "OTHER_REASON" },
            "correlation_id" => ApprovedRequest() with { CorrelationId = Guid.NewGuid() },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

        var act = () => PostgresControlledUatFiscalVoidSmokeStore.ValidateApprovedVoidSmokeRequest(request);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*not_approved*");
    }

    [Fact]
    public void VoidSmokeSql_PreservesProductionDatabaseGuard()
    {
        var sql = VoidSmokeSql();

        sql.Should().Contain("current_database()");
        sql.Should().Contain("(prod|production|shared|live)");
        sql.Should().Contain("Refusing controlled UAT fiscal void smoke");
    }

    [Fact]
    public void VoidSmokeSql_RecordsHeaderPostureAndStatusHistoryOnly()
    {
        var sql = VoidSmokeSql();

        sql.Should().Contain("UPDATE pos.fiscal_documents");
        sql.Should().Contain("controlledUatVoidSmoke");
        sql.Should().Contain("INSERT INTO pos.fiscal_document_status_history");
        sql.Should().Contain("@reason_code");
        sql.Should().Contain("central-pms-controlled-uat-void-smoke");
        sql.Should().NotContain("pos.fiscal_sequence_states");
        sql.Should().NotContain("current_sequence_value");
        sql.Should().NotContain("last_issued_sequence_value");
        sql.Should().NotContain("core.payment");
        sql.Should().NotContain("core.exit_authorizations");
        sql.Should().NotContain("gates.");
        var lowerSql = sql.ToLowerInvariant();
        lowerSql.Should().NotContain("pdf");
        lowerSql.Should().NotContain("html");
        lowerSql.Should().NotContain("qr");
    }

    [Fact]
    public void VoidSmokeSql_IsIdempotentForRepeatedApprovedVoid()
    {
        var sql = VoidSmokeSql();

        sql.Should().Contain("already_recorded");
        sql.Should().Contain("COALESCE((");
        sql.Should().Contain("FALSE) AS already_recorded");
        sql.Should().Contain("WHERE already_recorded = FALSE");
        sql.Should().Contain("#>> '{controlledUatVoidSmoke,posture}'");
    }

    [Fact]
    public void AddParameters_UsesOnlyApprovedReferenceMetadata()
    {
        using var command = new NpgsqlCommand();
        var method = typeof(PostgresControlledUatFiscalVoidSmokeStore)
            .GetMethod("AddParameters", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { command, ApprovedRequest() });

        command.Parameters["fiscal_document_id"].Value.Should()
            .Be(Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"));
        command.Parameters["fiscal_document_number"].Value.Should().Be("SI-00000002-UAT");
        command.Parameters["reason_code"].Value.Should().Be(FiscalIssuanceControlledUatVoidSmokeService.ApprovedReasonCode);
        command.Parameters["correlation_id"].Value.Should()
            .Be(Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"));
    }

    private static string VoidSmokeSql()
    {
        var field = typeof(PostgresControlledUatFiscalVoidSmokeStore)
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        return field!.GetRawConstantValue().Should().BeOfType<string>().Subject;
    }

    private static ControlledUatFiscalVoidSmokeStoreRequest ApprovedRequest() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalDocumentNumber: "SI-00000002-UAT",
            PaymentFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            ReasonCode: FiscalIssuanceControlledUatVoidSmokeService.ApprovedReasonCode,
            CorrelationId: Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"),
            ApprovedBy: "Darwin Pasco");
}
