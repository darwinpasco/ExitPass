using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit checks for Operator Console access evaluation persistence mapping boundaries.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationWriterTests
{
    /// <summary>
    /// Verifies the writer maps allowed and denied evaluation fields to the inspected Operator Console tables.
    /// </summary>
    [Fact]
    public void WriterSource_MapsEvaluationAndDenialReasonColumns()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleAccessEvaluationWriter.cs");

        source.Should().Contain("INSERT INTO operator_console.operator_access_evaluations");
        source.Should().Contain("operator_access_evaluation_id");
        source.Should().Contain("requested_action");
        source.Should().Contain("evaluation_status");
        source.Should().Contain("operator_user_id");
        source.Should().Contain("hr_identity_mapping_id");
        source.Should().Contain("operator_device_binding_id");
        source.Should().Contain("operator_shift_id");
        source.Should().Contain("site_group_id");
        source.Should().Contain("site_id");
        source.Should().Contain("decision_snapshot_json");
        source.Should().Contain("INSERT INTO operator_console.operator_access_evaluation_reasons");
        source.Should().Contain("reason_code");
        source.Should().Contain("display_order");
    }

    /// <summary>
    /// Verifies persistence remains scoped to Operator Console audit evidence.
    /// </summary>
    [Fact]
    public void WriterSource_DoesNotMutatePaymentGateCouponProviderSettlementReconciliationOrAubState()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleAccessEvaluationWriter.cs");

        Assert.DoesNotContain("core.payment_attempts", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.payment_confirmations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("core.exit_authorizations", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payments.provider_outcomes", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gates.", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coupon", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settlement", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reconciliation.", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AUB", source, StringComparison.OrdinalIgnoreCase);
    }

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
