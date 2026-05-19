using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace ExitPass.PaymentOrchestrator.IntegrationTests.Persistence;

/// <summary>
/// Contract tests for provider session persistence SQL against the v1.2 database DDL.
/// </summary>
public sealed class ProviderSessionRepositorySqlContractTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EXITPASS_TEST_MAIN_DB")
        ?? Environment.GetEnvironmentVariable("EXITPASS_INTEGRATION_DB")
        ?? Environment.GetEnvironmentVariable("EXITPASS_TEST_DB_CONNECTION_STRING")
        ?? Environment.GetEnvironmentVariable("ConnectionStrings__MainDatabase")
        ?? throw new InvalidOperationException("Payment Orchestrator persistence tests require a main database connection string.");

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

        var providerSessionsDdl = ExtractProviderSessionsDdl(ddl);
        Assert.Contains("provider_session_ref varchar(128)", providerSessionsDdl, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_payment_ref", providerSessionsDdl, StringComparison.Ordinal);

        Assert.Contains("where rail_code = @rail_code", repositorySource, StringComparison.Ordinal);
        Assert.Contains("command.Parameters.AddWithValue(\"rail_code\", paymentRailCode)", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("payment_rail_code", repositorySource, StringComparison.Ordinal);
        Assert.Contains("provider_session_ref", repositorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("provider_payment_ref", repositorySource, StringComparison.Ordinal);
        AssertInsertColumnsExistInProviderSessionsDdl(repositorySource, providerSessionsDdl);

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

    /// <summary>
    /// Verifies PayMongo hosted checkout provider sessions persist against the v1.2 provider_sessions table.
    /// </summary>
    [Fact]
    public async Task ProviderSessionRepository_AddAsync_PersistsPayMongoHostedCheckoutSessionAgainstV12Schema()
    {
        await ApplySqlFileAsync(Path.Combine("infra", "db", "seed", "ExitPass_Reference_Data_v1.2.sql"));
        await ApplySqlFileAsync(Path.Combine("infra", "db", "patches", "ExitPass_PayMongoPaymentRailReferenceData_v1.2.sql"));

        var serviceIdentityId = await QueryGuidAsync(
            "select service_identity_id from identity.service_identities where service_identity_code = 'payment-orchestrator';");
        var vendorSystemId = await QueryGuidAsync(
            "select vendor_system_id from integration.vendor_systems where vendor_code = 'MOCK_VENDOR_PMS' and environment_code = 'DEV';");
        var siteGroupId = await QueryGuidAsync(
            "select site_group_id from sites.site_groups where site_group_code = 'DEV_PROPERTY';");
        var siteId = await QueryGuidAsync(
            """
            select s.site_id
            from sites.sites s
            join sites.site_groups sg on sg.site_group_id = s.site_group_id
            where sg.site_group_code = 'DEV_PROPERTY'
              and s.site_code = 'DEV_PARKING';
            """);
        var parkingSessionId = Guid.NewGuid();
        var tariffSnapshotId = Guid.NewGuid();
        var paymentAttemptId = Guid.NewGuid();
        var providerSessionRecordId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var idempotencyKey = $"provider-session-{Guid.NewGuid():N}";

        await InsertPaymentAttemptFixtureAsync(
            parkingSessionId,
            tariffSnapshotId,
            paymentAttemptId,
            vendorSystemId,
            siteGroupId,
            siteId,
            serviceIdentityId);

        try
        {
            var repository = new ProviderSessionRepository(
                CreateConfiguration(),
                NullLogger<ProviderSessionRepository>.Instance);

            var record = new ProviderSessionRecord(
                providerSessionRecordId,
                paymentAttemptId,
                "PAYMONGO",
                "PAYMONGO_CHECKOUT_SESSION",
                "cs_test_checkout_123",
                "pi_test_123",
                "PENDING_PROVIDER",
                "https://checkout.paymongo.test/session/cs_test_checkout_123",
                null,
                DateTimeOffset.UtcNow.AddMinutes(30),
                idempotencyKey,
                correlationId,
                "{\"AmountMinor\":12500,\"Currency\":\"PHP\"}",
                "{\"data\":{\"id\":\"cs_test_checkout_123\"}}",
                DateTimeOffset.UtcNow);

            await repository.AddAsync(record, CancellationToken.None);

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                select
                    ps.provider_session_ref,
                    ps.provider_transaction_ref,
                    ps.idempotency_key,
                    ps.session_status,
                    ps.currency_code,
                    ps.amount,
                    ps.checkout_url,
                    ps.correlation_id,
                    pr.rail_code
                from payments.provider_sessions ps
                join payments.payment_rails pr on pr.payment_rail_id = ps.payment_rail_id
                where ps.provider_session_id = @provider_session_id;
                """,
                connection);
            command.Parameters.AddWithValue("provider_session_id", providerSessionRecordId);

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("cs_test_checkout_123", reader.GetString(reader.GetOrdinal("provider_session_ref")));
            Assert.Equal("pi_test_123", reader.GetString(reader.GetOrdinal("provider_transaction_ref")));
            Assert.Equal(idempotencyKey, reader.GetString(reader.GetOrdinal("idempotency_key")));
            Assert.Equal("PENDING", reader.GetString(reader.GetOrdinal("session_status")));
            Assert.Equal("PHP", reader.GetString(reader.GetOrdinal("currency_code")));
            Assert.Equal(12500m, reader.GetDecimal(reader.GetOrdinal("amount")));
            Assert.Equal("https://checkout.paymongo.test/session/cs_test_checkout_123", reader.GetString(reader.GetOrdinal("checkout_url")));
            Assert.Equal(correlationId, reader.GetGuid(reader.GetOrdinal("correlation_id")));
            Assert.Equal("PAYMONGO_CHECKOUT_SESSION", reader.GetString(reader.GetOrdinal("rail_code")));
        }
        finally
        {
            await DeletePaymentAttemptFixtureAsync(providerSessionRecordId, paymentAttemptId, tariffSnapshotId, parkingSessionId);
        }
    }

    private static void AssertInsertColumnsExistInProviderSessionsDdl(
        string repositorySource,
        string providerSessionsDdl)
    {
        var insertStart = repositorySource.IndexOf("insert into payments.provider_sessions", StringComparison.Ordinal);
        Assert.True(insertStart >= 0, "ProviderSessionRepository provider_sessions insert was not found.");

        var columnListStart = repositorySource.IndexOf('(', insertStart);
        var columnListEnd = repositorySource.IndexOf(")\r\n            values", columnListStart, StringComparison.Ordinal);
        if (columnListEnd < 0)
        {
            columnListEnd = repositorySource.IndexOf(")\n            values", columnListStart, StringComparison.Ordinal);
        }

        Assert.True(columnListEnd > columnListStart, "ProviderSessionRepository provider_sessions insert column list was not found.");

        var columnList = repositorySource[(columnListStart + 1)..columnListEnd];
        var insertColumns = columnList
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(column => column.Trim())
            .ToArray();

        foreach (var column in insertColumns)
        {
            Assert.Contains($"{column} ", providerSessionsDdl, StringComparison.Ordinal);
        }
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

    private static string ExtractProviderSessionsDdl(string ddl)
    {
        var start = ddl.IndexOf("CREATE TABLE IF NOT EXISTS payments.provider_sessions", StringComparison.Ordinal);
        Assert.True(start >= 0, "payments.provider_sessions DDL was not found.");

        var end = ddl.IndexOf("-- ------------------------------------------------------------\r\n-- payments.provider_callbacks", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = ddl.IndexOf("-- ------------------------------------------------------------\n-- payments.provider_callbacks", start, StringComparison.Ordinal);
        }

        Assert.True(end > start, "payments.provider_sessions DDL end marker was not found.");
        return ddl[start..end];
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = ConnectionString
            })
            .Build();
    }

    private static async Task ApplySqlFileAsync(string relativePath)
    {
        var sql = await File.ReadAllTextAsync(ResolveRepoPath(relativePath));

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> QueryGuidAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var scalar = await command.ExecuteScalarAsync();
        Assert.IsType<Guid>(scalar);
        return (Guid)scalar;
    }

    private static async Task InsertPaymentAttemptFixtureAsync(
        Guid parkingSessionId,
        Guid tariffSnapshotId,
        Guid paymentAttemptId,
        Guid vendorSystemId,
        Guid siteGroupId,
        Guid siteId,
        Guid serviceIdentityId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            insert into core.parking_sessions (
                parking_session_id,
                site_group_id,
                site_id,
                vendor_system_id,
                vendor_session_ref,
                session_status,
                created_by_service_identity_id
            )
            values (
                @parking_session_id,
                @site_group_id,
                @site_id,
                @vendor_system_id,
                @vendor_session_ref,
                'ACTIVE',
                @service_identity_id
            );

            insert into core.tariff_snapshots (
                tariff_snapshot_id,
                parking_session_id,
                vendor_system_id,
                currency_code,
                gross_amount,
                statutory_discount_amount,
                coupon_discount_amount,
                net_amount,
                snapshot_status,
                calculated_at,
                expires_at,
                created_by_service_identity_id
            )
            values (
                @tariff_snapshot_id,
                @parking_session_id,
                @vendor_system_id,
                'PHP',
                12500,
                0,
                0,
                12500,
                'ACTIVE',
                now(),
                now() + interval '30 minutes',
                @service_identity_id
            );

            insert into core.payment_attempts (
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                idempotency_key,
                currency_code,
                amount,
                attempt_status,
                requested_at,
                expires_at,
                created_by_service_identity_id
            )
            values (
                @payment_attempt_id,
                @parking_session_id,
                @tariff_snapshot_id,
                @payment_attempt_idempotency_key,
                'PHP',
                12500,
                'PENDING_PROVIDER',
                now(),
                now() + interval '30 minutes',
                @service_identity_id
            );
            """,
            connection);

        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
        command.Parameters.AddWithValue("site_group_id", siteGroupId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("vendor_system_id", vendorSystemId);
        command.Parameters.AddWithValue("vendor_session_ref", $"session-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("payment_attempt_idempotency_key", $"payment-attempt-{Guid.NewGuid():N}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DeletePaymentAttemptFixtureAsync(
        Guid providerSessionRecordId,
        Guid paymentAttemptId,
        Guid tariffSnapshotId,
        Guid parkingSessionId)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            delete from payments.provider_sessions where provider_session_id = @provider_session_id;
            delete from core.payment_attempts where payment_attempt_id = @payment_attempt_id;
            delete from core.tariff_snapshots where tariff_snapshot_id = @tariff_snapshot_id;
            delete from core.parking_sessions where parking_session_id = @parking_session_id;
            """,
            connection);
        command.Parameters.AddWithValue("provider_session_id", providerSessionRecordId);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);
        command.Parameters.AddWithValue("tariff_snapshot_id", tariffSnapshotId);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);
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
