using System.Reflection;
using ExitPass.CentralPms.Application.FiscalIssuance;
using ExitPass.CentralPms.Infrastructure.FiscalIssuance;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class PostgresControlledUatFiscalVoidSafetyGuardTests
{
    [Fact]
    public void ValidateApprovedRealVoidRequest_WhenApprovedRequest_DoesNotThrow()
    {
        var act = () => PostgresControlledUatFiscalVoidSafetyGuard.ValidateApprovedRealVoidRequest(ApprovedRequest());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("fiscalReference")]
    [InlineData("document")]
    [InlineData("number")]
    [InlineData("paymentFinality")]
    [InlineData("sequence")]
    [InlineData("reason")]
    [InlineData("correlation")]
    public void ValidateApprovedRealVoidRequest_WhenTargetValueIsNotApproved_RejectsBeforeDatabaseOpen(string field)
    {
        var request = field switch
        {
            "profile" => ApprovedRequest() with { ProfileId = "OTHER" },
            "fiscalReference" => ApprovedRequest() with { FiscalIssuanceReferenceId = Guid.NewGuid() },
            "document" => ApprovedRequest() with { PosServerFiscalDocumentId = Guid.NewGuid() },
            "number" => ApprovedRequest() with { FiscalDocumentNumber = "SI-OTHER" },
            "paymentFinality" => ApprovedRequest() with { PaymentFinalityRef = "OTHER" },
            "sequence" => ApprovedRequest() with { FiscalSequenceValue = 3 },
            "reason" => ApprovedRequest() with { ReasonCode = "CONTROLLED_UAT_VOID_SMOKE" },
            "correlation" => ApprovedRequest() with { CorrelationId = Guid.NewGuid() },
            _ => ApprovedRequest()
        };

        var act = () => PostgresControlledUatFiscalVoidSafetyGuard.ValidateApprovedRealVoidRequest(request);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SafetyGuardSql_PreservesProductionDatabaseGuardAndApprovedDocumentLookup()
    {
        var sql = GuardSql();

        sql.Should().Contain("current_database() ~* '(prod|production|shared|live)'");
        sql.Should().Contain("FROM pos.fiscal_documents");
        sql.Should().Contain("fiscal_document_id = @fiscal_document_id");
        sql.Should().Contain("fiscal_document_number = @fiscal_document_number");
        sql.Should().Contain("payment_finality_ref = @payment_finality_ref");
        sql.Should().Contain("fiscal_sequence_value = @fiscal_sequence_value");
        var lowerSql = sql.ToLowerInvariant();
        lowerSql.Should().NotContain("update ");
        lowerSql.Should().NotContain("insert ");
        lowerSql.Should().NotContain("delete ");
    }

    private static string GuardSql()
    {
        var field = typeof(PostgresControlledUatFiscalVoidSafetyGuard)
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        return (string)field!.GetValue(null)!;
    }

    private static ControlledUatFiscalVoidSafetyGuardRequest ApprovedRequest() =>
        new(
            ProfileId: "CPS-POS-UAT-20260709-DEV-ATC-001",
            FiscalIssuanceReferenceId: Guid.Parse("14479d9a-844f-4dba-9578-e863ece93fbf"),
            PosServerFiscalDocumentId: Guid.Parse("9bdf2948-dadd-450b-8776-be688b579395"),
            FiscalDocumentNumber: "SI-00000002-UAT",
            PaymentFinalityRef: "CPS-POS-UAT:CPS-POS-UAT-20260709-DEV-ATC-001:newly_created:001",
            FiscalSequenceValue: 2,
            ReasonCode: "CONTROLLED_UAT_REAL_VOID",
            CorrelationId: Guid.Parse("b7b4cbea-0c8c-4d06-9f6f-728a0a3fc2df"));
}
