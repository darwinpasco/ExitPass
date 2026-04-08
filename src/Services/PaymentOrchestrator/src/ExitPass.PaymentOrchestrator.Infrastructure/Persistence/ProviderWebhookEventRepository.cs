using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ExitPass.PaymentOrchestrator.Infrastructure.Persistence;

/// <summary>
/// Persists immutable provider callback evidence for deduplication, audit, and traceability.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 14 Audit, Logging, and Reporting
///
/// SDD:
/// - 10.5.2 Payment Provider Webhook
///
/// Invariants Enforced:
/// - Duplicate provider callbacks must be detected deterministically.
/// - Raw provider callback evidence must be persisted outside core payment truth.
/// - Provider callback evidence must reference a known provider session.
/// - Only authoritative callbacks that reach persistence are written as immutable evidence.
/// </summary>
public sealed class ProviderWebhookEventRepository : IProviderWebhookEventRepository
{
    private const string DuplicateCallbackConstraintName = "uq_provider_callbacks__rail_callback_ref";

    private readonly string _connectionString;
    private readonly ILogger<ProviderWebhookEventRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderWebhookEventRepository"/> class.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="logger">The structured logger.</param>
    public ProviderWebhookEventRepository(
        IConfiguration configuration,
        ILogger<ProviderWebhookEventRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _connectionString = configuration.GetConnectionString("MainDatabase")
            ?? throw new InvalidOperationException("Connection string 'MainDatabase' is required.");

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByProviderEventIdAsync(
        string providerCode,
        string providerEventId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var paymentRailId = await ResolvePaymentRailIdByProviderCodeAsync(
            connection,
            providerCode,
            cancellationToken);

        const string sql = """
            select exists (
                select 1
                from payments.provider_callbacks
                where payment_rail_id = @payment_rail_id
                  and callback_reference = @callback_reference
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("callback_reference", providerEventId);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is bool value && value;
    }

    /// <inheritdoc />
    public async Task AddAsync(
        ProviderWebhookEventRecord record,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var paymentRailId = await ResolvePaymentRailIdByProviderCodeAsync(
            connection,
            record.ProviderCode,
            cancellationToken);

        Guid providerSessionId;
        try
        {
            providerSessionId = await ResolveProviderSessionPrimaryKeyAsync(
                connection,
                paymentRailId,
                record.ProviderSessionId,
                cancellationToken);
        }
        catch (UnknownProviderSessionException)
        {
            _logger.LogWarning(
                "Provider callback references an unknown provider session. ProviderCode {ProviderCode}, ProviderSessionRef {ProviderSessionRef}, CallbackReference {CallbackReference}",
                record.ProviderCode,
                record.ProviderSessionId,
                record.ProviderEventId);

            throw;
        }

        var payloadJson = TryNormalizeJson(record.RawBodyJson);
        var payloadHash = ComputeSha256(record.RawBodyJson);
        var sourceIp = ParseSourceIp(record.RawHeadersJson);
        var signatureKeyId = TryExtractSignatureKeyId(record.RawHeadersJson);
        var signaturePresent = TryDetectSignaturePresence(record.RawHeadersJson);
        var processedAt = DateTimeOffset.UtcNow;

        const string sql = """
            insert into payments.provider_callbacks
            (
                provider_callback_id,
                provider_session_id,
                payment_rail_id,
                callback_reference,
                http_method,
                source_ip,
                signature_key_id,
                signature_present,
                signature_valid,
                replay_detected,
                provider_status_raw,
                headers_json,
                payload_json,
                payload_text,
                payload_hash,
                received_at,
                processed_at,
                processing_status,
                processing_error_code,
                created_at,
                created_by
            )
            values
            (
                @provider_callback_id,
                @provider_session_id,
                @payment_rail_id,
                @callback_reference,
                @http_method,
                @source_ip,
                @signature_key_id,
                @signature_present,
                @signature_valid,
                @replay_detected,
                @provider_status_raw,
                cast(@headers_json as jsonb),
                cast(@payload_json as jsonb),
                @payload_text,
                @payload_hash,
                @received_at,
                @processed_at,
                cast(@processing_status as payments.provider_callback_processing_status_enum),
                @processing_error_code,
                @created_at,
                @created_by
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_callback_id", record.ProviderWebhookEventRecordId);
        command.Parameters.AddWithValue("provider_session_id", providerSessionId);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("callback_reference", record.ProviderEventId);
        command.Parameters.AddWithValue("http_method", "POST");
        command.Parameters.AddWithValue("source_ip", (object?)sourceIp ?? DBNull.Value);
        command.Parameters.AddWithValue("signature_key_id", (object?)signatureKeyId ?? DBNull.Value);
        command.Parameters.AddWithValue("signature_present", signaturePresent);
        command.Parameters.AddWithValue("signature_valid", record.IsAuthentic);
        command.Parameters.AddWithValue("replay_detected", record.IsDuplicate);
        command.Parameters.AddWithValue("provider_status_raw", record.ProviderEventType);
        command.Parameters.AddWithValue("headers_json", record.RawHeadersJson);
        command.Parameters.AddWithValue("payload_json", payloadJson);
        command.Parameters.AddWithValue("payload_text", record.RawBodyJson);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        command.Parameters.AddWithValue("received_at", record.ReceivedAtUtc);
        command.Parameters.AddWithValue("processed_at", processedAt);
        command.Parameters.AddWithValue("processing_status", NormalizeProcessingStatus(record.IsAuthentic));
        command.Parameters.AddWithValue("processing_error_code", DBNull.Value);
        command.Parameters.AddWithValue("created_at", record.ReceivedAtUtc);
        command.Parameters.AddWithValue("created_by", "payment-orchestrator");

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (
            ex.SqlState == "23505" &&
            string.Equals(ex.ConstraintName, DuplicateCallbackConstraintName, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Detected duplicate provider callback during insert. ProviderCode {ProviderCode}, CallbackReference {CallbackReference}",
                record.ProviderCode,
                record.ProviderEventId);

            throw new DuplicateProviderWebhookEventException(
                $"Provider callback already exists for callback reference '{record.ProviderEventId}'.");
        }

        _logger.LogInformation(
            "Persisted provider callback evidence. ProviderCode {ProviderCode}, CallbackReference {CallbackReference}, ReplayDetected {ReplayDetected}",
            record.ProviderCode,
            record.ProviderEventId,
            record.IsDuplicate);
    }

    private static string TryNormalizeJson(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch
        {
            return "{}";
        }
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static IPAddress? ParseSourceIp(string rawHeadersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawHeadersJson);
            if (document.RootElement.TryGetProperty("X-Forwarded-For", out var forwardedFor))
            {
                var value = forwardedFor.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    var first = value.Split(',')[0].Trim();
                    if (IPAddress.TryParse(first, out var ip))
                    {
                        return ip;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? TryExtractSignatureKeyId(string rawHeadersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawHeadersJson);
            if (document.RootElement.TryGetProperty("X-Key-Id", out var keyId))
            {
                return keyId.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool TryDetectSignaturePresence(string rawHeadersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawHeadersJson);

            return document.RootElement.TryGetProperty("X-Signature", out _) ||
                   document.RootElement.TryGetProperty("Paymongo-Signature", out _) ||
                   document.RootElement.TryGetProperty("paymongo-signature", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeProcessingStatus(bool isAuthentic)
    {
        return isAuthentic ? "VERIFIED" : "REJECTED";
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

    private static async Task<Guid> ResolveProviderSessionPrimaryKeyAsync(
        NpgsqlConnection connection,
        Guid paymentRailId,
        string providerSessionRef,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select provider_session_id
            from payments.provider_sessions
            where payment_rail_id = @payment_rail_id
              and provider_session_ref = @provider_session_ref
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("payment_rail_id", paymentRailId);
        command.Parameters.AddWithValue("provider_session_ref", providerSessionRef);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is Guid providerSessionId)
        {
            return providerSessionId;
        }

        throw new UnknownProviderSessionException(providerSessionRef);
    }
}
