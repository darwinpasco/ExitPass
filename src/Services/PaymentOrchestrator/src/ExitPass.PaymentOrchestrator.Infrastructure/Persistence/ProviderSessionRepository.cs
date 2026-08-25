using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Persists provider session evidence records created by the Payment Orchestrator.
///
/// BRD:
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 9.2 Payments Domain
///
/// Invariants Enforced:
/// - Provider execution evidence must be persisted outside core payment truth.
/// </summary>
public sealed class ProviderSessionRepository : IProviderSessionRepository
{
    private const string ServiceIdentityCode = "payment-orchestrator";
    private const string DuplicateUniqueConstraintSqlState = "23505";

    private readonly string _connectionString;
    private readonly ILogger<ProviderSessionRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderSessionRepository"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The structured logger.</param>
    public ProviderSessionRepository(
        IConfiguration configuration,
        ILogger<ProviderSessionRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is required.");

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ProviderSessionInitiationReservationResult> TryReserveInitiationAsync(
        ProviderSessionInitiationReservation reservation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var paymentRailId = await ResolvePaymentRailIdAsync(
            connection,
            reservation.ProviderProduct,
            cancellationToken);
        var serviceIdentityId = await ResolvePaymentOrchestratorServiceIdentityIdAsync(
            connection,
            cancellationToken);

        const string sql = """
            insert into payments.provider_sessions
            (
                provider_session_id,
                payment_attempt_id,
                payment_rail_id,
                provider_session_ref,
                provider_transaction_ref,
                idempotency_key,
                session_status,
                currency_code,
                amount,
                checkout_url,
                qr_payload,
                expires_at,
                provider_created_at,
                provider_expires_at,
                raw_provider_metadata_ref,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            values
            (
                @provider_session_id,
                @payment_attempt_id,
                @payment_rail_id,
                null,
                null,
                @idempotency_key,
                'CREATED',
                @currency_code,
                @amount,
                null,
                null,
                null,
                null,
                null,
                null,
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_session_id", reservation.ProviderSessionRecordId);
        command.Parameters.AddWithValue("payment_attempt_id", reservation.PaymentAttemptId);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("idempotency_key", reservation.IdempotencyKey);
        command.Parameters.AddWithValue("currency_code", reservation.CurrencyCode);
        command.Parameters.AddWithValue("amount", reservation.AmountMinorUnits);
        command.Parameters.AddWithValue("correlation_id", (object?)reservation.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by_service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("updated_by_service_identity_id", serviceIdentityId);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);

            var reserved = new ProviderSessionRecord(
                reservation.ProviderSessionRecordId,
                reservation.PaymentAttemptId,
                string.Empty,
                reservation.ProviderProduct,
                string.Empty,
                null,
                "CREATED",
                null,
                null,
                null,
                reservation.IdempotencyKey,
                reservation.CorrelationId,
                reservation.RequestPayloadJson,
                "{}",
                reservation.CreatedAtUtc,
                reservation.AmountMinorUnits,
                reservation.CurrencyCode);

            _logger.LogInformation(
                "Reserved provider session initiation. PaymentAttemptId {PaymentAttemptId}, ProviderProduct {ProviderProduct}",
                reservation.PaymentAttemptId,
                reservation.ProviderProduct);

            return new ProviderSessionInitiationReservationResult(
                ProviderSessionInitiationReservationOutcome.Acquired,
                reserved);
        }
        catch (PostgresException exception) when (exception.SqlState == DuplicateUniqueConstraintSqlState)
        {
            var existing = await FindLatestByPaymentAttemptIdAsync(
                reservation.PaymentAttemptId,
                cancellationToken);

            if (existing is not null)
            {
                _logger.LogInformation(
                    "Provider session initiation already exists. PaymentAttemptId {PaymentAttemptId}, ProviderProduct {ProviderProduct}, ProviderSessionStatus {ProviderSessionStatus}",
                    reservation.PaymentAttemptId,
                    existing.ProviderProduct,
                    existing.SessionStatus);

                return new ProviderSessionInitiationReservationResult(
                    ProviderSessionInitiationReservationOutcome.Existing,
                    existing);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task CompleteInitiationAsync(
        Guid providerSessionRecordId,
        ProviderSessionRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var serviceIdentityId = await ResolvePaymentOrchestratorServiceIdentityIdAsync(
            connection,
            cancellationToken);

        const string sql = """
            update payments.provider_sessions
            set
                provider_session_ref = @provider_session_ref,
                provider_transaction_ref = @provider_transaction_ref,
                session_status = cast(@session_status as payments.provider_session_status_enum),
                checkout_url = @checkout_url,
                qr_payload = @qr_payload,
                expires_at = @expires_at,
                provider_expires_at = @provider_expires_at,
                updated_at = now(),
                updated_by_service_identity_id = @updated_by_service_identity_id
            where provider_session_id = @provider_session_id
              and payment_attempt_id = @payment_attempt_id
              and provider_session_ref is null;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_session_id", providerSessionRecordId);
        command.Parameters.AddWithValue("payment_attempt_id", record.PaymentAttemptId);
        command.Parameters.AddWithValue("provider_session_ref", record.ProviderSessionId);
        command.Parameters.AddWithValue("provider_transaction_ref", (object?)record.ProviderReference ?? DBNull.Value);
        command.Parameters.AddWithValue("session_status", NormalizeProviderSessionStatus(record.SessionStatus));
        command.Parameters.AddWithValue("checkout_url", (object?)record.RedirectUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("qr_payload", (object?)record.QrPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("updated_by_service_identity_id", serviceIdentityId);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
        {
            throw new InvalidOperationException("Provider session initiation reservation could not be completed.");
        }

        _logger.LogInformation(
            "Completed provider session initiation reservation. PaymentAttemptId {PaymentAttemptId}, ProviderProduct {ProviderProduct}, ProviderSessionRef {ProviderSessionRef}",
            record.PaymentAttemptId,
            record.ProviderProduct,
            record.ProviderSessionId);
    }

    /// <inheritdoc />
    public async Task AddAsync(
        ProviderSessionRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var paymentRailId = await ResolvePaymentRailIdAsync(
            connection,
            record.ProviderProduct,
            cancellationToken);
        var serviceIdentityId = await ResolvePaymentOrchestratorServiceIdentityIdAsync(
            connection,
            cancellationToken);

        const string sql = """
            insert into payments.provider_sessions
            (
                provider_session_id,
                payment_attempt_id,
                payment_rail_id,
                provider_session_ref,
                provider_transaction_ref,
                idempotency_key,
                session_status,
                currency_code,
                amount,
                checkout_url,
                qr_payload,
                expires_at,
                provider_created_at,
                provider_expires_at,
                raw_provider_metadata_ref,
                correlation_id,
                created_by_service_identity_id,
                updated_by_service_identity_id
            )
            values
            (
                @provider_session_id,
                @payment_attempt_id,
                @payment_rail_id,
                @provider_session_ref,
                @provider_transaction_ref,
                @idempotency_key,
                cast(@session_status as payments.provider_session_status_enum),
                @currency_code,
                @amount,
                @checkout_url,
                @qr_payload,
                @expires_at,
                @provider_created_at,
                @provider_expires_at,
                @raw_provider_metadata_ref,
                @correlation_id,
                @created_by_service_identity_id,
                @updated_by_service_identity_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_session_id", record.ProviderSessionRecordId);
        command.Parameters.AddWithValue("payment_attempt_id", record.PaymentAttemptId);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("provider_session_ref", record.ProviderSessionId);
        command.Parameters.AddWithValue("provider_transaction_ref", (object?)record.ProviderReference ?? DBNull.Value);
        command.Parameters.AddWithValue("idempotency_key", record.IdempotencyKey);
        command.Parameters.AddWithValue("session_status", NormalizeProviderSessionStatus(record.SessionStatus));
        command.Parameters.AddWithValue("currency_code", ExtractCurrencyCode(record));
        command.Parameters.AddWithValue("amount", ExtractAmountRequested(record));
        command.Parameters.AddWithValue("checkout_url", (object?)record.RedirectUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("qr_payload", (object?)record.QrPayload ?? DBNull.Value);
        command.Parameters.AddWithValue("expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_created_at", DBNull.Value);
        command.Parameters.AddWithValue("provider_expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("raw_provider_metadata_ref", DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", (object?)record.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by_service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("updated_by_service_identity_id", serviceIdentityId);

        await command.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted provider session evidence. PaymentAttemptId {PaymentAttemptId}, ProviderProduct {ProviderProduct}, ProviderSessionRef {ProviderSessionRef}",
            record.PaymentAttemptId,
            record.ProviderProduct,
            record.ProviderSessionId);
    }

    /// <inheritdoc />
    public async Task<ProviderSessionRecord?> FindByProviderSessionIdAsync(
        string providerCode,
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var providerSessionRecordId = await ResolveProviderSessionRecordIdAsync(
            connection,
            providerCode,
            providerSessionId,
            cancellationToken);

        if (providerSessionRecordId is null)
        {
            return null;
        }

        const string sql = """
            select
                provider_session_id,
                payment_attempt_id,
                provider_session_ref,
                provider_transaction_ref,
                idempotency_key,
                session_status,
                currency_code,
                amount,
                checkout_url,
                qr_payload,
                expires_at,
                provider_expires_at,
                correlation_id,
                created_at
            from payments.provider_sessions
            where provider_session_id = @provider_session_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_session_id", providerSessionRecordId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadProviderSessionRecord(reader, providerCode);
    }

    /// <inheritdoc />
    public async Task<ProviderSessionRecord?> FindLatestActiveByParkingSessionIdAsync(
        Guid parkingSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select
                ps.provider_session_id,
                ps.payment_attempt_id,
                ps.provider_session_ref,
                ps.provider_transaction_ref,
                ps.idempotency_key,
                ps.session_status,
                ps.currency_code,
                ps.amount,
                ps.checkout_url,
                ps.qr_payload,
                ps.expires_at,
                ps.provider_expires_at,
                ps.correlation_id,
                ps.created_at,
                pr.provider_code,
                pr.rail_code
            from payments.provider_sessions ps
            join core.payment_attempts pa on pa.payment_attempt_id = ps.payment_attempt_id
            join payments.payment_rails pr on pr.payment_rail_id = ps.payment_rail_id
            where pa.parking_session_id = @parking_session_id
              and ps.checkout_url is not null
            order by ps.created_at desc
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("parking_session_id", parkingSessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadProviderSessionRecord(
            reader,
            reader.GetString(reader.GetOrdinal("provider_code")),
            reader.GetString(reader.GetOrdinal("rail_code")));
    }

    /// <inheritdoc />
    public async Task<ProviderSessionRecord?> FindLatestByPaymentAttemptIdAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            select
                ps.provider_session_id,
                ps.payment_attempt_id,
                ps.provider_session_ref,
                ps.provider_transaction_ref,
                ps.idempotency_key,
                ps.session_status,
                ps.currency_code,
                ps.amount,
                ps.checkout_url,
                ps.qr_payload,
                ps.expires_at,
                ps.provider_expires_at,
                ps.correlation_id,
                ps.created_at,
                pr.provider_code,
                pr.rail_code
            from payments.provider_sessions ps
            join payments.payment_rails pr on pr.payment_rail_id = ps.payment_rail_id
            where ps.payment_attempt_id = @payment_attempt_id
            order by ps.created_at desc
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_attempt_id", paymentAttemptId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadProviderSessionRecord(
            reader,
            reader.GetString(reader.GetOrdinal("provider_code")),
            reader.GetString(reader.GetOrdinal("rail_code")));
    }

    /// <inheritdoc />
    public async Task MarkWebhookOutcomeAsync(
        string providerCode,
        string providerSessionId,
        string? providerReference,
        string sessionStatus,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionStatus);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var providerSessionRecordId = await ResolveProviderSessionRecordIdAsync(
            connection,
            providerCode,
            providerSessionId,
            cancellationToken);

        if (providerSessionRecordId is null)
        {
            throw new UnknownProviderSessionException(providerSessionId);
        }

        var serviceIdentityId = await ResolvePaymentOrchestratorServiceIdentityIdAsync(
            connection,
            cancellationToken);

        const string sql = """
            update payments.provider_sessions
            set
                provider_transaction_ref = coalesce(nullif(@provider_transaction_ref, ''), provider_transaction_ref),
                session_status = cast(@session_status as payments.provider_session_status_enum),
                updated_at = now(),
                updated_by_service_identity_id = @updated_by_service_identity_id
            where provider_session_id = @provider_session_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_transaction_ref", (object?)providerReference ?? string.Empty);
        command.Parameters.AddWithValue("session_status", NormalizeProviderSessionStatus(sessionStatus));
        command.Parameters.AddWithValue("updated_by_service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("provider_session_id", providerSessionRecordId.Value);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        if (rows == 0)
        {
            throw new UnknownProviderSessionException(providerSessionId);
        }

        _logger.LogInformation(
            "Updated provider session evidence from verified webhook. ProviderCode {ProviderCode}, ProviderSessionRef {ProviderSessionRef}, SessionStatus {SessionStatus}",
            providerCode,
            providerSessionId,
            sessionStatus);
    }

    private static string NormalizeProviderSessionStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "CREATED" => "CREATED",
            "ACTIVE" => "ACTIVE",
            "HANDOFF_READY" => "PENDING",
            "PENDING_PROVIDER" => "PENDING",
            "PENDING" => "PENDING",
            "SUCCEEDED" => "PAID",
            "PAID" => "PAID",
            "FAILED" => "FAILED",
            "EXPIRED" => "EXPIRED",
            "CANCELLED" => "CANCELLED",
            _ => "UNKNOWN"
        };
    }

    private static decimal ExtractAmountRequested(ProviderSessionRecord record)
    {
        using var document = JsonDocument.Parse(record.RequestPayloadJson);
        if (document.RootElement.TryGetProperty("AmountMinor", out var amountMinorProperty) &&
            amountMinorProperty.TryGetInt64(out var amountMinor))
        {
            return amountMinor;
        }

        return 0m;
    }

    private static string ExtractCurrencyCode(ProviderSessionRecord record)
    {
        using var document = JsonDocument.Parse(record.RequestPayloadJson);
        if (document.RootElement.TryGetProperty("Currency", out var currencyProperty))
        {
            var currency = currencyProperty.GetString();
            if (!string.IsNullOrWhiteSpace(currency))
            {
                return currency;
            }
        }

        return "PHP";
    }

    private static async Task<Guid> ResolvePaymentOrchestratorServiceIdentityIdAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select service_identity_id
            from identity.service_identities
            where service_identity_code = @service_identity_code
              and identity_status = 'ACTIVE'
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("service_identity_code", ServiceIdentityCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        if (scalar is Guid serviceIdentityId)
        {
            return serviceIdentityId;
        }

        throw new InvalidOperationException(
            $"No active service identity found for service_identity_code '{ServiceIdentityCode}'.");
    }

    private static ProviderSessionRecord ReadProviderSessionRecord(
        NpgsqlDataReader reader,
        string providerCode,
        string providerProduct = "")
    {
        return new ProviderSessionRecord(
            ProviderSessionRecordId: reader.GetGuid(reader.GetOrdinal("provider_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderCode: providerCode,
            ProviderProduct: providerProduct,
            ProviderSessionId: reader.IsDBNull(reader.GetOrdinal("provider_session_ref"))
                ? string.Empty
                : reader.GetString(reader.GetOrdinal("provider_session_ref")),
            ProviderReference: reader.IsDBNull(reader.GetOrdinal("provider_transaction_ref"))
                ? null
                : reader.GetString(reader.GetOrdinal("provider_transaction_ref")),
            SessionStatus: reader.GetString(reader.GetOrdinal("session_status")),
            RedirectUrl: reader.IsDBNull(reader.GetOrdinal("checkout_url"))
                ? null
                : reader.GetString(reader.GetOrdinal("checkout_url")),
            QrPayload: reader.IsDBNull(reader.GetOrdinal("qr_payload"))
                ? null
                : reader.GetString(reader.GetOrdinal("qr_payload")),
            ExpiresAtUtc: reader.IsDBNull(reader.GetOrdinal("expires_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("expires_at")),
            IdempotencyKey: reader.GetString(reader.GetOrdinal("idempotency_key")),
            CorrelationId: reader.IsDBNull(reader.GetOrdinal("correlation_id"))
                ? null
                : reader.GetGuid(reader.GetOrdinal("correlation_id")),
            RequestPayloadJson: "{}",
            ResponsePayloadJson: "{}",
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")),
            AmountMinorUnits: ReadAmountMinorUnits(reader),
            CurrencyCode: reader.IsDBNull(reader.GetOrdinal("currency_code"))
                ? null
                : reader.GetString(reader.GetOrdinal("currency_code")));
    }

    private static long? ReadAmountMinorUnits(NpgsqlDataReader reader)
    {
        if (reader.IsDBNull(reader.GetOrdinal("amount")))
        {
            return null;
        }

        var amount = reader.GetDecimal(reader.GetOrdinal("amount"));
        return decimal.Truncate(amount) == amount
            ? decimal.ToInt64(amount)
            : null;
    }

    private static async Task<Guid> ResolvePaymentRailIdAsync(
        NpgsqlConnection connection,
        string paymentRailCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select payment_rail_id
            from payments.payment_rails
            where rail_code = @rail_code
              and rail_status = 'ACTIVE'
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("rail_code", paymentRailCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        if (scalar is Guid paymentRailId)
        {
            return paymentRailId;
        }

        throw new InvalidOperationException(
            $"No active payment rail found for rail_code '{paymentRailCode}'.");
    }

    private static async Task<Guid?> ResolveProviderSessionRecordIdAsync(
        NpgsqlConnection connection,
        string providerCode,
        string providerSessionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select ps.provider_session_id
            from payments.provider_sessions ps
            join payments.payment_rails pr on pr.payment_rail_id = ps.payment_rail_id
            where pr.provider_code = @provider_code
              and ps.provider_session_ref = @provider_session_ref
            order by ps.created_at desc
            limit 2;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_code", providerCode);
        command.Parameters.AddWithValue("provider_session_ref", providerSessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var providerSessionRecordId = reader.GetGuid(0);

        if (await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Provider session reference is ambiguous for the authenticated provider.");
        }

        return providerSessionRecordId;
    }
}
