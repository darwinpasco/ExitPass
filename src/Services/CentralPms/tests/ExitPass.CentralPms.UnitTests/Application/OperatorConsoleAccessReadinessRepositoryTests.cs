using FluentAssertions;
using Xunit;

namespace ExitPass.CentralPms.UnitTests.Application;

/// <summary>
/// Source-inspection tests for the Operator Console access readiness repository.
/// </summary>
public sealed class OperatorConsoleAccessReadinessRepositoryTests
{
    /// <summary>Verifies repository SQL is parameterized and uses the operator-console readiness tables.</summary>
    [Fact]
    public void RepositorySource_UsesOperatorConsoleTablesAndParameterizedQueries()
    {
        var source = ReadSource(
            "src",
            "Services",
            "CentralPms",
            "src",
            "ExitPass.CentralPms.Infrastructure",
            "OperatorConsole",
            "OperatorConsoleAccessReadinessRepository.cs");

        source.Should().Contain("information_schema.tables");
        source.Should().Contain("operator_console.hr_identity_mappings");
        source.Should().Contain("operator_console.operator_device_bindings");
        source.Should().Contain("operator_console.operator_device_assignment_history");
        source.Should().Contain("operator_console.operator_shifts");
        source.Should().Contain("@operator_user_id");
        source.Should().Contain("@operator_device_binding_id");
        source.Should().Contain("@operator_shift_id");
        source.Should().Contain("@evaluated_at");
        source.Should().Contain("NpgsqlDbType.Uuid");
        source.Should().NotContain("string.Format(");
        source.Should().NotContain("$\"SELECT");
    }

    private static string ReadSource(params string[] pathParts)
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
