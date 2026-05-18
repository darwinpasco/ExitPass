using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.Persistence;

/// <summary>
/// Contract tests for provider session persistence SQL against the v1.2 database DDL.
/// </summary>
public sealed class ProviderSessionRepositorySqlContractTests
{
    /// <summary>
    /// Verifies provider session persistence resolves payment rails by the v1.2 rail_code column.
    /// </summary>
    [Fact]
    public async Task ProviderSessionRepository_ResolvesPaymentRailByV12RailCode()
    {
        var repositorySource = await File.ReadAllTextAsync(ResolveRepoPath(Path.Combine(
            "src",
            "Services",
            "PaymentOrchestrator",
            "src",
            "ExitPass.PaymentOrchestrator.Infrastructure",
            "Persistence",
            "ProviderSessionRepository.cs")));
        var ddl = await File.ReadAllTextAsync(ResolveRepoPath("ExitPass_Full_Database_Creation_DDL_v1.2.sql"));

        Assert.Contains("rail_code varchar(64) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_rail_code", ExtractPaymentRailsDdl(ddl), StringComparison.Ordinal);

        Assert.Contains("where rail_code = @rail_code", repositorySource, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"rail_code\", paymentRailCode)", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_rail_code", repositorySource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies missing payment rails continue to produce a deterministic repository error.
    /// </summary>
    [Fact]
    public async Task ProviderSessionRepository_MissingRailErrorNamesRailCode()
    {
        var repositorySource = await File.ReadAllTextAsync(ResolveRepoPath(Path.Combine(
            "src",
            "Services",
            "PaymentOrchestrator",
            "src",
            "ExitPass.PaymentOrchestrator.Infrastructure",
            "Persistence",
            "ProviderSessionRepository.cs")));

        Assert.Contains("No active payment rail found for rail_code", repositorySource, StringComparison.Ordinal);
    }

    private static string ExtractPaymentRailsDdl(string ddl)
    {
        var start = ddl.IndexOf("CREATE TABLE IF NOT EXISTS payments.payment_rails", StringComparison.Ordinal);
        Assert.True(start >= 0, "payments.payment_rails DDL was not found.");

        var end = ddl.IndexOf("-- ------------------------------------------------------------\r\n-- payments.provider_sessions", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = ddl.IndexOf("-- ------------------------------------------------------------\n-- payments.provider_sessions", start, StringComparison.Ordinal);
        }

        Assert.True(end > start, "payments.payment_rails DDL end marker was not found.");
        return ddl[start..end];
    }

    private static string ResolveRepoPath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository path '{relativePath}'.");
    }
}
