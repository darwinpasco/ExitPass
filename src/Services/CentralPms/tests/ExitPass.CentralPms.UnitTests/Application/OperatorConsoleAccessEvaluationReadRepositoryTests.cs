using ExitPass.CentralPms.Application.OperatorConsole;
using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit tests for Operator Console access evaluation read-model repository contracts.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationReadRepositoryTests
{
    /// <summary>
    /// Verifies the empty read context is fail-safe for missing read model rows.
    /// </summary>
    [Fact]
    public void Empty_WhenCreated_ReturnsSafeMissingContext()
    {
        var request = CreateRequest();

        var context = OperatorConsoleAccessEvaluationReadContext.Empty(request);

        context.Request.Should().Be(request);
        context.HrIdentityMapping.Should().BeNull();
        context.DeviceBinding.Should().BeNull();
        context.DeviceAssignment.Should().BeNull();
        context.ActiveShift.Should().BeNull();
        context.LatestShiftVersion.Should().BeNull();
        context.LatestShiftRevocation.Should().BeNull();
        context.ActiveShiftTakeover.Should().BeNull();
        context.StatutoryEntitlementFingerprint.Should().BeNull();
    }

    /// <summary>
    /// Verifies the repository implementation is read-only and does not mutate operational truth tables.
    /// </summary>
    [Fact]
    public void RepositorySource_RemainsReadOnlyAndDoesNotTouchPaymentGateCouponProviderSettlementTables()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleAccessEvaluationReadRepository.cs");

        source.Should().NotContain("INSERT ", because: "this slice must not persist access evaluations");
        source.Should().NotContain("UPDATE ", because: "this slice must not mutate read model state");
        source.Should().NotContain("DELETE ", because: "this slice must not delete read model state");
        source.Should().NotContain("MERGE ", because: "this slice must not upsert read model state");
        source.Should().NotContain("ExecuteNonQuery", because: "the repository must only read");
        source.Should().NotContain("BeginTransaction", because: "read-only skeleton queries do not need write transactions");
        Assert.DoesNotContain("core.payment_attempts", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.payment_confirmations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.exit_authorizations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payments.provider_outcomes", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gates.", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settlement", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coupon", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUB", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies SQL is parameterized around access-evaluation request identifiers.
    /// </summary>
    [Fact]
    public void RepositorySource_UsesParameterizedQueries()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleAccessEvaluationReadRepository.cs");

        source.Should().Contain("@user_id");
        source.Should().Contain("@site_id");
        source.Should().Contain("command.Parameters.Add");
        source.Should().Contain("NpgsqlDbType.Uuid");
    }

    private static OperatorConsoleAccessEvaluationReadRequest CreateRequest() =>
        new(
            Guid.Parse("42000000-0000-0000-0000-000000000001"),
            Guid.Parse("42000000-0000-0000-0000-000000000002"),
            Guid.Parse("42000000-0000-0000-0000-000000000003"),
            Guid.Parse("42000000-0000-0000-0000-000000000004"),
            Guid.Parse("42000000-0000-0000-0000-000000000005"),
            Guid.Parse("42000000-0000-0000-0000-000000000006"),
            "STATUTORY_VALIDATION",
            "STATUTORY_VALIDATION.APPROVE",
            "VIEW_EVIDENCE_FOR_DECISION",
            DateTimeOffset.Parse("2026-05-29T00:00:00Z"),
            Guid.Parse("42000000-0000-0000-0000-000000000007"));

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
