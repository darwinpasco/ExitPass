using ExitPass.CentralPms.Application.OperatorConsole;
using ExitPass.CentralPms.IntegrationTests.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Integration tests for Operator Console access evaluation read-model repository composition and safe reads.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationReadRepositoryIntegrationTests
{
    /// <summary>
    /// Verifies the read repository is registered in the Central PMS API container.
    /// </summary>
    [Fact]
    public void Repository_CanBeResolvedFromCentralPmsServices()
    {
        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IOperatorConsoleAccessEvaluationReadRepository>();

        repository.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies random missing identifiers return an empty context instead of throwing not-found exceptions.
    /// </summary>
    [Fact]
    public async Task LoadAsync_WhenReadModelRowsAreMissing_ReturnsSafeEmptyContext()
    {
        if (!await CanOpenDatabaseAsync())
        {
            return;
        }

        using var factory = new CustomWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOperatorConsoleAccessEvaluationReadRepository>();
        var request = new OperatorConsoleAccessEvaluationReadRequest(
            Guid.Parse("43000000-0000-0000-0000-000000000001"),
            Guid.Parse("43000000-0000-0000-0000-000000000002"),
            Guid.Parse("43000000-0000-0000-0000-000000000003"),
            Guid.Parse("43000000-0000-0000-0000-000000000004"),
            Guid.Parse("43000000-0000-0000-0000-000000000005"),
            Guid.Parse("43000000-0000-0000-0000-000000000006"),
            "STATUTORY_VALIDATION",
            "STATUTORY_VALIDATION.APPROVE",
            "VIEW_EVIDENCE_FOR_DECISION",
            DateTimeOffset.Parse("2026-05-29T00:00:00Z"),
            Guid.Parse("43000000-0000-0000-0000-000000000007"));

        var context = await repository.LoadAsync(request, CancellationToken.None);

        context.Request.Should().Be(request);
        context.HrIdentityMapping.Should().BeNull();
        context.DeviceBinding.Should().BeNull();
        context.DeviceAssignment.Should().BeNull();
        context.ActiveShift.Should().BeNull();
        context.LatestShiftVersion.Should().BeNull();
        context.LatestShiftRevocation.Should().BeNull();
        context.ActiveShiftTakeover.Should().BeNull();
        context.StatutoryEntitlementFingerprint.Should().BeNull();
    }

    private static async Task<bool> CanOpenDatabaseAsync()
    {
        try
        {
            await using var connection = new NpgsqlConnection(
                CentralPmsIntegrationTestConfiguration.GetDatabaseConnectionString());
            await connection.OpenAsync();
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }
}
