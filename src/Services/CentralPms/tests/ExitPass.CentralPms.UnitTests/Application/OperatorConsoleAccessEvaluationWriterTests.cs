using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Unit checks for Operator Console access evaluation persistence mapping boundaries.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationWriterTests
{
    /// <summary>
    /// Verifies the writer maps allowed and denied evaluation fields to the inspected Operator Console action log.
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

        source.Should().Contain("INSERT INTO operations.operator_action_logs");
        source.Should().Contain("operator_action_log_id");
        source.Should().Contain("action_reason_code");
        source.Should().Contain("action_status");
        source.Should().Contain("action_notes");
        source.Should().Contain("operator_user_id");
        source.Should().Contain("target_entity_type");
        source.Should().Contain("target_entity_id");
        source.Should().Contain("site_id");
        source.Should().Contain("correlation_id");
        source.Should().Contain("FiscalStatusViewResultClass");
        source.Should().Contain("FiscalStatusViewSafeErrorCode");
        source.Should().Contain("FiscalStatusViewSafeErrorPosture");
        source.Should().Contain("FiscalStatusViewSourceModule");
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
