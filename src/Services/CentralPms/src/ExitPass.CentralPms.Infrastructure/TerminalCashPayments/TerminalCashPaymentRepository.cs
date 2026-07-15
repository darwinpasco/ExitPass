using System.Data;
using System.Text.Json;
using ExitPass.CentralPms.Application.TerminalCashPayments;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.TerminalCashPayments;

/// <summary>
/// PostgreSQL repository for terminal cash-payment command, confirmation, and readback state.
/// </summary>
public sealed class TerminalCashPaymentRepository : ITerminalCashPaymentRepository
{
    private const string CashRailCode = "CASH";
    private const string CanonicalPaymentStatusConfirmed = "CONFIRMED";
    private const string FiscalStatusNotStarted = "NOT_STARTED_IN_THIS_SLICE";

    private readonly string _connectionString;

    /// <summary>
    /// Creates a terminal cash payment repository.
    /// </summary>
    public TerminalCashPaymentRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<TerminalCashPaymentResult> CreateOrReadAsync(
        TerminalCashPaymentRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var committed = false;
        try
        {
            await AcquireIdempotencyLockAsync(connection, transaction, command, cancellationToken);

            var existingByIdempotency = await ReadByIdempotencyAsync(connection, transaction, command, cancellationToken);
            if (existingByIdempotency is not null)
            {
                var conflict = await EnsureSemanticMatchAndAuditAsync(
                    connection,
                    transaction,
                    command,
                    existingByIdempotency,
                    conflictCode: "IDEMPOTENCY_SEMANTIC_CONFLICT",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                committed = true;
                if (conflict is not null)
                {
                    throw conflict;
                }

                return ToReplayResult(existingByIdempotency);
            }

            var existingByTender = await ReadByTenderAsync(
                connection,
                transaction,
                command.Command.TerminalCashTenderId,
                cancellationToken);
            if (existingByTender is not null)
            {
                var conflict = await EnsureSemanticMatchAndAuditAsync(
                    connection,
                    transaction,
                    command,
                    existingByTender,
                    conflictCode: "DUPLICATE_CASH_TENDER",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                committed = true;
                if (conflict is not null)
                {
                    throw conflict;
                }

                return ToReplayResult(existingByTender);
            }

            await EnsureNoExistingFinalPaymentAsync(connection, transaction, command.Command.ParkingSessionId, cancellationToken);
            var payable = await ReadAndValidatePayableBasisAsync(connection, transaction, command, cancellationToken);

            var paymentRailId = await ReadCashPaymentRailIdAsync(connection, transaction, cancellationToken);
            var serviceIdentityId = await ReadServiceIdentityIdAsync(connection, transaction, cancellationToken);
            var paymentAttemptId = await InsertPaymentAttemptAsync(
                connection,
                transaction,
                command,
                payable,
                paymentRailId,
                serviceIdentityId,
                cancellationToken);
            var paymentConfirmationId = await InsertPaymentConfirmationAsync(
                connection,
                transaction,
                command,
                payable,
                paymentRailId,
                serviceIdentityId,
                paymentAttemptId,
                cancellationToken);

            await MarkTariffConsumedAsync(connection, transaction, command, serviceIdentityId, cancellationToken);
            var created = await InsertTerminalCashCommandAsync(
                connection,
                transaction,
                command,
                paymentAttemptId,
                paymentConfirmationId,
                cancellationToken);

            await InsertAuditAsync(
                connection,
                transaction,
                created.TerminalCashPaymentCommandId,
                command.Command.TerminalCashTenderId,
                "ACCEPTED",
                null,
                command.Command.CorrelationId,
                command.RequestedAt,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return ToCreatedResult(created);
        }
        catch
        {
            if (!committed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private static async Task AcquireIdempotencyLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("lock_key", NpgsqlDbType.Text).Value = command.IdempotencyScope;
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TerminalCashPaymentReadback?> GetByTerminalCashTenderIdAsync(
        Guid terminalCashTenderId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                terminal_cash_payment_command_id,
                terminal_cash_tender_id,
                cash_custody_session_id,
                parking_session_id,
                tariff_snapshot_id,
                terminal_id,
                site_id,
                site_group_id,
                pos_server_id,
                cashier_id,
                cashier_shift_id,
                currency_code,
                amount_due_minor_units,
                amount_tendered_minor_units,
                change_due_minor_units,
                canonical_payment_status,
                payment_confirmation_id,
                result_classification,
                idempotency_scope,
                semantic_hash_source_version,
                created_at,
                confirmed_at,
                last_updated_at,
                original_correlation_id,
                fiscal_status
            FROM core.terminal_cash_payment_commands
            WHERE terminal_cash_tender_id = @terminal_cash_tender_id
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("terminal_cash_tender_id", NpgsqlDbType.Uuid).Value = terminalCashTenderId;

        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReadback(reader) : null;
    }

    private static async Task<TerminalCashPaymentRecord?> ReadByIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM core.terminal_cash_payment_commands
            WHERE idempotency_scope = @idempotency_scope
              AND idempotency_key = @idempotency_key
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task<TerminalCashPaymentRecord?> ReadByTenderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid terminalCashTenderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT *
            FROM core.terminal_cash_payment_commands
            WHERE terminal_cash_tender_id = @terminal_cash_tender_id
            FOR UPDATE;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("terminal_cash_tender_id", NpgsqlDbType.Uuid).Value = terminalCashTenderId;
        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static async Task<TerminalCashPaymentRejectedException?> EnsureSemanticMatchAndAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        TerminalCashPaymentRecord existing,
        string conflictCode,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existing.SemanticRequestHash, command.SemanticRequestHash, StringComparison.Ordinal))
        {
            await InsertAuditAsync(
                connection,
                transaction,
                existing.TerminalCashPaymentCommandId,
                command.Command.TerminalCashTenderId,
                "SEMANTIC_CONFLICT",
                conflictCode,
                command.Command.CorrelationId,
                command.RequestedAt,
                cancellationToken);

            return new TerminalCashPaymentRejectedException(
                conflictCode,
                "Terminal cash payment replay does not match the original semantic request.");
        }

        await InsertAuditAsync(
            connection,
            transaction,
            existing.TerminalCashPaymentCommandId,
            command.Command.TerminalCashTenderId,
            "IDEMPOTENT_REPLAY",
            null,
            command.Command.CorrelationId,
            command.RequestedAt,
            cancellationToken);
        return null;
    }

    private static async Task<PayableBasis> ReadAndValidatePayableBasisAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                ps.site_id,
                ps.site_group_id,
                ts.parking_session_id,
                ts.currency_code::text,
                ts.net_amount,
                ts.snapshot_status::text,
                ts.expires_at,
                ts.consumed_at
            FROM core.parking_sessions ps
            INNER JOIN core.tariff_snapshots ts
                ON ts.parking_session_id = ps.parking_session_id
            WHERE ps.parking_session_id = @parking_session_id
              AND ts.tariff_snapshot_id = @tariff_snapshot_id
            FOR UPDATE OF ts;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        dbCommand.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = command.Command.TariffSnapshotId;

        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new TerminalCashPaymentRejectedException(
                "INVALID_SESSION_TARIFF_RELATIONSHIP",
                "Tariff snapshot was not found for the supplied parking session.");
        }

        var siteId = reader.GetGuid(reader.GetOrdinal("site_id"));
        var siteGroupId = reader.GetGuid(reader.GetOrdinal("site_group_id"));
        if (siteId != command.Command.SiteId || siteGroupId != command.Command.SiteGroupId)
        {
            throw new TerminalCashPaymentRejectedException(
                "INVALID_SESSION_TARIFF_RELATIONSHIP",
                "Submitted site or site group does not match the parking session.");
        }

        var status = reader.GetString(reader.GetOrdinal("snapshot_status"));
        var expiresAt = reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at"));
        if (!string.Equals(status, "ACTIVE", StringComparison.Ordinal) ||
            expiresAt <= command.RequestedAt ||
            !reader.IsDBNull(reader.GetOrdinal("consumed_at")))
        {
            throw new TerminalCashPaymentRejectedException("STALE_TARIFF", "Tariff snapshot is stale or expired.");
        }

        var currency = reader.GetString(reader.GetOrdinal("currency_code")).Trim();
        var netAmount = reader.GetDecimal(reader.GetOrdinal("net_amount"));
        if (!string.Equals(currency, command.Command.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new TerminalCashPaymentRejectedException(
                "PAYABLE_BASIS_MISMATCH",
                "Cash command currency does not match the accepted payable basis.");
        }

        var requestedAmount = command.Command.AmountDueMinorUnits / 100m;
        if (netAmount != requestedAmount)
        {
            throw new TerminalCashPaymentRejectedException(
                "PAYABLE_BASIS_MISMATCH",
                "Cash command amount due does not match the accepted payable basis.");
        }

        return new PayableBasis(currency, netAmount);
    }

    private static async Task EnsureNoExistingFinalPaymentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT pc.payment_confirmation_id
            FROM core.payment_confirmations pc
            INNER JOIN core.payment_attempts pa
                ON pa.payment_attempt_id = pc.payment_attempt_id
            WHERE pa.parking_session_id = @parking_session_id
            LIMIT 1;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = parkingSessionId;
        var existing = await dbCommand.ExecuteScalarAsync(cancellationToken);
        if (existing is not null)
        {
            throw new TerminalCashPaymentRejectedException(
                "PAYMENT_ALREADY_FINAL",
                "Parking session already has a canonical payment confirmation.");
        }
    }

    private static async Task<Guid> ReadCashPaymentRailIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT payment_rail_id
            FROM payments.payment_rails
            WHERE rail_code = @rail_code
              AND rail_status = 'ACTIVE'
              AND supported_currency_code = 'PHP';
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("rail_code", NpgsqlDbType.Varchar).Value = CashRailCode;
        var value = await dbCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? id
            : throw new TerminalCashPaymentRejectedException("CASH_PAYMENT_RAIL_NOT_CONFIGURED", "Active CASH payment rail is not configured.");
    }

    private static async Task<Guid> ReadServiceIdentityIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT service_identity_id
            FROM identity.service_identities
            WHERE identity_status = 'ACTIVE'
            ORDER BY created_at DESC
            LIMIT 1;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        var value = await dbCommand.ExecuteScalarAsync(cancellationToken);
        return value is Guid id
            ? id
            : throw new TerminalCashPaymentRejectedException("SERVICE_IDENTITY_NOT_FOUND", "Active Central PMS service identity was not found.");
    }

    private static async Task<Guid> InsertPaymentAttemptAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        PayableBasis payable,
        Guid paymentRailId,
        Guid serviceIdentityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.payment_attempts (
                payment_attempt_id,
                parking_session_id,
                tariff_snapshot_id,
                idempotency_key,
                payment_rail_id,
                currency_code,
                amount,
                attempt_status,
                requested_at,
                expires_at,
                finalized_at,
                failure_reason_code,
                correlation_id,
                created_at,
                created_by_service_identity_id,
                updated_at,
                updated_by_service_identity_id,
                row_version
            )
            VALUES (
                gen_random_uuid(),
                @parking_session_id,
                @tariff_snapshot_id,
                @idempotency_key,
                @payment_rail_id,
                @currency_code,
                @amount,
                'CONFIRMED',
                @now,
                @now + INTERVAL '15 minutes',
                @now,
                NULL,
                @correlation_id,
                @now,
                @service_identity_id,
                @now,
                @service_identity_id,
                1
            )
            RETURNING payment_attempt_id;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddCommonParameters(dbCommand, command, payable, paymentRailId, serviceIdentityId);
        return (Guid)(await dbCommand.ExecuteScalarAsync(cancellationToken) ??
            throw new InvalidOperationException("Payment attempt insert returned no id."));
    }

    private static async Task<Guid> InsertPaymentConfirmationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        PayableBasis payable,
        Guid paymentRailId,
        Guid serviceIdentityId,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.payment_confirmations (
                payment_confirmation_id,
                payment_attempt_id,
                provider_outcome_id,
                payment_rail_id,
                provider_transaction_ref,
                currency_code,
                confirmed_amount,
                confirmation_status,
                verified_at,
                confirmed_at,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            VALUES (
                gen_random_uuid(),
                @payment_attempt_id,
                NULL,
                @payment_rail_id,
                @provider_transaction_ref,
                @currency_code,
                @amount,
                'RECORDED',
                @now,
                @now,
                @correlation_id,
                @now,
                @service_identity_id
            )
            RETURNING payment_confirmation_id;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddCommonParameters(dbCommand, command, payable, paymentRailId, serviceIdentityId);
        dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;
        dbCommand.Parameters.Add("provider_transaction_ref", NpgsqlDbType.Varchar).Value =
            $"CASH:{command.Command.TerminalCashTenderId:N}";
        return (Guid)(await dbCommand.ExecuteScalarAsync(cancellationToken) ??
            throw new InvalidOperationException("Payment confirmation insert returned no id."));
    }

    private static async Task MarkTariffConsumedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        Guid serviceIdentityId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE core.tariff_snapshots
            SET
                consumed_at = @now,
                snapshot_status = 'CONSUMED',
                updated_at = @now,
                updated_by_service_identity_id = @service_identity_id,
                row_version = row_version + 1
            WHERE tariff_snapshot_id = @tariff_snapshot_id;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = command.Command.TariffSnapshotId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;
        dbCommand.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = serviceIdentityId;
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TerminalCashPaymentRecord> InsertTerminalCashCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TerminalCashPaymentRepositoryCommand command,
        Guid paymentAttemptId,
        Guid paymentConfirmationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.terminal_cash_payment_commands (
                terminal_cash_tender_id,
                cash_custody_session_id,
                parking_session_id,
                tariff_snapshot_id,
                cashier_id,
                cashier_session_reference,
                cashier_shift_id,
                terminal_id,
                site_id,
                site_group_id,
                pos_server_id,
                currency_code,
                amount_due_minor_units,
                amount_tendered_minor_units,
                change_due_minor_units,
                cash_received_at,
                denomination_entries,
                local_event_reference,
                idempotency_key,
                idempotency_scope,
                semantic_request_hash,
                semantic_hash_source_version,
                original_correlation_id,
                payment_attempt_id,
                payment_confirmation_id,
                canonical_payment_status,
                result_classification,
                fiscal_status,
                created_at,
                confirmed_at,
                last_updated_at
            )
            VALUES (
                @terminal_cash_tender_id,
                @cash_custody_session_id,
                @parking_session_id,
                @tariff_snapshot_id,
                @cashier_id,
                @cashier_session_reference,
                @cashier_shift_id,
                @terminal_id,
                @site_id,
                @site_group_id,
                @pos_server_id,
                @currency_code,
                @amount_due_minor_units,
                @amount_tendered_minor_units,
                @change_due_minor_units,
                @cash_received_at,
                @denomination_entries,
                @local_event_reference,
                @idempotency_key,
                @idempotency_scope,
                @semantic_request_hash,
                @semantic_hash_source_version,
                @correlation_id,
                @payment_attempt_id,
                @payment_confirmation_id,
                @canonical_payment_status,
                'CREATED',
                @fiscal_status,
                @now,
                @now,
                @now
            )
            RETURNING *;
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        AddCommandParameters(dbCommand, command);
        dbCommand.Parameters.Add("payment_attempt_id", NpgsqlDbType.Uuid).Value = paymentAttemptId;
        dbCommand.Parameters.Add("payment_confirmation_id", NpgsqlDbType.Uuid).Value = paymentConfirmationId;
        dbCommand.Parameters.Add("canonical_payment_status", NpgsqlDbType.Varchar).Value = CanonicalPaymentStatusConfirmed;
        dbCommand.Parameters.Add("fiscal_status", NpgsqlDbType.Varchar).Value = FiscalStatusNotStarted;

        await using var reader = await dbCommand.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Terminal cash payment command insert returned no row.");
        }

        return ReadRecord(reader);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid? commandId,
        Guid terminalCashTenderId,
        string eventType,
        string? errorCode,
        Guid correlationId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO core.terminal_cash_payment_command_audits (
                terminal_cash_payment_command_id,
                terminal_cash_tender_id,
                audit_event_type,
                error_code,
                correlation_id,
                occurred_at
            )
            VALUES (
                @terminal_cash_payment_command_id,
                @terminal_cash_tender_id,
                @audit_event_type,
                @error_code,
                @correlation_id,
                @occurred_at
            );
            """;

        await using var dbCommand = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 30 };
        dbCommand.Parameters.Add("terminal_cash_payment_command_id", NpgsqlDbType.Uuid).Value =
            (object?)commandId ?? DBNull.Value;
        dbCommand.Parameters.Add("terminal_cash_tender_id", NpgsqlDbType.Uuid).Value = terminalCashTenderId;
        dbCommand.Parameters.Add("audit_event_type", NpgsqlDbType.Varchar).Value = eventType;
        dbCommand.Parameters.Add("error_code", NpgsqlDbType.Varchar).Value = (object?)errorCode ?? DBNull.Value;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        dbCommand.Parameters.Add("occurred_at", NpgsqlDbType.TimestampTz).Value = occurredAt;
        await dbCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCommonParameters(
        NpgsqlCommand dbCommand,
        TerminalCashPaymentRepositoryCommand command,
        PayableBasis payable,
        Guid paymentRailId,
        Guid serviceIdentityId)
    {
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = command.Command.ParkingSessionId;
        dbCommand.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = command.Command.TariffSnapshotId;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = command.Command.IdempotencyKey;
        dbCommand.Parameters.Add("payment_rail_id", NpgsqlDbType.Uuid).Value = paymentRailId;
        dbCommand.Parameters.Add("currency_code", NpgsqlDbType.Char).Value = payable.CurrencyCode;
        dbCommand.Parameters.Add("amount", NpgsqlDbType.Numeric).Value = payable.Amount;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = command.Command.CorrelationId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;
        dbCommand.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = serviceIdentityId;
    }

    private static void AddCommandParameters(NpgsqlCommand dbCommand, TerminalCashPaymentRepositoryCommand command)
    {
        var body = command.Command;
        dbCommand.Parameters.Add("terminal_cash_tender_id", NpgsqlDbType.Uuid).Value = body.TerminalCashTenderId;
        dbCommand.Parameters.Add("cash_custody_session_id", NpgsqlDbType.Uuid).Value = body.CashCustodySessionId;
        dbCommand.Parameters.Add("parking_session_id", NpgsqlDbType.Uuid).Value = body.ParkingSessionId;
        dbCommand.Parameters.Add("tariff_snapshot_id", NpgsqlDbType.Uuid).Value = body.TariffSnapshotId;
        dbCommand.Parameters.Add("cashier_id", NpgsqlDbType.Varchar).Value = body.CashierId;
        dbCommand.Parameters.Add("cashier_session_reference", NpgsqlDbType.Varchar).Value = body.CashierSessionReference;
        dbCommand.Parameters.Add("cashier_shift_id", NpgsqlDbType.Varchar).Value = body.CashierShiftId;
        dbCommand.Parameters.Add("terminal_id", NpgsqlDbType.Varchar).Value = body.TerminalId;
        dbCommand.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = body.SiteId;
        dbCommand.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = body.SiteGroupId;
        dbCommand.Parameters.Add("pos_server_id", NpgsqlDbType.Varchar).Value = body.PosServerId;
        dbCommand.Parameters.Add("currency_code", NpgsqlDbType.Char).Value = body.Currency;
        dbCommand.Parameters.Add("amount_due_minor_units", NpgsqlDbType.Bigint).Value = body.AmountDueMinorUnits;
        dbCommand.Parameters.Add("amount_tendered_minor_units", NpgsqlDbType.Bigint).Value = body.AmountTenderedMinorUnits;
        dbCommand.Parameters.Add("change_due_minor_units", NpgsqlDbType.Bigint).Value = body.ChangeDueMinorUnits;
        dbCommand.Parameters.Add("cash_received_at", NpgsqlDbType.TimestampTz).Value = body.CashReceivedAt;
        dbCommand.Parameters.Add("denomination_entries", NpgsqlDbType.Jsonb).Value =
            JsonSerializer.Serialize(body.DenominationEntries, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        dbCommand.Parameters.Add("local_event_reference", NpgsqlDbType.Varchar).Value = body.LocalEventReference;
        dbCommand.Parameters.Add("idempotency_key", NpgsqlDbType.Varchar).Value = body.IdempotencyKey;
        dbCommand.Parameters.Add("idempotency_scope", NpgsqlDbType.Varchar).Value = command.IdempotencyScope;
        dbCommand.Parameters.Add("semantic_request_hash", NpgsqlDbType.Varchar).Value = command.SemanticRequestHash;
        dbCommand.Parameters.Add("semantic_hash_source_version", NpgsqlDbType.Varchar).Value = command.SemanticHashSourceVersion;
        dbCommand.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = body.CorrelationId;
        dbCommand.Parameters.Add("now", NpgsqlDbType.TimestampTz).Value = command.RequestedAt;
    }

    private static TerminalCashPaymentRecord ReadRecord(NpgsqlDataReader reader)
    {
        return new TerminalCashPaymentRecord(
            reader.GetGuid(reader.GetOrdinal("terminal_cash_payment_command_id")),
            reader.GetGuid(reader.GetOrdinal("terminal_cash_tender_id")),
            reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            reader.GetString(reader.GetOrdinal("canonical_payment_status")),
            reader.GetString(reader.GetOrdinal("result_classification")),
            reader.GetString(reader.GetOrdinal("idempotency_scope")),
            reader.GetString(reader.GetOrdinal("idempotency_key")),
            reader.GetString(reader.GetOrdinal("semantic_request_hash")),
            reader.GetString(reader.GetOrdinal("semantic_hash_source_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("confirmed_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_updated_at")),
            reader.GetGuid(reader.GetOrdinal("original_correlation_id")),
            reader.GetString(reader.GetOrdinal("fiscal_status")));
    }

    private static TerminalCashPaymentReadback ReadReadback(NpgsqlDataReader reader)
    {
        return new TerminalCashPaymentReadback(
            reader.GetGuid(reader.GetOrdinal("terminal_cash_payment_command_id")),
            reader.GetGuid(reader.GetOrdinal("terminal_cash_tender_id")),
            reader.GetGuid(reader.GetOrdinal("cash_custody_session_id")),
            reader.GetGuid(reader.GetOrdinal("parking_session_id")),
            reader.GetGuid(reader.GetOrdinal("tariff_snapshot_id")),
            reader.GetString(reader.GetOrdinal("terminal_id")),
            reader.GetGuid(reader.GetOrdinal("site_id")),
            reader.GetGuid(reader.GetOrdinal("site_group_id")),
            reader.GetString(reader.GetOrdinal("pos_server_id")),
            reader.GetString(reader.GetOrdinal("cashier_id")),
            reader.GetString(reader.GetOrdinal("cashier_shift_id")),
            reader.GetString(reader.GetOrdinal("currency_code")).Trim(),
            reader.GetInt64(reader.GetOrdinal("amount_due_minor_units")),
            reader.GetInt64(reader.GetOrdinal("amount_tendered_minor_units")),
            reader.GetInt64(reader.GetOrdinal("change_due_minor_units")),
            reader.GetString(reader.GetOrdinal("canonical_payment_status")),
            reader.GetGuid(reader.GetOrdinal("payment_confirmation_id")),
            reader.GetString(reader.GetOrdinal("result_classification")),
            reader.GetString(reader.GetOrdinal("idempotency_scope")),
            reader.GetString(reader.GetOrdinal("semantic_hash_source_version")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("confirmed_at")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("last_updated_at")),
            reader.GetGuid(reader.GetOrdinal("original_correlation_id")),
            reader.GetString(reader.GetOrdinal("fiscal_status")));
    }

    private static TerminalCashPaymentResult ToCreatedResult(TerminalCashPaymentRecord record) =>
        ToResult(record, "CREATED");

    private static TerminalCashPaymentResult ToReplayResult(TerminalCashPaymentRecord record) =>
        ToResult(record, "IDEMPOTENT_REPLAY");

    private static TerminalCashPaymentResult ToResult(TerminalCashPaymentRecord record, string resultClassification)
    {
        return new TerminalCashPaymentResult(
            record.TerminalCashPaymentCommandId,
            record.TerminalCashTenderId,
            record.PaymentAttemptId,
            record.PaymentConfirmationId,
            record.CanonicalPaymentStatus,
            resultClassification,
            record.IdempotencyScope,
            record.SemanticHashSourceVersion,
            record.CreatedAt,
            record.ConfirmedAt,
            record.LastUpdatedAt,
            record.CorrelationId,
            record.FiscalStatus);
    }

    private sealed record PayableBasis(string CurrencyCode, decimal Amount);

    private sealed record TerminalCashPaymentRecord(
        Guid TerminalCashPaymentCommandId,
        Guid TerminalCashTenderId,
        Guid PaymentAttemptId,
        Guid PaymentConfirmationId,
        string CanonicalPaymentStatus,
        string ResultClassification,
        string IdempotencyScope,
        string IdempotencyKey,
        string SemanticRequestHash,
        string SemanticHashSourceVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset ConfirmedAt,
        DateTimeOffset LastUpdatedAt,
        Guid CorrelationId,
        string FiscalStatus);
}
