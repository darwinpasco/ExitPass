using System.Data;
using ExitPass.CentralPms.Application.ManagementPlatform;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.ManagementPlatform;

public sealed class PostgresManagementPlatformIdentityAdministrationRepository : IManagementPlatformIdentityAdministrationRepository
{
    private const string UserViewPermission = "user.view";
    private const string UserManagePermission = "user.manage";
    private const string RoleViewPermission = "role.view";
    private const string PermissionViewPermission = "permission.view";
    private const string RoleAssignmentPermission = "identity.role-assignment.manage";
    private const string ScopeAssignmentPermission = "identity.scope-assignment.manage";
    private const string PrivilegedDecisionPermission = "identity.privileged-access.decide";
    private const string AccessReviewPermission = "identity.access-review.manage";
    private const string SessionViewPermission = "human-authentication.session.admin.view";
    private const string SessionRevokePermission = "human-authentication.session.admin.revoke";
    private const string CredentialResetPermission = "human-authentication.credential.reset";
    private const string MfaStatusPermission = "human-authentication.mfa.status.view";
    private const string MfaResetPermission = "human-authentication.mfa.reset";
    private const string MfaRemovePermission = "human-authentication.mfa.remove";
    private readonly string _connectionString;

    public PostgresManagementPlatformIdentityAdministrationRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("A database connection string is required.", nameof(connectionString));
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentityUserSummary>>> ListUsersAsync(
        IdentityAdministrationActor actor,
        IdentityUserSearch search,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, UserViewPermission, cancellationToken))
        {
            return Forbidden<IReadOnlyList<IdentityUserSummary>>(correlationId);
        }

        const string sql = """
            SELECT u.user_id, u.username, u.display_name, u.email, u.mobile_number_masked,
                   u.user_type::text, u.user_status::text, u.effective_from, u.effective_to,
                   u.last_login_at, u.row_version
            FROM identity.users u
            WHERE (@status IS NULL OR u.user_status::text = @status)
              AND (@query IS NULL OR u.username_normalized LIKE @query OR lower(u.display_name) LIKE @query)
              AND (u.user_id = @actor_user_id OR EXISTS (
                    SELECT 1
                    FROM identity.user_roles aur
                    JOIN identity.user_role_scope_grants ag ON ag.user_role_id = aur.user_role_id
                    WHERE aur.user_id = @actor_user_id
                      AND aur.assignment_status = 'ACTIVE'
                      AND aur.effective_from <= now()
                      AND (aur.effective_to IS NULL OR aur.effective_to > now())
                      AND ag.grant_status = 'ACTIVE'
                      AND ag.effective_from <= now()
                      AND (ag.effective_to IS NULL OR ag.effective_to > now())
                      AND (ag.scope_type = 'GLOBAL' OR EXISTS (
                          SELECT 1
                          FROM identity.user_roles tur
                          JOIN identity.user_role_scope_grants tg ON tg.user_role_id = tur.user_role_id
                          WHERE tur.user_id = u.user_id
                            AND tur.assignment_status = 'ACTIVE'
                            AND tg.grant_status = 'ACTIVE'
                            AND ((ag.scope_type = 'SITE' AND tg.scope_type = 'SITE' AND ag.site_id = tg.site_id)
                              OR (ag.scope_type = 'SITE_GROUP' AND tg.scope_type = 'SITE_GROUP' AND ag.site_group_id = tg.site_group_id)
                              OR (ag.scope_type = 'SITE_GROUP' AND tg.scope_type = 'SITE' AND EXISTS (
                                  SELECT 1 FROM sites.sites s WHERE s.site_id = tg.site_id AND s.site_group_id = ag.site_group_id)))
                      ))
              ))
            ORDER BY u.username_normalized, u.user_id
            OFFSET @offset LIMIT @limit;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("actor_user_id", actor.UserId);
        command.Parameters.Add("status", NpgsqlDbType.Text).Value = Db(search.Status?.Trim().ToUpperInvariant());
        command.Parameters.Add("query", NpgsqlDbType.Text).Value = Db(string.IsNullOrWhiteSpace(search.Query) ? null : $"%{search.Query.Trim().ToLowerInvariant()}%");
        command.Parameters.AddWithValue("offset", search.Offset);
        command.Parameters.AddWithValue("limit", search.Limit);

        var users = new List<IdentityUserSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(ReadUser(reader));
        }

        return IdentityAdministrationResult<IReadOnlyList<IdentityUserSummary>>.Succeeded(users, correlationId);
    }

    public async Task<IdentityAdministrationResult<IdentityUserDetail>> GetUserAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, UserViewPermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, null, actor.UserId, userReference, cancellationToken))
        {
            return NotFound<IdentityUserDetail>(correlationId);
        }

        var user = await ReadUserAsync(connection, null, userReference, cancellationToken);
        if (user is null)
        {
            return NotFound<IdentityUserDetail>(correlationId);
        }

        var assignments = await ReadAssignmentsAsync(connection, null, userReference, cancellationToken);
        var grants = await ReadScopeGrantsAsync(connection, null, userReference, cancellationToken);
        return IdentityAdministrationResult<IdentityUserDetail>.Succeeded(new(user, assignments, grants), correlationId);
    }

    public async Task<IdentityAdministrationResult<IdentityUserSummary>> CreateUserAsync(
        IdentityAdministrationActor actor,
        CreateIdentityUserCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAsync(connection, transaction, actor, UserManagePermission, cancellationToken))
        {
            return Forbidden<IdentityUserSummary>(command.CorrelationId);
        }

        const string sql = """
            INSERT INTO identity.users (
                user_id, username, email, email_normalized, display_name, mobile_number_masked,
                user_type, user_status, effective_from, effective_to,
                created_by_user_id, updated_by_user_id)
            VALUES (
                gen_random_uuid(), @username, @email, @email_normalized, @display_name, @mobile,
                @user_type::identity.user_type_enum, 'INVITED', @effective_from, @effective_to,
                @actor_user_id, @actor_user_id)
            RETURNING user_id;
            """;

        Guid userId;
        try
        {
            await using var insert = new NpgsqlCommand(sql, connection, transaction);
            insert.Parameters.AddWithValue("username", command.Username);
            insert.Parameters.Add("email", NpgsqlDbType.Text).Value = Db(command.Email?.Trim());
            insert.Parameters.Add("email_normalized", NpgsqlDbType.Text).Value = Db(command.Email?.Trim().ToLowerInvariant());
            insert.Parameters.AddWithValue("display_name", command.DisplayName);
            insert.Parameters.Add("mobile", NpgsqlDbType.Text).Value = Db(command.MaskedMobileNumber?.Trim());
            insert.Parameters.AddWithValue("user_type", command.UserType);
            insert.Parameters.AddWithValue("effective_from", command.EffectiveFrom);
            insert.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = Db(command.EffectiveTo);
            insert.Parameters.AddWithValue("actor_user_id", actor.UserId);
            userId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityUserSummary>(command.CorrelationId, "IDENTITY_USER_ALREADY_EXISTS", "The requested user identifier conflicts with an existing identity.");
        }

        await InsertAuditAsync(connection, transaction, "USER_CREATED", "SUCCESS", command.ReasonCode, "IdentityUser", userId, actor.UserId, command.CorrelationId, "A human identity was invited.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var created = await ReadUserAsync(connection, null, userId, cancellationToken);
        return IdentityAdministrationResult<IdentityUserSummary>.Succeeded(created!, command.CorrelationId, "CREATED");
    }

    public async Task<IdentityAdministrationResult<IdentityUserSummary>> UpdateUserAsync(
        IdentityAdministrationActor actor,
        UpdateIdentityUserCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, transaction, actor, UserManagePermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityUserSummary>(command.CorrelationId);
        }

        const string sql = """
            UPDATE identity.users
            SET display_name = @display_name,
                email = @email,
                email_normalized = @email_normalized,
                mobile_number_masked = @mobile,
                effective_from = @effective_from,
                effective_to = @effective_to,
                updated_at = now(),
                updated_by_user_id = @actor_user_id,
                row_version = row_version + 1
            WHERE user_id = @user_id
              AND row_version = @expected_row_version;
            """;
        await using var update = new NpgsqlCommand(sql, connection, transaction);
        update.Parameters.AddWithValue("user_id", command.UserReference);
        update.Parameters.AddWithValue("display_name", command.DisplayName);
        update.Parameters.Add("email", NpgsqlDbType.Text).Value = Db(command.Email?.Trim());
        update.Parameters.Add("email_normalized", NpgsqlDbType.Text).Value = Db(command.Email?.Trim().ToLowerInvariant());
        update.Parameters.Add("mobile", NpgsqlDbType.Text).Value = Db(command.MaskedMobileNumber?.Trim());
        update.Parameters.AddWithValue("effective_from", command.EffectiveFrom);
        update.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = Db(command.EffectiveTo);
        update.Parameters.AddWithValue("actor_user_id", actor.UserId);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityUserSummary>(command.CorrelationId);
        }

        await InsertAuditAsync(connection, transaction, "USER_PROFILE_UPDATED", "SUCCESS", command.ReasonCode, "IdentityUser", command.UserReference, actor.UserId, command.CorrelationId, "A human identity profile was updated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityAdministrationResult<IdentityUserSummary>.Succeeded((await ReadUserAsync(connection, null, command.UserReference, cancellationToken))!, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IdentityUserSummary>> ChangeUserLifecycleAsync(
        IdentityAdministrationActor actor,
        ChangeIdentityUserLifecycleCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await IsAuthorizedAsync(connection, transaction, actor, UserManagePermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityUserSummary>(command.CorrelationId);
        }

        if (actor.UserId == command.UserReference)
        {
            return Forbidden<IdentityUserSummary>(command.CorrelationId, "SELF_LIFECYCLE_CHANGE_PROHIBITED");
        }

        var targetStatus = command.Transition switch
        {
            "ACTIVATE" => "ACTIVE",
            "SUSPEND" => "SUSPENDED",
            "INACTIVATE" => "INACTIVE",
            "RETIRE" => "RETIRED",
            "LOCK" => "LOCKED",
            "UNLOCK" => "ACTIVE",
            _ => null
        };
        if (targetStatus is null)
        {
            return Invalid<IdentityUserSummary>(command.CorrelationId, "INVALID_USER_LIFECYCLE_TRANSITION");
        }

        if (targetStatus is "SUSPENDED" or "INACTIVE" or "RETIRED")
        {
            await using var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('exitpass.identity.last-active-admin'));", connection, transaction);
            await advisory.ExecuteNonQueryAsync(cancellationToken);
            if (await IsLastActiveAdministratorAsync(connection, transaction, command.UserReference, cancellationToken))
            {
                return Conflict<IdentityUserSummary>(command.CorrelationId, "LAST_ACTIVE_ADMIN_PROTECTED", "The final active identity administrator cannot be disabled.");
            }
        }

        const string sql = """
            UPDATE identity.users
            SET user_status = @status::identity.user_status_enum,
                locked_at = CASE WHEN @status = 'LOCKED' THEN now() ELSE NULL END,
                lockout_expires_at = CASE WHEN @status = 'LOCKED' THEN @lockout_expires_at ELSE NULL END,
                lockout_reason_code = CASE WHEN @status = 'LOCKED' THEN @reason_code ELSE NULL END,
                suspended_at = CASE WHEN @status = 'SUSPENDED' THEN now() ELSE suspended_at END,
                retired_at = CASE WHEN @status = 'RETIRED' THEN now() ELSE retired_at END,
                authorization_epoch = authorization_epoch + 1,
                updated_at = now(),
                updated_by_user_id = @actor_user_id,
                row_version = row_version + 1
            WHERE user_id = @user_id
              AND row_version = @expected_row_version
              AND user_status <> 'RETIRED'
              AND ((@transition = 'ACTIVATE' AND user_status IN ('INVITED', 'INACTIVE', 'SUSPENDED'))
                OR (@transition = 'SUSPEND' AND user_status IN ('ACTIVE', 'LOCKED'))
                OR (@transition = 'INACTIVATE' AND user_status IN ('INVITED', 'ACTIVE', 'LOCKED', 'SUSPENDED'))
                OR (@transition = 'RETIRE')
                OR (@transition = 'LOCK' AND user_status = 'ACTIVE')
                OR (@transition = 'UNLOCK' AND user_status = 'LOCKED'));
            """;
        await using var update = new NpgsqlCommand(sql, connection, transaction);
        update.Parameters.AddWithValue("status", targetStatus);
        update.Parameters.AddWithValue("transition", command.Transition);
        update.Parameters.Add("lockout_expires_at", NpgsqlDbType.TimestampTz).Value = Db(command.LockoutExpiresAt);
        update.Parameters.AddWithValue("reason_code", command.ReasonCode);
        update.Parameters.AddWithValue("actor_user_id", actor.UserId);
        update.Parameters.AddWithValue("user_id", command.UserReference);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityUserSummary>(command.CorrelationId);
        }

        if (targetStatus is "SUSPENDED" or "INACTIVE" or "RETIRED" or "LOCKED")
        {
            await RevokeActiveSessionsAsync(connection, transaction, command.UserReference, actor.UserId, command.ReasonCode, cancellationToken);
        }

        var eventType = command.Transition switch
        {
            "ACTIVATE" => "USER_ACTIVATED",
            "SUSPEND" => "USER_SUSPENDED",
            "INACTIVATE" => "USER_INACTIVATED",
            "RETIRE" => "USER_RETIRED",
            "LOCK" => "ACCOUNT_LOCKED",
            "UNLOCK" => "ACCOUNT_UNLOCKED",
            _ => "USER_LIFECYCLE_CHANGED"
        };
        await InsertAuditAsync(connection, transaction, eventType, "SUCCESS", command.ReasonCode, "IdentityUser", command.UserReference, actor.UserId, command.CorrelationId, "A governed human identity lifecycle transition completed.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityAdministrationResult<IdentityUserSummary>.Succeeded((await ReadUserAsync(connection, null, command.UserReference, cancellationToken))!, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>> ListRolesAsync(
        IdentityAdministrationActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, null, actor, [RoleViewPermission, RoleAssignmentPermission, "role.manage"], cancellationToken))
        {
            return Forbidden<IReadOnlyList<IdentityRoleDefinition>>(correlationId);
        }

        const string sql = """
            SELECT role_id, role_code, role_name, role_description, role_type::text, role_status::text,
                   is_privileged, requires_elevated_approval, effective_from, effective_to, row_version
            FROM identity.roles
            ORDER BY role_code;
            """;
        var roles = new List<IdentityRoleDefinition>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), GetNullableString(reader, 3),
                reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7),
                reader.GetFieldValue<DateTimeOffset>(8), GetNullableDateTimeOffset(reader, 9), reader.GetInt64(10)));
        }

        return IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>.Succeeded(roles, correlationId);
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentityPermissionDefinition>>> ListPermissionsAsync(
        IdentityAdministrationActor actor,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, null, actor, [PermissionViewPermission, "permission.manage", RoleAssignmentPermission], cancellationToken))
        {
            return Forbidden<IReadOnlyList<IdentityPermissionDefinition>>(correlationId);
        }

        const string sql = """
            SELECT permission_id, permission_code, permission_name, permission_domain, permission_action,
                   permission_status::text, is_sensitive, requires_audit, row_version
            FROM identity.permissions
            ORDER BY permission_code;
            """;
        var permissions = new List<IdentityPermissionDefinition>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            permissions.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7), reader.GetInt64(8)));
        }

        return IdentityAdministrationResult<IReadOnlyList<IdentityPermissionDefinition>>.Succeeded(permissions, correlationId);
    }

    public async Task<IdentityAdministrationResult<IdentityRoleAssignment>> AssignRoleAsync(
        IdentityAdministrationActor actor,
        AssignIdentityRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == command.UserReference)
        {
            return Forbidden<IdentityRoleAssignment>(command.CorrelationId, "SELF_ROLE_ASSIGNMENT_PROHIBITED");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, transaction, actor, [RoleAssignmentPermission, "assignment.manage"], cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityRoleAssignment>(command.CorrelationId);
        }

        var role = await ReadRoleAsync(connection, transaction, command.RoleReference, cancellationToken);
        if (role is null || role.Status != "ACTIVE")
        {
            return NotFound<IdentityRoleAssignment>(command.CorrelationId);
        }

        if (role.IsPrivileged || role.RequiresElevatedApproval)
        {
            return Forbidden<IdentityRoleAssignment>(command.CorrelationId, "PRIVILEGED_ACCESS_REQUEST_REQUIRED");
        }

        if (!await ActorMayDelegateRoleAsync(connection, transaction, actor.UserId, command.RoleReference, cancellationToken))
        {
            return Forbidden<IdentityRoleAssignment>(command.CorrelationId, "DELEGATION_CEILING_EXCEEDED");
        }

        const string sql = """
            INSERT INTO identity.user_roles (
                user_role_id, user_id, role_id, assignment_status, assignment_reason_code,
                assigned_by_user_id, effective_from, effective_to,
                created_by_user_id, updated_by_user_id)
            VALUES (
                gen_random_uuid(), @user_id, @role_id, 'ACTIVE', @reason_code,
                @actor_user_id, @effective_from, @effective_to,
                @actor_user_id, @actor_user_id)
            RETURNING user_role_id;
            """;
        Guid assignmentId;
        try
        {
            await using var insert = new NpgsqlCommand(sql, connection, transaction);
            insert.Parameters.AddWithValue("user_id", command.UserReference);
            insert.Parameters.AddWithValue("role_id", command.RoleReference);
            insert.Parameters.AddWithValue("reason_code", command.ReasonCode);
            insert.Parameters.AddWithValue("actor_user_id", actor.UserId);
            insert.Parameters.AddWithValue("effective_from", command.EffectiveFrom);
            insert.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = Db(command.EffectiveTo);
            assignmentId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            var existing = (await ReadAssignmentsAsync(connection, null, command.UserReference, cancellationToken))
                .SingleOrDefault(item => item.RoleReference == command.RoleReference && item.Status == "ACTIVE");
            return existing is not null
                ? IdentityAdministrationResult<IdentityRoleAssignment>.Succeeded(existing, command.CorrelationId, "IDEMPOTENT_REPLAY")
                : Conflict<IdentityRoleAssignment>(command.CorrelationId);
        }

        await IncrementAuthorizationEpochAsync(connection, transaction, command.UserReference, actor.UserId, cancellationToken);
        await InsertAuditAsync(connection, transaction, "ROLE_ASSIGNED", "SUCCESS", command.ReasonCode, "UserRole", assignmentId, actor.UserId, command.CorrelationId, "A bounded role assignment was activated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var assignment = (await ReadAssignmentsAsync(connection, null, command.UserReference, cancellationToken)).Single(item => item.AssignmentReference == assignmentId);
        return IdentityAdministrationResult<IdentityRoleAssignment>.Succeeded(assignment, command.CorrelationId, "CREATED");
    }

    public async Task<IdentityAdministrationResult<IdentityRoleAssignment>> RevokeRoleAsync(
        IdentityAdministrationActor actor,
        RevokeIdentityRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == command.UserReference)
        {
            return Forbidden<IdentityRoleAssignment>(command.CorrelationId, "SELF_ROLE_REVOCATION_PROHIBITED");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, transaction, actor, [RoleAssignmentPermission, "assignment.manage"], cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityRoleAssignment>(command.CorrelationId);
        }

        await using var advisory = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext('exitpass.identity.last-active-admin'));", connection, transaction);
        await advisory.ExecuteNonQueryAsync(cancellationToken);
        if (await AssignmentIsIdentityAdministratorAsync(connection, transaction, command.AssignmentReference, cancellationToken) &&
            await IsLastActiveAdministratorAsync(connection, transaction, command.UserReference, cancellationToken))
        {
            return Conflict<IdentityRoleAssignment>(command.CorrelationId, "LAST_ACTIVE_ADMIN_PROTECTED", "The final active identity administrator assignment cannot be revoked.");
        }

        const string sql = """
            UPDATE identity.user_roles
            SET assignment_status = 'REVOKED', revoked_at = now(), revoked_by_user_id = @actor_user_id,
                revocation_reason_code = @reason_code, updated_at = now(), updated_by_user_id = @actor_user_id,
                row_version = row_version + 1
            WHERE user_role_id = @assignment_id AND user_id = @user_id
              AND assignment_status <> 'REVOKED' AND row_version = @expected_row_version;
            """;
        await using var update = new NpgsqlCommand(sql, connection, transaction);
        update.Parameters.AddWithValue("actor_user_id", actor.UserId);
        update.Parameters.AddWithValue("reason_code", command.ReasonCode);
        update.Parameters.AddWithValue("assignment_id", command.AssignmentReference);
        update.Parameters.AddWithValue("user_id", command.UserReference);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityRoleAssignment>(command.CorrelationId);
        }

        await IncrementAuthorizationEpochAsync(connection, transaction, command.UserReference, actor.UserId, cancellationToken);
        await InsertAuditAsync(connection, transaction, "ROLE_REVOKED", "SUCCESS", command.ReasonCode, "UserRole", command.AssignmentReference, actor.UserId, command.CorrelationId, "A role assignment was revoked.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var assignment = (await ReadAssignmentsAsync(connection, null, command.UserReference, cancellationToken)).Single(item => item.AssignmentReference == command.AssignmentReference);
        return IdentityAdministrationResult<IdentityRoleAssignment>.Succeeded(assignment, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IdentityScopeGrant>> GrantScopeAsync(
        IdentityAdministrationActor actor,
        GrantIdentityScopeCommand command,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == command.UserReference)
        {
            return Forbidden<IdentityScopeGrant>(command.CorrelationId, "SELF_SCOPE_GRANT_PROHIBITED");
        }

        if (!ValidScopeShape(command.ScopeType, command.SiteReference, command.SiteGroupReference))
        {
            return Invalid<IdentityScopeGrant>(command.CorrelationId, "INVALID_SCOPE_SHAPE");
        }

        if (command.ScopeType == "GLOBAL")
        {
            return Forbidden<IdentityScopeGrant>(command.CorrelationId, "GLOBAL_SCOPE_POLICY_NOT_APPROVED");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, transaction, actor, [ScopeAssignmentPermission, "assignment.manage"], cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken) ||
            !await AssignmentBelongsToUserAsync(connection, transaction, command.AssignmentReference, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityScopeGrant>(command.CorrelationId);
        }

        if (!await ActorMayDelegateScopeAsync(connection, transaction, actor.UserId, command.ScopeType, command.SiteReference, command.SiteGroupReference, cancellationToken))
        {
            return Forbidden<IdentityScopeGrant>(command.CorrelationId, "DELEGATION_CEILING_EXCEEDED");
        }

        const string sql = """
            INSERT INTO identity.user_role_scope_grants (
                user_role_scope_grant_id, user_role_id, scope_type, site_id, site_group_id,
                grant_status, grant_reason_code, effective_from, effective_to,
                granted_by_user_id, created_by_user_id, updated_by_user_id)
            VALUES (
                gen_random_uuid(), @assignment_id, @scope_type::identity.authorization_scope_type_enum,
                @site_id, @site_group_id, 'ACTIVE', @reason_code, @effective_from, @effective_to,
                @actor_user_id, @actor_user_id, @actor_user_id)
            RETURNING user_role_scope_grant_id;
            """;
        Guid grantId;
        try
        {
            await using var insert = new NpgsqlCommand(sql, connection, transaction);
            insert.Parameters.AddWithValue("assignment_id", command.AssignmentReference);
            insert.Parameters.AddWithValue("scope_type", command.ScopeType);
            insert.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = Db(command.SiteReference);
            insert.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = Db(command.SiteGroupReference);
            insert.Parameters.AddWithValue("reason_code", command.ReasonCode);
            insert.Parameters.AddWithValue("effective_from", command.EffectiveFrom);
            insert.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = Db(command.EffectiveTo);
            insert.Parameters.AddWithValue("actor_user_id", actor.UserId);
            grantId = (Guid)(await insert.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            var existing = (await ReadScopeGrantsAsync(connection, null, command.UserReference, cancellationToken))
                .SingleOrDefault(item => item.AssignmentReference == command.AssignmentReference && item.ScopeType == command.ScopeType && item.SiteReference == command.SiteReference && item.SiteGroupReference == command.SiteGroupReference && item.Status != "REVOKED");
            return existing is not null
                ? IdentityAdministrationResult<IdentityScopeGrant>.Succeeded(existing, command.CorrelationId, "IDEMPOTENT_REPLAY")
                : Conflict<IdentityScopeGrant>(command.CorrelationId);
        }

        await IncrementAuthorizationEpochAsync(connection, transaction, command.UserReference, actor.UserId, cancellationToken);
        var eventType = command.ScopeType == "SITE" ? "SITE_SCOPE_GRANTED" : "SITE_GROUP_SCOPE_GRANTED";
        await InsertAuditAsync(connection, transaction, eventType, "SUCCESS", command.ReasonCode, "UserRoleScopeGrant", grantId, actor.UserId, command.CorrelationId, "An assignment-scoped authorization grant was activated.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var grant = (await ReadScopeGrantsAsync(connection, null, command.UserReference, cancellationToken)).Single(item => item.GrantReference == grantId);
        return IdentityAdministrationResult<IdentityScopeGrant>.Succeeded(grant, command.CorrelationId, "CREATED");
    }

    public async Task<IdentityAdministrationResult<IdentityScopeGrant>> RevokeScopeAsync(
        IdentityAdministrationActor actor,
        RevokeIdentityScopeCommand command,
        CancellationToken cancellationToken)
    {
        if (actor.UserId == command.UserReference)
        {
            return Forbidden<IdentityScopeGrant>(command.CorrelationId, "SELF_SCOPE_REVOCATION_PROHIBITED");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, transaction, actor, [ScopeAssignmentPermission, "assignment.manage"], cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<IdentityScopeGrant>(command.CorrelationId);
        }

        const string sql = """
            UPDATE identity.user_role_scope_grants g
            SET grant_status = 'REVOKED', revoked_at = now(), revoked_by_user_id = @actor_user_id,
                revocation_reason_code = @reason_code, updated_at = now(), updated_by_user_id = @actor_user_id,
                row_version = g.row_version + 1
            FROM identity.user_roles ur
            WHERE g.user_role_scope_grant_id = @grant_id AND g.user_role_id = @assignment_id
              AND ur.user_role_id = g.user_role_id AND ur.user_id = @user_id
              AND g.grant_status <> 'REVOKED' AND g.row_version = @expected_row_version
            RETURNING g.scope_type::text;
            """;
        await using var update = new NpgsqlCommand(sql, connection, transaction);
        update.Parameters.AddWithValue("actor_user_id", actor.UserId);
        update.Parameters.AddWithValue("reason_code", command.ReasonCode);
        update.Parameters.AddWithValue("grant_id", command.GrantReference);
        update.Parameters.AddWithValue("assignment_id", command.AssignmentReference);
        update.Parameters.AddWithValue("user_id", command.UserReference);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        var scopeType = (string?)await update.ExecuteScalarAsync(cancellationToken);
        if (scopeType is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityScopeGrant>(command.CorrelationId);
        }

        await IncrementAuthorizationEpochAsync(connection, transaction, command.UserReference, actor.UserId, cancellationToken);
        var eventType = scopeType switch { "SITE" => "SITE_SCOPE_REVOKED", "SITE_GROUP" => "SITE_GROUP_SCOPE_REVOKED", _ => "GLOBAL_SCOPE_REVOKED" };
        await InsertAuditAsync(connection, transaction, eventType, "SUCCESS", command.ReasonCode, "UserRoleScopeGrant", command.GrantReference, actor.UserId, command.CorrelationId, "An assignment-scoped authorization grant was revoked.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var grant = (await ReadScopeGrantsAsync(connection, null, command.UserReference, cancellationToken)).Single(item => item.GrantReference == command.GrantReference);
        return IdentityAdministrationResult<IdentityScopeGrant>.Succeeded(grant, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> CreatePrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor,
        CreatePrivilegedAccessRequestCommand command,
        CancellationToken cancellationToken)
    {
        if (!ValidOptionalScopeShape(command.ScopeType, command.SiteReference, command.SiteGroupReference))
        {
            return Invalid<IdentityPrivilegedAccessRequest>(command.CorrelationId, "INVALID_SCOPE_SHAPE");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, transaction, actor, [RoleAssignmentPermission, ScopeAssignmentPermission, "assignment.manage"], cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.TargetUserReference, cancellationToken))
        {
            return NotFound<IdentityPrivilegedAccessRequest>(command.CorrelationId);
        }

        var role = await ReadRoleAsync(connection, transaction, command.RoleReference, cancellationToken);
        if (role is null || role.Status != "ACTIVE")
        {
            return NotFound<IdentityPrivilegedAccessRequest>(command.CorrelationId);
        }

        if (command.ScopeType == "GLOBAL")
        {
            // DR-11 is unresolved. A request may be recorded, but no direct grant or implicit eligibility is created.
        }
        else if (command.ScopeType is not null &&
                 !await ActorMayDelegateScopeAsync(connection, transaction, actor.UserId, command.ScopeType, command.SiteReference, command.SiteGroupReference, cancellationToken))
        {
            return Forbidden<IdentityPrivilegedAccessRequest>(command.CorrelationId, "DELEGATION_CEILING_EXCEEDED");
        }

        const string sql = """
            INSERT INTO identity.privileged_access_requests (
                privileged_access_request_id, request_reference, target_user_id, requested_role_id,
                requested_scope_type, requested_site_id, requested_site_group_id,
                request_status, request_reason_code, requested_effective_from, requested_effective_to,
                requested_at, requested_by_user_id, expires_at, correlation_id,
                created_by_user_id, updated_by_user_id)
            VALUES (
                gen_random_uuid(), gen_random_uuid(), @target_user_id, @role_id,
                @scope_type::identity.authorization_scope_type_enum, @site_id, @site_group_id,
                'PENDING_DECISION', @reason_code, @effective_from, @effective_to,
                now(), @actor_user_id, @expires_at, @correlation_id,
                @actor_user_id, @actor_user_id)
            RETURNING request_reference;
            """;
        await using var insert = new NpgsqlCommand(sql, connection, transaction);
        insert.Parameters.AddWithValue("target_user_id", command.TargetUserReference);
        insert.Parameters.AddWithValue("role_id", command.RoleReference);
        insert.Parameters.Add("scope_type", NpgsqlDbType.Text).Value = Db(command.ScopeType);
        insert.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = Db(command.SiteReference);
        insert.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = Db(command.SiteGroupReference);
        insert.Parameters.AddWithValue("reason_code", command.ReasonCode);
        insert.Parameters.AddWithValue("effective_from", command.EffectiveFrom);
        insert.Parameters.Add("effective_to", NpgsqlDbType.TimestampTz).Value = Db(command.EffectiveTo);
        insert.Parameters.AddWithValue("actor_user_id", actor.UserId);
        insert.Parameters.Add("expires_at", NpgsqlDbType.TimestampTz).Value = Db(command.ExpiresAt);
        insert.Parameters.AddWithValue("correlation_id", command.CorrelationId);
        var requestReference = (Guid)(await insert.ExecuteScalarAsync(cancellationToken))!;

        await InsertAuditAsync(connection, transaction, "PRIVILEGED_ACCESS_REQUESTED", "SUCCESS", command.ReasonCode, "PrivilegedAccessRequest", requestReference, actor.UserId, command.CorrelationId, "A privileged access request was submitted; no authority was granted.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetPrivilegedAccessRequestAsync(actor, requestReference, command.CorrelationId, cancellationToken);
    }

    public async Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> GetPrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor,
        Guid requestReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAnyAsync(connection, null, actor, [RoleAssignmentPermission, PrivilegedDecisionPermission, "assignment.manage"], cancellationToken))
        {
            return NotFound<IdentityPrivilegedAccessRequest>(correlationId);
        }

        var request = await ReadPrivilegedRequestAsync(connection, null, requestReference, cancellationToken);
        if (request is null || !await CanAccessUserAsync(connection, null, actor.UserId, request.TargetUserReference, cancellationToken))
        {
            return NotFound<IdentityPrivilegedAccessRequest>(correlationId);
        }

        return IdentityAdministrationResult<IdentityPrivilegedAccessRequest>.Succeeded(request, correlationId);
    }

    public async Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> DecidePrivilegedAccessAsync(
        IdentityAdministrationActor actor,
        DecidePrivilegedAccessCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Decision is not ("APPROVE" or "REJECT"))
        {
            return Invalid<IdentityPrivilegedAccessRequest>(command.CorrelationId, "INVALID_PRIVILEGED_DECISION");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAsync(connection, transaction, actor, PrivilegedDecisionPermission, cancellationToken))
        {
            return Forbidden<IdentityPrivilegedAccessRequest>(command.CorrelationId);
        }

        var request = await ReadPrivilegedRequestAsync(connection, transaction, command.RequestReference, cancellationToken, forUpdate: true);
        if (request is null || !await CanAccessUserAsync(connection, transaction, actor.UserId, request.TargetUserReference, cancellationToken))
        {
            return NotFound<IdentityPrivilegedAccessRequest>(command.CorrelationId);
        }

        if (request.RequestedByUserReference == actor.UserId)
        {
            return Forbidden<IdentityPrivilegedAccessRequest>(command.CorrelationId, "SELF_PRIVILEGED_APPROVAL_PROHIBITED");
        }

        if (request.Status != "PENDING_DECISION" || request.RowVersion != command.ExpectedRowVersion)
        {
            return Conflict<IdentityPrivilegedAccessRequest>(command.CorrelationId, "PRIVILEGED_REQUEST_CONFLICT", "The privileged request is no longer pending at the expected version.");
        }

        const string decisionSql = """
            INSERT INTO identity.privileged_access_decisions (
                privileged_access_decision_id, privileged_access_request_id, decision_sequence,
                decision, decision_reason_code, decided_at, decided_by_user_id,
                decider_human_session_id, correlation_id)
            SELECT gen_random_uuid(), privileged_access_request_id,
                   COALESCE((SELECT max(d.decision_sequence) + 1 FROM identity.privileged_access_decisions d WHERE d.privileged_access_request_id = r.privileged_access_request_id), 1),
                   @decision::identity.privileged_access_decision_enum, @reason_code, now(), @actor_user_id,
                   @human_session_id, @correlation_id
            FROM identity.privileged_access_requests r
            WHERE r.request_reference = @request_reference;
            """;
        try
        {
            await using var insert = new NpgsqlCommand(decisionSql, connection, transaction);
            insert.Parameters.AddWithValue("decision", command.Decision);
            insert.Parameters.AddWithValue("reason_code", command.ReasonCode);
            insert.Parameters.AddWithValue("actor_user_id", actor.UserId);
            insert.Parameters.AddWithValue("human_session_id", actor.HumanSessionId);
            insert.Parameters.AddWithValue("correlation_id", command.CorrelationId);
            insert.Parameters.AddWithValue("request_reference", command.RequestReference);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityPrivilegedAccessRequest>(command.CorrelationId, "DUPLICATE_PRIVILEGED_DECISION", "This administrator already decided the privileged request.");
        }

        // DR-10 and DR-11 are unresolved. APPROVE records durable approval evidence but does not activate authority.
        var nextStatus = command.Decision == "REJECT" ? "REJECTED" : "APPROVED";
        const string updateSql = """
            UPDATE identity.privileged_access_requests
            SET request_status = @status::identity.privileged_access_request_status_enum,
                closed_at = CASE WHEN @status = 'REJECTED' THEN now() ELSE NULL END,
                updated_at = now(), updated_by_user_id = @actor_user_id, row_version = row_version + 1
            WHERE request_reference = @request_reference AND row_version = @expected_row_version;
            """;
        await using var update = new NpgsqlCommand(updateSql, connection, transaction);
        update.Parameters.AddWithValue("status", nextStatus);
        update.Parameters.AddWithValue("actor_user_id", actor.UserId);
        update.Parameters.AddWithValue("request_reference", command.RequestReference);
        update.Parameters.AddWithValue("expected_row_version", command.ExpectedRowVersion);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict<IdentityPrivilegedAccessRequest>(command.CorrelationId);
        }

        await InsertAuditAsync(connection, transaction, "PRIVILEGED_ACCESS_DECIDED", "SUCCESS", command.ReasonCode, "PrivilegedAccessRequest", command.RequestReference, actor.UserId, command.CorrelationId, "A privileged access decision was recorded; unresolved policy prevents automatic activation.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var decided = await ReadPrivilegedRequestAsync(connection, null, command.RequestReference, cancellationToken);
        return IdentityAdministrationResult<IdentityPrivilegedAccessRequest>.Succeeded(decided!, command.CorrelationId, nextStatus);
    }

    public async Task<IdentityAdministrationResult<bool>> ReviewAccessAsync(
        IdentityAdministrationActor actor,
        ReviewIdentityAccessCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Outcome != "CONFIRMED")
        {
            return Invalid<bool>(command.CorrelationId, "UNSUPPORTED_ACCESS_REVIEW_OUTCOME");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await IsAuthorizedAsync(connection, transaction, actor, AccessReviewPermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, transaction, actor.UserId, command.UserReference, cancellationToken))
        {
            return NotFound<bool>(command.CorrelationId);
        }

        foreach (var assignmentId in command.AssignmentReferences.Distinct())
        {
            const string sql = """
                UPDATE identity.user_roles
                SET last_reviewed_at = now(), last_reviewed_by_user_id = @actor_user_id,
                    updated_at = now(), updated_by_user_id = @actor_user_id, row_version = row_version + 1
                WHERE user_role_id = @reference AND user_id = @user_id;
                """;
            await using var update = new NpgsqlCommand(sql, connection, transaction);
            update.Parameters.AddWithValue("actor_user_id", actor.UserId);
            update.Parameters.AddWithValue("reference", assignmentId);
            update.Parameters.AddWithValue("user_id", command.UserReference);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound<bool>(command.CorrelationId);
            }
        }

        foreach (var grantId in command.ScopeGrantReferences.Distinct())
        {
            const string sql = """
                UPDATE identity.user_role_scope_grants g
                SET last_reviewed_at = now(), last_reviewed_by_user_id = @actor_user_id,
                    updated_at = now(), updated_by_user_id = @actor_user_id, row_version = g.row_version + 1
                FROM identity.user_roles ur
                WHERE g.user_role_scope_grant_id = @reference AND ur.user_role_id = g.user_role_id AND ur.user_id = @user_id;
                """;
            await using var update = new NpgsqlCommand(sql, connection, transaction);
            update.Parameters.AddWithValue("actor_user_id", actor.UserId);
            update.Parameters.AddWithValue("reference", grantId);
            update.Parameters.AddWithValue("user_id", command.UserReference);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound<bool>(command.CorrelationId);
            }
        }

        await InsertAuditAsync(connection, transaction, "ACCESS_REVIEW_COMPLETED", "SUCCESS", command.ReasonCode, "IdentityUser", command.UserReference, actor.UserId, command.CorrelationId, "A bounded role and scope access review was recorded.", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return IdentityAdministrationResult<bool>.Succeeded(true, command.CorrelationId);
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentitySessionSummary>>> ListSessionsAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, SessionViewPermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, null, actor.UserId, userReference, cancellationToken))
        {
            return NotFound<IReadOnlyList<IdentitySessionSummary>>(correlationId);
        }

        const string sql = """
            SELECT session_reference, session_audience::text, session_status::text, assurance_context_code,
                   mfa_requirement_satisfied, device_service_identity_id, authenticated_at, last_seen_at,
                   idle_expires_at, absolute_expires_at, revoked_at, row_version
            FROM identity.human_sessions
            WHERE user_id = @user_id
            ORDER BY authenticated_at DESC
            LIMIT 200;
            """;
        var sessions = new List<IdentitySessionSummary>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4),
                GetNullableGuid(reader, 5), reader.GetFieldValue<DateTimeOffset>(6), reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8), reader.GetFieldValue<DateTimeOffset>(9), GetNullableDateTimeOffset(reader, 10), reader.GetInt64(11)));
        }

        return IdentityAdministrationResult<IReadOnlyList<IdentitySessionSummary>>.Succeeded(sessions, correlationId);
    }

    public async Task<IdentityAdministrationResult<IdentityMfaStatus>> GetMfaStatusAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, MfaStatusPermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, null, actor.UserId, userReference, cancellationToken))
        {
            return NotFound<IdentityMfaStatus>(correlationId);
        }

        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM identity.user_roles ur JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE ur.user_id = @user_id AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now() AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE' AND r.is_privileged
            ) AS required,
            a.authenticator_status::text, a.enrollment_started_at, a.activated_at,
            a.last_successfully_used_at, a.reset_at, a.revoked_at, a.row_version
            FROM (SELECT 1) seed
            LEFT JOIN LATERAL (
                SELECT * FROM identity.user_mfa_authenticators
                WHERE user_id = @user_id AND authenticator_type = 'TOTP'
                ORDER BY created_at DESC LIMIT 1
            ) a ON true;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userReference);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var status = reader.IsDBNull(1) ? "NOT_ENROLLED" : reader.GetString(1);
        var value = new IdentityMfaStatus(
            reader.GetBoolean(0), status is "ACTIVE" or "SUSPENDED" or "RESET_REQUIRED", status,
            GetNullableDateTimeOffset(reader, 2), GetNullableDateTimeOffset(reader, 3), GetNullableDateTimeOffset(reader, 4),
            GetNullableDateTimeOffset(reader, 5), GetNullableDateTimeOffset(reader, 6), reader.IsDBNull(7) ? null : reader.GetInt64(7));
        return IdentityAdministrationResult<IdentityMfaStatus>.Succeeded(value, correlationId);
    }

    public async Task<IdentityAdministrationResult<bool>> AuthorizeAuthenticationAdministrationAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        string action,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var permission = action switch
        {
            "CREDENTIAL_RESET" => CredentialResetPermission,
            "SESSION_REVOKE" => SessionRevokePermission,
            "MFA_RESET" => MfaResetPermission,
            "MFA_REMOVE" => MfaRemovePermission,
            _ => null
        };
        if (permission is null)
        {
            return Invalid<bool>(correlationId, "INVALID_AUTHENTICATION_ADMINISTRATION_ACTION");
        }

        if (actor.UserId == userReference && action is "CREDENTIAL_RESET" or "MFA_RESET" or "MFA_REMOVE")
        {
            return Forbidden<bool>(correlationId, "SELF_AUTHENTICATION_ADMINISTRATION_PROHIBITED");
        }

        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, permission, cancellationToken) ||
            !await CanAccessUserAsync(connection, null, actor.UserId, userReference, cancellationToken))
        {
            return NotFound<bool>(correlationId);
        }

        if (action is "MFA_RESET" or "MFA_REMOVE" &&
            await UserHasPrivilegedRoleAsync(connection, null, userReference, cancellationToken) &&
            !await UserHasSystemIdentityAdministratorRoleAsync(connection, null, actor.UserId, cancellationToken))
        {
            return Forbidden<bool>(correlationId, "MFA_PRIVILEGE_CEILING_EXCEEDED");
        }

        return IdentityAdministrationResult<bool>.Succeeded(true, correlationId);
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentityAuditEntry>>> ListAuditEventsAsync(
        IdentityAdministrationActor actor,
        Guid userReference,
        int limit,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (!await IsAuthorizedAsync(connection, null, actor, UserViewPermission, cancellationToken) ||
            !await CanAccessUserAsync(connection, null, actor.UserId, userReference, cancellationToken))
        {
            return NotFound<IReadOnlyList<IdentityAuditEntry>>(correlationId);
        }

        const string sql = """
            SELECT audit_event_id, event_type, event_result::text, event_reason_code,
                   actor_user_id, summary, occurred_at, correlation_id
            FROM audit.audit_events
            WHERE (target_entity_type = 'IdentityUser' AND target_entity_id = @user_id)
               OR actor_user_id = @user_id
            ORDER BY occurred_at DESC, audit_event_id
            LIMIT @limit;
            """;
        var events = new List<IdentityAuditEntry>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("user_id", userReference);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), GetNullableString(reader, 3),
                GetNullableGuid(reader, 4), GetNullableString(reader, 5), reader.GetFieldValue<DateTimeOffset>(6), GetNullableGuid(reader, 7)));
        }

        return IdentityAdministrationResult<IReadOnlyList<IdentityAuditEntry>>.Succeeded(events, correlationId);
    }

    private static async Task<bool> IsAuthorizedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IdentityAdministrationActor actor,
        string permission,
        CancellationToken cancellationToken) =>
        await IsAuthorizedAnyAsync(connection, transaction, actor, [permission], cancellationToken);

    private static async Task<bool> IsAuthorizedAnyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        IdentityAdministrationActor actor,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.human_sessions hs
                JOIN identity.users u ON u.user_id = hs.user_id
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_permissions rp ON rp.role_id = r.role_id
                JOIN identity.permissions p ON p.permission_id = rp.permission_id
                WHERE hs.human_session_id = @human_session_id
                  AND hs.user_id = @user_id
                  AND hs.session_audience = 'MANAGEMENT_PLATFORM'
                  AND hs.session_status = 'ACTIVE'
                  AND hs.idle_expires_at > now()
                  AND hs.absolute_expires_at > now()
                  AND hs.authorization_epoch_snapshot = u.authorization_epoch
                  AND u.user_status = 'ACTIVE'
                  AND u.effective_from <= now()
                  AND (u.effective_to IS NULL OR u.effective_to > now())
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= now()
                  AND (r.effective_to IS NULL OR r.effective_to > now())
                  AND rp.binding_status = 'ACTIVE'
                  AND rp.effective_from <= now()
                  AND (rp.effective_to IS NULL OR rp.effective_to > now())
                  AND p.permission_status = 'ACTIVE'
                  AND p.permission_code = ANY(@permissions)
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("human_session_id", actor.HumanSessionId);
        command.Parameters.AddWithValue("user_id", actor.UserId);
        command.Parameters.AddWithValue("permissions", permissions.ToArray());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> CanAccessUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId == targetUserId)
        {
            return true;
        }

        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.user_roles aur
                JOIN identity.user_role_scope_grants ag ON ag.user_role_id = aur.user_role_id
                WHERE aur.user_id = @actor_user_id
                  AND aur.assignment_status = 'ACTIVE'
                  AND aur.effective_from <= now()
                  AND (aur.effective_to IS NULL OR aur.effective_to > now())
                  AND ag.grant_status = 'ACTIVE'
                  AND ag.effective_from <= now()
                  AND (ag.effective_to IS NULL OR ag.effective_to > now())
                  AND (ag.scope_type = 'GLOBAL' OR EXISTS (
                      SELECT 1
                      FROM identity.user_roles tur
                      JOIN identity.user_role_scope_grants tg ON tg.user_role_id = tur.user_role_id
                      WHERE tur.user_id = @target_user_id
                        AND tur.assignment_status = 'ACTIVE'
                        AND tg.grant_status = 'ACTIVE'
                        AND ((ag.scope_type = 'SITE' AND tg.scope_type = 'SITE' AND ag.site_id = tg.site_id)
                          OR (ag.scope_type = 'SITE_GROUP' AND tg.scope_type = 'SITE_GROUP' AND ag.site_group_id = tg.site_group_id)
                          OR (ag.scope_type = 'SITE_GROUP' AND tg.scope_type = 'SITE' AND EXISTS (
                              SELECT 1 FROM sites.sites s WHERE s.site_id = tg.site_id AND s.site_group_id = ag.site_group_id)))
                  ))
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("target_user_id", targetUserId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> ActorMayDelegateRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.user_roles ur
                JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE ur.user_id = @actor_user_id
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE'
                  AND (ur.role_id = @role_id OR r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR')
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("role_id", roleId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> ActorMayDelegateScopeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid actorUserId,
        string scopeType,
        Guid? siteId,
        Guid? siteGroupId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.user_roles ur
                JOIN identity.user_role_scope_grants g ON g.user_role_id = ur.user_role_id
                WHERE ur.user_id = @actor_user_id
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND g.grant_status = 'ACTIVE'
                  AND g.effective_from <= now()
                  AND (g.effective_to IS NULL OR g.effective_to > now())
                  AND (g.scope_type = 'GLOBAL'
                    OR (@scope_type = 'SITE' AND g.scope_type = 'SITE' AND g.site_id = @site_id)
                    OR (@scope_type = 'SITE_GROUP' AND g.scope_type = 'SITE_GROUP' AND g.site_group_id = @site_group_id)
                    OR (@scope_type = 'SITE' AND g.scope_type = 'SITE_GROUP' AND EXISTS (
                        SELECT 1 FROM sites.sites s WHERE s.site_id = @site_id AND s.site_group_id = g.site_group_id)))
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("scope_type", scopeType);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = Db(siteId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = Db(siteGroupId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> AssignmentBelongsToUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid assignmentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM identity.user_roles
                WHERE user_role_id = @assignment_id AND user_id = @user_id
                  AND assignment_status = 'ACTIVE'
                  AND effective_from <= now()
                  AND (effective_to IS NULL OR effective_to > now())
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> AssignmentIsIdentityAdministratorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid assignmentId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM identity.user_roles ur JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE ur.user_role_id = @assignment_id AND r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
                  AND ur.assignment_status = 'ACTIVE'
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("assignment_id", assignmentId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> IsLastActiveAdministratorAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM identity.user_roles tur JOIN identity.roles tr ON tr.role_id = tur.role_id
                WHERE tur.user_id = @target_user_id AND tur.assignment_status = 'ACTIVE'
                  AND tur.effective_from <= now() AND (tur.effective_to IS NULL OR tur.effective_to > now())
                  AND tr.role_status = 'ACTIVE' AND tr.effective_from <= now()
                  AND (tr.effective_to IS NULL OR tr.effective_to > now())
                  AND tr.role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
            ) AND (
                SELECT count(DISTINCT u.user_id)
                FROM identity.users u
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE u.user_status = 'ACTIVE'
                  AND u.effective_from <= now() AND (u.effective_to IS NULL OR u.effective_to > now())
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now() AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE' AND r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
            ) <= 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("target_user_id", targetUserId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> UserHasPrivilegedRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.user_roles ur
                JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE ur.user_id = @user_id
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= now()
                  AND (r.effective_to IS NULL OR r.effective_to > now())
                  AND r.is_privileged
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> UserHasSystemIdentityAdministratorRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM identity.user_roles ur
                JOIN identity.roles r ON r.role_id = ur.role_id
                WHERE ur.user_id = @user_id
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.effective_from <= now()
                  AND (ur.effective_to IS NULL OR ur.effective_to > now())
                  AND r.role_status = 'ACTIVE'
                  AND r.role_code = 'SYSTEM_RBAC_ADMINISTRATOR'
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task IncrementAuthorizationEpochAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.users
            SET authorization_epoch = authorization_epoch + 1,
                updated_at = now(), updated_by_user_id = @actor_user_id, row_version = row_version + 1
            WHERE user_id = @user_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeActiveSessionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid userId,
        Guid actorUserId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE identity.human_sessions
            SET session_status = 'REVOKED', revoked_at = now(), revoked_by_user_id = @actor_user_id,
                revocation_reason_code = @reason_code, updated_at = now(), updated_by_user_id = @actor_user_id,
                row_version = row_version + 1
            WHERE user_id = @user_id AND session_status = 'ACTIVE';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string eventType,
        string eventResult,
        string reasonCode,
        string targetType,
        Guid targetId,
        Guid actorUserId,
        Guid correlationId,
        string summary,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO audit.audit_events (
                audit_event_id, event_type, event_category, event_result, event_reason_code,
                target_entity_type, target_entity_id, source_schema, source_service_name, source_channel,
                actor_user_id, summary, occurred_at, recorded_at, correlation_id, created_at)
            VALUES (
                gen_random_uuid(), @event_type, 'SECURITY_RELEVANT', @event_result::audit.audit_event_result_enum,
                @reason_code, @target_type, @target_id, 'identity', 'central-pms', 'MANAGEMENT_PLATFORM',
                @actor_user_id, @summary, now(), now(), @correlation_id, now());
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_type", eventType);
        command.Parameters.AddWithValue("event_result", eventResult);
        command.Parameters.AddWithValue("reason_code", reasonCode);
        command.Parameters.AddWithValue("target_type", targetType);
        command.Parameters.AddWithValue("target_id", targetId);
        command.Parameters.AddWithValue("actor_user_id", actorUserId);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IdentityUserSummary?> ReadUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_id, username, display_name, email, mobile_number_masked, user_type::text,
                   user_status::text, effective_from, effective_to, last_login_at, row_version
            FROM identity.users WHERE user_id = @user_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    private static IdentityUserSummary ReadUser(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), MaskEmail(GetNullableString(reader, 3)),
            GetNullableString(reader, 4), reader.GetString(5), reader.GetString(6),
            reader.GetFieldValue<DateTimeOffset>(7), GetNullableDateTimeOffset(reader, 8),
            GetNullableDateTimeOffset(reader, 9), reader.GetInt64(10));

    private static async Task<IReadOnlyList<IdentityRoleAssignment>> ReadAssignmentsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ur.user_role_id, ur.user_id, ur.role_id, r.role_code, r.role_name,
                   ur.assignment_status::text, ur.effective_from, ur.effective_to,
                   ur.last_reviewed_at, ur.row_version
            FROM identity.user_roles ur JOIN identity.roles r ON r.role_id = ur.role_id
            WHERE ur.user_id = @user_id ORDER BY ur.assigned_at DESC;
            """;
        var values = new List<IdentityRoleAssignment>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6), GetNullableDateTimeOffset(reader, 7),
                GetNullableDateTimeOffset(reader, 8), reader.GetInt64(9)));
        }

        return values;
    }

    private static async Task<IReadOnlyList<IdentityScopeGrant>> ReadScopeGrantsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT g.user_role_scope_grant_id, g.user_role_id, g.scope_type::text, g.site_id,
                   g.site_group_id, g.grant_status::text, g.effective_from, g.effective_to,
                   g.last_reviewed_at, g.row_version
            FROM identity.user_role_scope_grants g
            JOIN identity.user_roles ur ON ur.user_role_id = g.user_role_id
            WHERE ur.user_id = @user_id ORDER BY g.granted_at DESC;
            """;
        var values = new List<IdentityScopeGrant>();
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), GetNullableGuid(reader, 3),
                GetNullableGuid(reader, 4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6),
                GetNullableDateTimeOffset(reader, 7), GetNullableDateTimeOffset(reader, 8), reader.GetInt64(9)));
        }

        return values;
    }

    private static async Task<IdentityRoleDefinition?> ReadRoleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT role_id, role_code, role_name, role_description, role_type::text, role_status::text,
                   is_privileged, requires_elevated_approval, effective_from, effective_to, row_version
            FROM identity.roles WHERE role_id = @role_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("role_id", roleId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), GetNullableString(reader, 3),
            reader.GetString(4), reader.GetString(5), reader.GetBoolean(6), reader.GetBoolean(7),
            reader.GetFieldValue<DateTimeOffset>(8), GetNullableDateTimeOffset(reader, 9), reader.GetInt64(10));
    }

    private static async Task<IdentityPrivilegedAccessRequest?> ReadPrivilegedRequestAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid requestReference,
        CancellationToken cancellationToken,
        bool forUpdate = false)
    {
        var sql = """
            SELECT privileged_access_request_id, request_reference, target_user_id, requested_role_id,
                   requested_scope_type::text, requested_site_id, requested_site_group_id,
                   request_status::text, request_reason_code, requested_effective_from, requested_effective_to,
                   requested_at, requested_by_user_id, expires_at, row_version
            FROM identity.privileged_access_requests
            WHERE request_reference = @request_reference
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        Guid requestId;
        IdentityPrivilegedAccessRequest request;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("request_reference", requestReference);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            requestId = reader.GetGuid(0);
            request = new(
                reader.GetGuid(1), reader.GetGuid(2), reader.GetGuid(3), GetNullableString(reader, 4),
                GetNullableGuid(reader, 5), GetNullableGuid(reader, 6), reader.GetString(7), reader.GetString(8),
                reader.GetFieldValue<DateTimeOffset>(9), GetNullableDateTimeOffset(reader, 10),
                reader.GetFieldValue<DateTimeOffset>(11), reader.GetGuid(12), GetNullableDateTimeOffset(reader, 13),
                reader.GetInt64(14), []);
        }

        const string decisionSql = """
            SELECT decision_sequence, decision::text, decision_reason_code, decided_at, decided_by_user_id
            FROM identity.privileged_access_decisions
            WHERE privileged_access_request_id = @request_id ORDER BY decision_sequence;
            """;
        var decisions = new List<IdentityPrivilegedAccessDecision>();
        await using (var decision = new NpgsqlCommand(decisionSql, connection, transaction))
        {
            decision.Parameters.AddWithValue("request_id", requestId);
            await using var reader = await decision.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                decisions.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetGuid(4)));
            }
        }

        return request with { Decisions = decisions };
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static bool ValidScopeShape(string scopeType, Guid? siteId, Guid? siteGroupId) =>
        scopeType switch
        {
            "SITE" => siteId.HasValue && siteId != Guid.Empty && !siteGroupId.HasValue,
            "SITE_GROUP" => !siteId.HasValue && siteGroupId.HasValue && siteGroupId != Guid.Empty,
            "GLOBAL" => !siteId.HasValue && !siteGroupId.HasValue,
            _ => false
        };

    private static bool ValidOptionalScopeShape(string? scopeType, Guid? siteId, Guid? siteGroupId) =>
        scopeType is null ? !siteId.HasValue && !siteGroupId.HasValue : ValidScopeShape(scopeType, siteId, siteGroupId);

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var separator = email.IndexOf('@');
        return separator <= 0 ? "***" : $"{email[0]}***{email[separator..]}";
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static Guid? GetNullableGuid(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetGuid(ordinal);

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);

    private static IdentityAdministrationResult<T> Forbidden<T>(Guid correlationId, string classification = "IDENTITY_ADMIN_FORBIDDEN") =>
        IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Forbidden, classification, "The requested identity administration operation is not permitted.", correlationId);

    private static IdentityAdministrationResult<T> NotFound<T>(Guid correlationId) =>
        IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.NotFound, "IDENTITY_ADMIN_RESOURCE_NOT_FOUND", "The requested identity administration resource is unavailable.", correlationId);

    private static IdentityAdministrationResult<T> Conflict<T>(Guid correlationId, string classification = "IDENTITY_ADMIN_VERSION_CONFLICT", string message = "The identity administration state changed. Refresh and retry with the current version.") =>
        IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Conflict, classification, message, correlationId);

    private static IdentityAdministrationResult<T> Invalid<T>(Guid correlationId, string classification) =>
        IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Invalid, classification, "The identity administration request is invalid.", correlationId);
}
