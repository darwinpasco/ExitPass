using ExitPass.CentralPms.Application.ManagementPlatform;
using ExitPass.CentralPms.Contracts.ManagementPlatform;
using Npgsql;
using System.Runtime.CompilerServices;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

/// <summary>
/// Persists governed devices by extending canonical service identities and Site assignments.
/// </summary>
/// <remarks>
/// BRD v1.2 Sections 9.16 and 9.21; SDD v1.2 Sections 10 and 14.
/// All lifecycle mutations are atomic with runtime trust state and canonical audit evidence.
/// </remarks>
public sealed class PostgresDeviceAdministrationRepository : IDeviceAdministrationRepository
{
    private readonly string _connectionString;

    public PostgresDeviceAdministrationRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<IReadOnlyList<DeviceSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(SummarySql + " ORDER BY device.created_at DESC, device.device_id;", connection);
        return await ReadSummariesAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceAdministrationSiteScopeDto>> ListScopesAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT site.site_id,site.site_code,site.site_name,
                   site_group.site_group_id,site_group.site_group_code,site_group.site_group_name
            FROM sites.sites site
            JOIN sites.site_groups site_group ON site_group.site_group_id=site.site_group_id
            WHERE site.site_status='ACTIVE' AND site.effective_from<=now()
              AND (site.effective_to IS NULL OR site.effective_to>now())
              AND site_group.site_group_status='ACTIVE' AND site_group.effective_from<=now()
              AND (site_group.effective_to IS NULL OR site_group.effective_to>now())
            ORDER BY site_group.site_group_name,site.site_name,site.site_id;
            """;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<DeviceAdministrationSiteScopeDto>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetGuid(3), reader.GetString(4), reader.GetString(5)));
        }
        return results;
    }

    public async Task<DeviceDetailsDto?> GetAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var summary = await ReadSummaryAsync(connection, null, deviceId, cancellationToken);
        if (summary is null) return null;

        var provisioning = new List<DeviceProvisioningHistoryDto>();
        await using (var command = new NpgsqlCommand("""
            SELECT device_provisioning_credential_id, provisioning_purpose::text,
                   provisioning_status::text, issued_at, expires_at, consumed_at,
                   cancelled_at, intended_credential_version, correlation_id
            FROM identity.device_provisioning_credentials
            WHERE device_id=@device_id
            ORDER BY issued_at DESC;
            """, connection))
        {
            command.Parameters.AddWithValue("device_id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                provisioning.Add(new(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4),
                    NullableDateTime(reader, 5), NullableDateTime(reader, 6), reader.GetInt64(7), reader.GetGuid(8)));
            }
        }

        var credentials = new List<DeviceCredentialHistoryDto>();
        await using (var command = new NpgsqlCommand("""
            SELECT credential_version, credential_type::text, credential_status::text,
                   activated_at, expires_at, revoked_at, revocation_reason_code, correlation_id
            FROM identity.device_credential_history
            WHERE device_id=@device_id
            ORDER BY credential_version DESC;
            """, connection))
        {
            command.Parameters.AddWithValue("device_id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                credentials.Add(new(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3), NullableDateTime(reader, 4),
                    NullableDateTime(reader, 5), NullableString(reader, 6), reader.GetGuid(7)));
            }
        }

        var actions = new List<DeviceAdministrativeActionDto>();
        await using (var command = new NpgsqlCommand("""
            SELECT action_code, previous_status::text, resulting_status::text,
                   site_id, site_group_id, previous_credential_version,
                   resulting_credential_version, actor_user_id, actor_service_identity_id,
                   reason_code, correlation_id, occurred_at
            FROM identity.device_lifecycle_history
            WHERE device_id=@device_id
            ORDER BY occurred_at DESC
            LIMIT 100;
            """, connection))
        {
            command.Parameters.AddWithValue("device_id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actions.Add(new(
                    reader.GetString(0), NullableString(reader, 1), reader.GetString(2),
                    reader.GetGuid(3), NullableGuid(reader, 4), NullableInt64(reader, 5),
                    reader.GetInt64(6), NullableGuid(reader, 7), NullableGuid(reader, 8),
                    NullableString(reader, 9), reader.GetGuid(10), reader.GetFieldValue<DateTimeOffset>(11)));
            }
        }

        Guid? predecessorId = null;
        string? predecessorReference = null;
        Guid? replacementId = null;
        string? replacementReference = null;
        await using (var command = new NpgsqlCommand("""
            SELECT device.predecessor_device_id, predecessor.device_reference,
                   device.replacement_device_id, replacement.device_reference
            FROM identity.devices device
            LEFT JOIN identity.devices predecessor ON predecessor.device_id=device.predecessor_device_id
            LEFT JOIN identity.devices replacement ON replacement.device_id=device.replacement_device_id
            WHERE device.device_id=@device_id;
            """, connection))
        {
            command.Parameters.AddWithValue("device_id", deviceId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                predecessorId = NullableGuid(reader, 0);
                predecessorReference = NullableString(reader, 1);
                replacementId = NullableGuid(reader, 2);
                replacementReference = NullableString(reader, 3);
            }
        }

        return new(summary, provisioning, credentials, actions,
            predecessorId, predecessorReference, replacementId, replacementReference);
    }

    public async Task<DeviceSummaryDto> RegisterAsync(DeviceRegistrationCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorAsync(connection, transaction, command.ActorUserId, cancellationToken);
        var site = await ResolveSiteAsync(connection, transaction, command.SiteId, command.SiteGroupId, cancellationToken);
        var assignmentType = AssignmentType(command.DeviceType);

        await ExecuteAsync(connection, transaction, """
            INSERT INTO identity.service_identities (
                service_identity_id, service_identity_code, service_identity_name,
                identity_type, identity_status, owning_service_name, credential_type,
                effective_from, created_at, created_by_user_id, updated_at, updated_by_user_id)
            VALUES (@service_identity_id,@device_reference,@device_name,'DEVICE','DRAFT',
                    @owning_service_name,'NONE',@now,@now,@actor_user_id,@now,@actor_user_id);

            INSERT INTO identity.devices (
                device_id,device_reference,service_identity_id,device_name,device_type,
                device_description,terminal_identifier,lifecycle_status,credential_version,
                effective_from,created_at,created_by_user_id,updated_at,updated_by_user_id)
            VALUES (@device_id,@device_reference,@service_identity_id,@device_name,
                    @device_type::identity.device_type_enum,@description,@terminal_identifier,
                    'REGISTERED',0,@now,@now,@actor_user_id,@now,@actor_user_id);

            INSERT INTO sites.device_assignments (
                device_assignment_id,site_id,service_identity_id,assignment_type,
                assignment_status,assignment_reason_code,assigned_at,assigned_by_user_id,
                created_at,created_by_user_id,updated_at,updated_by_user_id)
            VALUES (gen_random_uuid(),@site_id,@service_identity_id,
                    @assignment_type::sites.device_assignment_type_enum,'SUSPENDED',
                    'DEVICE_REGISTERED',@now,@actor_user_id,@now,@actor_user_id,@now,@actor_user_id);
            """, cancellationToken,
            ("service_identity_id", command.ServiceIdentityId), ("device_reference", command.DeviceReference),
            ("device_name", command.DeviceName), ("device_type", command.DeviceType),
            ("owning_service_name", OwningServiceName(command.DeviceType)),
            ("device_id", command.DeviceId), ("description", command.Description),
            ("terminal_identifier", command.TerminalIdentifier), ("now", command.Now),
            ("actor_user_id", command.ActorUserId), ("site_id", command.SiteId),
            ("assignment_type", assignmentType));

        if (command.DeviceType == DeviceTypes.OperatorConsole)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO operator_console.operator_device_bindings (
                    operator_device_binding_id,device_binding_code,device_name,site_group_id,site_id,
                    service_identity_id,device_status,trust_level,binding_source,correlation_id,
                    created_at,created_by_user_id,updated_at,updated_by_user_id)
                VALUES (@device_id,@device_reference,@device_name,@site_group_id,@site_id,
                        @service_identity_id,'PENDING','UNVERIFIED','MANAGEMENT_PLATFORM',@correlation_id,
                        @now,@actor_user_id,@now,@actor_user_id);

                INSERT INTO operator_console.operator_device_assignment_history (
                    operator_device_assignment_history_id,operator_device_binding_id,site_group_id,
                    site_id,assignment_status_code,assignment_source_code,assignment_reason_code,
                    assigned_at,assigned_by_user_id,effective_from,correlation_id,
                    created_at,created_by_user_id)
                VALUES (gen_random_uuid(),@device_id,@site_group_id,@site_id,'PENDING',
                        'MANAGEMENT_PLATFORM','DEVICE_REGISTERED',@now,@actor_user_id,@now,
                        @correlation_id,@now,@actor_user_id);
                """, cancellationToken,
                ("device_id", command.DeviceId), ("device_reference", command.DeviceReference),
                ("device_name", command.DeviceName), ("site_group_id", site.SiteGroupId),
                ("site_id", command.SiteId), ("service_identity_id", command.ServiceIdentityId),
                ("correlation_id", command.CorrelationId), ("now", command.Now),
                ("actor_user_id", command.ActorUserId));
        }

        await InsertHistoryAndAuditAsync(connection, transaction, command.DeviceId, command.ServiceIdentityId,
            command.DeviceReference, "REGISTER", null, "REGISTERED", command.SiteId, site.SiteGroupId,
            null, 0, command.ActorUserId, null, "DEVICE_REGISTERED", command.CorrelationId, command.Now,
            cancellationToken);
        var result = await ReadSummaryRequiredAsync(connection, transaction, command.DeviceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DeviceSummaryDto> IssueProvisioningAsync(DeviceProvisioningCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorAsync(connection, transaction, command.ActorUserId, cancellationToken);
        var device = await LoadForUpdateAsync(connection, transaction, command.DeviceId, cancellationToken);
        var rotation = command.Purpose == "ROTATION";
        if ((!rotation && device.LifecycleStatus is not ("REGISTERED" or "PROVISIONING_PENDING")) ||
            (rotation && device.LifecycleStatus is not ("ACTIVE" or "SUSPENDED" or "CREDENTIAL_ROTATION")))
        {
            throw new DeviceAdministrationException(rotation ? "DEVICE_ROTATION_NOT_ALLOWED" : "DEVICE_PROVISIONING_NOT_ALLOWED");
        }

        await ExecuteAsync(connection, transaction, """
            UPDATE identity.device_provisioning_credentials
            SET provisioning_status='CANCELLED',cancelled_at=@now,
                cancellation_reason_code='SUPERSEDED_BY_NEW_ISSUANCE'
            WHERE device_id=@device_id AND provisioning_status='ISSUED';
            """, cancellationToken, ("now", command.IssuedAt), ("device_id", command.DeviceId));

        if (rotation)
        {
            await InvalidateRuntimeAsync(connection, transaction, device, "CREDENTIAL_ROTATION", command.IssuedAt, false, cancellationToken, command.ActorUserId);
        }

        var resulting = rotation ? "CREDENTIAL_ROTATION" : "PROVISIONING_PENDING";
        var nextVersion = checked(device.CredentialVersion + 1);
        await ExecuteAsync(connection, transaction, """
            INSERT INTO identity.device_provisioning_credentials (
                device_provisioning_credential_id,device_id,token_hash,provisioning_purpose,
                provisioning_status,intended_device_type,intended_site_id,intended_site_group_id,
                intended_credential_version,issued_at,expires_at,issued_by_user_id,correlation_id)
            VALUES (gen_random_uuid(),@device_id,@token_hash,
                    @purpose::identity.device_provisioning_purpose_enum,'ISSUED',
                    @device_type::identity.device_type_enum,@site_id,@site_group_id,
                    @credential_version,@issued_at,@expires_at,@actor_user_id,@correlation_id);

            UPDATE identity.devices
            SET lifecycle_status=@status::identity.device_lifecycle_status_enum,
                last_provisioning_status='ISSUED',updated_at=@issued_at,
                updated_by_user_id=@actor_user_id,row_version=row_version+1
            WHERE device_id=@device_id;
            """, cancellationToken,
            ("device_id", command.DeviceId), ("token_hash", command.TokenHash),
            ("purpose", command.Purpose), ("device_type", device.DeviceType),
            ("site_id", device.SiteId), ("site_group_id", device.SiteGroupId),
            ("credential_version", nextVersion), ("issued_at", command.IssuedAt),
            ("expires_at", command.ExpiresAt), ("actor_user_id", command.ActorUserId),
            ("correlation_id", command.CorrelationId), ("status", resulting));

        await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
            device.DeviceReference, rotation ? "ROTATE" : "PROVISION", device.LifecycleStatus, resulting,
            device.SiteId, device.SiteGroupId, device.CredentialVersion, device.CredentialVersion,
            command.ActorUserId, null, command.ReasonCode ?? "PROVISIONING_ISSUED", command.CorrelationId,
            command.IssuedAt, cancellationToken);
        var result = await ReadSummaryRequiredAsync(connection, transaction, command.DeviceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<DeviceEnrollmentResultDto> EnrollAsync(DeviceEnrollmentCommand command, string? runtimeCredential, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var provisioning = await LoadProvisioningAsync(connection, transaction, command.TokenHash, cancellationToken)
            ?? throw new DeviceAdministrationException("PROVISIONING_CREDENTIAL_INVALID");
        var device = await LoadForUpdateAsync(connection, transaction, provisioning.DeviceId, cancellationToken);

        if (provisioning.Status != "ISSUED")
        {
            await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
                device.DeviceReference, "ENROLL_REJECTED", device.LifecycleStatus, device.LifecycleStatus,
                device.SiteId, device.SiteGroupId, device.CredentialVersion, device.CredentialVersion,
                null, device.ServiceIdentityId, "PROVISIONING_CREDENTIAL_ALREADY_USED", command.CorrelationId,
                command.Now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DeviceAdministrationException("PROVISIONING_CREDENTIAL_ALREADY_USED");
        }

        if (provisioning.ExpiresAt <= command.Now)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE identity.device_provisioning_credentials
                SET provisioning_status='EXPIRED'
                WHERE device_provisioning_credential_id=@provisioning_id AND provisioning_status='ISSUED';
                UPDATE identity.devices SET last_provisioning_status='EXPIRED',updated_at=@now,
                    row_version=row_version+1 WHERE device_id=@device_id;
                """, cancellationToken, ("provisioning_id", provisioning.Id),
                ("device_id", device.DeviceId), ("now", command.Now));
            await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
                device.DeviceReference, "ENROLL_REJECTED", device.LifecycleStatus, device.LifecycleStatus,
                device.SiteId, device.SiteGroupId, device.CredentialVersion, device.CredentialVersion,
                null, device.ServiceIdentityId, "PROVISIONING_CREDENTIAL_EXPIRED", command.CorrelationId,
                command.Now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DeviceAdministrationException("PROVISIONING_CREDENTIAL_EXPIRED");
        }

        var mismatch = command.DeviceReference != device.DeviceReference ||
                       command.DeviceType != device.DeviceType ||
                       command.SiteId != provisioning.SiteId ||
                       command.SiteId != device.SiteId ||
                       command.SiteGroupId != provisioning.SiteGroupId ||
                       command.SiteGroupId != device.SiteGroupId ||
                       (device.TerminalIdentifier is not null && command.TerminalIdentifier != device.TerminalIdentifier);
        if (mismatch)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE identity.device_provisioning_credentials
                SET provisioning_status='CANCELLED',cancelled_at=@now,
                    cancellation_reason_code='ENROLLMENT_CONTEXT_MISMATCH'
                WHERE device_provisioning_credential_id=@provisioning_id AND provisioning_status='ISSUED';
                UPDATE identity.devices SET last_provisioning_status='CANCELLED',updated_at=@now,
                    row_version=row_version+1 WHERE device_id=@device_id;
                """, cancellationToken, ("now", command.Now), ("provisioning_id", provisioning.Id),
                ("device_id", device.DeviceId));
            await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
                device.DeviceReference, "ENROLL_REJECTED", device.LifecycleStatus, device.LifecycleStatus,
                device.SiteId, device.SiteGroupId, device.CredentialVersion, device.CredentialVersion,
                null, device.ServiceIdentityId, "ENROLLMENT_CONTEXT_MISMATCH", command.CorrelationId,
                command.Now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new DeviceAdministrationException("ENROLLMENT_CONTEXT_MISMATCH");
        }

        if (device.LifecycleStatus is not ("PROVISIONING_PENDING" or "CREDENTIAL_ROTATION") ||
            provisioning.CredentialVersion != device.CredentialVersion + 1)
        {
            throw new DeviceAdministrationException("DEVICE_ENROLLMENT_STATE_INVALID");
        }

        await EnsureSiteCurrentAsync(connection, transaction, device, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE identity.device_credential_history
            SET credential_status='REVOKED',revoked_at=@now,
                revocation_reason_code='SUPERSEDED_BY_ENROLLMENT'
            WHERE device_id=@device_id AND credential_status='ACTIVE';

            INSERT INTO identity.device_credential_history (
                device_credential_history_id,device_id,credential_version,credential_type,
                credential_status,credential_fingerprint,activated_at,provisioning_credential_id,
                correlation_id)
            VALUES (gen_random_uuid(),@device_id,@credential_version,
                    @credential_type::identity.service_credential_type_enum,'ACTIVE',
                    @credential_fingerprint,@now,@provisioning_id,@correlation_id);

            UPDATE identity.service_identities
            SET identity_status='ACTIVE',credential_reference=@credential_reference,
                credential_type=@credential_type::identity.service_credential_type_enum,
                credential_expires_at=NULL,last_rotated_at=@now,revoked_at=NULL,
                revocation_reason_code=NULL,effective_to=NULL,updated_at=@now,row_version=row_version+1
            WHERE service_identity_id=@service_identity_id AND identity_status <> 'REVOKED';

            UPDATE sites.device_assignments
            SET assignment_status='ACTIVE',assignment_reason_code='DEVICE_ENROLLED',
                unassigned_at=NULL,unassigned_by_user_id=NULL,updated_at=@now,row_version=row_version+1
            WHERE device_assignment_id=@assignment_id;

            UPDATE identity.device_provisioning_credentials
            SET provisioning_status='CONSUMED',consumed_at=@now
            WHERE device_provisioning_credential_id=@provisioning_id AND provisioning_status='ISSUED';

            UPDATE identity.devices
            SET lifecycle_status='ACTIVE',credential_version=@credential_version,
                last_provisioning_status='CONSUMED',enrolled_at=@now,suspended_at=NULL,
                updated_at=@now,row_version=row_version+1
            WHERE device_id=@device_id;
            """, cancellationToken,
            ("now", command.Now), ("device_id", device.DeviceId),
            ("credential_version", provisioning.CredentialVersion),
            ("credential_type", command.CredentialType),
            ("credential_fingerprint", command.CredentialFingerprint),
            ("provisioning_id", provisioning.Id), ("correlation_id", command.CorrelationId),
            ("credential_reference", command.CredentialReference),
            ("service_identity_id", device.ServiceIdentityId), ("assignment_id", device.AssignmentId));

        if (device.DeviceType == DeviceTypes.OperatorConsole)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE operator_console.operator_device_bindings
                SET browser_key_thumbprint=@credential_fingerprint,device_status='ACTIVE',
                    trust_level='BROWSER_KEY_ONLY',binding_source='DEVICE_ADMINISTRATION',
                    last_seen_at=@now,revoked_at=NULL,revocation_reason_code=NULL,
                    correlation_id=@correlation_id,updated_at=@now,row_version=row_version+1
                WHERE operator_device_binding_id=@device_id;

                UPDATE operator_console.operator_device_assignment_history
                SET assignment_status_code='SUPERSEDED',effective_to=@now,ended_at=@now,
                    end_reason_code='DEVICE_ENROLLED'
                WHERE operator_device_binding_id=@device_id AND effective_to IS NULL;

                INSERT INTO operator_console.operator_device_assignment_history (
                    operator_device_assignment_history_id,operator_device_binding_id,site_group_id,
                    site_id,assignment_status_code,assignment_source_code,assignment_reason_code,
                    assigned_at,assigned_by_service_identity_id,effective_from,correlation_id,
                    created_at,created_by_service_identity_id)
                VALUES (gen_random_uuid(),@device_id,@site_group_id,@site_id,'ACTIVE',
                        'DEVICE_ADMINISTRATION','DEVICE_ENROLLED',@now,@service_identity_id,@now,
                        @correlation_id,@now,@service_identity_id);
                """, cancellationToken,
                ("credential_fingerprint", command.CredentialFingerprint), ("now", command.Now),
                ("correlation_id", command.CorrelationId), ("device_id", device.DeviceId),
                ("site_group_id", device.SiteGroupId), ("site_id", device.SiteId),
                ("service_identity_id", device.ServiceIdentityId));
        }

        await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
            device.DeviceReference, "ENROLL", device.LifecycleStatus, "ACTIVE", device.SiteId,
            device.SiteGroupId, device.CredentialVersion, provisioning.CredentialVersion, null,
            device.ServiceIdentityId, "DEVICE_ENROLLED", command.CorrelationId, command.Now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(device.DeviceId, device.DeviceReference, device.ServiceIdentityId, device.DeviceType,
            device.SiteId, device.SiteGroupId, provisioning.CredentialVersion, runtimeCredential,
            device.DeviceType == DeviceTypes.OperatorConsole ? "BROWSER_KEY" : "MTLS_CERTIFICATE_REFERENCE",
            runtimeCredential is null ? "Certificate binding activated." : "Shown once. Store only on this workstation.",
            command.CorrelationId);
    }

    public async Task<DeviceSummaryDto> ChangeLifecycleAsync(DeviceLifecycleCommand command, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorAsync(connection, transaction, command.ActorUserId, cancellationToken);
        var device = await LoadForUpdateAsync(connection, transaction, command.DeviceId, cancellationToken);
        string resulting;

        switch (command.Action)
        {
            case "CANCEL_PROVISIONING" when device.LifecycleStatus is "PROVISIONING_PENDING" or "CREDENTIAL_ROTATION":
                if (!await HasIssuedProvisioningAsync(connection, transaction, device.DeviceId, cancellationToken))
                    throw new DeviceAdministrationException("DEVICE_PROVISIONING_NOT_PENDING");
                resulting = device.LifecycleStatus == "PROVISIONING_PENDING" ? "REGISTERED" : "SUSPENDED";
                await ExecuteAsync(connection, transaction, """
                    UPDATE identity.device_provisioning_credentials
                    SET provisioning_status='CANCELLED',cancelled_at=@now,
                        cancellation_reason_code=@reason
                    WHERE device_id=@device_id AND provisioning_status='ISSUED';
                    UPDATE identity.devices
                    SET lifecycle_status=@status::identity.device_lifecycle_status_enum,
                        last_provisioning_status='CANCELLED',
                        suspended_at=CASE WHEN @status='SUSPENDED' THEN @now ELSE NULL END,
                        updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE device_id=@device_id;
                    """, cancellationToken,
                    ("now", command.Now), ("reason", command.ReasonCode),
                    ("device_id", device.DeviceId), ("status", resulting),
                    ("actor_user_id", command.ActorUserId));
                break;

            case "SUSPEND" when device.LifecycleStatus == "ACTIVE":
                resulting = "SUSPENDED";
                await ExecuteAsync(connection, transaction, """
                    UPDATE identity.devices SET lifecycle_status='SUSPENDED',suspended_at=@now,
                        updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE device_id=@device_id;
                    UPDATE identity.service_identities SET identity_status='SUSPENDED',updated_at=@now,
                        updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE service_identity_id=@service_identity_id;
                    UPDATE sites.device_assignments SET assignment_status='SUSPENDED',
                        assignment_reason_code=@reason,updated_at=@now,updated_by_user_id=@actor_user_id,
                        row_version=row_version+1 WHERE device_assignment_id=@assignment_id;
                    """, cancellationToken, LifecycleParameters(command, device));
                await RevokeDeviceHumanSessionsAsync(connection, transaction, device.ServiceIdentityId,
                    command.ReasonCode, command.Now, command.ActorUserId, cancellationToken);
                await UpdateOperatorStatusAsync(connection, transaction, device, "SUSPENDED", command, cancellationToken);
                break;

            case "REACTIVATE" when device.LifecycleStatus == "SUSPENDED":
                if (!await HasCurrentCredentialAsync(connection, transaction, device, command.Now, cancellationToken))
                    throw new DeviceAdministrationException("DEVICE_REPROVISIONING_REQUIRED");
                await EnsureSiteCurrentAsync(connection, transaction, device, cancellationToken);
                resulting = "ACTIVE";
                await ExecuteAsync(connection, transaction, """
                    UPDATE identity.devices SET lifecycle_status='ACTIVE',suspended_at=NULL,
                        updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE device_id=@device_id;
                    UPDATE identity.service_identities SET identity_status='ACTIVE',updated_at=@now,
                        updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE service_identity_id=@service_identity_id AND revoked_at IS NULL;
                    UPDATE sites.device_assignments SET assignment_status='ACTIVE',
                        assignment_reason_code=@reason,updated_at=@now,updated_by_user_id=@actor_user_id,
                        row_version=row_version+1 WHERE device_assignment_id=@assignment_id;
                    """, cancellationToken, LifecycleParameters(command, device));
                await UpdateOperatorStatusAsync(connection, transaction, device, "ACTIVE", command, cancellationToken);
                break;

            case "REVOKE" when device.LifecycleStatus is not ("REVOKED" or "REPLACED"):
                resulting = "REVOKED";
                await InvalidateRuntimeAsync(connection, transaction, device, command.ReasonCode, command.Now, true, cancellationToken, command.ActorUserId);
                await ExecuteAsync(connection, transaction, """
                    UPDATE identity.devices SET lifecycle_status='REVOKED',revoked_at=@now,effective_to=@now,
                        updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
                    WHERE device_id=@device_id;
                    """, cancellationToken, ("now", command.Now), ("actor_user_id", command.ActorUserId),
                    ("device_id", device.DeviceId));
                break;

            default:
                throw new DeviceAdministrationException("DEVICE_LIFECYCLE_TRANSITION_INVALID");
        }

        await InsertHistoryAndAuditAsync(connection, transaction, device.DeviceId, device.ServiceIdentityId,
            device.DeviceReference, command.Action, device.LifecycleStatus, resulting, device.SiteId,
            device.SiteGroupId, device.CredentialVersion, device.CredentialVersion, command.ActorUserId,
            null, command.ReasonCode, command.CorrelationId, command.Now, cancellationToken);
        var result = await ReadSummaryRequiredAsync(connection, transaction, device.DeviceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<ReplaceDeviceResultDto> ReplaceAsync(DeviceReplacementCommand command, string rawToken, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAdministratorAsync(connection, transaction, command.ActorUserId, cancellationToken);
        var old = await LoadForUpdateAsync(connection, transaction, command.DeviceId, cancellationToken);
        if (old.LifecycleStatus != "ACTIVE") throw new DeviceAdministrationException("DEVICE_REPLACEMENT_NOT_ALLOWED");
        var siteId = command.SiteId ?? old.SiteId;
        var site = await ResolveSiteAsync(connection, transaction, siteId, command.SiteGroupId ?? old.SiteGroupId, cancellationToken);
        var name = command.DeviceName ?? old.DeviceName;
        var description = command.Description ?? old.Description;
        var terminal = command.TerminalIdentifier ?? old.TerminalIdentifier;
        if (old.DeviceType == DeviceTypes.AssistedPaymentTerminal && string.IsNullOrWhiteSpace(terminal))
            throw new DeviceAdministrationException("APT_TERMINAL_IDENTIFIER_REQUIRED");

        await InvalidateRuntimeAsync(connection, transaction, old, "DEVICE_REPLACED", command.IssuedAt, true, cancellationToken, command.ActorUserId);
        await ExecuteAsync(connection, transaction, """
            UPDATE identity.devices
            SET lifecycle_status='REPLACED',replaced_at=@now,replacement_device_id=@replacement_device_id,
                effective_to=@now,updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
            WHERE device_id=@device_id;

            INSERT INTO identity.service_identities (
                service_identity_id,service_identity_code,service_identity_name,identity_type,
                identity_status,owning_service_name,credential_type,effective_from,created_at,
                created_by_user_id,updated_at,updated_by_user_id)
            VALUES (@replacement_service_identity_id,@replacement_reference,@device_name,'DEVICE','DRAFT',
                    @owning_service_name,'NONE',@now,@now,@actor_user_id,@now,@actor_user_id);

            INSERT INTO identity.devices (
                device_id,device_reference,service_identity_id,device_name,device_type,
                device_description,terminal_identifier,lifecycle_status,credential_version,
                last_provisioning_status,predecessor_device_id,effective_from,created_at,
                created_by_user_id,updated_at,updated_by_user_id)
            VALUES (@replacement_device_id,@replacement_reference,@replacement_service_identity_id,
                    @device_name,@device_type::identity.device_type_enum,@description,@terminal_identifier,
                    'PROVISIONING_PENDING',0,'ISSUED',@device_id,@now,@now,@actor_user_id,@now,@actor_user_id);

            INSERT INTO sites.device_assignments (
                device_assignment_id,site_id,service_identity_id,assignment_type,assignment_status,
                assignment_reason_code,assigned_at,assigned_by_user_id,created_at,created_by_user_id,
                updated_at,updated_by_user_id)
            VALUES (gen_random_uuid(),@site_id,@replacement_service_identity_id,
                    @assignment_type::sites.device_assignment_type_enum,'SUSPENDED','DEVICE_REPLACEMENT',
                    @now,@actor_user_id,@now,@actor_user_id,@now,@actor_user_id);

            INSERT INTO identity.device_provisioning_credentials (
                device_provisioning_credential_id,device_id,token_hash,provisioning_purpose,
                provisioning_status,intended_device_type,intended_site_id,intended_site_group_id,
                intended_credential_version,issued_at,expires_at,issued_by_user_id,correlation_id)
            VALUES (gen_random_uuid(),@replacement_device_id,@token_hash,'REPLACEMENT','ISSUED',
                    @device_type::identity.device_type_enum,@site_id,@site_group_id,1,@now,@expires_at,
                    @actor_user_id,@correlation_id);
            """, cancellationToken,
            ("now", command.IssuedAt), ("replacement_device_id", command.ReplacementDeviceId),
            ("actor_user_id", command.ActorUserId), ("device_id", old.DeviceId),
            ("replacement_service_identity_id", command.ReplacementServiceIdentityId),
            ("replacement_reference", command.ReplacementReference), ("device_name", name),
            ("device_type", old.DeviceType), ("owning_service_name", OwningServiceName(old.DeviceType)),
            ("description", description),
            ("terminal_identifier", terminal), ("site_id", siteId),
            ("site_group_id", site.SiteGroupId), ("assignment_type", AssignmentType(old.DeviceType)),
            ("token_hash", command.TokenHash), ("expires_at", command.ExpiresAt),
            ("correlation_id", command.CorrelationId));

        if (old.DeviceType == DeviceTypes.OperatorConsole)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT INTO operator_console.operator_device_bindings (
                    operator_device_binding_id,device_binding_code,device_name,site_group_id,site_id,
                    service_identity_id,device_status,trust_level,binding_source,correlation_id,
                    created_at,created_by_user_id,updated_at,updated_by_user_id)
                VALUES (@device_id,@device_reference,@device_name,@site_group_id,@site_id,
                        @service_identity_id,'PENDING','UNVERIFIED','DEVICE_REPLACEMENT',@correlation_id,
                        @now,@actor_user_id,@now,@actor_user_id);
                INSERT INTO operator_console.operator_device_assignment_history (
                    operator_device_assignment_history_id,operator_device_binding_id,site_group_id,site_id,
                    assignment_status_code,assignment_source_code,assignment_reason_code,assigned_at,
                    assigned_by_user_id,effective_from,correlation_id,created_at,created_by_user_id)
                VALUES (gen_random_uuid(),@device_id,@site_group_id,@site_id,'PENDING',
                        'DEVICE_ADMINISTRATION','DEVICE_REPLACEMENT',@now,@actor_user_id,@now,
                        @correlation_id,@now,@actor_user_id);
                """, cancellationToken,
                ("device_id", command.ReplacementDeviceId), ("device_reference", command.ReplacementReference),
                ("device_name", name), ("site_group_id", site.SiteGroupId), ("site_id", siteId),
                ("service_identity_id", command.ReplacementServiceIdentityId),
                ("correlation_id", command.CorrelationId), ("now", command.IssuedAt),
                ("actor_user_id", command.ActorUserId));
        }

        await InsertHistoryAndAuditAsync(connection, transaction, old.DeviceId, old.ServiceIdentityId,
            old.DeviceReference, "REPLACE", old.LifecycleStatus, "REPLACED", old.SiteId, old.SiteGroupId,
            old.CredentialVersion, old.CredentialVersion, command.ActorUserId, null, command.ReasonCode,
            command.CorrelationId, command.IssuedAt, cancellationToken);
        await InsertHistoryAndAuditAsync(connection, transaction, command.ReplacementDeviceId,
            command.ReplacementServiceIdentityId, command.ReplacementReference, "REGISTER_REPLACEMENT",
            null, "PROVISIONING_PENDING", siteId, site.SiteGroupId, null, 0, command.ActorUserId, null,
            command.ReasonCode, command.CorrelationId, command.IssuedAt, cancellationToken);

        var replaced = await ReadSummaryRequiredAsync(connection, transaction, old.DeviceId, cancellationToken);
        var replacement = await ReadSummaryRequiredAsync(connection, transaction, command.ReplacementDeviceId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(replaced, replacement, new(command.ReplacementDeviceId, command.ReplacementReference,
            replacement.DeviceType, replacement.SiteId, replacement.SiteGroupId, replacement.TerminalIdentifier,
            rawToken, command.ExpiresAt, "REPLACEMENT",
            "Shown once. Store it only on the intended replacement device; it cannot be recovered.",
            command.CorrelationId));
    }

    private static async Task EnsureAdministratorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid actorUserId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM identity.users user_record
                JOIN identity.user_roles assignment ON assignment.user_id=user_record.user_id
                JOIN identity.roles role ON role.role_id=assignment.role_id
                WHERE user_record.user_id=@actor_user_id AND user_record.user_status='ACTIVE'
                  AND user_record.effective_from<=now() AND (user_record.effective_to IS NULL OR user_record.effective_to>now())
                  AND assignment.assignment_status='ACTIVE' AND assignment.revoked_at IS NULL
                  AND assignment.effective_from<=now() AND (assignment.effective_to IS NULL OR assignment.effective_to>now())
                  AND role.role_code='SYSTEM_ADMINISTRATOR' AND role.role_status='ACTIVE'
                  AND role.effective_from<=now() AND (role.effective_to IS NULL OR role.effective_to>now()));
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        if (!((bool?)await command.ExecuteScalarAsync(cancellationToken) ?? false))
            throw new DeviceAdministrationException("SYSTEM_ADMINISTRATOR_REQUIRED");
    }

    private static async Task<SiteRecord> ResolveSiteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid siteId, Guid? expectedSiteGroupId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT site.site_group_id
            FROM sites.sites site
            JOIN sites.site_groups site_group ON site_group.site_group_id=site.site_group_id
            WHERE site.site_id=@site_id AND site.site_status='ACTIVE'
              AND site.effective_from<=now() AND (site.effective_to IS NULL OR site.effective_to>now())
              AND site_group.site_group_status='ACTIVE';
            """, connection, transaction);
        command.Parameters.AddWithValue("site_id", siteId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is not Guid siteGroupId) throw new DeviceAdministrationException("DEVICE_SITE_INVALID");
        if (expectedSiteGroupId.HasValue && expectedSiteGroupId.Value != siteGroupId)
            throw new DeviceAdministrationException("DEVICE_SITE_GROUP_MISMATCH");
        return new(siteGroupId);
    }

    private static async Task EnsureSiteCurrentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DeviceState device, CancellationToken cancellationToken)
    {
        _ = await ResolveSiteAsync(connection, transaction, device.SiteId, device.SiteGroupId, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM sites.device_assignments
            WHERE service_identity_id=@service_identity_id AND device_assignment_id=@assignment_id
              AND site_id=@site_id AND unassigned_at IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("service_identity_id", device.ServiceIdentityId);
        command.Parameters.AddWithValue("assignment_id", device.AssignmentId);
        command.Parameters.AddWithValue("site_id", device.SiteId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new DeviceAdministrationException("DEVICE_SITE_ASSIGNMENT_INVALID");
    }

    private static async Task<DeviceState> LoadForUpdateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT device.device_id,device.device_reference,device.device_name,device.device_type::text,
                   device.device_description,device.terminal_identifier,device.service_identity_id,
                   device.lifecycle_status::text,device.credential_version,assignment.device_assignment_id,
                   assignment.site_id,site.site_group_id
            FROM identity.devices device
            JOIN identity.service_identities service ON service.service_identity_id=device.service_identity_id
            JOIN LATERAL (
                SELECT current_assignment.device_assignment_id,current_assignment.site_id
                FROM sites.device_assignments current_assignment
                WHERE current_assignment.service_identity_id=device.service_identity_id
                ORDER BY (current_assignment.unassigned_at IS NULL) DESC,current_assignment.assigned_at DESC
                LIMIT 1
            ) assignment ON true
            JOIN sites.sites site ON site.site_id=assignment.site_id
            WHERE device.device_id=@device_id
            FOR UPDATE OF device,service;
            """, connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new DeviceAdministrationException("DEVICE_NOT_FOUND");
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            NullableString(reader, 4), NullableString(reader, 5), reader.GetGuid(6), reader.GetString(7),
            reader.GetInt64(8), reader.GetGuid(9), reader.GetGuid(10), reader.GetGuid(11));
    }

    private static async Task<ProvisioningState?> LoadProvisioningAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string tokenHash, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT device_provisioning_credential_id,device_id,provisioning_status::text,
                   intended_site_id,intended_site_group_id,intended_credential_version,expires_at
            FROM identity.device_provisioning_credentials
            WHERE token_hash=@token_hash
            FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("token_hash", tokenHash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetGuid(3),
                NullableGuid(reader, 4), reader.GetInt64(5), reader.GetFieldValue<DateTimeOffset>(6))
            : null;
    }

    private static async Task<bool> HasCurrentCredentialAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DeviceState device, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM identity.device_credential_history credential
                JOIN identity.service_identities service ON service.service_identity_id=@service_identity_id
                WHERE credential.device_id=@device_id AND credential.credential_version=@credential_version
                  AND credential.credential_status='ACTIVE'
                  AND (credential.expires_at IS NULL OR credential.expires_at>@now)
                  AND service.revoked_at IS NULL AND service.credential_reference IS NOT NULL
                  AND (service.credential_expires_at IS NULL OR service.credential_expires_at>@now));
            """, connection, transaction);
        command.Parameters.AddWithValue("service_identity_id", device.ServiceIdentityId);
        command.Parameters.AddWithValue("device_id", device.DeviceId);
        command.Parameters.AddWithValue("credential_version", device.CredentialVersion);
        command.Parameters.AddWithValue("now", now);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> HasIssuedProvisioningAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM identity.device_provisioning_credentials
                WHERE device_id=@device_id AND provisioning_status='ISSUED');
            """, connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task InvalidateRuntimeAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DeviceState device, string reason, DateTimeOffset now, bool terminal, CancellationToken cancellationToken, Guid actorUserId)
    {
        await ExecuteAsync(connection, transaction, """
            UPDATE identity.device_provisioning_credentials
            SET provisioning_status='CANCELLED',cancelled_at=@now,cancellation_reason_code=@reason
            WHERE device_id=@device_id AND provisioning_status='ISSUED';
            UPDATE identity.device_credential_history
            SET credential_status='REVOKED',revoked_at=@now,revocation_reason_code=@reason
            WHERE device_id=@device_id AND credential_status='ACTIVE';
            UPDATE identity.service_identities
            SET identity_status=CASE WHEN @terminal THEN 'REVOKED'::identity.service_identity_status_enum
                                     ELSE 'SUSPENDED'::identity.service_identity_status_enum END,
                revoked_at=CASE WHEN @terminal THEN @now ELSE NULL END,
                revocation_reason_code=CASE WHEN @terminal THEN @reason ELSE NULL END,
                updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
            WHERE service_identity_id=@service_identity_id;
            UPDATE sites.device_assignments
            SET assignment_status=CASE WHEN @terminal THEN 'SUPERSEDED'::sites.device_assignment_status_enum
                                       ELSE 'SUSPENDED'::sites.device_assignment_status_enum END,
                assignment_reason_code=@reason,
                unassigned_at=CASE WHEN @terminal THEN @now ELSE NULL END,
                unassigned_by_user_id=CASE WHEN @terminal THEN @actor_user_id ELSE NULL END,
                updated_at=@now,updated_by_user_id=@actor_user_id,row_version=row_version+1
            WHERE device_assignment_id=@assignment_id;
            """, cancellationToken, ("now", now), ("reason", reason), ("device_id", device.DeviceId),
            ("terminal", terminal), ("actor_user_id", actorUserId),
            ("service_identity_id", device.ServiceIdentityId), ("assignment_id", device.AssignmentId));

        await RevokeDeviceHumanSessionsAsync(connection, transaction, device.ServiceIdentityId,
            reason, now, actorUserId, cancellationToken);

        if (device.DeviceType == DeviceTypes.OperatorConsole)
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE operator_console.operator_device_bindings
                SET device_status=CASE WHEN @terminal THEN 'REVOKED'::operator_console.operator_device_binding_status_enum
                                       ELSE 'SUSPENDED'::operator_console.operator_device_binding_status_enum END,
                    revoked_at=CASE WHEN @terminal THEN @now ELSE NULL END,
                    revocation_reason_code=@reason,updated_at=@now,updated_by_user_id=@actor_user_id,
                    row_version=row_version+1
                WHERE operator_device_binding_id=@device_id;
                UPDATE operator_console.operator_device_assignment_history
                SET assignment_status_code=CASE WHEN @terminal THEN 'SUPERSEDED' ELSE 'SUSPENDED' END,
                    effective_to=CASE WHEN @terminal THEN @now ELSE effective_to END,
                    ended_at=CASE WHEN @terminal THEN @now ELSE ended_at END,
                    ended_by_user_id=CASE WHEN @terminal THEN @actor_user_id ELSE ended_by_user_id END,
                    end_reason_code=@reason
                WHERE operator_device_binding_id=@device_id AND effective_to IS NULL;
                """, cancellationToken, ("terminal", terminal), ("now", now), ("reason", reason),
                ("actor_user_id", actorUserId), ("device_id", device.DeviceId));
        }
    }

    private static Task RevokeDeviceHumanSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid serviceIdentityId,
        string reason,
        DateTimeOffset now,
        Guid actorUserId,
        CancellationToken cancellationToken) =>
        ExecuteAsync(connection, transaction, """
            UPDATE identity.human_sessions
            SET session_status='REVOKED',revoked_at=@now,revoked_by_user_id=@actor_user_id,
                revoked_by_service_identity_id=NULL,revocation_reason_code=@reason,
                updated_at=@now,updated_by_user_id=@actor_user_id,
                updated_by_service_identity_id=NULL,row_version=row_version+1
            WHERE device_service_identity_id=@service_identity_id AND session_status='ACTIVE';
            """, cancellationToken, ("now", now), ("actor_user_id", actorUserId),
            ("reason", reason), ("service_identity_id", serviceIdentityId));

    private static Task UpdateOperatorStatusAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, DeviceState device, string status, DeviceLifecycleCommand command, CancellationToken cancellationToken)
    {
        if (device.DeviceType != DeviceTypes.OperatorConsole) return Task.CompletedTask;
        return ExecuteAsync(connection, transaction, """
            UPDATE operator_console.operator_device_bindings
            SET device_status=@status::operator_console.operator_device_binding_status_enum,
                revoked_at=NULL,revocation_reason_code=@reason,updated_at=@now,
                updated_by_user_id=@actor_user_id,row_version=row_version+1
            WHERE operator_device_binding_id=@device_id;
            UPDATE operator_console.operator_device_assignment_history
            SET assignment_status_code=@status,assignment_reason_code=@reason
            WHERE operator_device_binding_id=@device_id AND effective_to IS NULL;
            """, cancellationToken, ("status", status), ("reason", command.ReasonCode),
            ("now", command.Now), ("actor_user_id", command.ActorUserId), ("device_id", device.DeviceId));
    }

    private static async Task InsertHistoryAndAuditAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId, Guid serviceIdentityId,
        string deviceReference, string action, string? previous, string resulting, Guid siteId,
        Guid? siteGroupId, long? previousVersion, long resultingVersion, Guid? actorUserId,
        Guid? actorServiceIdentityId, string reason, Guid correlationId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var summary = $"{action}: device={deviceReference}; {previous ?? "NONE"}->{resulting}; site={siteId:D}; group={siteGroupId?.ToString("D") ?? "NONE"}; reason={reason}";
        if (summary.Length > 256) summary = summary[..256];
        var sourceChannel = actorUserId.HasValue ? "MANAGEMENT_PLATFORM" : "DEVICE_ENROLLMENT";
        await ExecuteAsync(connection, transaction, """
            INSERT INTO identity.device_lifecycle_history (
                device_lifecycle_history_id,device_id,action_code,previous_status,resulting_status,
                site_id,site_group_id,previous_credential_version,resulting_credential_version,
                actor_user_id,actor_service_identity_id,reason_code,correlation_id,occurred_at)
            VALUES (gen_random_uuid(),@device_id,@action,
                    @previous::identity.device_lifecycle_status_enum,
                    @resulting::identity.device_lifecycle_status_enum,@site_id,@site_group_id,
                    @previous_version,@resulting_version,@actor_user_id,@actor_service_identity_id,
                    @reason,@correlation_id,@occurred_at);

            INSERT INTO audit.audit_events (
                audit_event_id,event_type,event_category,event_result,event_reason_code,
                target_entity_type,target_entity_id,related_entity_type,related_entity_id,
                source_schema,source_service_name,source_channel,actor_user_id,
                actor_service_identity_id,summary,occurred_at,recorded_at,correlation_id,created_at)
            VALUES (gen_random_uuid(),'Device' || replace(initcap(lower(@action)),'_',''),'CONFIGURATION_CHANGE','SUCCESS',
                    @reason,'DEVICE',@device_id,'SERVICE_IDENTITY',@service_identity_id,'identity',
                    'ExitPass.CentralPms.Api',@source_channel,@actor_user_id,
                    @actor_service_identity_id,@summary,@occurred_at,@occurred_at,@correlation_id,@occurred_at);
            """, cancellationToken,
            ("device_id", deviceId), ("action", action), ("previous", previous),
            ("resulting", resulting), ("site_id", siteId), ("site_group_id", siteGroupId),
            ("previous_version", previousVersion), ("resulting_version", resultingVersion),
            ("actor_user_id", actorUserId), ("actor_service_identity_id", actorServiceIdentityId),
            ("reason", reason), ("correlation_id", correlationId), ("occurred_at", occurredAt),
            ("service_identity_id", serviceIdentityId), ("summary", summary),
            ("source_channel", sourceChannel));
    }

    private static object?[] LifecycleParameters(DeviceLifecycleCommand command, DeviceState device) =>
    [
        new KeyValuePair<string, object?>("now", command.Now),
        new KeyValuePair<string, object?>("actor_user_id", command.ActorUserId),
        new KeyValuePair<string, object?>("device_id", device.DeviceId),
        new KeyValuePair<string, object?>("service_identity_id", device.ServiceIdentityId),
        new KeyValuePair<string, object?>("assignment_id", device.AssignmentId),
        new KeyValuePair<string, object?>("reason", command.ReasonCode)
    ];

    private static string AssignmentType(string deviceType) =>
        deviceType == DeviceTypes.AssistedPaymentTerminal ? "PAYMENT_DEVICE" : "SERVICE_PRINCIPAL";

    private static string OwningServiceName(string deviceType) =>
        deviceType == DeviceTypes.AssistedPaymentTerminal
            ? "ExitPass.AssistedPaymentTerminal"
            : "ExitPass.OperatorConsole";

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken, params object?[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        AddParameters(command, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameters(NpgsqlCommand command, object?[] parameters)
    {
        foreach (var value in parameters)
        {
            if (value is ITuple { Length: 2 } tuple && tuple[0] is string name)
                command.Parameters.AddWithValue(name, tuple[1] ?? DBNull.Value);
            else if (value is KeyValuePair<string, object?> pair)
                command.Parameters.AddWithValue(pair.Key, pair.Value ?? DBNull.Value);
            else
                throw new ArgumentException("SQL parameters must be named pairs.", nameof(parameters));
        }
    }

    private static async Task<DeviceSummaryDto> ReadSummaryRequiredAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid deviceId, CancellationToken cancellationToken) =>
        await ReadSummaryAsync(connection, transaction, deviceId, cancellationToken)
        ?? throw new DeviceAdministrationException("DEVICE_NOT_FOUND");

    private static async Task<DeviceSummaryDto?> ReadSummaryAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid deviceId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SummarySql + " WHERE device.device_id=@device_id;", connection, transaction);
        command.Parameters.AddWithValue("device_id", deviceId);
        var results = await ReadSummariesAsync(command, cancellationToken);
        return results.SingleOrDefault();
    }

    private static async Task<IReadOnlyList<DeviceSummaryDto>> ReadSummariesAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var results = new List<DeviceSummaryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                NullableString(reader, 4), NullableString(reader, 5), reader.GetGuid(6), reader.GetGuid(7),
                reader.GetString(8), reader.GetString(9), reader.GetGuid(10), reader.GetString(11),
                reader.GetString(12), reader.GetString(13), NullableString(reader, 14), reader.GetInt64(15),
                reader.GetString(16), reader.GetFieldValue<DateTimeOffset>(17),
                reader.GetFieldValue<DateTimeOffset>(18), reader.GetInt64(19)));
        }
        return results;
    }

    private const string SummarySql = """
        SELECT device.device_id,device.device_reference,device.device_name,device.device_type::text,
               device.device_description,device.terminal_identifier,device.service_identity_id,
               assignment.site_id,site.site_code,site.site_name,site.site_group_id,
               site_group.site_group_code,site_group.site_group_name,device.lifecycle_status::text,
               device.last_provisioning_status::text,device.credential_version,
               COALESCE(credential.credential_status::text,
                        CASE WHEN device.credential_version=0 THEN 'NOT_ISSUED' ELSE 'REVOKED' END),
               device.created_at,device.updated_at,device.row_version
        FROM identity.devices device
        JOIN LATERAL (
            SELECT current_assignment.site_id
            FROM sites.device_assignments current_assignment
            WHERE current_assignment.service_identity_id=device.service_identity_id
            ORDER BY (current_assignment.unassigned_at IS NULL) DESC,current_assignment.assigned_at DESC
            LIMIT 1
        ) assignment ON true
        JOIN sites.sites site ON site.site_id=assignment.site_id
        JOIN sites.site_groups site_group ON site_group.site_group_id=site.site_group_id
        LEFT JOIN identity.device_credential_history credential
          ON credential.device_id=device.device_id AND credential.credential_status='ACTIVE'
        """;

    private static string? NullableString(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static Guid? NullableGuid(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);
    private static long? NullableInt64(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    private static DateTimeOffset? NullableDateTime(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private sealed record SiteRecord(Guid SiteGroupId);
    private sealed record DeviceState(Guid DeviceId, string DeviceReference, string DeviceName, string DeviceType,
        string? Description, string? TerminalIdentifier, Guid ServiceIdentityId, string LifecycleStatus,
        long CredentialVersion, Guid AssignmentId, Guid SiteId, Guid SiteGroupId);
    private sealed record ProvisioningState(Guid Id, Guid DeviceId, string Status, Guid SiteId,
        Guid? SiteGroupId, long CredentialVersion, DateTimeOffset ExpiresAt);
}
