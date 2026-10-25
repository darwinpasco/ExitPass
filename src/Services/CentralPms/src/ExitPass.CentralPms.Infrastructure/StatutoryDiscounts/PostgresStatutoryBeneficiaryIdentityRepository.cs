using ExitPass.CentralPms.Application.StatutoryDiscounts;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.StatutoryDiscounts;

public sealed class PostgresStatutoryBeneficiaryIdentityRepository : IStatutoryBeneficiaryIdentityRepository
{
    private readonly string _connectionString;
    private readonly IStatutoryBeneficiaryIdentityProtector _protector;

    public PostgresStatutoryBeneficiaryIdentityRepository(
        string connectionString,
        IStatutoryBeneficiaryIdentityProtector protector)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<StatutoryBeneficiaryIdentityWriteResult> SaveBeforeDecisionAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid actorUserId,
        string captureChannel,
        StatutoryBeneficiaryIdentityInput identity,
        Guid correlationReference,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountDecisionCommandId == Guid.Empty || actorUserId == Guid.Empty ||
            correlationReference == Guid.Empty ||
            !StatutoryBeneficiaryIdentityCaptureChannels.IsAuthoritative(captureChannel) ||
            !StatutoryBeneficiaryIdentityNormalizer.TryNormalize(identity, out var normalized))
            return new(StatutoryBeneficiaryIdentityWriteOutcome.Invalid, "STATUTORY_BENEFICIARY_IDENTITY_INVALID");

        if (!_protector.IsConfigured)
            return new(StatutoryBeneficiaryIdentityWriteOutcome.ProtectionUnavailable, "STATUTORY_BENEFICIARY_PROTECTION_UNAVAILABLE");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var authority = await ReadAuthorityAsync(connection, transaction, statutoryDiscountDecisionCommandId, cancellationToken).ConfigureAwait(false);
        if (authority is null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(StatutoryBeneficiaryIdentityWriteOutcome.NotFound, "STATUTORY_BENEFICIARY_DECISION_NOT_FOUND");
        }

        if (authority.IsTerminal)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(StatutoryBeneficiaryIdentityWriteOutcome.Conflict, "STATUTORY_BENEFICIARY_IDENTITY_FINAL");
        }

        var protectedIdentity = _protector.Protect(statutoryDiscountDecisionCommandId, authority.SiteId, normalized!);
        const string sql = """
            INSERT INTO discounts.statutory_beneficiary_identities (
                statutory_beneficiary_identity_id,
                statutory_discount_decision_command_id,
                site_id,
                entitlement_type,
                statutory_id_type,
                protected_identity_envelope,
                protection_key_reference,
                protection_key_version,
                envelope_format_version,
                captured_by_actor_ref,
                captured_by_channel,
                captured_at,
                last_updated_by_actor_ref,
                last_updated_by_channel,
                last_updated_at,
                correlation_reference,
                is_final
            ) VALUES (
                @identity_id, @decision_id, @site_id, @entitlement_type, @statutory_id_type,
                @envelope, @key_reference, @key_version, @envelope_version,
                @actor_id, @capture_channel, now(), @actor_id, @capture_channel, now(),
                @correlation_reference, false
            )
            ON CONFLICT (statutory_discount_decision_command_id) DO UPDATE SET
                statutory_id_type = EXCLUDED.statutory_id_type,
                protected_identity_envelope = EXCLUDED.protected_identity_envelope,
                protection_key_reference = EXCLUDED.protection_key_reference,
                protection_key_version = EXCLUDED.protection_key_version,
                envelope_format_version = EXCLUDED.envelope_format_version,
                last_updated_by_actor_ref = EXCLUDED.last_updated_by_actor_ref,
                last_updated_by_channel = EXCLUDED.last_updated_by_channel,
                last_updated_at = now(),
                correlation_reference = EXCLUDED.correlation_reference
            WHERE NOT discounts.statutory_beneficiary_identities.is_final;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("identity_id", Guid.NewGuid());
        command.Parameters.AddWithValue("decision_id", statutoryDiscountDecisionCommandId);
        command.Parameters.AddWithValue("site_id", authority.SiteId);
        command.Parameters.AddWithValue("entitlement_type", authority.EntitlementType);
        command.Parameters.AddWithValue("statutory_id_type", normalized!.StatutoryIdType);
        command.Parameters.Add("envelope", NpgsqlDbType.Bytea).Value = protectedIdentity.Envelope;
        command.Parameters.AddWithValue("key_reference", protectedIdentity.KeyReference);
        command.Parameters.AddWithValue("key_version", protectedIdentity.KeyVersion);
        command.Parameters.AddWithValue("envelope_version", protectedIdentity.EnvelopeFormatVersion);
        command.Parameters.AddWithValue("actor_id", actorUserId);
        command.Parameters.AddWithValue("capture_channel", captureChannel);
        command.Parameters.AddWithValue("correlation_reference", correlationReference);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new(StatutoryBeneficiaryIdentityWriteOutcome.Conflict, "STATUTORY_BENEFICIARY_IDENTITY_FINAL");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(StatutoryBeneficiaryIdentityWriteOutcome.Saved, "STATUTORY_BENEFICIARY_IDENTITY_SAVED",
            await GetAsync(statutoryDiscountDecisionCommandId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<StatutoryBeneficiaryIdentityWriteResult> FinalizeApprovedAsync(
        Guid statutoryDiscountDecisionCommandId,
        Guid reviewedByActorRef,
        string approvalDecisionReference,
        Guid correlationReference,
        CancellationToken cancellationToken)
    {
        if (statutoryDiscountDecisionCommandId == Guid.Empty || reviewedByActorRef == Guid.Empty ||
            correlationReference == Guid.Empty || string.IsNullOrWhiteSpace(approvalDecisionReference))
            return new(StatutoryBeneficiaryIdentityWriteOutcome.Invalid, "STATUTORY_BENEFICIARY_FINALIZATION_INVALID");

        const string sql = """
            UPDATE discounts.statutory_beneficiary_identities identity
               SET reviewed_by_actor_ref = @reviewer_id,
                   reviewed_at = now(),
                   approval_decision_reference = @approval_reference,
                   correlation_reference = @correlation_reference,
                   is_final = true
              FROM operator_console.statutory_discount_service_channel_reviews review
             WHERE identity.statutory_discount_decision_command_id = @decision_id
               AND review.statutory_discount_decision_command_id = identity.statutory_discount_decision_command_id
               AND review.review_status = 'APPROVED'
               AND NOT identity.is_final;
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("decision_id", statutoryDiscountDecisionCommandId);
        command.Parameters.AddWithValue("reviewer_id", reviewedByActorRef);
        command.Parameters.AddWithValue("approval_reference", approvalDecisionReference.Trim());
        command.Parameters.AddWithValue("correlation_reference", correlationReference);
        var affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetAsync(statutoryDiscountDecisionCommandId, cancellationToken).ConfigureAwait(false);
        if (affected == 1 || current is { IsFinal: true } &&
            string.Equals(current.ApprovalDecisionReference, approvalDecisionReference.Trim(), StringComparison.Ordinal))
            return new(StatutoryBeneficiaryIdentityWriteOutcome.Saved, "STATUTORY_BENEFICIARY_IDENTITY_FINALIZED", current);

        return new(StatutoryBeneficiaryIdentityWriteOutcome.Invalid, "STATUTORY_BENEFICIARY_IDENTITY_REQUIRED");
    }

    public async Task<StatutoryBeneficiaryIdentityRecord?> GetAsync(
        Guid statutoryDiscountDecisionCommandId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT site_id, entitlement_type, statutory_id_type, protected_identity_envelope,
                   protection_key_reference, protection_key_version, envelope_format_version,
                   captured_by_actor_ref, captured_by_channel, captured_at,
                   last_updated_by_actor_ref, last_updated_by_channel, last_updated_at,
                   reviewed_by_actor_ref, reviewed_at, approval_decision_reference,
                   correlation_reference, is_final
              FROM discounts.statutory_beneficiary_identities
             WHERE statutory_discount_decision_command_id = @decision_id;
            """;
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("decision_id", statutoryDiscountDecisionCommandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;

        var siteId = reader.GetGuid(0);
        var identity = _protector.Unprotect(statutoryDiscountDecisionCommandId, siteId,
            new(reader.GetFieldValue<byte[]>(3), reader.GetString(4), reader.GetString(5), reader.GetInt16(6)));
        return new(
            statutoryDiscountDecisionCommandId,
            siteId,
            reader.GetString(1),
            identity.BeneficiaryFullName,
            reader.GetString(2),
            identity.StatutoryIdNumber,
            identity.BeneficiaryTin,
            reader.GetGuid(7),
            reader.GetString(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.GetGuid(10),
            reader.GetString(11),
            reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetGuid(13),
            reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.GetGuid(16),
            reader.GetBoolean(17));
    }

    private static async Task<DecisionAuthority?> ReadAuthorityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid decisionId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT review.site_id, review.entitlement_type, review.review_status
              FROM operator_console.statutory_discount_service_channel_reviews review
             WHERE review.statutory_discount_decision_command_id = @decision_id
             FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("decision_id", decisionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var status = reader.GetString(2);
        return new(reader.GetGuid(0), reader.GetString(1), status is "APPROVED" or "REJECTED");
    }

    private sealed record DecisionAuthority(Guid SiteId, string EntitlementType, bool IsTerminal);
}
