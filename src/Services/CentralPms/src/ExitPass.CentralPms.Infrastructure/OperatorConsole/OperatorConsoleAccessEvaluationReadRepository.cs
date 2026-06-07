using System.Data;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed read-only repository for Operator Console access evaluation inputs.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationReadRepository : IOperatorConsoleAccessEvaluationReadRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console access evaluation read repository.
    /// </summary>
    public OperatorConsoleAccessEvaluationReadRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleAccessEvaluationReadContext> LoadAsync(
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var user = await ReadUserAsync(connection, request, cancellationToken);
        var site = request.SiteId.HasValue
            ? await ReadSiteAsync(connection, request.SiteId.Value, cancellationToken)
            : null;

        if (user is null)
        {
            return OperatorConsoleAccessEvaluationReadContext.Empty(request);
        }

        var effectiveFrom = user.EffectiveFrom ?? DateTimeOffset.MinValue;
        var effectiveTo = user.EffectiveTo;
        var active = string.Equals(user.UserStatus, "ACTIVE", StringComparison.Ordinal) &&
            effectiveFrom <= request.EvaluatedAt &&
            (!effectiveTo.HasValue || effectiveTo.Value > request.EvaluatedAt);

        var mapping = new OperatorHrIdentityMappingReadModel(
            user.UserId,
            user.UserId,
            "IDENTITY_USERS",
            active ? "ACTIVE" : user.UserStatus,
            effectiveFrom,
            effectiveTo,
            active ? null : user.SuspendedAt ?? user.LockedAt ?? user.RetiredAt,
            active ? null : user.UserStatus);

        var device = BuildDeviceBinding(request, site);
        var assignment = device is null ? null : BuildDeviceAssignment(request, device);
        var shift = BuildShift(request, mapping, site);

        return new OperatorConsoleAccessEvaluationReadContext(
            request,
            mapping,
            device,
            assignment,
            shift,
            LatestShiftVersion: null,
            LatestShiftRevocation: null,
            ActiveShiftTakeover: null,
            StatutoryEntitlementFingerprint: null);
    }

    private static async Task<UserRow?> ReadUserAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessEvaluationReadRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                user_id,
                user_status::text,
                effective_from,
                effective_to,
                locked_at,
                suspended_at,
                retired_at
            FROM identity.users
            WHERE user_id = @user_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("user_id", NpgsqlDbType.Uuid).Value = request.UserId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserRow(
            reader.GetGuid("user_id"),
            reader.GetString("user_status"),
            GetNullableDateTimeOffset(reader, "effective_from"),
            GetNullableDateTimeOffset(reader, "effective_to"),
            GetNullableDateTimeOffset(reader, "locked_at"),
            GetNullableDateTimeOffset(reader, "suspended_at"),
            GetNullableDateTimeOffset(reader, "retired_at"));
    }

    private static async Task<SiteRow?> ReadSiteAsync(
        NpgsqlConnection connection,
        Guid siteId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT site_id, site_group_id, site_status::text
            FROM sites.sites
            WHERE site_id = @site_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SiteRow(
            reader.GetGuid("site_id"),
            reader.GetGuid("site_group_id"),
            reader.GetString("site_status"));
    }

    private static OperatorDeviceBindingReadModel? BuildDeviceBinding(
        OperatorConsoleAccessEvaluationReadRequest request,
        SiteRow? site)
    {
        if (!request.OperatorDeviceBindingId.HasValue)
        {
            return null;
        }

        var siteId = site?.SiteId ?? request.SiteId;
        var siteGroupId = site?.SiteGroupId ?? request.SiteGroupId;
        if (!siteId.HasValue || !siteGroupId.HasValue)
        {
            return null;
        }

        var active = site is null || string.Equals(site.SiteStatus, "ACTIVE", StringComparison.Ordinal);
        return new OperatorDeviceBindingReadModel(
            request.OperatorDeviceBindingId.Value,
            $"LOCKED-SCHEMA-{request.OperatorDeviceBindingId.Value:N}",
            "Locked Schema Operator Console Device",
            siteGroupId.Value,
            siteId.Value,
            ServiceIdentityId: null,
            active ? "ACTIVE" : "INACTIVE",
            "BROWSER_KEY_AND_MTLS",
            "LOCKED_SCHEMA",
            request.EvaluatedAt,
            RevokedAt: null,
            RevocationReasonCode: null);
    }

    private static OperatorDeviceAssignmentReadModel BuildDeviceAssignment(
        OperatorConsoleAccessEvaluationReadRequest request,
        OperatorDeviceBindingReadModel device) =>
        new(
            request.OperatorDeviceBindingId!.Value,
            device.OperatorDeviceBindingId,
            device.SiteGroupId,
            device.SiteId,
            "ACTIVE",
            "LOCKED_SCHEMA",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            EndedAt: null);

    private static OperatorShiftReadModel? BuildShift(
        OperatorConsoleAccessEvaluationReadRequest request,
        OperatorHrIdentityMappingReadModel mapping,
        SiteRow? site)
    {
        if (!request.OperatorShiftId.HasValue)
        {
            return null;
        }

        var siteId = site?.SiteId ?? request.SiteId;
        var siteGroupId = site?.SiteGroupId ?? request.SiteGroupId;
        if (!siteId.HasValue || !siteGroupId.HasValue)
        {
            return null;
        }

        return new OperatorShiftReadModel(
            request.OperatorShiftId.Value,
            mapping.HrIdentityMappingId,
            request.UserId,
            siteGroupId.Value,
            siteId.Value,
            "IDENTITY_USERS",
            "ACTIVE",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            RevokedAt: null,
            RevocationReasonCode: null,
            CurrentTakeoverId: null);
    }

    private static DateTimeOffset? GetNullableDateTimeOffset(NpgsqlDataReader reader, string columnName)
    {
        var ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    private sealed record UserRow(
        Guid UserId,
        string UserStatus,
        DateTimeOffset? EffectiveFrom,
        DateTimeOffset? EffectiveTo,
        DateTimeOffset? LockedAt,
        DateTimeOffset? SuspendedAt,
        DateTimeOffset? RetiredAt);

    private sealed record SiteRow(Guid SiteId, Guid SiteGroupId, string SiteStatus);
}
