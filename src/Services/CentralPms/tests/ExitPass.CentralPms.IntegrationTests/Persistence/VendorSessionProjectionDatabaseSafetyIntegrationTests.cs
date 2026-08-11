using FluentAssertions;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class VendorSessionProjectionDatabaseSafetyIntegrationTests
{
    private const string ConnectionVariable = "EXITPASS_HIKCENTRAL_PROJECTION_TEST_DB";

    [Fact]
    public async Task ProjectionTargetSchema_DefaultsDisabledAndSupportsDeferredHealth()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_hikcentral_projection_",
            "the test is restricted to a task-owned disposable database");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT
                (SELECT column_default
                 FROM information_schema.columns
                 WHERE table_schema = 'sessions'
                   AND table_name = 'vendor_session_projection_sync_targets'
                   AND column_name = 'enabled_flag') AS enabled_default,
                (SELECT column_default
                 FROM information_schema.columns
                 WHERE table_schema = 'sessions'
                   AND table_name = 'vendor_session_projection_sync_targets'
                   AND column_name = 'poll_interval_seconds') AS poll_default,
                (SELECT column_default
                 FROM information_schema.columns
                 WHERE table_schema = 'sessions'
                   AND table_name = 'vendor_session_projection_sync_targets'
                   AND column_name = 'health_status') AS health_default,
                (SELECT pg_get_constraintdef(oid)
                 FROM pg_constraint
                 WHERE conname = 'ck_vendor_session_projection_sync_targets__health_status') AS health_check,
                EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'sessions'
                      AND table_name = 'vendor_session_projection_sync_targets'
                      AND column_name = 'last_lock_contention_at') AS has_last_contention,
                EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'sessions'
                      AND table_name = 'vendor_session_projection_sync_targets'
                      AND column_name = 'lock_contention_count') AS has_contention_count;
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();

        reader.GetString(0).Should().Be("false");
        reader.GetString(1).Should().StartWith("60");
        reader.GetString(2).Should().Contain("DISABLED");
        reader.GetString(3).Should().Contain("DEFERRED");
        reader.GetBoolean(4).Should().BeTrue();
        reader.GetBoolean(5).Should().BeTrue();
    }

    [Fact]
    public async Task ProjectionLookup_UsesTargetCompletionJoinWithoutSqlFailure()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException($"{ConnectionVariable} is required.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_hikcentral_projection_",
            "the test is restricted to a task-owned disposable database");
        var repository = new PostgresVendorSessionProjectionRepository(connectionString);

        var result = await repository.FindLatestAsync(
            new VendorSessionProjectionLookupQuery(
                CardNum: $"NO-MATCH-{Guid.NewGuid():N}",
                PlateLicense: null,
                SiteId: null,
                SiteGroupId: null,
                ParkingLotIndexCode: null,
                RequestedAt: DateTimeOffset.UtcNow,
                CorrelationId: Guid.NewGuid()),
            CancellationToken.None);

        result.Should().BeNull();
    }
}
