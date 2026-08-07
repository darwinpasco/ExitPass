using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class PostgresHumanAuthenticationRepository : IHumanAuthenticationRepository
{
    private readonly string _connectionString;
    private readonly IHumanSessionTokenService _tokens;

    public PostgresHumanAuthenticationRepository(string connectionString, IHumanSessionTokenService tokens)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString) ? connectionString : throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _tokens = tokens;
    }

    public async Task<HumanLoginRecord?> FindLocalLoginAsync(string normalizedUsername, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.user_id, u.username, u.display_name, u.user_status::text, u.effective_from,
                   u.effective_to, u.lockout_expires_at, u.lockout_reason_code,
                   u.credential_version, u.authorization_epoch,
                   EXISTS (
                       SELECT 1 FROM identity.user_roles ur
                       JOIN identity.roles r ON r.role_id = ur.role_id
                       WHERE ur.user_id = u.user_id AND ur.assignment_status = 'ACTIVE'
                         AND ur.effective_from <= @now AND (ur.effective_to IS NULL OR ur.effective_to > @now)
                         AND ur.revoked_at IS NULL AND r.role_status = 'ACTIVE'
                         AND r.effective_from <= @now AND (r.effective_to IS NULL OR r.effective_to > @now)
                         AND r.is_privileged
                   ) AS has_privileged_role,
                   lc.local_credential_id, lc.credential_status::text, lc.password_verifier,
                   lc.verifier_salt, lc.verifier_algorithm_code, lc.verifier_algorithm_version,
                   lc.verifier_work_factor, lc.verifier_memory_kib, lc.verifier_parallelism,
                   lc.credential_version AS local_credential_version, lc.row_version AS local_credential_row_version,
                   ma.user_mfa_authenticator_id, ma.authenticator_status::text,
                   ma.protected_secret_envelope, ma.protection_key_reference, ma.protection_key_version,
                   ma.envelope_format_version, ma.last_successfully_used_time_step,
                   ma.row_version AS authenticator_row_version
            FROM identity.users u
            LEFT JOIN LATERAL (
                SELECT * FROM identity.local_credentials c
                WHERE c.user_id = u.user_id
                  AND c.credential_status IN ('PENDING_ACTIVATION','ACTIVE','CHANGE_REQUIRED','LOCKED')
                ORDER BY c.created_at DESC LIMIT 1
            ) lc ON true
            LEFT JOIN LATERAL (
                SELECT * FROM identity.user_mfa_authenticators a
                WHERE a.user_id = u.user_id
                  AND a.authenticator_type = 'TOTP'
                  AND a.authenticator_status IN ('PENDING_ENROLLMENT','ACTIVE','SUSPENDED','RESET_REQUIRED')
                ORDER BY a.created_at DESC LIMIT 1
            ) ma ON true
            WHERE u.username_normalized = @username_normalized;
            """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("username_normalized", normalizedUsername);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLogin(reader) : null;
    }

    public async Task<int> CountRecentFailedAttemptsAsync(Guid? userId, string loginIdentifierHash, string? sourceIpHash, string attemptType, DateTimeOffset since, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)::integer
            FROM identity.authentication_attempts
            WHERE attempt_type = @attempt_type::identity.authentication_attempt_type_enum
              AND attempt_result IN ('INVALID','THROTTLED','LOCKED')
              AND observed_at >= @since
              AND ((@user_id IS NOT NULL AND user_id = @user_id)
                   OR login_identifier_hash = @login_hash
                   OR (@source_hash IS NOT NULL AND source_ip_hash = @source_hash));
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.AddWithValue("login_hash", loginIdentifierHash);
        command.Parameters.Add("source_hash", NpgsqlDbType.Char).Value = (object?)sourceIpHash ?? DBNull.Value;
        command.Parameters.AddWithValue("attempt_type", attemptType);
        command.Parameters.AddWithValue("since", since);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task RecordAuthenticationAttemptAsync(Guid? userId, string loginIdentifierHash, string? sourceIpHash, string? userAgentHash, string attemptType, string result, string audience, string reasonCode, DateTimeOffset observedAt, Guid correlationId, Guid serviceIdentityId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO identity.authentication_attempts (
                authentication_attempt_id, user_id, login_identifier_hash, attempt_type, attempt_result,
                session_audience, source_ip_hash, user_agent_hash, reason_code, observed_at,
                correlation_id, recorded_by_service_identity_id)
            VALUES (gen_random_uuid(), @user_id, @login_hash,
                @attempt_type::identity.authentication_attempt_type_enum,
                @result::identity.authentication_attempt_result_enum,
                @audience::identity.human_session_audience_enum,
                @source_hash, @agent_hash, @reason_code, @observed_at, @correlation_id, @service_identity_id);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = (object?)userId ?? DBNull.Value;
        command.Parameters.AddWithValue("login_hash", loginIdentifierHash);
        command.Parameters.AddWithValue("attempt_type", attemptType);
        command.Parameters.AddWithValue("result", result);
        command.Parameters.AddWithValue("audience", audience);
        command.Parameters.Add("source_hash", NpgsqlDbType.Char).Value = (object?)sourceIpHash ?? DBNull.Value;
        command.Parameters.Add("agent_hash", NpgsqlDbType.Char).Value = (object?)userAgentHash ?? DBNull.Value;
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.AddWithValue("observed_at", observedAt);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task ApplyAuthenticationLockoutAsync(Guid userId, DateTimeOffset lockedAt, DateTimeOffset expiresAt, string reasonCode, Guid serviceIdentityId, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE identity.users
            SET user_status = 'LOCKED', locked_at = @now, lockout_expires_at = @expires_at,
                lockout_reason_code = @reason_code, updated_at = @now,
                updated_by_service_identity_id = @service_identity_id, row_version = row_version + 1
            WHERE user_id = @user_id AND user_status = 'ACTIVE';
            """, cancellationToken,
            ("user_id", userId), ("now", lockedAt), ("expires_at", expiresAt),
            ("reason_code", reasonCode), ("service_identity_id", serviceIdentityId));

    public Task ReleaseExpiredAuthenticationLockoutAsync(Guid userId, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE identity.users
            SET user_status = 'ACTIVE', locked_at = NULL, lockout_expires_at = NULL,
                lockout_reason_code = NULL, updated_at = @now,
                updated_by_service_identity_id = @service_identity_id, row_version = row_version + 1
            WHERE user_id = @user_id AND user_status = 'LOCKED'
              AND lockout_reason_code = 'AUTHENTICATION_FAILURE'
              AND lockout_expires_at <= @now;
            """, cancellationToken, ("user_id", userId), ("now", now), ("service_identity_id", serviceIdentityId));

    public async Task UpdateCredentialVerifierAsync(Guid localCredentialId, long expectedRowVersion, PasswordHashMaterial material, Guid serviceIdentityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.local_credentials
            SET password_verifier=@verifier, verifier_salt=@salt, verifier_algorithm_code=@algorithm,
                verifier_algorithm_version=@algorithm_version, verifier_work_factor=@work_factor,
                verifier_memory_kib=@memory_kib, verifier_parallelism=@parallelism,
                updated_at=@now, updated_by_service_identity_id=@service_identity_id, row_version=row_version+1
            WHERE local_credential_id=@credential_id AND row_version=@row_version;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        AddHashParameters(command, material);
        command.Parameters.AddWithValue("credential_id", localCredentialId);
        command.Parameters.AddWithValue("row_version", expectedRowVersion);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SessionIssue> CreateSessionAsync(Guid userId, Guid localCredentialId, string audience, Guid? deviceServiceIdentityId, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, long credentialVersion, long authorizationEpoch, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, SessionCredential credential, CancellationToken cancellationToken)
    {
        var humanSessionId = Guid.NewGuid();
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await InsertSessionAsync(connection, transaction, humanSessionId, userId, localCredentialId, audience,
            deviceServiceIdentityId, mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt, assuranceContext,
            credentialVersion, authorizationEpoch, now, idleExpiresAt, absoluteExpiresAt, correlationId,
            credential, cancellationToken);
        await using (var command = new NpgsqlCommand("UPDATE identity.users SET last_login_at=@now, updated_at=@now, row_version=row_version+1 WHERE user_id=@user_id;", connection, transaction))
        {
            command.Parameters.AddWithValue("now", now);
            command.Parameters.AddWithValue("user_id", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        var record = await FindSessionAsync(credential.SessionReference, cancellationToken) ?? throw new InvalidOperationException("Issued human session could not be read back.");
        return new SessionIssue(humanSessionId, credential, record);
    }

    public async Task<SessionIssue?> RotateSessionAsync(HumanSessionRecord currentSession, SessionCredential replacementCredential, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, CancellationToken cancellationToken)
    {
        if (!currentSession.LocalCredentialId.HasValue) return null;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var revoke = new NpgsqlCommand("""
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@user_id, revocation_reason_code='SESSION_ROTATED', updated_at=@now,
                updated_by_user_id=@user_id, row_version=row_version+1
            WHERE human_session_id=@session_id AND session_status='ACTIVE' AND row_version=@row_version;
            """, connection, transaction))
        {
            revoke.Parameters.AddWithValue("session_id", currentSession.HumanSessionId);
            revoke.Parameters.AddWithValue("user_id", currentSession.UserId);
            revoke.Parameters.AddWithValue("row_version", currentSession.RowVersion);
            revoke.Parameters.AddWithValue("now", now);
            if (await revoke.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        }

        var replacementId = Guid.NewGuid();
        await InsertSessionAsync(connection, transaction, replacementId, currentSession.UserId,
            currentSession.LocalCredentialId.Value, currentSession.Audience, currentSession.DeviceServiceIdentityId,
            mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt, assuranceContext,
            currentSession.CurrentCredentialVersion, currentSession.CurrentAuthorizationEpoch, now,
            idleExpiresAt, absoluteExpiresAt, correlationId, replacementCredential, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var record = await FindSessionAsync(replacementCredential.SessionReference, cancellationToken)
            ?? throw new InvalidOperationException("Rotated human session could not be read back.");
        return new SessionIssue(replacementId, replacementCredential, record);
    }

    public async Task<SessionIssue?> ChangePasswordAndRotateSessionAsync(HumanSessionRecord currentSession, long expectedCredentialRowVersion, PasswordHashMaterial material, SessionCredential replacementCredential, bool mfaSatisfied, Guid? mfaAuthenticatorId, DateTimeOffset? mfaVerifiedAt, string assuranceContext, DateTimeOffset now, DateTimeOffset idleExpiresAt, DateTimeOffset absoluteExpiresAt, Guid correlationId, CancellationToken cancellationToken)
    {
        if (!currentSession.LocalCredentialId.HasValue) return null;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        long credentialVersion;
        long authorizationEpoch;
        await using (var command = new NpgsqlCommand("""
            UPDATE identity.local_credentials SET credential_status='ACTIVE', password_verifier=@verifier,
                verifier_salt=@salt, verifier_algorithm_code=@algorithm, verifier_algorithm_version=@algorithm_version,
                verifier_work_factor=@work_factor, verifier_memory_kib=@memory_kib, verifier_parallelism=@parallelism,
                credential_version=credential_version+1, activated_at=COALESCE(activated_at,@now), last_changed_at=@now,
                updated_at=@now, updated_by_user_id=@user_id, row_version=row_version+1
            WHERE local_credential_id=@credential_id AND user_id=@user_id AND row_version=@row_version;
            """, connection, transaction))
        {
            AddHashParameters(command, material);
            command.Parameters.AddWithValue("credential_id", currentSession.LocalCredentialId.Value);
            command.Parameters.AddWithValue("user_id", currentSession.UserId);
            command.Parameters.AddWithValue("row_version", expectedCredentialRowVersion);
            command.Parameters.AddWithValue("now", now);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        }
        await using (var command = new NpgsqlCommand("""
            UPDATE identity.users SET credential_version=credential_version+1, updated_at=@now,
                updated_by_user_id=@user_id, row_version=row_version+1
            WHERE user_id=@user_id
            RETURNING credential_version, authorization_epoch;
            """, connection, transaction))
        {
            command.Parameters.AddWithValue("user_id", currentSession.UserId);
            command.Parameters.AddWithValue("now", now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            credentialVersion = reader.GetInt64(0);
            authorizationEpoch = reader.GetInt64(1);
        }
        await using (var revoke = new NpgsqlCommand("""
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@user_id, revocation_reason_code='CREDENTIAL_CHANGED', updated_at=@now,
                updated_by_user_id=@user_id, row_version=row_version+1
            WHERE user_id=@user_id AND session_status='ACTIVE';
            """, connection, transaction))
        {
            revoke.Parameters.AddWithValue("user_id", currentSession.UserId);
            revoke.Parameters.AddWithValue("now", now);
            await revoke.ExecuteNonQueryAsync(cancellationToken);
        }
        var replacementId = Guid.NewGuid();
        await InsertSessionAsync(connection, transaction, replacementId, currentSession.UserId,
            currentSession.LocalCredentialId.Value, currentSession.Audience, currentSession.DeviceServiceIdentityId,
            mfaSatisfied, mfaAuthenticatorId, mfaVerifiedAt, assuranceContext, credentialVersion,
            authorizationEpoch, now, idleExpiresAt, absoluteExpiresAt, correlationId, replacementCredential,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var record = await FindSessionAsync(replacementCredential.SessionReference, cancellationToken)
            ?? throw new InvalidOperationException("Password-change session could not be read back.");
        return new SessionIssue(replacementId, replacementCredential, record);
    }

    public async Task<HumanSessionRecord?> FindSessionAsync(Guid sessionReference, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT hs.human_session_id, hs.session_reference, hs.session_secret_hash, hs.user_id,
                   u.username, u.display_name, u.user_status::text, u.effective_from, u.effective_to,
                   u.lockout_expires_at, hs.authentication_provider::text, hs.local_credential_id,
                   lc.credential_status::text, hs.external_identity_binding_id, hs.session_audience::text,
                   hs.device_service_identity_id, hs.session_status::text, hs.assurance_context_code,
                   hs.mfa_requirement_satisfied, hs.mfa_authenticator_id, hs.mfa_verified_at,
                   hs.authenticated_at, hs.last_seen_at, hs.idle_expires_at, hs.absolute_expires_at,
                   hs.credential_version_snapshot, hs.authorization_epoch_snapshot,
                   u.credential_version, u.authorization_epoch,
                   EXISTS (
                       SELECT 1 FROM identity.user_roles ur JOIN identity.roles r ON r.role_id=ur.role_id
                       WHERE ur.user_id=u.user_id AND ur.assignment_status='ACTIVE' AND ur.revoked_at IS NULL
                         AND ur.effective_from<=now() AND (ur.effective_to IS NULL OR ur.effective_to>now())
                         AND r.role_status='ACTIVE' AND r.is_privileged
                         AND r.effective_from<=now() AND (r.effective_to IS NULL OR r.effective_to>now())
                   ), hs.correlation_id, hs.row_version
            FROM identity.human_sessions hs
            JOIN identity.users u ON u.user_id=hs.user_id
            LEFT JOIN identity.local_credentials lc ON lc.local_credential_id=hs.local_credential_id
            WHERE hs.session_reference=@session_reference;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("session_reference", sessionReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new HumanSessionRecord(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7), GetNullableDateTime(reader, 8), GetNullableDateTime(reader, 9), reader.GetString(10),
            GetNullableGuid(reader, 11), GetNullableString(reader, 12), GetNullableGuid(reader, 13), reader.GetString(14), GetNullableGuid(reader, 15),
            reader.GetString(16), reader.GetString(17), reader.GetBoolean(18), GetNullableGuid(reader, 19), GetNullableDateTime(reader, 20),
            reader.GetFieldValue<DateTimeOffset>(21), reader.GetFieldValue<DateTimeOffset>(22), reader.GetFieldValue<DateTimeOffset>(23), reader.GetFieldValue<DateTimeOffset>(24),
            reader.GetInt64(25), reader.GetInt64(26), reader.GetInt64(27), reader.GetInt64(28), reader.GetBoolean(29), reader.GetGuid(30), reader.GetInt64(31));
    }

    public async Task<EffectiveHumanAuthorization> GetEffectiveAuthorizationAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH effective_roles AS (
                SELECT ur.user_role_id, ur.role_id FROM identity.user_roles ur
                JOIN identity.roles r ON r.role_id=ur.role_id
                WHERE ur.user_id=@user_id AND ur.assignment_status='ACTIVE' AND ur.revoked_at IS NULL
                  AND ur.effective_from<=@now AND (ur.effective_to IS NULL OR ur.effective_to>@now)
                  AND r.role_status='ACTIVE' AND r.effective_from<=@now AND (r.effective_to IS NULL OR r.effective_to>@now)
            ), perms AS (
                SELECT DISTINCT p.permission_code FROM effective_roles er
                JOIN identity.role_permissions rp ON rp.role_id=er.role_id
                JOIN identity.permissions p ON p.permission_id=rp.permission_id
                WHERE rp.binding_status='ACTIVE' AND rp.revoked_at IS NULL
                  AND rp.effective_from<=@now AND (rp.effective_to IS NULL OR rp.effective_to>@now)
                  AND p.permission_status='ACTIVE'
            ), scopes AS (
                SELECT DISTINCT g.scope_type::text AS scope_type, g.site_id, g.site_group_id
                FROM effective_roles er JOIN identity.user_role_scope_grants g ON g.user_role_id=er.user_role_id
                WHERE g.grant_status='ACTIVE' AND g.revoked_at IS NULL
                  AND g.effective_from<=@now AND (g.effective_to IS NULL OR g.effective_to>@now)
            )
            SELECT COALESCE((SELECT array_agg(permission_code ORDER BY permission_code) FROM perms), ARRAY[]::varchar[]),
                   COALESCE((SELECT array_agg(site_id ORDER BY site_id) FROM scopes WHERE scope_type='SITE'), ARRAY[]::uuid[]),
                   COALESCE((SELECT array_agg(site_group_id ORDER BY site_group_id) FROM scopes WHERE scope_type='SITE_GROUP'), ARRAY[]::uuid[]),
                   EXISTS(SELECT 1 FROM scopes WHERE scope_type='GLOBAL');
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new EffectiveHumanAuthorization(reader.GetFieldValue<string[]>(0), reader.GetFieldValue<Guid[]>(1), reader.GetFieldValue<Guid[]>(2), reader.GetBoolean(3));
    }

    public async Task<bool> TouchSessionAsync(Guid humanSessionId, long expectedRowVersion, DateTimeOffset now, DateTimeOffset idleExpiresAt, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.human_sessions
            SET last_seen_at=@now, idle_expires_at=LEAST(@idle_expires_at, absolute_expires_at), updated_at=@now, row_version=row_version+1
            WHERE human_session_id=@session_id AND row_version=@row_version AND session_status='ACTIVE'
              AND idle_expires_at>@now AND absolute_expires_at>@now;
            """;
        return await ExecuteCountAsync(sql, cancellationToken, ("session_id", humanSessionId), ("row_version", expectedRowVersion), ("now", now), ("idle_expires_at", idleExpiresAt)) == 1;
    }

    public Task MarkSessionExpiredAsync(Guid humanSessionId, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE identity.human_sessions SET session_status='EXPIRED', updated_at=@now, row_version=row_version+1
            WHERE human_session_id=@session_id AND session_status='ACTIVE';
            """, cancellationToken, ("session_id", humanSessionId), ("now", now));

    public Task RevokeSessionAsync(Guid humanSessionId, Guid actorUserId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@actor_user_id, revocation_reason_code=@reason_code,
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=row_version+1
            WHERE human_session_id=@session_id AND session_status='ACTIVE';
            """, cancellationToken, ("session_id", humanSessionId), ("actor_user_id", actorUserId), ("reason_code", reasonCode), ("now", now));

    public Task<int> RevokeAllUserSessionsAsync(Guid userId, Guid actorUserId, string reasonCode, DateTimeOffset now, Guid? exceptHumanSessionId, CancellationToken cancellationToken) =>
        ExecuteCountAsync("""
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@actor_user_id, revocation_reason_code=@reason_code,
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=row_version+1
            WHERE user_id=@user_id AND session_status='ACTIVE'
              AND (@except_session_id='00000000-0000-0000-0000-000000000000'::uuid OR human_session_id<>@except_session_id);
            """, cancellationToken, ("user_id", userId), ("actor_user_id", actorUserId), ("reason_code", reasonCode), ("now", now), ("except_session_id", exceptHumanSessionId ?? Guid.Empty));

    public async Task<bool> IsActiveDeviceServiceAtSiteAsync(Guid serviceIdentityId, Guid siteId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM identity.service_identities si
                JOIN sites.device_assignments da ON da.service_identity_id=si.service_identity_id
                WHERE si.service_identity_id=@service_identity_id AND si.identity_status='ACTIVE'
                  AND si.effective_from<=@now AND (si.effective_to IS NULL OR si.effective_to>@now)
                  AND si.revoked_at IS NULL AND da.site_id=@site_id AND da.assignment_status='ACTIVE'
                  AND da.assignment_type IN ('PAYMENT_DEVICE','SERVICE_PRINCIPAL') AND da.unassigned_at IS NULL);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        command.Parameters.AddWithValue("site_id", siteId);
        command.Parameters.AddWithValue("now", now);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<bool> TryRecordTotpSuccessAsync(Guid authenticatorId, long expectedRowVersion, long matchedTimeStep, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.user_mfa_authenticators
            SET last_successfully_used_at=@now, last_successfully_used_time_step=@time_step,
                updated_at=@now, updated_by_service_identity_id=@service_identity_id, row_version=row_version+1
            WHERE user_mfa_authenticator_id=@authenticator_id AND row_version=@row_version
              AND authenticator_status='ACTIVE'
              AND (last_successfully_used_time_step IS NULL OR last_successfully_used_time_step<@time_step);
            """;
        return await ExecuteCountAsync(sql, cancellationToken, ("authenticator_id", authenticatorId), ("row_version", expectedRowVersion), ("time_step", matchedTimeStep), ("now", now), ("service_identity_id", serviceIdentityId)) == 1;
    }

    public async Task<TotpAuthenticatorRecord?> CreatePendingTotpAuthenticatorAsync(Guid authenticatorId, Guid userId, byte[] protectedEnvelope, string keyReference, string keyVersion, short formatVersion, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.user_mfa_authenticators
            SET authenticator_status='REVOKED', reset_at=NULL, revoked_at=@now,
                reset_or_revoked_by_user_id=@actor_user_id, status_reason_code='RESET_REENROLLMENT',
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=row_version+1
            WHERE user_id=@user_id AND authenticator_type='TOTP' AND authenticator_status='RESET_REQUIRED';

            INSERT INTO identity.user_mfa_authenticators (
                user_mfa_authenticator_id, user_id, authenticator_type, authenticator_status,
                protected_secret_envelope, protection_key_reference, protection_key_version,
                envelope_format_version, enrollment_started_at, created_at, created_by_user_id,
                updated_at, updated_by_user_id)
            VALUES (@id,@user_id,'TOTP','PENDING_ENROLLMENT',@envelope,@key_reference,@key_version,
                @format_version,@now,@now,@actor_user_id,@now,@actor_user_id)
            RETURNING user_mfa_authenticator_id, authenticator_status::text, protected_secret_envelope,
                protection_key_reference, protection_key_version, envelope_format_version,
                last_successfully_used_time_step, row_version;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", authenticatorId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("envelope", protectedEnvelope);
        command.Parameters.AddWithValue("key_reference", keyReference);
        command.Parameters.AddWithValue("key_version", keyVersion);
        command.Parameters.AddWithValue("format_version", formatVersion);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = await reader.ReadAsync(cancellationToken) ? ReadAuthenticator(reader) : null;
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<TotpAuthenticatorRecord?> GetCurrentTotpAuthenticatorAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_mfa_authenticator_id, authenticator_status::text, protected_secret_envelope,
                   protection_key_reference, protection_key_version, envelope_format_version,
                   last_successfully_used_time_step, row_version
            FROM identity.user_mfa_authenticators
            WHERE user_id=@user_id AND authenticator_type='TOTP'
              AND authenticator_status IN ('PENDING_ENROLLMENT','ACTIVE','SUSPENDED','RESET_REQUIRED')
            ORDER BY created_at DESC LIMIT 1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadAuthenticator(reader) : null;
    }

    public async Task<bool> ConfirmTotpAuthenticatorAsync(Guid authenticatorId, long expectedRowVersion, long matchedTimeStep, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.user_mfa_authenticators
            SET authenticator_status='ACTIVE', activated_at=@now,
                last_successfully_used_at=@now, last_successfully_used_time_step=@time_step,
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=row_version+1
            WHERE user_mfa_authenticator_id=@id AND row_version=@row_version
              AND authenticator_status='PENDING_ENROLLMENT';
            """;
        return await ExecuteCountAsync(sql, cancellationToken, ("id", authenticatorId), ("row_version", expectedRowVersion), ("time_step", matchedTimeStep), ("now", now), ("actor_user_id", actorUserId)) == 1;
    }

    public async Task ResetTotpAuthenticatorAsync(Guid userId, Guid actorUserId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE identity.user_mfa_authenticators
            SET authenticator_status='RESET_REQUIRED', reset_at=@now,
                reset_or_revoked_by_user_id=@actor_user_id, status_reason_code=@reason_code,
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=row_version+1
            WHERE user_id=@user_id AND authenticator_type='TOTP' AND authenticator_status='ACTIVE';

            UPDATE identity.human_sessions hs SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@actor_user_id, revocation_reason_code='MFA_RESET',
                updated_at=@now, updated_by_user_id=@actor_user_id, row_version=hs.row_version+1
            WHERE hs.user_id=@user_id AND hs.session_status='ACTIVE' AND hs.mfa_requirement_satisfied;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task ChangePasswordAsync(Guid userId, Guid localCredentialId, long expectedCredentialRowVersion, PasswordHashMaterial material, DateTimeOffset now, Guid actorUserId, CancellationToken cancellationToken) =>
        ReplacePasswordAsync(userId, localCredentialId, expectedCredentialRowVersion, material, now, actorUserId, null, cancellationToken);

    public async Task<(Guid Reference, string Secret)> CreateCredentialChallengeAsync(Guid userId, string purpose, DateTimeOffset issuedAt, DateTimeOffset expiresAt, Guid requestorServiceIdentityId, Guid correlationId, CancellationToken cancellationToken)
    {
        var reference = Guid.NewGuid();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        const string sql = """
            UPDATE identity.credential_challenges SET challenge_status='REVOKED', revoked_at=@issued_at,
                revoked_by_service_identity_id=@service_identity_id, reason_code='SUPERSEDED', row_version=row_version+1
            WHERE user_id=@user_id AND challenge_purpose=@purpose::identity.credential_challenge_purpose_enum
              AND challenge_status='ISSUED';
            INSERT INTO identity.credential_challenges (
                credential_challenge_id, challenge_reference, user_id, challenge_purpose,
                challenge_status, challenge_secret_hash, issued_at, expires_at,
                requested_by_service_identity_id, reason_code, correlation_id)
            VALUES (gen_random_uuid(),@reference,@user_id,@purpose::identity.credential_challenge_purpose_enum,
                'ISSUED',@secret_hash,@issued_at,@expires_at,@service_identity_id,'USER_REQUEST',@correlation_id);
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("reference", reference);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("purpose", purpose);
        command.Parameters.AddWithValue("secret_hash", _tokens.HashSecret(secret));
        command.Parameters.AddWithValue("issued_at", issuedAt);
        command.Parameters.AddWithValue("expires_at", expiresAt);
        command.Parameters.AddWithValue("service_identity_id", requestorServiceIdentityId);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (reference, secret);
    }

    public async Task<(Guid UserId, Guid ChallengeId)?> ConsumeCredentialChallengeAsync(Guid challengeReference, string challengeSecretHash, string purpose, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.credential_challenges
            SET challenge_status='CONSUMED', consumed_at=@now, row_version=row_version+1
            WHERE challenge_reference=@reference AND challenge_secret_hash=@secret_hash
              AND challenge_purpose=@purpose::identity.credential_challenge_purpose_enum
              AND challenge_status='ISSUED' AND expires_at>@now
            RETURNING user_id, credential_challenge_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("reference", challengeReference);
        command.Parameters.AddWithValue("secret_hash", challengeSecretHash);
        command.Parameters.AddWithValue("purpose", purpose);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetGuid(0), reader.GetGuid(1)) : null;
    }

    public Task RevokeCredentialChallengeAsync(Guid challengeReference, Guid serviceIdentityId, string reasonCode, DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync("""
            UPDATE identity.credential_challenges SET challenge_status='REVOKED', revoked_at=@now,
                revoked_by_service_identity_id=@service_identity_id, reason_code=@reason_code,
                row_version=row_version+1
            WHERE challenge_reference=@reference AND challenge_status='ISSUED';
            """, cancellationToken, ("reference", challengeReference), ("service_identity_id", serviceIdentityId),
            ("reason_code", reasonCode), ("now", now));

    public async Task<LocalCredentialRecord?> GetCurrentLocalCredentialAsync(Guid userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT local_credential_id, credential_status::text, password_verifier, verifier_salt,
                   verifier_algorithm_code, verifier_algorithm_version, verifier_work_factor,
                   verifier_memory_kib, verifier_parallelism, credential_version, row_version
            FROM identity.local_credentials WHERE user_id=@user_id
              AND credential_status IN ('PENDING_ACTIVATION','ACTIVE','CHANGE_REQUIRED','LOCKED')
            ORDER BY created_at DESC LIMIT 1;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadCredential(reader, 0) : null;
    }

    public async Task CompletePasswordResetAsync(Guid userId, Guid challengeId, PasswordHashMaterial material, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken)
    {
        var credential = await GetCurrentLocalCredentialAsync(userId, cancellationToken) ?? throw new InvalidOperationException("A current local credential is required.");
        await ReplacePasswordAsync(userId, credential.LocalCredentialId, credential.RowVersion, material, now, null, serviceIdentityId, cancellationToken);
    }

    public async Task<Guid?> CompleteCredentialChallengeAsync(Guid challengeReference, string challengeSecretHash, string purpose, PasswordHashMaterial material, DateTimeOffset now, Guid serviceIdentityId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        Guid userId;
        Guid challengeId;
        await using (var challenge = new NpgsqlCommand("""
            SELECT user_id, credential_challenge_id
            FROM identity.credential_challenges
            WHERE challenge_reference=@reference AND challenge_secret_hash=@secret_hash
              AND challenge_purpose=@purpose::identity.credential_challenge_purpose_enum
              AND challenge_status='ISSUED' AND expires_at>@now
            FOR UPDATE;
            """, connection, transaction))
        {
            challenge.Parameters.AddWithValue("reference", challengeReference);
            challenge.Parameters.AddWithValue("secret_hash", challengeSecretHash);
            challenge.Parameters.AddWithValue("purpose", purpose);
            challenge.Parameters.AddWithValue("now", now);
            await using var reader = await challenge.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            userId = reader.GetGuid(0);
            challengeId = reader.GetGuid(1);
        }

        Guid credentialId;
        long credentialRowVersion;
        await using (var credential = new NpgsqlCommand("""
            SELECT local_credential_id, row_version
            FROM identity.local_credentials
            WHERE user_id=@user_id AND credential_status IN ('PENDING_ACTIVATION','ACTIVE','CHANGE_REQUIRED','LOCKED')
            ORDER BY created_at DESC LIMIT 1 FOR UPDATE;
            """, connection, transaction))
        {
            credential.Parameters.AddWithValue("user_id", userId);
            await using var reader = await credential.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            credentialId = reader.GetGuid(0);
            credentialRowVersion = reader.GetInt64(1);
        }

        await using (var credential = new NpgsqlCommand("""
            UPDATE identity.local_credentials SET credential_status='ACTIVE', password_verifier=@verifier,
                verifier_salt=@salt, verifier_algorithm_code=@algorithm, verifier_algorithm_version=@algorithm_version,
                verifier_work_factor=@work_factor, verifier_memory_kib=@memory_kib, verifier_parallelism=@parallelism,
                credential_version=credential_version+1, activated_at=COALESCE(activated_at,@now), last_changed_at=@now,
                updated_at=@now, updated_by_service_identity_id=@service_identity_id, row_version=row_version+1
            WHERE local_credential_id=@credential_id AND user_id=@user_id AND row_version=@row_version;
            """, connection, transaction))
        {
            AddHashParameters(credential, material);
            credential.Parameters.AddWithValue("credential_id", credentialId);
            credential.Parameters.AddWithValue("user_id", userId);
            credential.Parameters.AddWithValue("row_version", credentialRowVersion);
            credential.Parameters.AddWithValue("now", now);
            credential.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
            if (await credential.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        }

        await using (var complete = new NpgsqlCommand("""
            UPDATE identity.credential_challenges SET challenge_status='CONSUMED', consumed_at=@now,
                row_version=row_version+1 WHERE credential_challenge_id=@challenge_id AND challenge_status='ISSUED';
            """, connection, transaction))
        {
            complete.Parameters.AddWithValue("challenge_id", challengeId);
            complete.Parameters.AddWithValue("now", now);
            if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        }
        await using (var updateUser = new NpgsqlCommand("""
            UPDATE identity.users SET user_status=CASE WHEN @purpose='ACCOUNT_ACTIVATION'
                    THEN 'ACTIVE'::identity.user_status_enum ELSE user_status END,
                credential_version=credential_version+1, updated_at=@now,
                updated_by_service_identity_id=@service_identity_id, row_version=row_version+1
            WHERE user_id=@user_id AND (@purpose<>'ACCOUNT_ACTIVATION' OR user_status='INVITED');
            """, connection, transaction))
        {
            updateUser.Parameters.AddWithValue("user_id", userId);
            updateUser.Parameters.AddWithValue("purpose", purpose);
            updateUser.Parameters.AddWithValue("now", now);
            updateUser.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
            if (await updateUser.ExecuteNonQueryAsync(cancellationToken) != 1) return null;
        }
        await using (var revokeSessions = new NpgsqlCommand("""
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_service_identity_id=@service_identity_id, revocation_reason_code='CREDENTIAL_CHANGED',
                updated_at=@now, updated_by_service_identity_id=@service_identity_id, row_version=row_version+1
            WHERE user_id=@user_id AND session_status='ACTIVE';
            """, connection, transaction))
        {
            revokeSessions.Parameters.AddWithValue("user_id", userId);
            revokeSessions.Parameters.AddWithValue("now", now);
            revokeSessions.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
            await revokeSessions.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return userId;
    }

    public async Task RecordSecurityEventAsync(string eventType, string result, string reasonCode, Guid? targetEntityId, Guid? actorUserId, string? sourceIpHash, string? userAgentHash, Guid correlationId, Guid serviceIdentityId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH audit_entry AS (
                INSERT INTO audit.audit_events (
                    audit_event_id, event_type, event_category, event_result, event_reason_code,
                    target_entity_type, target_entity_id, source_schema, source_service_name,
                    actor_user_id, actor_service_identity_id, actor_ip_hash, actor_user_agent_hash,
                    summary, occurred_at, recorded_at, correlation_id, created_at,
                    created_by_service_identity_id)
                VALUES (gen_random_uuid(),@event_type,'SECURITY_RELEVANT',
                    CASE WHEN @result='ALLOWED' THEN 'SUCCESS'
                         WHEN @result='FAILED' THEN 'FAILED'
                         WHEN @result IN ('BLOCKED','DENIED') THEN 'DENIED'
                         WHEN @result='REJECTED' THEN 'REJECTED'
                         ELSE 'UNKNOWN' END::audit.audit_event_result_enum,
                    @reason_code,'HUMAN_IDENTITY',@target_entity_id,'identity','ExitPass.CentralPms.Api',
                    @actor_user_id,@service_identity_id,@source_ip_hash,@user_agent_hash,
                    'Privacy-safe human authentication event.',@now,@now,@correlation_id,@now,@service_identity_id)
                RETURNING audit_event_id
            )
            INSERT INTO audit.security_events (
                security_event_id, audit_event_id, security_event_type, security_event_category, security_severity,
                security_event_status, result, reason_code, target_entity_type, target_entity_id,
                actor_user_id, actor_service_identity_id, source_ip_hash, user_agent_hash,
                detected_at, recorded_at, correlation_id, created_at, created_by_service_identity_id)
            SELECT gen_random_uuid(),audit_event_id,@event_type,'AUTHENTICATION',
                CASE WHEN @result IN ('BLOCKED','FAILED') THEN 'MEDIUM' ELSE 'LOW' END::audit.security_severity_enum,
                'CLOSED',@result::audit.security_event_result_enum,@reason_code,'HUMAN_IDENTITY',@target_entity_id,
                @actor_user_id,@service_identity_id,@source_ip_hash,@user_agent_hash,
                @now,@now,@correlation_id,@now,@service_identity_id
            FROM audit_entry;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("result", result);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.Add("target_entity_id", NpgsqlDbType.Uuid).Value = (object?)targetEntityId ?? DBNull.Value;
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)actorUserId ?? DBNull.Value;
        command.Parameters.AddWithValue("service_identity_id", serviceIdentityId);
        command.Parameters.Add("source_ip_hash", NpgsqlDbType.Char).Value = (object?)sourceIpHash ?? DBNull.Value;
        command.Parameters.Add("user_agent_hash", NpgsqlDbType.Char).Value = (object?)userAgentHash ?? DBNull.Value;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReplacePasswordAsync(Guid userId, Guid localCredentialId, long expectedCredentialRowVersion, PasswordHashMaterial material, DateTimeOffset now, Guid? actorUserId, Guid? serviceIdentityId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE identity.local_credentials SET credential_status='ACTIVE', password_verifier=@verifier,
                verifier_salt=@salt, verifier_algorithm_code=@algorithm, verifier_algorithm_version=@algorithm_version,
                verifier_work_factor=@work_factor, verifier_memory_kib=@memory_kib, verifier_parallelism=@parallelism,
                credential_version=credential_version+1, activated_at=COALESCE(activated_at,@now), last_changed_at=@now,
                updated_at=@now, updated_by_user_id=@actor_user_id, updated_by_service_identity_id=@service_identity_id,
                row_version=row_version+1
            WHERE local_credential_id=@credential_id AND user_id=@user_id AND row_version=@row_version;
            UPDATE identity.users SET credential_version=credential_version+1, updated_at=@now,
                updated_by_user_id=@actor_user_id, updated_by_service_identity_id=@service_identity_id,
                row_version=row_version+1 WHERE user_id=@user_id;
            UPDATE identity.human_sessions SET session_status='REVOKED', revoked_at=@now,
                revoked_by_user_id=@actor_user_id, revoked_by_service_identity_id=@service_identity_id,
                revocation_reason_code='CREDENTIAL_CHANGED', updated_at=@now,
                updated_by_user_id=@actor_user_id, updated_by_service_identity_id=@service_identity_id,
                row_version=row_version+1 WHERE user_id=@user_id AND session_status='ACTIVE';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddHashParameters(command, material);
        command.Parameters.AddWithValue("credential_id", localCredentialId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("row_version", expectedCredentialRowVersion);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add("actor_user_id", NpgsqlDbType.Uuid).Value = (object?)actorUserId ?? DBNull.Value;
        command.Parameters.Add("service_identity_id", NpgsqlDbType.Uuid).Value = (object?)serviceIdentityId ?? DBNull.Value;
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected < 2) throw new InvalidOperationException("The credential changed concurrently.");
        await transaction.CommitAsync(cancellationToken);
    }

    private static HumanLoginRecord ReadLogin(NpgsqlDataReader reader)
    {
        LocalCredentialRecord? credential = null;
        if (!reader.IsDBNull(11))
        {
            credential = new LocalCredentialRecord(reader.GetGuid(11), reader.GetString(12), (byte[])reader[13], (byte[])reader[14], reader.GetString(15), reader.GetInt16(16), reader.GetInt32(17), GetNullableInt(reader, 18), GetNullableShort(reader, 19), reader.GetInt64(20), reader.GetInt64(21));
        }
        TotpAuthenticatorRecord? authenticator = null;
        if (!reader.IsDBNull(22))
        {
            authenticator = new TotpAuthenticatorRecord(reader.GetGuid(22), reader.GetString(23), (byte[])reader[24], reader.GetString(25), reader.GetString(26), reader.GetInt16(27), GetNullableLong(reader, 28), reader.GetInt64(29));
        }
        return new HumanLoginRecord(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4), GetNullableDateTime(reader, 5), GetNullableDateTime(reader, 6), GetNullableString(reader, 7), reader.GetInt64(8), reader.GetInt64(9), reader.GetBoolean(10), credential, authenticator);
    }

    private static LocalCredentialRecord ReadCredential(NpgsqlDataReader reader, int offset) =>
        new(reader.GetGuid(offset), reader.GetString(offset + 1), (byte[])reader[offset + 2], (byte[])reader[offset + 3], reader.GetString(offset + 4), reader.GetInt16(offset + 5), reader.GetInt32(offset + 6), GetNullableInt(reader, offset + 7), GetNullableShort(reader, offset + 8), reader.GetInt64(offset + 9), reader.GetInt64(offset + 10));

    private static TotpAuthenticatorRecord ReadAuthenticator(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), (byte[])reader[2], reader.GetString(3), reader.GetString(4), reader.GetInt16(5), GetNullableLong(reader, 6), reader.GetInt64(7));

    private static void AddHashParameters(NpgsqlCommand command, PasswordHashMaterial material)
    {
        command.Parameters.AddWithValue("verifier", material.Verifier);
        command.Parameters.AddWithValue("salt", material.Salt);
        command.Parameters.AddWithValue("algorithm", material.AlgorithmCode);
        command.Parameters.AddWithValue("algorithm_version", material.AlgorithmVersion);
        command.Parameters.AddWithValue("work_factor", material.Iterations);
        command.Parameters.AddWithValue("memory_kib", material.MemoryKiB);
        command.Parameters.AddWithValue("parallelism", material.Parallelism);
    }

    private async Task InsertSessionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid humanSessionId,
        Guid userId,
        Guid localCredentialId,
        string audience,
        Guid? deviceServiceIdentityId,
        bool mfaSatisfied,
        Guid? mfaAuthenticatorId,
        DateTimeOffset? mfaVerifiedAt,
        string assuranceContext,
        long credentialVersion,
        long authorizationEpoch,
        DateTimeOffset now,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt,
        Guid correlationId,
        SessionCredential credential,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO identity.human_sessions (
                human_session_id, session_reference, session_secret_hash, user_id, authentication_provider,
                local_credential_id, session_audience, device_service_identity_id, session_status,
                assurance_context_code, mfa_requirement_satisfied, mfa_authenticator_id, mfa_verified_at,
                authenticated_at, last_seen_at, idle_expires_at, absolute_expires_at,
                credential_version_snapshot, authorization_epoch_snapshot, correlation_id)
            VALUES (@session_id, @session_reference, @secret_hash, @user_id, 'LOCAL', @credential_id,
                @audience::identity.human_session_audience_enum, @device_service_identity_id, 'ACTIVE',
                @assurance, @mfa_satisfied, @mfa_authenticator_id, @mfa_verified_at,
                @now, @now, @idle_expires_at, @absolute_expires_at,
                @credential_version, @authorization_epoch, @correlation_id);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("session_id", humanSessionId);
        command.Parameters.AddWithValue("session_reference", credential.SessionReference);
        command.Parameters.AddWithValue("secret_hash", _tokens.HashSecret(credential.Secret));
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("credential_id", localCredentialId);
        command.Parameters.AddWithValue("audience", audience);
        command.Parameters.Add("device_service_identity_id", NpgsqlDbType.Uuid).Value = (object?)deviceServiceIdentityId ?? DBNull.Value;
        command.Parameters.AddWithValue("assurance", assuranceContext);
        command.Parameters.AddWithValue("mfa_satisfied", mfaSatisfied);
        command.Parameters.Add("mfa_authenticator_id", NpgsqlDbType.Uuid).Value = (object?)mfaAuthenticatorId ?? DBNull.Value;
        command.Parameters.Add("mfa_verified_at", NpgsqlDbType.TimestampTz).Value = (object?)mfaVerifiedAt ?? DBNull.Value;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("idle_expires_at", idleExpiresAt);
        command.Parameters.AddWithValue("absolute_expires_at", absoluteExpiresAt);
        command.Parameters.AddWithValue("credential_version", credentialVersion);
        command.Parameters.AddWithValue("authorization_epoch", authorizationEpoch);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters) =>
        _ = await ExecuteCountAsync(sql, cancellationToken, parameters);

    private async Task<int> ExecuteCountAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static DateTimeOffset? GetNullableDateTime(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    private static int? GetNullableInt(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    private static short? GetNullableShort(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt16(ordinal);
    private static long? GetNullableLong(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
}
