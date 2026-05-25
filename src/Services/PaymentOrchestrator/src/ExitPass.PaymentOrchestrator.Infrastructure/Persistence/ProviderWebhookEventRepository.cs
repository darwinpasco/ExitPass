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
    private const string DuplicatePayloadConstraintName = "uq_provider_callbacks__payload_hash";
    private const string DuplicateProviderEventConstraintName = "ux_provider_callbacks__provider_event";
    private const string PaymentOrchestratorServiceIdentityCode = "payment-orchestrator";

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

        const string sql = """
            select exists (
                select 1
                from payments.provider_callbacks pc
                join payments.payment_rails pr on pr.payment_rail_id = pc.payment_rail_id
                where pr.provider_code = @provider_code
                  and pc.provider_event_ref = @provider_event_ref
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_code", providerCode);
        command.Parameters.AddWithValue("provider_event_ref", providerEventId);

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

        ProviderCallbackSessionContext sessionContext;
        try
        {
            sessionContext = await ResolveProviderCallbackSessionContextAsync(
                connection,
                record.ProviderCode,
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

        var payloadHash = ComputeSha256(record.RawBodyJson);
        var headersHash = ComputeSha256(record.RawHeadersJson);
        var sourceIp = ParseSourceIp(record.RawHeadersJson);
        var processedAt = DateTimeOffset.UtcNow;
        var serviceIdentityId = await ResolvePaymentOrchestratorServiceIdentityIdAsync(connection, cancellationToken);

        const string sql = """
            insert into payments.provider_callbacks
            (
                provider_callback_id,
                payment_rail_id,
                provider_session_id,
                payment_attempt_id,
                provider_event_ref,
                provider_transaction_ref,
                callback_type,
                payload_hash,
                payload_storage_ref,
                headers_hash,
                signature_valid,
                timestamp_valid,
                source_valid,
                verification_status,
                processing_status,
                received_at,
                processed_at,
                failure_reason_code,
                correlation_id,
                created_at,
                created_by_service_identity_id
            )
            values
            (
                @provider_callback_id,
                @payment_rail_id,
                @provider_session_id,
                @payment_attempt_id,
                @provider_event_ref,
                @provider_transaction_ref,
                @callback_type,
                @payload_hash,
                @payload_storage_ref,
                @headers_hash,
                @signature_valid,
                @timestamp_valid,
                @source_valid,
                cast(@verification_status as payments.provider_callback_verification_status_enum),
                cast(@processing_status as payments.provider_callback_processing_status_enum),
                @received_at,
                @processed_at,
                @failure_reason_code,
                @correlation_id,
                @created_at,
                @created_by_service_identity_id
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_callback_id", record.ProviderWebhookEventRecordId);
        command.Parameters.AddWithValue("payment_rail_id", sessionContext.PaymentRailId);
        command.Parameters.AddWithValue("provider_session_id", sessionContext.ProviderSessionId);
        command.Parameters.AddWithValue("payment_attempt_id", sessionContext.PaymentAttemptId);
        command.Parameters.AddWithValue("provider_event_ref", record.ProviderEventId);
        command.Parameters.AddWithValue("provider_transaction_ref", record.ProviderReference);
        command.Parameters.AddWithValue("callback_type", record.ProviderEventType);
        command.Parameters.AddWithValue("payload_hash", payloadHash);
        command.Parameters.AddWithValue("payload_storage_ref", DBNull.Value);
        command.Parameters.AddWithValue("headers_hash", headersHash);
        command.Parameters.AddWithValue("signature_valid", record.IsAuthentic);
        command.Parameters.AddWithValue("timestamp_valid", DBNull.Value);
        command.Parameters.AddWithValue("source_valid", sourceIp is null ? DBNull.Value : true);
        command.Parameters.AddWithValue("verification_status", NormalizeVerificationStatus(record.IsAuthentic));
        command.Parameters.AddWithValue("processing_status", NormalizeProcessingStatus(record.IsAuthentic));
        command.Parameters.AddWithValue("received_at", record.ReceivedAtUtc);
        command.Parameters.AddWithValue("processed_at", processedAt);
        command.Parameters.AddWithValue("failure_reason_code", DBNull.Value);
        command.Parameters.AddWithValue("correlation_id", DBNull.Value);
        command.Parameters.AddWithValue("created_at", record.ReceivedAtUtc);
        command.Parameters.AddWithValue("created_by_service_identity_id", serviceIdentityId);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (
            ex.SqlState == "23505" &&
            (string.Equals(ex.ConstraintName, DuplicatePayloadConstraintName, StringComparison.Ordinal) ||
             string.Equals(ex.ConstraintName, DuplicateProviderEventConstraintName, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "Detected duplicate provider callback payload during insert. ProviderCode {ProviderCode}, CallbackReference {CallbackReference}",
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

    private static string NormalizeVerificationStatus(bool isAuthentic)
    {
        return isAuthentic ? "VERIFIED" : "FAILED_SIGNATURE";
    }

    private static string NormalizeProcessingStatus(bool isAuthentic)
    {
        return isAuthentic ? "PROCESSED" : "REJECTED";
    }

    private static async Task<ProviderCallbackSessionContext> ResolveProviderCallbackSessionContextAsync(
        NpgsqlConnection connection,
        string providerCode,
        string providerSessionRef,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select
                ps.provider_session_id,
                ps.payment_attempt_id,
                ps.payment_rail_id
            from payments.provider_sessions ps
            join payments.payment_rails pr on pr.payment_rail_id = ps.payment_rail_id
            where pr.provider_code = @provider_code
              and ps.provider_session_ref = @provider_session_ref
            limit 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("provider_code", providerCode);
        command.Parameters.AddWithValue("provider_session_ref", providerSessionRef);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ProviderCallbackSessionContext(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetGuid(2));
        }

        throw new UnknownProviderSessionException(providerSessionRef);
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
        command.Parameters.AddWithValue("service_identity_code", PaymentOrchestratorServiceIdentityCode);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is Guid serviceIdentityId)
        {
            return serviceIdentityId;
        }

        throw new InvalidOperationException(
            $"Active service identity '{PaymentOrchestratorServiceIdentityCode}' was not found.");
    }

    private readonly record struct ProviderCallbackSessionContext(
        Guid ProviderSessionId,
        Guid PaymentAttemptId,
        Guid PaymentRailId);
}
