using FluentAssertions;
using ExitPass.CentralPms.Application.VendorSessions;
using ExitPass.CentralPms.Infrastructure.VendorSessions;
using ExitPass.CentralPms.IntegrationTests.Shared;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class VendorSessionProjectionDatabaseSafetyIntegrationTests
{
    private static readonly Guid CentralPmsServiceIdentityId =
        Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");

    [Fact]
    public async Task ProjectionTargetSchema_DefaultsDisabledAndSupportsDeferredHealth()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_central_pms_it_",
            "the canonical integration harness restricts the test to its task-owned disposable database");

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
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_central_pms_it_",
            "the canonical integration harness restricts the test to its task-owned disposable database");
        var repository = new PostgresVendorSessionProjectionRepository(
            connectionString,
            CentralPmsServiceIdentityId);

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

    [Fact]
    public async Task ProjectionUpsert_UsesConfiguredCentralPmsServiceIdentity()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        parsed.Database.Should().StartWith(
            "exitpass_central_pms_it_",
            "the canonical integration harness restricts the test to its task-owned disposable database");

        var repository = new PostgresVendorSessionProjectionRepository(
            connectionString,
            CentralPmsServiceIdentityId);
        var projectionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var projection = new VendorSessionProjection(
            projectionId,
            VendorSystemId: null,
            SiteId: null,
            SiteGroupId: null,
            ParkingLotIndexCode: null,
            ParkingLotName: null,
            PassagewayIndexCode: null,
            PassagewayName: null,
            LaneIndexCode: null,
            LaneName: null,
            LaneDirection: null,
            VendorRecordGuid: null,
            CardNum: $"IDENTITY-REGRESSION-{projectionId:N}",
            PlateLicense: null,
            EnterTime: now,
            ExitTime: null,
            AllowType: "TEST",
            AllowResult: "ALLOWED",
            ImageUrl: null,
            SourceApi: "integration-test",
            SourcePayloadHash: new string('a', 64),
            SourcePayloadReference: null,
            SourceEventAt: now,
            StableIdentityType: "CARD_NUM",
            StableIdentityKey: $"identity-regression:{projectionId:N}",
            FirstSeenAt: now,
            LastSeenAt: now,
            LastRefreshedAt: now,
            ProjectionStatus: VendorSessionProjectionStatus.Active,
            CorrelationId: Guid.NewGuid(),
            CreatedAt: now,
            UpdatedAt: now);

        try
        {
            await repository.UpsertAsync(projection, CancellationToken.None);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT created_by_service_identity_id, updated_by_service_identity_id
                FROM sessions.vendor_session_projections
                WHERE vendor_session_projection_id = @projection_id;
                """,
                connection);
            command.Parameters.AddWithValue("projection_id", projectionId);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetGuid(0).Should().Be(CentralPmsServiceIdentityId);
            reader.GetGuid(1).Should().Be(CentralPmsServiceIdentityId);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "DELETE FROM sessions.vendor_session_projections WHERE vendor_session_projection_id = @projection_id;",
                connection);
            command.Parameters.AddWithValue("projection_id", projectionId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
