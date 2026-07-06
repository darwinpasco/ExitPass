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

            if (!await RetrySchedulePreparationTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, RetrySchedulePreparationTableSql);
            }

            if (!await RetryExecutionAttemptTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, RetryExecutionAttemptTableSql);
            }

            if (!await SemanticHashRecalculationPreviewTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, SemanticHashRecalculationPreviewTableSql);
            }

            if (!await SemanticHashBackfillMutationPreparationTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, SemanticHashBackfillMutationPreparationTableSql);
            }
            else
            {
                await ExecuteSqlAsync(connectionString, SemanticHashBackfillMutationPreparationUpgradeSql);
            }

            if (!await SemanticHashBackfillWorkflowRequestTableExistsAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, SemanticHashBackfillWorkflowRequestTableSql);
            }

            if (!await SemanticRequestHashColumnsExistAsync(connectionString))
            {
                await ExecuteSqlAsync(connectionString, SemanticRequestHashColumnsSql);
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

    private static async Task<bool> RetrySchedulePreparationTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_retry_schedule_preparations') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> SemanticHashRecalculationPreviewTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_semantic_hash_recalculation_previews') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> RetryExecutionAttemptTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_retry_execution_attempts') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> SemanticHashBackfillMutationPreparationTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_semantic_hash_backfill_mutation_preparations') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> SemanticHashBackfillWorkflowRequestTableExistsAsync(string connectionString)
    {
        const string sql = "SELECT to_regclass('core.fiscal_issuance_semantic_hash_backfill_workflow_requests') IS NOT NULL;";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result is true;
    }

    private static async Task<bool> SemanticRequestHashColumnsExistAsync(string connectionString)
    {
        const string sql = """
            SELECT COUNT(*) = 7
            FROM information_schema.columns
            WHERE table_schema = 'core'
              AND table_name = 'fiscal_issuance_references'
              AND column_name IN (
                  'semantic_request_hash_status',
                  'semantic_request_hash_value',
                  'semantic_request_hash_algorithm',
                  'semantic_request_hash_source_version',
                  'semantic_request_hash_source_fact_count',
                  'semantic_request_hash_safe_summary',
                  'semantic_request_hash_recorded_at'
              );
            """;

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

    private const string SemanticRequestHashColumnsSql = """
        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_status varchar(40);

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_value varchar(64);

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_algorithm varchar(32);

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_source_version varchar(80);

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_source_fact_count integer;

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_safe_summary varchar(240);

        ALTER TABLE core.fiscal_issuance_references
            ADD COLUMN IF NOT EXISTS semantic_request_hash_recorded_at timestamptz;
        """;

    private const string RetrySchedulePreparationTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_schedule_preparations (
            retry_schedule_preparation_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            retry_command_preparation_attempt_id uuid
                REFERENCES core.fiscal_issuance_retry_command_preparations(retry_command_preparation_attempt_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            payment_confirmation_id uuid,
            payment_attempt_id uuid,
            parking_session_id uuid,
            site_id uuid,
            site_pos_server_id uuid,
            site_pos_server_ref varchar(128),
            latest_readback_classification varchar(40),
            retry_eligibility_decision varchar(40) NOT NULL,
            semantic_request_hash_availability varchar(80) NOT NULL,
            idempotency_context_availability varchar(80) NOT NULL,
            scheduling_preparation_status varchar(40) NOT NULL,
            scheduling_block_reason_code varchar(160),
            requested_at timestamptz DEFAULT now() NOT NULL,
            earliest_eligible_at timestamptz,
            safe_summary varchar(240) NOT NULL,
            correlation_id uuid,
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_issuance_retry_schedule_preparations PRIMARY KEY (retry_schedule_preparation_attempt_id),
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__readback_classification CHECK (
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
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__eligibility_decision CHECK (
                retry_eligibility_decision IN ('NOT_EVALUATED', 'ELIGIBLE', 'BLOCKED', 'UNAVAILABLE', 'NOT_REQUIRED')
            ),
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__semantic_hash_status CHECK (
                semantic_request_hash_availability IN (
                    'NOT_AVAILABLE_IN_CURRENT_MODEL',
                    'AVAILABLE_AND_CONFIRMED',
                    'REQUIRED_BUT_MISSING',
                    'REQUIRED_BUT_UNCONFIRMED'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__idempotency_status CHECK (
                idempotency_context_availability IN (
                    'NOT_EVALUATED',
                    'AVAILABLE',
                    'MISSING_UPSTREAM_FINALITY_REFERENCE',
                    'NEW_UPSTREAM_FINALITY_REFERENCE_REJECTED'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__status CHECK (
                scheduling_preparation_status IN (
                    'NOT_PREPARED',
                    'DISABLED',
                    'SCHEDULED_PREPARED',
                    'BLOCKED',
                    'UNAVAILABLE'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_schedule_preparations__prepared_has_command_audit CHECK (
                scheduling_preparation_status <> 'SCHEDULED_PREPARED'
                OR retry_command_preparation_attempt_id IS NOT NULL
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_schedule_preparations__reference_requested
            ON core.fiscal_issuance_retry_schedule_preparations (fiscal_issuance_reference_id, requested_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_retry_schedule_preparations IS
            'Central PMS v1.3 FEQ retry scheduling preparation audit records only. No executable retry job, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
        """;

    private const string RetryExecutionAttemptTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_retry_execution_attempts (
            retry_execution_attempt_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            retry_command_preparation_attempt_id uuid
                REFERENCES core.fiscal_issuance_retry_command_preparations(retry_command_preparation_attempt_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            retry_schedule_preparation_attempt_id uuid
                REFERENCES core.fiscal_issuance_retry_schedule_preparations(retry_schedule_preparation_attempt_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            readback_classification_basis varchar(40),
            semantic_request_hash_value varchar(64),
            semantic_request_hash_algorithm varchar(32),
            semantic_request_hash_source_version varchar(80),
            upstream_finality_reference varchar(200),
            execution_status varchar(40) NOT NULL,
            block_reason_code varchar(160),
            pos_server_outcome varchar(40),
            pos_server_result_classification varchar(40),
            pos_server_fiscal_document_id uuid,
            fiscal_document_number varchar(80),
            fiscal_identity_id uuid,
            fiscal_sequence_policy_id uuid,
            fiscal_sequence_value bigint,
            fiscal_series varchar(40),
            fiscal_number_prefix_text varchar(40),
            fiscal_number_suffix_text varchar(40),
            fiscal_number_assigned_at timestamptz,
            fiscal_number_assigned_by_ref varchar(160),
            attempted_at timestamptz DEFAULT now() NOT NULL,
            completed_at timestamptz,
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            correlation_id uuid,
            safe_summary varchar(240) NOT NULL,
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_issuance_retry_execution_attempts PRIMARY KEY (retry_execution_attempt_id),
            CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__readback_classification CHECK (
                readback_classification_basis IS NULL
                OR readback_classification_basis IN (
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
            CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__status CHECK (
                execution_status IN (
                    'NOT_ATTEMPTED',
                    'DISABLED',
                    'DRY_RUN_READY',
                    'EXECUTED',
                    'REPLAY_MATCHED',
                    'CONFLICT',
                    'BLOCKED',
                    'UNAVAILABLE',
                    'UNKNOWN',
                    'FAILED'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__pos_outcome CHECK (
                pos_server_outcome IS NULL
                OR pos_server_outcome IN (
                    'ACCEPTED',
                    'CONFLICT',
                    'FAILED_REQUEST',
                    'FAILED_CONFIGURATION',
                    'FAILED_SERVICE',
                    'INVALID_RESPONSE'
                )
            ),
            CONSTRAINT ck_fiscal_issuance_retry_execution_attempts__result_classification CHECK (
                pos_server_result_classification IS NULL
                OR pos_server_result_classification IN ('NEWLY_CREATED', 'IDEMPOTENT_REPLAY')
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_issuance_retry_execution_attempts__reference_attempted
            ON core.fiscal_issuance_retry_execution_attempts (fiscal_issuance_reference_id, attempted_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_retry_execution_attempts IS
            'Central PMS v1.3 FEQ controlled retry execution attempt audit records. Single-record feature-flagged POST path only; no public endpoint, batch retry, scheduler job, ExitAuthorization, or gate behavior.';
        """;

    private const string SemanticHashRecalculationPreviewTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_recalculation_previews (
            semantic_hash_recalculation_preview_audit_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            stored_semantic_hash_source_version varchar(80),
            required_semantic_hash_source_version varchar(80) NOT NULL,
            stored_semantic_hash_value varchar(64),
            recalculation_preview_status varchar(40) NOT NULL,
            recalculation_block_reason_code varchar(160),
            complete_original_request_facts_available boolean DEFAULT false NOT NULL,
            recalculated_hash_value varchar(64),
            recalculated_hash_algorithm varchar(32),
            recalculated_hash_source_version varchar(80),
            recalculated_source_fact_count integer,
            safe_source_summary varchar(240),
            recalculated_hash_matches_stored boolean,
            mutation_status varchar(40) NOT NULL,
            attempted_at timestamptz DEFAULT now() NOT NULL,
            safe_summary varchar(240) NOT NULL,
            correlation_id uuid,
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_issuance_semantic_hash_recalculation_previews
                PRIMARY KEY (semantic_hash_recalculation_preview_audit_id),
            CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__status CHECK (
                recalculation_preview_status IN ('NOT_REQUIRED', 'PREVIEW_CALCULATED', 'BLOCKED', 'UNAVAILABLE')
            ),
            CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__mutation CHECK (
                mutation_status IN ('NOT_MUTATED')
            ),
            CONSTRAINT ck_fiscal_issuance_semantic_hash_recalculation_previews__calculated_has_hash CHECK (
                recalculation_preview_status <> 'PREVIEW_CALCULATED'
                OR (
                    complete_original_request_facts_available = true
                    AND recalculated_hash_value IS NOT NULL
                    AND recalculated_hash_algorithm IS NOT NULL
                    AND recalculated_hash_source_version IS NOT NULL
                    AND recalculated_source_fact_count IS NOT NULL
                    AND recalculated_source_fact_count > 0
                )
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_recalc_previews__reference_attempted
            ON core.fiscal_issuance_semantic_hash_recalculation_previews (fiscal_issuance_reference_id, attempted_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_semantic_hash_recalculation_previews IS
            'Central PMS v1.3 FEQ semantic hash recalculation preview audit records only. No hash backfill mutation, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
        """;

    private const string SemanticHashBackfillMutationPreparationTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (
            semantic_hash_backfill_mutation_audit_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            semantic_hash_recalculation_preview_audit_id uuid
                REFERENCES core.fiscal_issuance_semantic_hash_recalculation_previews(semantic_hash_recalculation_preview_audit_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            mutation_preparation_audit_id uuid
                REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            controlled_backfill_approval_status varchar(60) NOT NULL,
            old_semantic_hash_source_version varchar(80),
            required_semantic_hash_source_version varchar(80) NOT NULL,
            old_semantic_hash_value varchar(64),
            new_semantic_hash_value varchar(64),
            new_semantic_hash_algorithm varchar(32),
            new_semantic_hash_source_version varchar(80),
            new_semantic_hash_source_fact_count integer,
            safe_source_summary varchar(240),
            mutation_preparation_status varchar(60) NOT NULL,
            mutation_block_reason_code varchar(160),
            mutation_mode varchar(40) NOT NULL,
            mutation_enabled boolean DEFAULT false NOT NULL,
            fiscal_issuance_reference_mutated boolean DEFAULT false NOT NULL,
            attempted_at timestamptz DEFAULT now() NOT NULL,
            safe_summary varchar(240) NOT NULL,
            correlation_id uuid,
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            approval_reference varchar(160),
            dual_control_reference varchar(160),
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_issuance_semantic_hash_backfill_mutation_preparations
                PRIMARY KEY (semantic_hash_backfill_mutation_audit_id),
            CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__approval_status CHECK (
                controlled_backfill_approval_status IN (
                    'NOT_REQUIRED_CURRENT',
                    'READY_FOR_CONTROLLED_BACKFILL',
                    'BLOCKED',
                    'PENDING_DUAL_CONTROL',
                    'UNAVAILABLE'
                )
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__status CHECK (
                mutation_preparation_status IN (
                    'NOT_PREPARED',
                    'PREPARED_BUT_MUTATION_DISABLED',
                    'PREPARED_FOR_CONTROLLED_MUTATION',
                    'MUTATED',
                    'FAILED',
                    'STALE',
                    'DISABLED',
                    'BLOCKED',
                    'UNAVAILABLE'
                )
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__mode CHECK (
                mutation_mode IN ('SINGLE_RECORD_ONLY')
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__mutation_guard CHECK (
                fiscal_issuance_reference_mutated = false
                OR (
                    fiscal_issuance_reference_mutated = true
                    AND mutation_preparation_status = 'MUTATED'
                    AND mutation_enabled = true
                    AND mutation_preparation_audit_id IS NOT NULL
                )
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__prepared_has_hash CHECK (
                mutation_preparation_status NOT IN (
                    'PREPARED_BUT_MUTATION_DISABLED',
                    'PREPARED_FOR_CONTROLLED_MUTATION',
                    'MUTATED'
                )
                OR (
                    semantic_hash_recalculation_preview_audit_id IS NOT NULL
                    AND new_semantic_hash_value IS NOT NULL
                    AND new_semantic_hash_algorithm IS NOT NULL
                    AND new_semantic_hash_source_version IS NOT NULL
                    AND new_semantic_hash_source_fact_count IS NOT NULL
                    AND new_semantic_hash_source_fact_count > 0
                )
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_backfill_mutation__reference_attempted
            ON core.fiscal_issuance_semantic_hash_backfill_mutation_preparations (fiscal_issuance_reference_id, attempted_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations IS
            'Central PMS v1.3 FEQ semantic hash controlled single-record backfill mutation audit records. No automatic batch backfill, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
        """;

    private const string SemanticHashBackfillMutationPreparationUpgradeSql = """
        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            ADD COLUMN IF NOT EXISTS mutation_preparation_audit_id uuid;

        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            DROP CONSTRAINT IF EXISTS ck_fiscal_sem_hash_backfill_mutation__status;
        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            DROP CONSTRAINT IF EXISTS ck_fiscal_sem_hash_backfill_mutation__not_mutated;
        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            DROP CONSTRAINT IF EXISTS ck_fiscal_sem_hash_backfill_mutation__mutation_guard;
        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            DROP CONSTRAINT IF EXISTS ck_fiscal_sem_hash_backfill_mutation__prepared_has_hash;

        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            ADD CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__status CHECK (
                mutation_preparation_status IN (
                    'NOT_PREPARED',
                    'PREPARED_BUT_MUTATION_DISABLED',
                    'PREPARED_FOR_CONTROLLED_MUTATION',
                    'MUTATED',
                    'FAILED',
                    'STALE',
                    'DISABLED',
                    'BLOCKED',
                    'UNAVAILABLE'
                )
            );

        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            ADD CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__mutation_guard CHECK (
                fiscal_issuance_reference_mutated = false
                OR (
                    fiscal_issuance_reference_mutated = true
                    AND mutation_preparation_status = 'MUTATED'
                    AND mutation_enabled = true
                    AND mutation_preparation_audit_id IS NOT NULL
                )
            );

        ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
            ADD CONSTRAINT ck_fiscal_sem_hash_backfill_mutation__prepared_has_hash CHECK (
                mutation_preparation_status NOT IN (
                    'PREPARED_BUT_MUTATION_DISABLED',
                    'PREPARED_FOR_CONTROLLED_MUTATION',
                    'MUTATED'
                )
                OR (
                    semantic_hash_recalculation_preview_audit_id IS NOT NULL
                    AND new_semantic_hash_value IS NOT NULL
                    AND new_semantic_hash_algorithm IS NOT NULL
                    AND new_semantic_hash_source_version IS NOT NULL
                    AND new_semantic_hash_source_fact_count IS NOT NULL
                    AND new_semantic_hash_source_fact_count > 0
                )
            );

        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1
                FROM pg_constraint
                WHERE conname = 'fk_fiscal_sem_hash_backfill_mutation__prep_audit_id'
            ) THEN
                ALTER TABLE core.fiscal_issuance_semantic_hash_backfill_mutation_preparations
                    ADD CONSTRAINT fk_fiscal_sem_hash_backfill_mutation__prep_audit_id
                    FOREIGN KEY (mutation_preparation_audit_id)
                    REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
                    DEFERRABLE INITIALLY IMMEDIATE;
            END IF;
        END $$;
        """;

    private const string SemanticHashBackfillWorkflowRequestTableSql = """
        CREATE TABLE IF NOT EXISTS core.fiscal_issuance_semantic_hash_backfill_workflow_requests (
            semantic_hash_backfill_workflow_request_id uuid DEFAULT gen_random_uuid() NOT NULL,
            fiscal_issuance_reference_id uuid NOT NULL
                REFERENCES core.fiscal_issuance_references(fiscal_issuance_reference_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            semantic_hash_recalculation_preview_audit_id uuid
                REFERENCES core.fiscal_issuance_semantic_hash_recalculation_previews(semantic_hash_recalculation_preview_audit_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            mutation_preparation_audit_id uuid
                REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            approval_reference varchar(160),
            dual_control_reference varchar(160),
            actor_service_identity_id uuid
                REFERENCES identity.service_identities(service_identity_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            reason_code varchar(80),
            safe_justification varchar(240),
            request_mode varchar(40) NOT NULL,
            workflow_status varchar(80) NOT NULL,
            workflow_block_reason_code varchar(160),
            mutation_invocation_posture varchar(40) NOT NULL,
            guarded_mutation_audit_id uuid
                REFERENCES core.fiscal_issuance_semantic_hash_backfill_mutation_preparations(semantic_hash_backfill_mutation_audit_id)
                DEFERRABLE INITIALLY IMMEDIATE,
            guarded_mutation_status varchar(60),
            execute_controlled_mutation_requested boolean DEFAULT false NOT NULL,
            mutation_invocation_enabled boolean DEFAULT false NOT NULL,
            dry_run_only boolean DEFAULT true NOT NULL,
            requested_at timestamptz DEFAULT now() NOT NULL,
            correlation_id uuid,
            safe_summary varchar(240) NOT NULL,
            created_at timestamptz DEFAULT now() NOT NULL,
            CONSTRAINT pk_fiscal_sem_hash_backfill_workflow_requests
                PRIMARY KEY (semantic_hash_backfill_workflow_request_id),
            CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__request_mode CHECK (
                request_mode IN ('SINGLE_RECORD_ONLY', 'BATCH')
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__status CHECK (
                workflow_status IN (
                    'NOT_REQUESTED',
                    'READY_FOR_OPERATOR_APPROVAL',
                    'PREPARED_BUT_MUTATION_INVOCATION_DISABLED',
                    'MUTATION_INVOKED',
                    'BLOCKED',
                    'UNAVAILABLE'
                )
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__invocation_posture CHECK (
                mutation_invocation_posture IN (
                    'NOT_REQUESTED',
                    'DRY_RUN_ONLY',
                    'DISABLED',
                    'INVOKED',
                    'BLOCKED'
                )
            ),
            CONSTRAINT ck_fiscal_sem_hash_backfill_workflow__guarded_status CHECK (
                guarded_mutation_status IS NULL
                OR guarded_mutation_status IN (
                    'NOT_PREPARED',
                    'PREPARED_BUT_MUTATION_DISABLED',
                    'PREPARED_FOR_CONTROLLED_MUTATION',
                    'MUTATED',
                    'FAILED',
                    'STALE',
                    'DISABLED',
                    'BLOCKED',
                    'UNAVAILABLE'
                )
            )
        );

        CREATE INDEX IF NOT EXISTS ix_fiscal_sem_hash_backfill_workflow__reference_requested
            ON core.fiscal_issuance_semantic_hash_backfill_workflow_requests (fiscal_issuance_reference_id, requested_at DESC);

        COMMENT ON TABLE core.fiscal_issuance_semantic_hash_backfill_workflow_requests IS
            'Central PMS v1.3 FEQ semantic hash internal operator workflow request audit records. Single-record governed request posture only; no public UI, batch backfill, retry execution, endpoint, POS Server POST, or ExitAuthorization gating behavior.';
        """;
}
