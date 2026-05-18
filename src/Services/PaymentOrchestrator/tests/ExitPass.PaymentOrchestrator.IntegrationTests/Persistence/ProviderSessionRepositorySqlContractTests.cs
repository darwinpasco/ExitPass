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
        var payMongoRailPatch = await File.ReadAllTextAsync(ResolveRepoPath(Path.Combine(
            "infra",
            "db",
            "patches",
            "ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql")));

        Assert.Contains("rail_code varchar(64) NOT NULL", ddl, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_rail_code", ExtractPaymentRailsDdl(ddl), StringComparison.Ordinal);

        Assert.Contains("where rail_code = @rail_code", repositorySource, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"rail_code\", paymentRailCode)", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_rail_code", repositorySource, StringComparison.Ordinal);

        Assert.Contains("UPDATE payments.payment_rails", payMongoRailPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("SET\r\n    payment_rail_id", payMongoRailPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("SET\n    payment_rail_id", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO payments.payment_rails", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("payment_rail_id", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("rail_code", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("rail_name", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("provider_code", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("rail_type", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("supported_currency_code", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("rail_status", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("is_primary", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("is_fallback", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("effective_from", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("effective_to", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("created_at", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("updated_at", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("row_version", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("WHERE rail_code = 'PAYMONGO_CHECKOUT_SESSION'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("'PAYMONGO_CHECKOUT_SESSION'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("'PayMongo Checkout Session'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("'PAYMONGO'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("'HOSTED_CHECKOUT'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("'ACTIVE'", payMongoRailPatch, StringComparison.Ordinal);
        Assert.Contains("WHERE NOT EXISTS", payMongoRailPatch, StringComparison.Ordinal);
        Assert.DoesNotContain("ON CONFLICT", payMongoRailPatch, StringComparison.OrdinalIgnoreCase);
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
