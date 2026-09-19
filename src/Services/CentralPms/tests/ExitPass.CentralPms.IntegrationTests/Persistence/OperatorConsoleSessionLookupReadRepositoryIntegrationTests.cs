using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.Infrastructure.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class OperatorConsoleSessionLookupReadRepositoryIntegrationTests
{
    private static readonly Guid CentralPmsServiceIdentityId =
        Guid.Parse("8063c159-dae6-57af-9f1f-e0a07d519fb2");

    [Fact]
    public async Task TicketLookup_UsesScopedProjectionWithoutMutation_ThenPrefersCoreSession()
    {
        var fixture = await Fixture.CreateAsync();
        try
        {
            var repository = new OperatorConsoleSessionLookupReadRepository(fixture.ConnectionString);
            var before = await fixture.CountBusinessRowsAsync();

            var projected = await repository.FindAsync(
                fixture.Request(fixture.SiteId, fixture.SiteGroupId),
                CancellationToken.None);

            projected.Should().NotBeNull();
            projected!.ParkingSessionId.Should().BeNull();
            projected.SessionSource.Should().Be("VENDOR_SESSION_PROJECTION");
            projected.SiteName.Should().Be("Projection Test Site");
            projected.PlateNumber.Should().Be("ABC****");
            projected.CurrentPayableAmountMinorUnits.Should().BeNull();
            projected.CurrencyCode.Should().BeNull();
            projected.PaymentStatus.Should().BeNull();
            projected.DiscountStatus.Should().BeNull();
            projected.ExitAuthorizationStatus.Should().BeNull();
            (await fixture.CountBusinessRowsAsync()).Should().Equal(before);

            var projectedFromDirectSiteScope = await repository.FindAsync(
                fixture.Request(fixture.SiteId, siteGroupId: null),
                CancellationToken.None);
            projectedFromDirectSiteScope.Should().NotBeNull();
            projectedFromDirectSiteScope!.SessionSource.Should().Be("VENDOR_SESSION_PROJECTION");
            projectedFromDirectSiteScope.SiteId.Should().Be(fixture.SiteId);
            projectedFromDirectSiteScope.SiteGroupId.Should().Be(fixture.SiteGroupId);
            (await fixture.CountBusinessRowsAsync()).Should().Equal(before);

            var crossSite = await repository.FindAsync(
                fixture.Request(Guid.NewGuid(), fixture.SiteGroupId),
                CancellationToken.None);
            crossSite.Should().BeNull();

            await fixture.InsertCoreSessionAsync();
            var core = await repository.FindAsync(
                fixture.Request(fixture.SiteId, fixture.SiteGroupId),
                CancellationToken.None);

            core.Should().NotBeNull();
            core!.ParkingSessionId.Should().Be(fixture.ParkingSessionId);
            core.SessionSource.Should().Be("CORE_PARKING_SESSION");
            core.PlateNumber.Should().Be("ABC****");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task TicketLookup_WhenScopedActiveProjectionIsAmbiguous_FailsClosed()
    {
        var fixture = await Fixture.CreateAsync();
        try
        {
            await fixture.InsertSecondProjectionAsync();
            var repository = new OperatorConsoleSessionLookupReadRepository(fixture.ConnectionString);

            var action = () => repository.FindAsync(
                fixture.Request(fixture.SiteId, fixture.SiteGroupId),
                CancellationToken.None);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("OPERATOR_CONSOLE_PROJECTION_IDENTIFIER_AMBIGUOUS");
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string connectionString)
        {
            ConnectionString = connectionString;
            SiteGroupId = Guid.NewGuid();
            SiteId = Guid.NewGuid();
            VendorSystemId = Guid.NewGuid();
            ProjectionId = Guid.NewGuid();
            ParkingSessionId = Guid.NewGuid();
            Ticket = $"PROJECTION-{Guid.NewGuid():N}";
        }

        public string ConnectionString { get; }
        public Guid SiteGroupId { get; }
        public Guid SiteId { get; }
        public Guid VendorSystemId { get; }
        public Guid ProjectionId { get; }
        public Guid ParkingSessionId { get; }
        public string Ticket { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            parsed.Database.Should().StartWith("exitpass_central_pms_it_");

            var fixture = new Fixture(connectionString);
            await fixture.SeedAsync();
            return fixture;
        }

        public OperatorConsoleSessionLookupReadRequest Request(Guid siteId, Guid? siteGroupId) =>
            new(null, Ticket, siteId, siteGroupId, "TICKET_REFERENCE");

        public async Task<long[]> CountBusinessRowsAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT
                    (SELECT COUNT(*) FROM core.parking_sessions WHERE ticket_number_masked = @ticket),
                    (SELECT COUNT(*) FROM core.tariff_snapshots WHERE parking_session_id = @parking_session_id),
                    (SELECT COUNT(*) FROM core.payment_attempts WHERE parking_session_id = @parking_session_id),
                    (SELECT COUNT(*)
                     FROM core.payment_confirmations confirmation
                     INNER JOIN core.payment_attempts attempt
                         ON attempt.payment_attempt_id = confirmation.payment_attempt_id
                     WHERE attempt.parking_session_id = @parking_session_id),
                    (SELECT COUNT(*) FROM core.exit_authorizations WHERE parking_session_id = @parking_session_id);
                """,
                connection);
            command.Parameters.AddWithValue("ticket", Ticket);
            command.Parameters.AddWithValue("parking_session_id", ParkingSessionId);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            return Enumerable.Range(0, 5).Select(reader.GetInt64).ToArray();
        }

        public async Task InsertCoreSessionAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO core.parking_sessions (
                    parking_session_id, site_group_id, site_id, vendor_system_id,
                    vendor_session_ref, plate_number_masked, ticket_number_masked,
                    entry_at, vendor_session_status, session_status, correlation_id,
                    created_by_service_identity_id, source_adapter_identity_id)
                VALUES (
                    @parking_session_id, @site_group_id, @site_id, @vendor_system_id,
                    @ticket, 'ABC****', @ticket,
                    now() - interval '1 hour', 'ACTIVE', 'ACTIVE', gen_random_uuid(),
                    @service_identity_id, @service_identity_id);
                """,
                connection);
            AddScopeParameters(command);
            command.Parameters.AddWithValue("parking_session_id", ParkingSessionId);
            command.Parameters.AddWithValue("ticket", Ticket);
            await command.ExecuteNonQueryAsync();
        }

        public async Task InsertSecondProjectionAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO sessions.vendor_session_projection_sync_targets (
                    projection_sync_target_id, site_id, site_group_id, vendor_system_id,
                    parking_lot_index_code, enabled_flag, health_status)
                VALUES (gen_random_uuid(), @site_id, @site_group_id, @vendor_system_id, 'LOT-2', true, 'HEALTHY');

                INSERT INTO sessions.vendor_session_projections (
                    vendor_session_projection_id, vendor_system_id, site_id, site_group_id,
                    source_adapter_identity_id, parking_lot_index_code, vendor_record_guid,
                    card_num, plate_license, enter_time, source_api, source_payload_hash,
                    source_event_at, stable_identity_type, stable_identity_key,
                    first_seen_at, last_seen_at, last_refreshed_at, projection_status,
                    correlation_id, created_by_service_identity_id)
                VALUES (
                    gen_random_uuid(), @vendor_system_id, @site_id, @site_group_id,
                    @service_identity_id, 'LOT-2', @second_record_guid,
                    @ticket, 'XYZ2042', now() - interval '30 minutes', 'integration-test', repeat('b', 64),
                    now() - interval '30 minutes', 'VENDOR_RECORD_GUID', @second_stable_key,
                    now(), now(), now(), 'ACTIVE', gen_random_uuid(), @service_identity_id);
                """,
                connection);
            AddScopeParameters(command);
            command.Parameters.AddWithValue("ticket", Ticket);
            command.Parameters.AddWithValue("second_record_guid", $"record-{Guid.NewGuid():N}");
            command.Parameters.AddWithValue("second_stable_key", $"stable-{Guid.NewGuid():N}");
            await command.ExecuteNonQueryAsync();
        }

        private async Task SeedAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO sites.site_groups (
                    site_group_id, site_group_code, site_group_name, timezone_name,
                    default_currency_code, site_group_status, effective_from)
                VALUES (@site_group_id, @site_group_code, 'Projection Test Group', 'Asia/Manila', 'PHP', 'ACTIVE', now());

                INSERT INTO sites.sites (
                    site_id, site_group_id, site_code, site_name, site_type,
                    timezone_name, country_code, site_status, effective_from)
                VALUES (@site_id, @site_group_id, @site_code, 'Projection Test Site', 'TERMINAL',
                    'Asia/Manila', 'PH', 'ACTIVE', now());

                INSERT INTO integration.vendor_systems (
                    vendor_system_id, vendor_code, vendor_name, vendor_system_type,
                    vendor_system_status, environment_code, effective_from)
                VALUES (@vendor_system_id, @vendor_code, 'Projection Test Vendor', 'VENDOR_PMS',
                    'ACTIVE', 'INTEGRATION_TEST', now());

                INSERT INTO sessions.vendor_session_projection_sync_targets (
                    projection_sync_target_id, site_id, site_group_id, vendor_system_id,
                    parking_lot_index_code, enabled_flag, health_status)
                VALUES (gen_random_uuid(), @site_id, @site_group_id, @vendor_system_id, 'LOT-1', true, 'HEALTHY');

                INSERT INTO sessions.vendor_session_projections (
                    vendor_session_projection_id, vendor_system_id, site_id, site_group_id,
                    source_adapter_identity_id, parking_lot_index_code, vendor_record_guid,
                    card_num, plate_license, enter_time, source_api, source_payload_hash,
                    source_event_at, stable_identity_type, stable_identity_key,
                    first_seen_at, last_seen_at, last_refreshed_at, projection_status,
                    correlation_id, created_by_service_identity_id)
                VALUES (
                    @projection_id, @vendor_system_id, @site_id, @site_group_id,
                    @service_identity_id, 'LOT-1', @record_guid,
                    @ticket, 'ABC1041', now() - interval '1 hour', 'integration-test', repeat('a', 64),
                    now() - interval '1 hour', 'VENDOR_RECORD_GUID', @stable_key,
                    now(), now(), now(), 'ACTIVE', gen_random_uuid(), @service_identity_id);
                """,
                connection);
            AddScopeParameters(command);
            command.Parameters.AddWithValue("site_group_code", $"SG-{SiteGroupId:N}");
            command.Parameters.AddWithValue("site_code", $"SITE-{SiteId:N}");
            command.Parameters.AddWithValue("vendor_code", $"VENDOR-{VendorSystemId:N}");
            command.Parameters.AddWithValue("projection_id", ProjectionId);
            command.Parameters.AddWithValue("record_guid", $"record-{ProjectionId:N}");
            command.Parameters.AddWithValue("stable_key", $"stable-{ProjectionId:N}");
            command.Parameters.AddWithValue("ticket", Ticket);
            await command.ExecuteNonQueryAsync();
        }

        private void AddScopeParameters(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("site_group_id", SiteGroupId);
            command.Parameters.AddWithValue("site_id", SiteId);
            command.Parameters.AddWithValue("vendor_system_id", VendorSystemId);
            command.Parameters.AddWithValue("service_identity_id", CentralPmsServiceIdentityId);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """
                DELETE FROM core.parking_sessions WHERE parking_session_id = @parking_session_id;
                DELETE FROM sessions.vendor_session_projections WHERE site_id = @site_id;
                DELETE FROM sessions.vendor_session_projection_sync_targets WHERE site_id = @site_id;
                DELETE FROM integration.vendor_systems WHERE vendor_system_id = @vendor_system_id;
                DELETE FROM sites.sites WHERE site_id = @site_id;
                DELETE FROM sites.site_groups WHERE site_group_id = @site_group_id;
                """,
                connection);
            AddScopeParameters(command);
            command.Parameters.AddWithValue("parking_session_id", ParkingSessionId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
