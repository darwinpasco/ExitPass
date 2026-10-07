using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Infrastructure.ManagementPlatform;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Persistence;

public sealed class ManagementConfigurationRepositoryIntegrationTests
{
    private static readonly Guid PitxLevel3SiteId = Guid.Parse("2d1dcdf8-f563-537c-8542-0bde7cc9da97");

    [Fact]
    public async Task TariffAdministration_ListsPITXSeedAndEnforcesDraftLifecycle()
    {
        var connectionString = CentralPmsIntegrationTestConfiguration.RequireDatabaseConnectionString();
        var actorId = await CreateActorAsync(connectionString);
        var repository = new PostgresManagementConfigurationRepository(connectionString);

        var pitxTariffs = await repository.ListTariffsAsync(PitxLevel3SiteId, "CAR", CancellationToken.None);
        var pitx = pitxTariffs.Single(x => x.Version == "PITX-L3-CAR-V1");

        pitx.Status.Should().Be("ACTIVE");
        pitx.CurrencyCode.Should().Be("PHP");
        pitx.ParkingGracePeriodMinutes.Should().Be(15);
        pitx.Rules.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new
            {
                Sequence = 1,
                RuleScope = "DURATION",
                ChargeType = "UNIT_DURATION",
                DurationStartMinutes = (int?)0,
                DurationEndMinutes = (int?)null,
                AmountMinorUnits = 5000L,
                BillingUnitMinutes = (int?)60,
                RoundingRule = "WHOLE_STARTED_HOUR"
            },
            options => options.ExcludingMissingMembers());

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var draft = new ManagementTariff(
            null,
            PitxLevel3SiteId,
            null,
            "MOTORCYCLE",
            $"PITX-L3-MOTORCYCLE-{suffix}",
            "Integration lifecycle tariff",
            $"PITX-L3-MOTORCYCLE-{suffix}-V1",
            "PHP",
            15,
            null,
            null,
            5,
            DateTimeOffset.UtcNow.AddDays(1),
            null,
            "DRAFT",
            false,
            0,
            [new ManagementTariffRule(null, 1, "DURATION", "UNIT_DURATION", 0, null, null, null, 2500, 60, "WHOLE_STARTED_HOUR")]);

        var saved = await repository.SaveTariffDraftAsync(draft, actorId, CancellationToken.None);
        saved.Status.Should().Be("DRAFT");
        saved.Verified.Should().BeFalse();

        var verified = await repository.VerifyTariffAsync(saved.TariffId!.Value, saved.RowVersion, actorId, CancellationToken.None);
        verified.Verified.Should().BeTrue();

        var active = await repository.ActivateTariffAsync(verified.TariffId!.Value, verified.RowVersion, actorId, CancellationToken.None);
        active.Status.Should().Be("ACTIVE");

        var retired = await repository.RetireTariffAsync(active.TariffId!.Value, active.RowVersion, actorId, CancellationToken.None);
        retired.Status.Should().Be("RETIRED");
    }

    private static async Task<Guid> CreateActorAsync(string connectionString)
    {
        var actorId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.users (
              user_id, username, display_name, user_type, user_status, effective_from)
            VALUES (@id, @username, 'Configuration integration actor', 'INTERNAL_ADMIN', 'ACTIVE', now());
            """,
            connection);
        command.Parameters.AddWithValue("id", actorId);
        command.Parameters.AddWithValue("username", $"configuration-it-{actorId:N}");
        await command.ExecuteNonQueryAsync();
        return actorId;
    }
}
