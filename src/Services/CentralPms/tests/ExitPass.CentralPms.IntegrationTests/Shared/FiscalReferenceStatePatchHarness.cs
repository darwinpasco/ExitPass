using Npgsql;

namespace ExitPass.CentralPms.IntegrationTests.Shared;

/// <summary>
/// Applies and validates the Central PMS fiscal reference state persistence patch in the disposable DB harness.
/// </summary>
public static class FiscalReferenceStatePatchHarness
{
    private static readonly SemaphoreSlim PatchLock = new(1, 1);

    public static async Task EnsureAppliedAndValidatedAsync(string connectionString)
    {
        await PatchLock.WaitAsync();
        try
        {
            if (!await FiscalReferenceTableExistsAsync(connectionString))
            {
                await ExecuteSqlFileAsync(
                    connectionString,
                    ResolveRepoPath("infra", "db", "patches", "ExitPass_CentralPms_FiscalReferenceStatePersistence_v1.3.sql"));
            }
            else if (!await RetryCommandPreparationTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, RetryCommandPreparationTableSql);
            }

            await ExecuteSqlFileAsync(
                connectionString,
                ResolveRepoPath("infra", "db", "patches", "validation", "Validate_CentralPmsFiscalReferenceStatePersistence_v1.3.sql"));
        }
        finally
        {
            PatchLock.Release();
        }
    }

    private static async Task<bool> FiscalReferenceTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_references') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> RetryCommandPreparationTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_retry_command_preparations') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task ExecuteSqlFileAsync(string connectionString, string path)
    {
        var sql = await File.ReadAllTextAsync(path);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };

        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 60
        };

        await command.ExecuteNonQueryAsync();
    }

    private static string ResolveRepoPath(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not resolve repository path: {Path.Combine(segments)}");
    }

    private const string RetryCommandPreparationTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_command_preparations (
            retry_command_preparation_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            payment_confirmation_id uuid,
            payment_attempt_id uuid,
            parking_session_id uuid,
            site_id uuid,
            site_pos_server_id uuid,
            site_pos_server_ref varchar(128),
            latest_readback_classification varchar(40),
            retry_eligibility_decision varchar(40) NOT NULL,
            command_preparation_status varchar(40) NOT NULL,
            command_block_reason_code varchar(160),
            semantic_request_hash_availability varchar(80) NOT NULL,
            idempotency_context_availability varchar(80) NOT NULL,
            attempted_at timestamptz DEFAULT now() NOT NULL,
            safe_summary varchar(240) NOT NULL,
            correlation_id uuid,
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_issuance_retry_command_preparations PRIMARY KEY (retry_command_preparation_attempt_id),
            CONSTRAINT ck_fiscal_issuance_retry_command_preparations__readback_classification CHECK (
                latest_readback_classification IS NULL
                OR latest_readback_classification IN (
                    'MATCHED',
                    'NOT_FOUND',
                    'MISMATCH',
                    'FAILED',
                    'UNAVAILABLE',
                    'UNKNOWN',
                    'IDENTIFIER_MISSING',
                    'NOT_SUPPORTED_YET'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_command_preparations__eligibility_decision CHECK (
                retry_eligibility_decision IN ('NOT_EVALUATED', 'ELIGIBLE', 'BLOCKED', 'UNAVAILABLE', 'NOT_REQUIRED')
            ),
            CONSTRAINT ck_fiscal_issuance_retry_command_preparations__preparation_status CHECK (
                command_preparation_status IN ('NOT_PREPARED', 'PREPARED_NON_EXECUTABLE', 'BLOCKED', 'UNAVAILABLE')
            ),
            CONSTRAINT ck_fiscal_issuance_retry_command_preparations__semantic_hash_status CHECK (
                semantic_request_hash_availability IN (
                    'NOT_AVAILABLE_IN_CURRENT_MODEL',
                    'AVAILABLE_AND_CONFIRMED',
                    'REQUIRED_BUT_MISSING',
                    'REQUIRED_BUT_UNCONFIRMED'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_command_preparations__idempotency_status CHECK (
                idempotency_context_availability IN (
                    'NOT_EVALUATED',
                    'AVAILABLE',
                    'MISSING_UPSTREAM_FINALITY_REFERENCE',
                    'NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED'
                )
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_command_preparations__reference_attempted
            ON core.fiscal_issuance_retry_command_preparations (fiscal_issuance_reference_id, attempted_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_retry_command_preparations IS
            'Central PMS v1.3 FEQ retry command preparation audit records only. No retry execution, scheduler, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
        """;
}
