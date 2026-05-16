using ExitPass.PaymentOrchestrator.Contracts.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Routing;
using ExitPass.PaymentOrchestrator.Infrastructure.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.Routing;

/// <summary>
/// Integration tests for database-backed payment provider routing policy resolution.
/// </summary>
public sealed class PaymentProviderRoutingPolicyResolverIntegrationTests
{
    private static readonly Guid CorrelationId = Guid.Parse("6de95bb4-8f5a-4170-9184-e8eb4cb15c57");

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EXITPASS_TEST_MAIN_DB")
        ?? Environment.GetEnvironmentVariable("EXITPASS_INTEGRATION_DB")
        ?? Environment.GetEnvironmentVariable("EXITPASS_TEST_DB_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__MainDatabase")
        ?? throw new InvalidOperationException("Payment Orchestrator routing tests require a main database connection string.");

    /// <summary>
    /// Verifies that the default QRPh policy is loaded from the database.
    /// </summary>
    [Fact]
    public async Task ResolveRoute_WhenPaymentMethodIsQrph_ReturnsAubPrimaryAndPayMongoFallback()
    {
        await EnsureRoutingPolicySchemaAsync();
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(CreateRequest(PaymentMethodCode.QrPh), CancellationToken.None);

        Assert.True(result.IsRouted);
        Assert.Equal(ProviderCode.Aub, result.SelectedProviderCode);
        Assert.Equal(ProviderCode.PayMongo, result.FallbackProviderCode);
        Assert.NotNull(result.RoutingPolicyId);
    }

    /// <summary>
    /// Verifies that route selection follows database policy rows rather than WebPay hard-coded logic.
    /// </summary>
    [Fact]
    public async Task ResolveRoute_UsesDatabasePolicyNotHardCodedWebPayLogic()
    {
        await EnsureRoutingPolicySchemaAsync();
        var siteId = Guid.NewGuid();

        await InsertSiteOverrideAsync(
            siteId,
            PaymentMethodCode.QrPh,
            primaryProviderCode: ProviderCode.PayMongo,
            fallbackProviderCode: ProviderCode.Aub);

        try
        {
            var resolver = CreateResolver();

            var result = await resolver.ResolveAsync(
                CreateRequest(PaymentMethodCode.QrPh, siteId: siteId),
                CancellationToken.None);

            Assert.True(result.IsRouted);
            Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
            Assert.Equal(ProviderCode.Aub, result.FallbackProviderCode);
            Assert.Equal(ProviderRoutingReason.PrimaryProviderSelected, result.RoutingReason);
        }
        finally
        {
            await DeleteSitePoliciesAsync(siteId);
        }
    }

    /// <summary>
    /// Verifies that a disabled primary provider selects the configured fallback provider from the database.
    /// </summary>
    [Fact]
    public async Task ResolveRoute_WhenPrimaryDisabled_ReturnsFallbackProvider()
    {
        await EnsureRoutingPolicySchemaAsync();
        var siteId = Guid.NewGuid();

        await InsertSiteOverrideAsync(
            siteId,
            PaymentMethodCode.Card,
            primaryProviderCode: ProviderCode.Aub,
            fallbackProviderCode: ProviderCode.PayMongo,
            primaryProviderEnabled: false);

        try
        {
            var resolver = CreateResolver();

            var result = await resolver.ResolveAsync(
                CreateRequest(PaymentMethodCode.Card, siteId: siteId),
                CancellationToken.None);

            Assert.True(result.IsRouted);
            Assert.Equal(ProviderCode.PayMongo, result.SelectedProviderCode);
            Assert.Equal(ProviderRoutingReason.FallbackProviderSelectedPrimaryDisabled, result.RoutingReason);
        }
        finally
        {
            await DeleteSitePoliciesAsync(siteId);
        }
    }

    /// <summary>
    /// Verifies that unsupported currency returns a deterministic no-route response.
    /// </summary>
    [Fact]
    public async Task ResolveRoute_WhenCurrencyUnsupported_ReturnsNoRouteError()
    {
        await EnsureRoutingPolicySchemaAsync();
        var resolver = CreateResolver();

        var result = await resolver.ResolveAsync(
            CreateRequest(PaymentMethodCode.QrPh, currency: "USD"),
            CancellationToken.None);

        Assert.False(result.IsRouted);
        Assert.Equal(ProviderRoutingReason.CurrencyUnsupported, result.ErrorCode);
    }

    private static PaymentProviderRoutingPolicyResolver CreateResolver()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = ConnectionString
            })
            .Build();

        return new PaymentProviderRoutingPolicyResolver(
            configuration,
            NullLogger<PaymentProviderRoutingPolicyResolver>.Instance);
    }

    private static ResolvePaymentProviderRouteRequest CreateRequest(
        string paymentMethod,
        string currency = "PHP",
        Guid? siteId = null)
    {
        return new ResolvePaymentProviderRouteRequest(
            SiteId: siteId,
            SiteGroupId: null,
            PaymentMethod: paymentMethod,
            AmountMinorUnits: 12500,
            Currency: currency,
            PreferredProviderCode: null,
            CorrelationId);
    }

    private static async Task EnsureRoutingPolicySchemaAsync()
    {
        var patchPath = ResolveRepoPath(
            Path.Combine("infra", "db", "patches", "ExitPass_PaymentProviderRoutingPolicy_v1.2.sql"));

        var sql = await File.ReadAllTextAsync(patchPath);

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertSiteOverrideAsync(
        Guid siteId,
        string paymentMethodCode,
        string primaryProviderCode,
        string? fallbackProviderCode,
        bool primaryProviderEnabled = true,
        bool fallbackProviderEnabled = true)
    {
        const string sql = """
            insert into payments.payment_provider_routing_policies (
                payment_routing_policy_id,
                site_id,
                payment_method_code,
                primary_provider_code,
                fallback_provider_code,
                currency_code,
                is_enabled,
                primary_provider_enabled,
                fallback_provider_enabled,
                effective_from
            )
            values (
                gen_random_uuid(),
                @site_id,
                @payment_method_code,
                @primary_provider_code,
                @fallback_provider_code,
                'PHP',
                true,
                @primary_provider_enabled,
                @fallback_provider_enabled,
                '2026-01-01T00:00:00Z'
            );
            """;

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("payment_method_code", paymentMethodCode);
        command.Parameters.AddWithValue("primary_provider_code", primaryProviderCode);
        command.Parameters.AddWithValue("fallback_provider_code", (object?)fallbackProviderCode ?? DBNull.Value);
        command.Parameters.AddWithValue("primary_provider_enabled", primaryProviderEnabled);
        command.Parameters.AddWithValue("fallback_provider_enabled", fallbackProviderEnabled);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeleteSitePoliciesAsync(Guid siteId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "delete from payments.payment_provider_routing_policies where site_id = @site_id;",
            connection);
        command.Parameters.AddWithValue("site_id", siteId);
        await command.ExecuteNonQueryAsync();
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
