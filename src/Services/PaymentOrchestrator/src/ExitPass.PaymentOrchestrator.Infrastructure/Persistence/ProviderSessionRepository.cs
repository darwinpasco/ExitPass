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

        var paymentRailId = await ResolvePaymentRailIdByProviderCodeAsync(
            connection,
            providerCode,
            cancellationToken);

        const string sql = """
            select
                provider_session_id,
                payment_attempt_id,
                provider_session_ref,
                provider_transaction_ref,
                idempotency_key,
                session_status,
                checkout_url,
                qr_payload,
                expires_at,
                provider_expires_at,
                correlation_id,
                created_at
            from payments.provider_sessions
            where payment_rail_id = @payment_rail_id
              and provider_session_ref = @provider_session_ref
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("provider_session_ref", providerSessionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProviderSessionRecord(
            ProviderSessionRecordId: reader.GetGuid(reader.GetOrdinal("provider_session_id")),
            PaymentAttemptId: reader.GetGuid(reader.GetOrdinal("payment_attempt_id")),
            ProviderCode: providerCode,
            ProviderProduct: string.Empty,
            ProviderSessionId: reader.GetString(reader.GetOrdinal("provider_session_ref")),
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
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
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

    private static async Task<Guid> ResolvePaymentRailIdByProviderCodeAsync(
        NpgsqlConnection connection,
        string providerCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select payment_rail_id
            from payments.payment_rails
            where provider_code = @provider_code
              and rail_status = 'ACTIVE'
            order by is_primary desc, effective_from desc
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_code", providerCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        if (scalar is Guid paymentRailId)
        {
            return paymentRailId;
        }

        throw new InvalidOperationException(
            $"No active payment rail found for provider_code '{providerCode}'.");
    }
}
