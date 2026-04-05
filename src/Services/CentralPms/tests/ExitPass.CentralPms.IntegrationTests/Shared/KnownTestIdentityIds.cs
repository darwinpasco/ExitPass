using System;
using Microsoft.Extensions.Configuration;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Known seeded identity identifiers used by integration tests.
///
/// BRD:
/// - 9.21 Audit and Traceability
///
/// SDD:
/// - 9.7 identity.service_identities
///
/// Invariants Enforced:
/// - Integration tests must use actor identifiers that already exist in identity.service_identities
/// - Exit-authorization created_by and updated_by references must remain FK-valid
/// </summary>
internal static class KnownTestIdentityIds
{
    private static readonly Lazy<Guid> ServiceIdentityIdValue = new(LoadServiceIdentityId);

    /// <summary>
    /// Gets the seeded service identity identifier used by integration tests that require a valid actor reference.
    /// </summary>
    public static Guid ServiceIdentityId => ServiceIdentityIdValue.Value;

    /// <summary>
    /// Loads the seeded service identity identifier from the integration test configuration file.
    /// </summary>
    /// <returns>A valid seeded service identity identifier.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the configuration value is missing or is not a valid GUID.
    /// </exception>
    private static Guid LoadServiceIdentityId()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.Test.json", optional: false, reloadOnChange: false)
            .Build();

        var rawValue = configuration["KnownTestIdentityIds:ServiceIdentityId"];

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new InvalidOperationException(
                "KnownTestIdentityIds:ServiceIdentityId is missing from appsettings.Test.json.");
        }

        if (!Guid.TryParse(rawValue, out var serviceIdentityId))
        {
            throw new InvalidOperationException(
                "KnownTestIdentityIds:ServiceIdentityId in appsettings.Test.json is not a valid GUID.");
        }

        return serviceIdentityId;
    }
}
