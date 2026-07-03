using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.FiscalIssuance;

public sealed class FiscalIssuanceReferenceRepositorySourceTests
{
    [Fact]
    public void RepositorySource_BindsNullableParametersWithExplicitNpgsqlTypes()
    {
        var source = ReadRepoFile(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "FiscalIssuance",
            "PostgresFiscalIssuanceReferenceRepository.cs");

        source.Should().Contain("using NpgsqlTypes;");
        source.Should().Contain("TryGetNpgsqlDbType");
        source.Should().Contain("NpgsqlDbType.Uuid");
        source.Should().Contain("NpgsqlDbType.Text");
        source.Should().Contain("NpgsqlDbType.TimestampTz");
        source.Should().Contain("NpgsqlDbType.Bigint");
    }

    private static string ReadRepoFile(params string[] pathParts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidateParts = new[] { current.FullName }.Concat(pathParts).ToArray();
            var candidate = Path.Combine(candidateParts);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(pathParts)} was not found from the test output path.");
    }
}
