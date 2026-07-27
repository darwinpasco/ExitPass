using ExitPass.CentralPms.IntegrationTests.Shared;
using Xunit;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Serializes Operator Console integration tests that rely on shared manual fixture seed data.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OperatorConsoleManualFixtureCollection : ICollectionFixture<StatutoryDiscountCanonicalDatabaseFixture>
{
    /// <summary>
    /// Shared collection name for Operator Console manual fixture tests.
    /// </summary>
    public const string Name = "OperatorConsoleManualFixture";
}
