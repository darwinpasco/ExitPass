using ExitPass.CentralPms.Application.HumanAuthentication;
using ExitPass.CentralPms.Application.Security;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.HumanAuthentication;

public sealed class PostgresAptCashCustodyCounterpartyDirectory(string connectionString)
    : IAptCashCustodyCounterpartyDirectory
{
    private readonly string _connectionString = !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : throw new ArgumentException("Connection string is required.", nameof(connectionString));

    public async Task<IReadOnlyList<AptCashCustodyCounterparty>> ListEligibleAsync(
        AptCashCustodyCounterpartyQuery query,
        CancellationToken cancellationToken)
    {
        if (!AptCashCustodyCounterpartyPolicy.TryResolveEligibleRoleCodes(
                query.MovementType,
                query.OriginalMovementType,
                out var roleCodes))
        {
            return Array.Empty<AptCashCustodyCounterparty>();
        }

        var required = query.RequiredCounterpartyReference?.Trim();
        var requiredUserId = Guid.TryParse(required, out var parsed) && parsed != Guid.Empty
            ? parsed
            : (Guid?)null;
        var requiredUsername = requiredUserId.HasValue || string.IsNullOrWhiteSpace(required)
            ? null
            : required.ToLowerInvariant();

        const string sql = """
            WITH eligible AS (
                SELECT u.user_id, u.username, u.display_name, r.role_code, r.role_name,
                       row_number() OVER (
                           PARTITION BY u.user_id
                           ORDER BY array_position(@role_codes, r.role_code), r.role_code) AS role_rank
                FROM identity.users u
                JOIN identity.user_roles ur ON ur.user_id = u.user_id
                JOIN identity.roles r ON r.role_id = ur.role_id
                JOIN identity.role_user_type_compatibility compatibility
                  ON compatibility.role_id = r.role_id
                 AND compatibility.user_type = u.user_type
                JOIN identity.user_role_scope_grants scope_grant
                  ON scope_grant.user_role_id = ur.user_role_id
                JOIN identity.role_permissions role_permission ON role_permission.role_id = r.role_id
                JOIN identity.permissions permission ON permission.permission_id = role_permission.permission_id
                WHERE u.user_status = 'ACTIVE'
                  AND u.effective_from <= @as_of
                  AND (u.effective_to IS NULL OR u.effective_to > @as_of)
                  AND u.user_id <> @initiating_user_id
                  AND ur.assignment_status = 'ACTIVE'
                  AND ur.revoked_at IS NULL
                  AND ur.effective_from <= @as_of
                  AND (ur.effective_to IS NULL OR ur.effective_to > @as_of)
                  AND r.role_status = 'ACTIVE'
                  AND r.effective_from <= @as_of
                  AND (r.effective_to IS NULL OR r.effective_to > @as_of)
                  AND r.role_code = ANY(@role_codes)
                  AND scope_grant.scope_type = 'SITE'
                  AND scope_grant.site_id = @site_id
                  AND scope_grant.grant_status = 'ACTIVE'
                  AND scope_grant.revoked_at IS NULL
                  AND scope_grant.effective_from <= @as_of
                  AND (scope_grant.effective_to IS NULL OR scope_grant.effective_to > @as_of)
                  AND role_permission.binding_status = 'ACTIVE'
                  AND role_permission.revoked_at IS NULL
                  AND role_permission.effective_from <= @as_of
                  AND (role_permission.effective_to IS NULL OR role_permission.effective_to > @as_of)
                  AND permission.permission_status = 'ACTIVE'
                  AND permission.permission_code = @acknowledge_permission
                  AND (@required_user_id IS NULL OR u.user_id = @required_user_id)
                  AND (@required_username IS NULL OR u.username_normalized = @required_username)
            )
            SELECT user_id, username, display_name, role_code, role_name
            FROM eligible
            WHERE role_rank = 1
            ORDER BY lower(display_name), user_id;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("initiating_user_id", query.InitiatingUserReference);
        command.Parameters.AddWithValue("site_id", query.SiteReference);
        command.Parameters.AddWithValue("role_codes", roleCodes.ToArray());
        command.Parameters.AddWithValue("acknowledge_permission", AptHumanPermissionCatalog.CashCustodyMovementAcknowledge);
        command.Parameters.AddWithValue("as_of", query.AsOf);
        command.Parameters.Add("required_user_id", NpgsqlDbType.Uuid).Value = (object?)requiredUserId ?? DBNull.Value;
        command.Parameters.Add("required_username", NpgsqlDbType.Text).Value = (object?)requiredUsername ?? DBNull.Value;

        var results = new List<AptCashCustodyCounterparty>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }
        return results;
    }
}
