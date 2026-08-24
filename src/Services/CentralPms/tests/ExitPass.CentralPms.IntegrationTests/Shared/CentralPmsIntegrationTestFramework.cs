using System.Diagnostics;
using System.Reflection;
using Npgsql;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

public sealed class CentralPmsIntegrationTestFramework : XunitTestFramework
{
    public CentralPmsIntegrationTestFramework(IMessageSink messageSink)
        : base(messageSink)
    {
    }

    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new CentralPmsIntegrationTestFrameworkExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
}

internal sealed class CentralPmsIntegrationTestFrameworkExecutor : XunitTestFrameworkExecutor
{
    public CentralPmsIntegrationTestFrameworkExecutor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink)
        : base(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
    }

    protected override void RunTestCases(
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
    {
        using var assemblyRunner = new CentralPmsIntegrationTestAssemblyRunner(
            TestAssembly,
            testCases,
            DiagnosticMessageSink,
            executionMessageSink,
            executionOptions);

        assemblyRunner.RunAsync().GetAwaiter().GetResult();
    }
}

internal sealed class CentralPmsIntegrationTestAssemblyRunner : XunitTestAssemblyRunner
{
    private CentralPmsIntegrationPostgresResource? _postgres;
    private StatutoryDiscountCanonicalDatabaseFixture? _databaseFixture;

    public CentralPmsIntegrationTestAssemblyRunner(
        ITestAssembly testAssembly,
        IEnumerable<IXunitTestCase> testCases,
        IMessageSink diagnosticMessageSink,
        IMessageSink executionMessageSink,
        ITestFrameworkExecutionOptions executionOptions)
        : base(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
    {
    }

    protected override async Task AfterTestAssemblyStartingAsync()
    {
        await base.AfterTestAssemblyStartingAsync();
        await Aggregator.RunAsync(async () =>
        {
            _postgres = new CentralPmsIntegrationPostgresResource();
            await _postgres.StartAsync();
            _databaseFixture = new StatutoryDiscountCanonicalDatabaseFixture(suiteOwner: true);
            await _databaseFixture.InitializeAsync();
        });
    }

    protected override async Task BeforeTestAssemblyFinishedAsync()
    {
        if (_databaseFixture is not null)
        {
            await Aggregator.RunAsync(_databaseFixture.DisposeAsync);
        }

        if (_postgres is not null)
        {
            await Aggregator.RunAsync(_postgres.DisposeAsync);
        }

        await base.BeforeTestAssemblyFinishedAsync();
    }
}

internal sealed class CentralPmsIntegrationPostgresResource
{
    private const string Image = "postgres:16-alpine";
    private readonly Dictionary<string, string?> _previousEnvironment = new();
    private readonly string _runId = $"cpms-it-{Guid.NewGuid():N}";
    private readonly string _containerName = $"exitpass-central-pms-it-{Guid.NewGuid():N}";
    private readonly string _password = $"it-{Guid.NewGuid():N}-{Guid.NewGuid():N}";
    private string? _containerId;

    public async Task StartAsync()
    {
        var containerId = (await RunAsync(
            "docker",
            [
                "run", "--detach",
                "--name", _containerName,
                "--label", "exitpass.central-pms.integration-test=true",
                "--label", $"exitpass.central-pms.integration-run-id={_runId}",
                "--publish", "127.0.0.1::5432",
                "--env", "POSTGRES_USER=exitpass",
                "--env", $"POSTGRES_PASSWORD={_password}",
                "--env", "POSTGRES_DB=postgres",
                Image
            ],
            "start task-owned PostgreSQL 16 container")).StandardOutput.Trim();

        if (containerId.Length != 64)
        {
            throw new InvalidOperationException("Task-owned PostgreSQL startup did not return an immutable container ID.");
        }

        _containerId = containerId;
        var inspection = (await RunAsync(
            "docker",
            [
                "inspect", "--format",
                "{{.Id}}|{{index .Config.Labels \"exitpass.central-pms.integration-test\"}}|{{index .Config.Labels \"exitpass.central-pms.integration-run-id\"}}|{{(index (index .NetworkSettings.Ports \"5432/tcp\") 0).HostIp}}|{{(index (index .NetworkSettings.Ports \"5432/tcp\") 0).HostPort}}",
                _containerName
            ],
            "verify task-owned PostgreSQL identity")).StandardOutput.Trim().Split('|');

        if (inspection.Length != 5 ||
            !string.Equals(inspection[0], _containerId, StringComparison.Ordinal) ||
            !string.Equals(inspection[1], "true", StringComparison.Ordinal) ||
            !string.Equals(inspection[2], _runId, StringComparison.Ordinal) ||
            !string.Equals(inspection[3], "127.0.0.1", StringComparison.Ordinal) ||
            !int.TryParse(inspection[4], out var port))
        {
            throw new InvalidOperationException("Task-owned PostgreSQL ownership or loopback mapping verification failed.");
        }

        await WaitUntilReadyAsync(port);

        PublishEnvironment(
            StatutoryDiscountCanonicalDatabaseFixture.AdminConnectionStringEnvVar,
            new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Database = "postgres",
                Username = "exitpass",
                Password = _password,
                Pooling = false,
                Timeout = 15,
                CommandTimeout = 120
            }.ConnectionString);
        PublishEnvironment(StatutoryDiscountCanonicalDatabaseFixture.DatabasePrefixEnvVar, "exitpass_central_pms_it_");
        PublishEnvironment(StatutoryDiscountCanonicalDatabaseFixture.DockerContainerEnvVar, _containerName);
        PublishEnvironment(StatutoryDiscountCanonicalDatabaseFixture.DockerUserEnvVar, "exitpass");
        PublishEnvironment(StatutoryDiscountCanonicalDatabaseFixture.TaskOwnedContainerIdEnvVar, _containerId);
        PublishEnvironment(StatutoryDiscountCanonicalDatabaseFixture.TaskOwnedRunIdEnvVar, _runId);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (_containerId is not null)
            {
                var inspection = (await RunAsync(
                    "docker",
                    [
                        "inspect", "--format",
                        "{{.Id}}|{{index .Config.Labels \"exitpass.central-pms.integration-test\"}}|{{index .Config.Labels \"exitpass.central-pms.integration-run-id\"}}",
                        _containerName
                    ],
                    "verify task-owned PostgreSQL identity before cleanup")).StandardOutput.Trim().Split('|');
                if (inspection.Length != 3 ||
                    !string.Equals(inspection[0], _containerId, StringComparison.Ordinal) ||
                    !string.Equals(inspection[1], "true", StringComparison.Ordinal) ||
                    !string.Equals(inspection[2], _runId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Refusing PostgreSQL cleanup because immutable task ownership could not be proven.");
                }

                await RunAsync(
                    "docker",
                    ["rm", "--force", "--volumes", _containerId],
                    "remove verified task-owned PostgreSQL container and anonymous volume");
            }
        }
        finally
        {
            foreach (var pair in _previousEnvironment)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    private async Task WaitUntilReadyAsync(int port)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            var result = await RunAsync(
                "docker",
                ["exec", _containerName, "pg_isready", "-U", "exitpass", "-d", "postgres"],
                "wait for task-owned PostgreSQL readiness",
                throwOnFailure: false);
            if (result.ExitCode == 0)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(
                        new NpgsqlConnectionStringBuilder
                        {
                            Host = "127.0.0.1",
                            Port = port,
                            Database = "postgres",
                            Username = "exitpass",
                            Password = _password,
                            Pooling = false,
                            Timeout = 2,
                            CommandTimeout = 2
                        }.ConnectionString);
                    await connection.OpenAsync();
                    await using var command = new NpgsqlCommand("SELECT 1;", connection);
                    await command.ExecuteScalarAsync();
                    return;
                }
                catch (NpgsqlException)
                {
                    // Container-local readiness can precede the loopback-mapped
                    // authenticated channel by a few hundred milliseconds.
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        throw new InvalidOperationException("Task-owned PostgreSQL did not become ready within 60 seconds.");
    }

    private void PublishEnvironment(string name, string value)
    {
        _previousEnvironment[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string operation,
        bool throwOnFailure = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to {operation}.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
        if (result.ExitCode != 0 && throwOnFailure)
        {
            throw new InvalidOperationException(
                $"Unable to {operation}; process exited with code {result.ExitCode}. {Sanitize(result.StandardError)}");
        }

        return result;
    }

    private static string Sanitize(string value)
    {
        value = value.Trim();
        return value.Length <= 1000 ? value : value[..1000] + "...";
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
