using System.Data;
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

        var handoffType = ResolveHandoffType(record.RedirectUrl);
        var handoffPayloadJson = BuildHandoffPayloadJson(record);

        const string sql = """
            insert into payments.provider_sessions
            (
                provider_session_id,
                payment_attempt_id,
                payment_rail_id,
                provider_session_ref,
                provider_payment_ref,
                provider_status,
                handoff_type,
                handoff_url,
                handoff_payload,
                amount_requested,
                currency_code,
                provider_expires_at,
                initiated_at,
                last_status_at,
                created_at,
                created_by,
                updated_at,
                updated_by,
                row_version
            )
            values
            (
                @provider_session_id,
                @payment_attempt_id,
                @payment_rail_id,
                @provider_session_ref,
                @provider_payment_ref,
                cast(@provider_status as payments.provider_session_status_enum),
                cast(@handoff_type as payments.provider_handoff_type_enum),
                @handoff_url,
                cast(@handoff_payload as jsonb),
                @amount_requested,
                @currency_code,
                @provider_expires_at,
                @initiated_at,
                @last_status_at,
                @created_at,
                @created_by,
                @updated_at,
                @updated_by,
                @row_version
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_session_id", record.ProviderSessionRecordId);
        command.Parameters.AddWithValue("payment_attempt_id", record.PaymentAttemptId);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("provider_session_ref", record.ProviderSessionId);
        command.Parameters.AddWithValue("provider_payment_ref", (object?)record.ProviderReference ?? DBNull.Value);
        command.Parameters.AddWithValue("provider_status", NormalizeProviderSessionStatus(record.SessionStatus));
        command.Parameters.AddWithValue("handoff_type", handoffType);
        command.Parameters.AddWithValue("handoff_url", (object?)record.RedirectUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("handoff_payload", handoffPayloadJson);
        command.Parameters.AddWithValue("amount_requested", ExtractAmountRequested(record));
        command.Parameters.AddWithValue("currency_code", ExtractCurrencyCode(record));
        command.Parameters.AddWithValue("provider_expires_at", (object?)record.ExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("initiated_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("last_status_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("created_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("created_by", "payment-orchestrator");
        command.Parameters.AddWithValue("updated_at", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_by", "payment-orchestrator");
        command.Parameters.AddWithValue("row_version", 1L);

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
                provider_payment_ref,
                provider_status,
                handoff_url,
                provider_expires_at,
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
            ProviderReference: reader.IsDBNull(reader.GetOrdinal("provider_payment_ref"))
                ? null
                : reader.GetString(reader.GetOrdinal("provider_payment_ref")),
            SessionStatus: reader.GetString(reader.GetOrdinal("provider_status")),
            RedirectUrl: reader.IsDBNull(reader.GetOrdinal("handoff_url"))
                ? null
                : reader.GetString(reader.GetOrdinal("handoff_url")),
            ExpiresAtUtc: reader.IsDBNull(reader.GetOrdinal("provider_expires_at"))
                ? null
                : reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("provider_expires_at")),
            IdempotencyKey: string.Empty,
            RequestPayloadJson: "{}",
            ResponsePayloadJson: "{}",
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
    }

    private static string ResolveHandoffType(string? redirectUrl)
    {
        return string.IsNullOrWhiteSpace(redirectUrl) ? "SERVER_TO_SERVER" : "REDIRECT";
    }

    private static string NormalizeProviderSessionStatus(string status)
    {
        return status.ToUpperInvariant() switch
        {
            "CREATED" => "CREATED",
            "HANDOFF_READY" => "HANDOFF_READY",
            "PENDING_PROVIDER" => "PENDING_PROVIDER",
            "SUCCEEDED" => "SUCCEEDED",
            "FAILED" => "FAILED",
            "EXPIRED" => "EXPIRED",
            "CANCELLED" => "CANCELLED",
            _ => "CREATED"
        };
    }

    private static string BuildHandoffPayloadJson(ProviderSessionRecord record)
    {
        var payload = new
        {
            redirect_url = record.RedirectUrl,
            request_payload = record.RequestPayloadJson,
            response_payload = record.ResponsePayloadJson
        };

        return JsonSerializer.Serialize(payload);
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

    private static async Task<Guid> ResolvePaymentRailIdAsync(
        NpgsqlConnection connection,
        string paymentRailCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select payment_rail_id
            from payments.payment_rails
            where payment_rail_code = @payment_rail_code
              and is_enabled = true
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_rail_code", paymentRailCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        if (scalar is Guid paymentRailId)
        {
            return paymentRailId;
        }

        throw new InvalidOperationException(
            $"No enabled payment rail found for payment_rail_code '{paymentRailCode}'.");
    }

    private static async Task<Guid> ResolvePaymentRailIdByProviderCodeAsync(
        NpgsqlConnection connection,
        string providerCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select payment_rail_id
            from payments.payment_rails
            where provider_name = @provider_name
              and is_enabled = true
            order by priority_rank asc
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_name", providerCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);

        if (scalar is Guid paymentRailId)
        {
            return paymentRailId;
        }

        throw new InvalidOperationException(
            $"No enabled payment rail found for provider_name '{providerCode}'.");
    }
}
