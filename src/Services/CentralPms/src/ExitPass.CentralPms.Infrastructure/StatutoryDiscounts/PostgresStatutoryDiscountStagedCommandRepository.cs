using System.Data;
using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

/// <summary>
/// PostgreSQL repository for internal staged statutory-discount decision and payable-basis application commands.
/// </summary>
public sealed class PostgresStatutoryDiscountStagedCommandRepository : IStatutoryDiscountStagedCommandRepository
{
    private readonly string _connectionString;

    public PostgresStatutoryDiscountStagedCommandRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public Task<T> ExecuteWithDecisionLockAsync<T>(
        StatutoryDiscountDecisionV2RepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteWithLockAsync(command.IdempotencyScope, operation, cancellationToken);
    }

    public async Task<StatutoryDiscountDecisionV2BeginResult> BeginDecisionAsync(
        StatutoryDiscountDecisionV2RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await ReadDecisionByIdempotencyAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            existing ??= await ReadDecisionByBusinessIdentityAsync(connection, transaction, command.BusinessIdentity, forUpdate: true, cancellationToken)
                .ConfigureAwait(false);
            existing ??= await ReadDecisionByRequestReferenceAsync(connection, transaction, command.Command.RequestReference, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new StatutoryDiscountDecisionV2BeginResult(
                    Existing: true,
                    SemanticConflict: !IsSameSemanticRequest(existing.SemanticHashSourceVersion, existing.SemanticRequestHash, command),
                    RecoverableWithOriginalKey: IsProcessing(existing.CommandStatus)
                        && string.Equals(existing.IdempotencyKey, command.Command.IdempotencyKey, StringComparison.Ordinal),
                    existing);
            }

            var created = await InsertDecisionAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StatutoryDiscountDecisionV2BeginResult(
                Existing: false,
                SemanticConflict: false,
                RecoverableWithOriginalKey: false,
                created);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<StatutoryDiscountDecisionV2Record?> GetDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadDecisionByIdAsync(connection, null, statutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record?> GetDecisionByBusinessIdentityAsync(
        string businessIdentity,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(businessIdentity))
        {
            throw new ArgumentException("Decision business identity is required.", nameof(businessIdentity));
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadDecisionByBusinessIdentityAsync(connection, null, businessIdentity, forUpdate: false, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountDecisionV2Record> UpdateDecisionAsync(
        StatutoryDiscountDecisionV2Record record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE discounts.statutory_discount_decision_commands
               SET command_status = @command_status,
                   decision_result_status = @decision_result_status,
                   result_classification = CASE
                       WHEN @result_classification IN ('ACCEPTED', 'IDEMPOTENT_REPLAY', 'AWAITING_REVIEW') THEN @result_classification
                       ELSE result_classification
                   END,
                   retryable = @retryable,
                   recovery_classification = @recovery_classification,
                   error_code = @safe_error_code,
                   statutory_discount_validation_id = @statutory_discount_validation_id,
                   original_tariff_snapshot_id = @original_tariff_snapshot_id,
                   applied_policy_reference_id = @applied_policy_reference_id,
                   fallback_policy_reference_id = @fallback_policy_reference_id,
                   policy_resolution_basis = @policy_resolution_basis,
                   local_ordinance_applied = @local_ordinance_applied,
                   gross_amount_minor_units = @gross_amount_minor_units,
                   vat_exclusive_amount_minor_units = @vat_exclusive_amount_minor_units,
                   vat_amount_minor_units = @vat_amount_minor_units,
                   statutory_discount_amount_minor_units = @statutory_discount_amount_minor_units,
                   net_payable_amount_minor_units = @net_payable_amount_minor_units,
                   currency_code = @currency_code,
                   evidence_required = @evidence_required,
                   evidence_recorded = @evidence_recorded,
                   reason_code = @reason_code,
                   original_correlation_id = @correlation_id,
                   processing_started_at = @processing_started_at,
                   decided_at = @decided_at,
                   completed_at = @completed_at,
                   failed_at = @failed_at,
                   updated_at = now()
             WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id
               AND (
                   @command_status <> 'COMPLETED'
                   OR command_status <> 'COMPLETED'
                   OR decision_result_status = @decision_result_status
               )
             RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddDecisionUpdateParameters(dbCommand, record);
        await using (var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false))
        {
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return ReadDecision(reader);
            }
        }

        var existing = await ReadDecisionByIdAsync(connection, null, record.StatutoryDiscountDecisionCommandId, cancellationToken)
            .ConfigureAwait(false);
        return existing
            ?? throw NotFound("STATUTORY_DISCOUNT_DECISION_NOT_FOUND", "Statutory discount decision command was not found.");
    }

    public Task<T> ExecuteWithApplicationLockAsync<T>(
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteWithLockAsync(command.IdempotencyScope, operation, cancellationToken);
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1BeginResult> BeginApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var existing = await ReadApplicationByIdempotencyAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            existing ??= await ReadApplicationByBusinessIdentityAsync(connection, transaction, command.BusinessIdentity, cancellationToken)
                .ConfigureAwait(false);
            existing ??= await ReadApplicationByRequestReferenceAsync(connection, transaction, command.Command.RequestReference, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new StatutoryDiscountPayableBasisApplicationV1BeginResult(
                    Existing: true,
                    SemanticConflict: !IsSameSemanticRequest(existing.SemanticHashSourceVersion, existing.SemanticRequestHash, command),
                    RecoverableWithOriginalKey: IsProcessing(existing.CommandStatus)
                        && string.Equals(existing.IdempotencyKey, command.Command.IdempotencyKey, StringComparison.Ordinal),
                    existing);
            }

            var created = await InsertApplicationAsync(connection, transaction, command, cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new StatutoryDiscountPayableBasisApplicationV1BeginResult(
                Existing: false,
                SemanticConflict: false,
                RecoverableWithOriginalKey: false,
                created);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationAsync(
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await ReadApplicationByIdAsync(connection, null, statutoryDiscountPayableBasisApplicationCommandId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record?> GetApplicationByDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadApplication(reader) : null;
    }

    public async Task<StatutoryDiscountPayableBasisApplicationV1Record> UpdateApplicationAsync(
        StatutoryDiscountPayableBasisApplicationV1Record record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE discounts.statutory_discount_payable_basis_application_commands
               SET command_status = @command_status,
                   result_classification = @result_classification,
                   retryable = @retryable,
                   recovery_classification = @recovery_classification,
                   safe_error_code = @safe_error_code,
                   statutory_discount_payable_basis_application_id = @statutory_discount_payable_basis_application_id,
                   applied_tariff_snapshot_id = @applied_tariff_snapshot_id,
                   original_correlation_id = @correlation_id,
                   processing_started_at = @processing_started_at,
                   applied_at = @applied_at,
                   completed_at = @completed_at,
                   failed_at = @failed_at,
                   updated_at = now()
             WHERE statutory_discount_payable_basis_application_command_id = @statutory_discount_payable_basis_application_command_id
             RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        AddApplicationUpdateParameters(dbCommand, record);
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw NotFound(
                "STATUTORY_DISCOUNT_PAYABLE_BASIS_APPLICATION_NOT_FOUND",
                "Statutory discount payable-basis application command was not found.");
        }

        return ReadApplication(reader);
    }

    private async Task<T> ExecuteWithLockAsync<T>(
        string lockKey,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await AcquireSessionLockAsync(connection, lockKey, cancellationToken).ConfigureAwait(false);

        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await ReleaseSessionLockAsync(connection, lockKey, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task AcquireSessionLockAsync(
        NpgsqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_lock(hashtextextended(@lock_key, 0));";
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("lock_key", NpgsqlDbType.Text).Value = lockKey;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReleaseSessionLockAsync(
        NpgsqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_unlock(hashtextextended(@lock_key, 0));";
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        command.Parameters.Add("lock_key", NpgsqlDbType.Text).Value = lockKey;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StatutoryDiscountDecisionV2Record?> ReadDecisionByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE statutory_discount_decision_command_id = @statutory_discount_decision_command_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountDecisionCommandId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadDecision(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionV2Record?> ReadDecisionByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountDecisionV2RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE idempotency_scope = @idempotency_scope
              AND idempotency_key = @idempotency_key
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadDecision(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionV2Record?> ReadDecisionByBusinessIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string businessIdentity,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE business_identity = @business_identity
               OR idempotency_scope = @business_identity
            """;
        if (forUpdate)
        {
            sql += " FOR UPDATE";
        }

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("business_identity", NpgsqlDbType.Varchar).Value = businessIdentity;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadDecision(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionV2Record?> ReadDecisionByRequestReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid requestReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_decision_commands
            WHERE request_reference = @request_reference
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = requestReference;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadDecision(reader) : null;
    }

    private static async Task<StatutoryDiscountDecisionV2Record> InsertDecisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountDecisionV2RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_decision_commands (
                request_reference,
                parking_session_id,
                source_channel,
                entitlement_type,
                business_identity,
                idempotency_scope,
                idempotency_key,
                semantic_request_hash,
                semantic_hash_source_version,
                command_status,
                decision_status,
                decision_result_status,
                result_classification,
                retryable,
                recovery_classification,
                original_tariff_snapshot_id,
                applied_policy_reference_id,
                fallback_policy_reference_id,
                policy_resolution_basis,
                local_ordinance_applied,
                gross_amount_minor_units,
                vat_exclusive_amount_minor_units,
                vat_amount_minor_units,
                statutory_discount_amount_minor_units,
                net_payable_amount_minor_units,
                currency_code,
                evidence_required,
                evidence_recorded,
                reason_code,
                error_code,
                original_correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @request_reference,
                @parking_session_id,
                @source_channel,
                @entitlement_type,
                @business_identity,
                @idempotency_scope,
                @idempotency_key,
                @semantic_request_hash,
                @semantic_hash_source_version,
                @command_status,
                @decision_status,
                @decision_result_status,
                @result_classification,
                @retryable,
                @recovery_classification,
                @original_tariff_snapshot_id,
                @applied_policy_reference_id,
                @fallback_policy_reference_id,
                @policy_resolution_basis,
                @local_ordinance_applied,
                @gross_amount_minor_units,
                @vat_exclusive_amount_minor_units,
                @vat_amount_minor_units,
                @statutory_discount_amount_minor_units,
                @net_payable_amount_minor_units,
                @currency_code,
                @evidence_required,
                @evidence_recorded,
                @reason_code,
                @safe_error_code,
                @correlation_id,
                @now,
                @now
            )
            RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddDecisionInsertParameters(dbCommand, command);
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Statutory discount decision-v2 command insert returned no row.");
        }

        return ReadDecision(reader);
    }

    private static async Task<StatutoryDiscountPayableBasisApplicationV1Record?> ReadApplicationByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid statutoryDiscountPayableBasisApplicationCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE statutory_discount_payable_basis_application_command_id = @statutory_discount_payable_basis_application_command_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        command.Parameters.Add("statutory_discount_payable_basis_application_command_id", NpgsqlDbType.Uuid).Value =
            statutoryDiscountPayableBasisApplicationCommandId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadApplication(reader) : null;
    }

    private static async Task<StatutoryDiscountPayableBasisApplicationV1Record?> ReadApplicationByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE idempotency_scope = @idempotency_scope
              AND idempotency_key = @idempotency_key
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadApplication(reader) : null;
    }

    private static async Task<StatutoryDiscountPayableBasisApplicationV1Record?> ReadApplicationByBusinessIdentityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string businessIdentity,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE business_identity = @business_identity
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("business_identity", NpgsqlDbType.Varchar).Value = businessIdentity;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadApplication(reader) : null;
    }

    private static async Task<StatutoryDiscountPayableBasisApplicationV1Record?> ReadApplicationByRequestReferenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid requestReference,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM discounts.statutory_discount_payable_basis_application_commands
            WHERE request_reference = @request_reference
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = requestReference;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadApplication(reader) : null;
    }

    private static async Task<StatutoryDiscountPayableBasisApplicationV1Record> InsertApplicationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discounts.statutory_discount_payable_basis_application_commands (
                request_reference,
                statutory_discount_decision_command_id,
                parking_session_id,
                site_id,
                entitlement_type,
                business_identity,
                idempotency_scope,
                idempotency_key,
                semantic_request_hash,
                semantic_hash_source_version,
                command_status,
                result_classification,
                retryable,
                recovery_classification,
                statutory_discount_validation_id,
                original_tariff_snapshot_id,
                target_tariff_snapshot_id,
                applied_tariff_snapshot_id,
                applied_policy_reference_id,
                policy_resolution_basis,
                approved_discount_amount_minor_units,
                approved_vat_exclusive_amount_minor_units,
                approved_vat_amount_minor_units,
                approved_final_payable_amount_minor_units,
                currency_code,
                source_channel,
                original_correlation_id,
                created_at,
                updated_at
            )
            VALUES (
                @request_reference,
                @statutory_discount_decision_command_id,
                @parking_session_id,
                @site_id,
                @entitlement_type,
                @business_identity,
                @idempotency_scope,
                @idempotency_key,
                @semantic_request_hash,
                @semantic_hash_source_version,
                @command_status,
                @result_classification,
                @retryable,
                @recovery_classification,
                @statutory_discount_validation_id,
                @original_tariff_snapshot_id,
                @target_tariff_snapshot_id,
                @applied_tariff_snapshot_id,
                @applied_policy_reference_id,
                @policy_resolution_basis,
                @approved_discount_amount_minor_units,
                @approved_vat_exclusive_amount_minor_units,
                @approved_vat_amount_minor_units,
                @approved_final_payable_amount_minor_units,
                @currency_code,
                @source_channel,
                @correlation_id,
                @now,
                @now
            )
            RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddApplicationInsertParameters(dbCommand, command);
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken)
            .ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Statutory discount payable-basis application command insert returned no row.");
        }

        return ReadApplication(reader);
    }

    private static bool IsSameSemanticRequest(
        string existingSourceVersion,
        string existingHash,
        StatutoryDiscountDecisionV2RepositoryCommand command) =>
        string.Equals(existingSourceVersion, command.SemanticHashSourceVersion, StringComparison.Ordinal)
        && string.Equals(existingHash, command.SemanticRequestHash, StringComparison.Ordinal);

    private static bool IsSameSemanticRequest(
        string existingSourceVersion,
        string existingHash,
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command) =>
        string.Equals(existingSourceVersion, command.SemanticHashSourceVersion, StringComparison.Ordinal)
        && string.Equals(existingHash, command.SemanticRequestHash, StringComparison.Ordinal);

    private static bool IsProcessing(string status) =>
        status is StatutoryDiscountDecisionV2CommandStates.Received
            or StatutoryDiscountDecisionV2CommandStates.Processing
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Received
            or StatutoryDiscountPayableBasisApplicationV1CommandStates.Processing;

    private static void AddDecisionInsertParameters(
        NpgsqlCommand dbCommand,
        StatutoryDiscountDecisionV2RepositoryCommand command)
    {
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = command.Command.RequestReference;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        dbCommand.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = command.Command.SourceChannel;
        dbCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value =
            Normalize(command.Command.EntitlementType);
        dbCommand.Parameters.Add("business_identity", NpgsqlDbType.Varchar).Value = command.BusinessIdentity;
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        dbCommand.Parameters.Add("semantic_request_hash", NpgsqlDbType.Varchar).Value = command.SemanticRequestHash;
        dbCommand.Parameters.Add("semantic_hash_source_version", NpgsqlDbType.Varchar).Value = command.SemanticHashSourceVersion;
        dbCommand.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = StatutoryDiscountDecisionV2CommandStates.Received;
        dbCommand.Parameters.Add("decision_status", NpgsqlDbType.Varchar).Value = "PROCESSING";
        dbCommand.Parameters.Add("decision_result_status", NpgsqlDbType.Varchar).Value = StatutoryDiscountDecisionV2ResultStates.NotDecided;
        dbCommand.Parameters.Add("result_classification", NpgsqlDbType.Varchar).Value = "ACCEPTED";
        dbCommand.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = false;
        dbCommand.Parameters.Add("recovery_classification", NpgsqlDbType.Varchar).Value = StatutoryDiscountDecisionRecoveryClassifications.None;
        AddNullable(dbCommand, "original_tariff_snapshot_id", NpgsqlDbType.Uuid, command.Command.OriginalTariffSnapshotId);
        AddNullable(dbCommand, "applied_policy_reference_id", NpgsqlDbType.Uuid, command.Command.AppliedPolicyReferenceId);
        AddNullable(dbCommand, "fallback_policy_reference_id", NpgsqlDbType.Uuid, command.Command.FallbackPolicyReferenceId);
        AddNullable(dbCommand, "policy_resolution_basis", NpgsqlDbType.Varchar, NormalizeOptional(command.Command.PolicyResolutionBasis));
        dbCommand.Parameters.Add("local_ordinance_applied", NpgsqlDbType.Boolean).Value = command.Command.LocalOrdinanceApplied;
        AddNullable(dbCommand, "gross_amount_minor_units", NpgsqlDbType.Bigint, command.Command.OriginalTariffFacts?.GrossAmountMinorUnits);
        AddNullable(dbCommand, "vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint, command.Command.OriginalTariffFacts?.VatExclusiveAmountMinorUnits);
        AddNullable(dbCommand, "vat_amount_minor_units", NpgsqlDbType.Bigint, command.Command.OriginalTariffFacts?.VatAmountMinorUnits);
        AddNullable(dbCommand, "statutory_discount_amount_minor_units", NpgsqlDbType.Bigint, command.Command.OriginalTariffFacts?.StatutoryDiscountAmountMinorUnits);
        AddNullable(dbCommand, "net_payable_amount_minor_units", NpgsqlDbType.Bigint, command.Command.OriginalTariffFacts?.NetPayableAmountMinorUnits);
        AddNullable(dbCommand, "currency_code", NpgsqlDbType.Char, NormalizeOptional(command.Command.OriginalTariffFacts?.Currency));
        dbCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = command.Command.EvidenceReferences.Count > 0;
        dbCommand.Parameters.Add("evidence_recorded", NpgsqlDbType.Boolean).Value = command.Command.EvidenceReferences.Count > 0;
        AddNullable(dbCommand, "reason_code", NpgsqlDbType.Varchar, NormalizeOptional(command.Command.Decision.DecisionReasonCode));
        AddNullable(dbCommand, "safe_error_code", NpgsqlDbType.Varchar, NormalizeOptional(command.Command.Decision.SafeErrorCode));
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.Command.CorrelationId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;
    }

    private static void AddDecisionUpdateParameters(NpgsqlCommand dbCommand, StatutoryDiscountDecisionV2Record record)
    {
        dbCommand.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            record.StatutoryDiscountDecisionCommandId;
        dbCommand.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = record.CommandStatus;
        dbCommand.Parameters.Add("decision_result_status", NpgsqlDbType.Varchar).Value = record.DecisionResultStatus;
        dbCommand.Parameters.Add("result_classification", NpgsqlDbType.Varchar).Value = record.ResultClassification;
        dbCommand.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = record.Retryable;
        dbCommand.Parameters.Add("recovery_classification", NpgsqlDbType.Varchar).Value = record.RecoveryClassification;
        AddNullable(dbCommand, "safe_error_code", NpgsqlDbType.Varchar, record.SafeErrorCode);
        AddNullable(dbCommand, "statutory_discount_validation_id", NpgsqlDbType.Uuid, record.StatutoryDiscountValidationId);
        AddNullable(dbCommand, "original_tariff_snapshot_id", NpgsqlDbType.Uuid, record.OriginalTariffSnapshotId);
        AddNullable(dbCommand, "applied_policy_reference_id", NpgsqlDbType.Uuid, record.AppliedPolicyReferenceId);
        AddNullable(dbCommand, "fallback_policy_reference_id", NpgsqlDbType.Uuid, record.FallbackPolicyReferenceId);
        AddNullable(dbCommand, "policy_resolution_basis", NpgsqlDbType.Varchar, record.PolicyResolutionBasis);
        dbCommand.Parameters.Add("local_ordinance_applied", NpgsqlDbType.Boolean).Value = record.LocalOrdinanceApplied;
        AddNullable(dbCommand, "gross_amount_minor_units", NpgsqlDbType.Bigint, record.GrossAmountMinorUnits);
        AddNullable(dbCommand, "vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint, record.VatExclusiveAmountMinorUnits);
        AddNullable(dbCommand, "vat_amount_minor_units", NpgsqlDbType.Bigint, record.VatAmountMinorUnits);
        AddNullable(dbCommand, "statutory_discount_amount_minor_units", NpgsqlDbType.Bigint, record.StatutoryDiscountAmountMinorUnits);
        AddNullable(dbCommand, "net_payable_amount_minor_units", NpgsqlDbType.Bigint, record.NetPayableAmountMinorUnits);
        AddNullable(dbCommand, "currency_code", NpgsqlDbType.Char, record.Currency);
        dbCommand.Parameters.Add("evidence_required", NpgsqlDbType.Boolean).Value = record.EvidenceRequired;
        dbCommand.Parameters.Add("evidence_recorded", NpgsqlDbType.Boolean).Value = record.EvidenceRecorded;
        AddNullable(dbCommand, "reason_code", NpgsqlDbType.Varchar, record.ReasonCode);
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = record.CorrelationId;
        AddNullable(dbCommand, "processing_started_at", NpgsqlDbType.TimestampTz, record.ProcessingStartedAt);
        AddNullable(dbCommand, "decided_at", NpgsqlDbType.TimestampTz, record.DecidedAt);
        AddNullable(dbCommand, "completed_at", NpgsqlDbType.TimestampTz, record.CompletedAt);
        AddNullable(dbCommand, "failed_at", NpgsqlDbType.TimestampTz, record.FailedAt);
    }

    private static void AddApplicationInsertParameters(
        NpgsqlCommand dbCommand,
        StatutoryDiscountPayableBasisApplicationV1RepositoryCommand command)
    {
        dbCommand.Parameters.Add("request_reference", NpgsqlDbType.Uuid).Value = command.Command.RequestReference;
        dbCommand.Parameters.Add("statutory_discount_decision_command_id", NpgsqlDbType.Uuid).Value =
            command.Command.StatutoryDiscountDecisionCommandId;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        AddNullable(dbCommand, "site_id", NpgsqlDbType.Uuid, command.Command.SiteId);
        dbCommand.Parameters.Add("entitlement_type", NpgsqlDbType.Varchar).Value =
            Normalize(command.Command.EntitlementType);
        dbCommand.Parameters.Add("business_identity", NpgsqlDbType.Varchar).Value = command.BusinessIdentity;
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        dbCommand.Parameters.Add("semantic_request_hash", NpgsqlDbType.Varchar).Value = command.SemanticRequestHash;
        dbCommand.Parameters.Add("semantic_hash_source_version", NpgsqlDbType.Varchar).Value = command.SemanticHashSourceVersion;
        dbCommand.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = StatutoryDiscountPayableBasisApplicationV1CommandStates.Received;
        dbCommand.Parameters.Add("result_classification", NpgsqlDbType.Varchar).Value = StatutoryDiscountPayableBasisApplicationV1ResultClassifications.InProgress;
        dbCommand.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = false;
        dbCommand.Parameters.Add("recovery_classification", NpgsqlDbType.Varchar).Value = StatutoryDiscountDecisionRecoveryClassifications.None;
        AddNullable(dbCommand, "statutory_discount_validation_id", NpgsqlDbType.Uuid, command.Command.StatutoryDiscountValidationId);
        AddNullable(dbCommand, "original_tariff_snapshot_id", NpgsqlDbType.Uuid, command.Command.OriginalTariffSnapshotId);
        AddNullable(dbCommand, "target_tariff_snapshot_id", NpgsqlDbType.Uuid, command.Command.TargetTariffSnapshotId);
        AddNullable(dbCommand, "applied_tariff_snapshot_id", NpgsqlDbType.Uuid, command.Command.AppliedTariffSnapshotId);
        AddNullable(dbCommand, "applied_policy_reference_id", NpgsqlDbType.Uuid, command.Command.AppliedPolicyReferenceId);
        AddNullable(dbCommand, "policy_resolution_basis", NpgsqlDbType.Varchar, NormalizeOptional(command.Command.PolicyResolutionBasis));
        dbCommand.Parameters.Add("approved_discount_amount_minor_units", NpgsqlDbType.Bigint).Value =
            command.Command.ApprovedDiscountAmountMinorUnits;
        AddNullable(dbCommand, "approved_vat_exclusive_amount_minor_units", NpgsqlDbType.Bigint, command.Command.ApprovedVatExclusiveAmountMinorUnits);
        AddNullable(dbCommand, "approved_vat_amount_minor_units", NpgsqlDbType.Bigint, command.Command.ApprovedVatAmountMinorUnits);
        dbCommand.Parameters.Add("approved_final_payable_amount_minor_units", NpgsqlDbType.Bigint).Value =
            command.Command.ApprovedFinalPayableAmountMinorUnits;
        dbCommand.Parameters.Add("currency_code", NpgsqlDbType.Char).Value = NormalizeOptional(command.Command.Currency) ?? string.Empty;
        dbCommand.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = command.Command.SourceChannel;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.Command.CorrelationId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;
    }

    private static void AddApplicationUpdateParameters(
        NpgsqlCommand dbCommand,
        StatutoryDiscountPayableBasisApplicationV1Record record)
    {
        dbCommand.Parameters.Add("statutory_discount_payable_basis_application_command_id", NpgsqlDbType.Uuid).Value =
            record.StatutoryDiscountPayableBasisApplicationCommandId;
        dbCommand.Parameters.Add("command_status", NpgsqlDbType.Varchar).Value = record.CommandStatus;
        dbCommand.Parameters.Add("result_classification", NpgsqlDbType.Varchar).Value = record.ResultClassification;
        dbCommand.Parameters.Add("retryable", NpgsqlDbType.Boolean).Value = record.Retryable;
        dbCommand.Parameters.Add("recovery_classification", NpgsqlDbType.Varchar).Value = record.RecoveryClassification;
        AddNullable(dbCommand, "safe_error_code", NpgsqlDbType.Varchar, record.SafeErrorCode);
        AddNullable(
            dbCommand,
            "statutory_discount_payable_basis_application_id",
            NpgsqlDbType.Uuid,
            record.StatutoryDiscountPayableBasisApplicationId);
        AddNullable(dbCommand, "applied_tariff_snapshot_id", NpgsqlDbType.Uuid, record.AppliedTariffSnapshotId);
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = record.CorrelationId;
        AddNullable(dbCommand, "processing_started_at", NpgsqlDbType.TimestampTz, record.ProcessingStartedAt);
        AddNullable(dbCommand, "applied_at", NpgsqlDbType.TimestampTz, record.AppliedAt);
        AddNullable(dbCommand, "completed_at", NpgsqlDbType.TimestampTz, record.CompletedAt);
        AddNullable(dbCommand, "failed_at", NpgsqlDbType.TimestampTz, record.FailedAt);
    }

    private static StatutoryDiscountDecisionV2Record ReadDecision(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("request_reference")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("source_channel")),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("business_identity")),
            reader.GetString(reader.GetOrdinal("idempotency_scope")),
            reader.GetString(reader.GetOrdinal("idempotency_key")),
            reader.GetString(reader.GetOrdinal("semantic_hash_source_version")),
            reader.GetString(reader.GetOrdinal("semantic_request_hash")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetString(reader.GetOrdinal("decision_result_status")),
            reader.GetString(reader.GetOrdinal("result_classification")),
            reader.GetBoolean(reader.GetOrdinal("retryable")),
            reader.GetString(reader.GetOrdinal("recovery_classification")),
            GetNullableString(reader, "error_code"),
            GetNullableGuid(reader, "statutory_discount_validation_id"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_policy_reference_id"),
            GetNullableGuid(reader, "fallback_policy_reference_id"),
            GetNullableString(reader, "policy_resolution_basis"),
            reader.GetBoolean(reader.GetOrdinal("local_ordinance_applied")),
            GetNullableInt64(reader, "gross_amount_minor_units"),
            GetNullableInt64(reader, "vat_exclusive_amount_minor_units"),
            GetNullableInt64(reader, "vat_amount_minor_units"),
            GetNullableInt64(reader, "statutory_discount_amount_minor_units"),
            GetNullableInt64(reader, "net_payable_amount_minor_units"),
            GetNullableString(reader, "currency_code")?.Trim(),
            reader.GetBoolean(reader.GetOrdinal("evidence_required")),
            reader.GetBoolean(reader.GetOrdinal("evidence_recorded")),
            GetNullableString(reader, "reason_code"),
            reader.GetGuid(reader.GetOrdinal("original_correlation_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            GetNullableDateTimeOffset(reader, "processing_started_at"),
            GetNullableDateTimeOffset(reader, "decided_at"),
            GetNullableDateTimeOffset(reader, "completed_at"),
            GetNullableDateTimeOffset(reader, "failed_at"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));

    private static StatutoryDiscountPayableBasisApplicationV1Record ReadApplication(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(reader.GetOrdinal("statutory_discount_payable_basis_application_command_id")),
            reader.GetGuid(reader.GetOrdinal("request_reference")),
            reader.GetGuid(reader.GetOrdinal("statutory_discount_decision_command_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetString(reader.GetOrdinal("entitlement_type")),
            reader.GetString(reader.GetOrdinal("business_identity")),
            reader.GetString(reader.GetOrdinal("idempotency_scope")),
            reader.GetString(reader.GetOrdinal("idempotency_key")),
            reader.GetString(reader.GetOrdinal("semantic_hash_source_version")),
            reader.GetString(reader.GetOrdinal("semantic_request_hash")),
            reader.GetString(reader.GetOrdinal("command_status")),
            reader.GetString(reader.GetOrdinal("result_classification")),
            reader.GetBoolean(reader.GetOrdinal("retryable")),
            reader.GetString(reader.GetOrdinal("recovery_classification")),
            GetNullableString(reader, "safe_error_code"),
            GetNullableGuid(reader, "statutory_discount_validation_id"),
            GetNullableGuid(reader, "statutory_discount_payable_basis_application_id"),
            GetNullableGuid(reader, "original_tariff_snapshot_id"),
            GetNullableGuid(reader, "target_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_tariff_snapshot_id"),
            GetNullableGuid(reader, "applied_policy_reference_id"),
            GetNullableString(reader, "policy_resolution_basis"),
            reader.GetInt64(reader.GetOrdinal("approved_discount_amount_minor_units")),
            GetNullableInt64(reader, "approved_vat_exclusive_amount_minor_units"),
            GetNullableInt64(reader, "approved_vat_amount_minor_units"),
            reader.GetInt64(reader.GetOrdinal("approved_final_payable_amount_minor_units")),
            reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
            reader.GetString(reader.GetOrdinal("source_channel")),
            reader.GetGuid(reader.GetOrdinal("original_correlation_id")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            GetNullableDateTimeOffset(reader, "processing_started_at"),
            GetNullableDateTimeOffset(reader, "applied_at"),
            GetNullableDateTimeOffset(reader, "completed_at"),
            GetNullableDateTimeOffset(reader, "failed_at"),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("updated_at")));

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();

    private static string? GetNullableString(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    }

    private static long? GetNullableInt64(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private static StatutoryDiscountDecisionRejectedException NotFound(string errorCode, string message) =>
        new(errorCode, message, isNotFound: true);
}
