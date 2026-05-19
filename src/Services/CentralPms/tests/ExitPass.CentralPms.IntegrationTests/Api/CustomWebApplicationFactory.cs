using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ExitPass.CentralPms.IntegrationTests.Shared;
using ExitPass.CentralPms.Api.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ExitPass.CentralPms.IntegrationTests.Api;

/// <summary>
/// Hosts the Central PMS API in-memory for API integration tests.
///
/// BRD:
/// - 9.16 Monitoring and Administration
///
/// SDD:
/// - 10 API Architecture
/// - 13 Deployment Architecture
///
/// Invariants Enforced:
/// - API integration tests exercise the real ASP.NET Core pipeline
/// - Test hosting must not bypass production endpoint wiring
/// - The API host must receive an explicit MainDatabase connection string before startup
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IReadOnlyDictionary<string, string?> _configurationOverrides;
    private readonly IInternalClientCertificateAccessor? _certificateAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomWebApplicationFactory"/> class.
    /// </summary>
    public CustomWebApplicationFactory()
        : this(
            new Dictionary<string, string?>(),
            certificateAccessor: null)
    {
    }

    private CustomWebApplicationFactory(
        IReadOnlyDictionary<string, string?> configurationOverrides,
        IInternalClientCertificateAccessor? certificateAccessor)
    {
        _configurationOverrides = configurationOverrides;
        _certificateAccessor = certificateAccessor;
    }

    /// <summary>
    /// Creates a factory configured for opt-in internal mTLS enforcement tests.
    /// </summary>
    /// <param name="trustedClientThumbprints">Trusted client certificate thumbprints.</param>
    /// <param name="certificateAccessor">Certificate accessor used by the in-memory test host.</param>
    /// <returns>A Central PMS API test factory with internal mTLS enabled.</returns>
    public CustomWebApplicationFactory WithInternalMtls(
        IEnumerable<string> trustedClientThumbprints,
        IInternalClientCertificateAccessor certificateAccessor)
    {
        ArgumentNullException.ThrowIfNull(trustedClientThumbprints);
        ArgumentNullException.ThrowIfNull(certificateAccessor);

        var overrides = new Dictionary<string, string?>
        {
            ["InternalSecurity:Mtls:Enabled"] = "true",
            ["InternalSecurity:Mtls:RequireClientCertificate"] = "true"
        };

        var index = 0;
        foreach (var thumbprint in trustedClientThumbprints)
        {
            overrides[$"InternalSecurity:Mtls:TrustedClientThumbprints:{index}"] = thumbprint;
            index++;
        }

        return new CustomWebApplicationFactory(overrides, certificateAccessor);
    }

    /// <summary>
    /// Configures the in-memory API host for integration testing.
    /// </summary>
    /// <param name="builder">Web host builder used to compose the test host.</param>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var integrationDbConnectionString =
            CentralPmsIntegrationTestConfiguration.PublishResolvedDatabaseConnectionString();

        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configBuilder) =>
        {
            configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MainDatabase"] = integrationDbConnectionString
            });

            configBuilder.AddInMemoryCollection(_configurationOverrides);
        });

        if (_certificateAccessor is not null)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInternalClientCertificateAccessor>();
                services.AddSingleton(_certificateAccessor);
            });
        }
    }
}
