<#
.SYNOPSIS
Runs the Operator Console statutory discount production policy readiness check.

.DESCRIPTION
This wrapper executes Verify-ProductionPolicyRegistryReadiness.sql as a read-only
inspection. It does not mutate database state, does not print the connection
string, and rolls back the read transaction after inspection.

By default, the script exits with code 2 when production readiness blockers are
detected. Use -WarnOnly for local development runs where a NOT READY result is
expected and should not fail the shell session.
#>

[CmdletBinding()]
param(
    [string] $ConnectionString = $env:EXITPASS_INTEGRATION_DB,
    [string] $SqlPath = (Join-Path $PSScriptRoot "Verify-ProductionPolicyRegistryReadiness.sql"),
    [switch] $WarnOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Connection string is required. Pass -ConnectionString or set EXITPASS_INTEGRATION_DB."
}

if (-not (Test-Path -LiteralPath $SqlPath)) {
    throw "SQL readiness script not found: $SqlPath"
}

$sqlText = Get-Content -LiteralPath $SqlPath -Raw
$withoutBlockComments = [regex]::Replace($sqlText, "/\*.*?\*/", "", [System.Text.RegularExpressions.RegexOptions]::Singleline)
$withoutLineComments = [regex]::Replace($withoutBlockComments, "--.*?$", "", [System.Text.RegularExpressions.RegexOptions]::Multiline)
$mutationPattern = "\b(INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|TRUNCATE|MERGE|GRANT|REVOKE|CALL|DO|EXECUTE)\b"

if ([regex]::IsMatch($withoutLineComments, $mutationPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
    throw "Refusing to run SQL because a prohibited mutation keyword was found after comments were stripped."
}

$runnerRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("exitpass-policy-readiness-runner-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $runnerRoot | Out-Null

try {
    dotnet new console --framework net8.0 --output $runnerRoot --force | Out-Null
    dotnet add $runnerRoot package Npgsql --version 10.0.2 | Out-Null

    $program = @'
using System.Data;
using Npgsql;

var sqlPath = args[0];
var connectionString = args[1];
var warnOnly = args.Length > 2 && string.Equals(args[2], "--warn-only", StringComparison.Ordinal);
var sql = await File.ReadAllTextAsync(sqlPath);

var classificationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var resultSet = 0;

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await using var command = new NpgsqlCommand(sql, connection, tx);
await using var reader = await command.ExecuteReaderAsync();

do
{
    resultSet++;
    var columnNames = Enumerable.Range(0, reader.FieldCount)
        .Select(reader.GetName)
        .ToArray();
    var classificationOrdinal = Array.FindIndex(
        columnNames,
        name => string.Equals(name, "readiness_classification", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"Result set {resultSet}: {string.Join(", ", columnNames)}");

    var rowCount = 0;
    while (await reader.ReadAsync())
    {
        rowCount++;
        if (classificationOrdinal >= 0 && !reader.IsDBNull(classificationOrdinal))
        {
            var classification = reader.GetString(classificationOrdinal);
            classificationCounts[classification] = classificationCounts.TryGetValue(classification, out var count)
                ? count + 1
                : 1;
        }
    }

    Console.WriteLine($"Rows: {rowCount}");
}
while (await reader.NextResultAsync());

await reader.DisposeAsync();
await tx.RollbackAsync();

var blockerClassifications = new[]
{
    "MISSING_REQUIRED_POLICY",
    "MISSING_SITE_MAPPING",
    "MISSING_EVIDENCE_RULE",
    "EXPIRED_OR_INACTIVE",
    "NOT_READY"
};

var warningClassifications = new[]
{
    "COMPATIBILITY_TABLE_ONLY",
    "CONFIGURED_BUT_UNVERIFIED",
    "SANDBOX_ONLY",
    "READY_WITH_MANUAL_REVIEW"
};

var hasBlocker = blockerClassifications.Any(classificationCounts.ContainsKey);
var hasWarning = warningClassifications.Any(classificationCounts.ContainsKey);

Console.WriteLine();
Console.WriteLine("Policy readiness classification counts:");
foreach (var item in classificationCounts.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"- {item.Key}: {item.Value}");
}

if (hasBlocker)
{
    Console.WriteLine();
    Console.WriteLine("FAIL: Production policy readiness blockers were detected.");
    Environment.ExitCode = warnOnly ? 0 : 2;
}
else if (hasWarning)
{
    Console.WriteLine();
    Console.WriteLine("WARN: Policy registry is not fully production-verified. Manual review or compensating controls are required.");
    Environment.ExitCode = 0;
}
else
{
    Console.WriteLine();
    Console.WriteLine("PASS: No production policy readiness blockers were detected by this read-only check.");
    Environment.ExitCode = 0;
}
'@

    Set-Content -LiteralPath (Join-Path $runnerRoot "Program.cs") -Value $program -Encoding UTF8

    Write-Host "Running Operator Console production policy readiness check."
    Write-Host "SQL: $SqlPath"
    Write-Host "Connection string: [redacted]"

    $arguments = @("run", "--project", $runnerRoot, "--", (Resolve-Path -LiteralPath $SqlPath).Path, $ConnectionString)
    if ($WarnOnly) {
        $arguments += "--warn-only"
    }

    & dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $runnerRoot) {
        Remove-Item -LiteralPath $runnerRoot -Recurse -Force
    }
}
