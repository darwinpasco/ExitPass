using ExitPass.AuditEventService.Application.AuditEvents;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.AuditEventService.Infrastructure.AuditEvents;

public sealed class PostgresAuditEventRepository(NpgsqlDataSource dataSource) : IAuditEventRepository
{
    public async Task<(AuditEventRecord Record, bool Created)> AppendAsync(
        AuditEventRecord record,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit.audit_events (
                audit_event_id, event_type, event_category, event_result, event_reason_code,
                target_entity_type, target_entity_id, related_entity_type, related_entity_id,
                source_schema, source_service_name, source_channel, actor_service_identity_id,
                summary, occurred_at, correlation_id, causation_id, created_by_service_identity_id)
            VALUES (
                @audit_event_id, @event_type, @event_category::audit.audit_event_category_enum,
                @event_result::audit.audit_event_result_enum, @event_reason_code,
                'SITE', @site_id, CASE WHEN @terminal_id IS NULL THEN NULL ELSE 'TERMINAL' END,
                @terminal_id, 'audit', @source_service_name, @source_channel,
                @actor_service_identity_id, @summary, @occurred_at, @correlation_id,
                @causation_id, @actor_service_identity_id)
            ON CONFLICT (audit_event_id) DO NOTHING
            RETURNING recorded_at;
            """;
        AddParameters(command, record);
        var recordedAtValue = await command.ExecuteScalarAsync(cancellationToken);
        var created = recordedAtValue is not null;
        var persisted = created
            ? record with { RecordedAt = new DateTimeOffset((DateTime)recordedAtValue!) }
            : await FindByIdAsync(connection, transaction, record.AuditEventId, cancellationToken)
                ?? throw new AuditEventIdentityConflictException();

        if (!created && !SameImmutableContent(persisted, record))
            throw new AuditEventIdentityConflictException();

        await transaction.CommitAsync(cancellationToken);
        return (persisted, created);
    }

    public async Task<IReadOnlyList<AuditEventRecord>> QueryAsync(
        Guid correlationId,
        Guid? siteId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT audit_event_id, event_type, event_category::text, event_result::text,
                   event_reason_code, target_entity_id, related_entity_id,
                   source_service_name, source_channel, actor_service_identity_id,
                   summary, occurred_at, recorded_at, correlation_id, causation_id
              FROM audit.audit_events
             WHERE correlation_id = @correlation_id
               AND target_entity_type = 'SITE'
               AND (@site_id IS NULL OR target_entity_id = @site_id)
             ORDER BY recorded_at, audit_event_id
             LIMIT 100;
            """);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = correlationId;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = siteId is null ? DBNull.Value : siteId.Value;
        var records = new List<AuditEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) records.Add(Read(reader));
        return records;
    }

    private static async Task<AuditEventRecord?> FindByIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid auditEventId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT audit_event_id, event_type, event_category::text, event_result::text,
                   event_reason_code, target_entity_id, related_entity_id,
                   source_service_name, source_channel, actor_service_identity_id,
                   summary, occurred_at, recorded_at, correlation_id, causation_id
              FROM audit.audit_events
             WHERE audit_event_id = @audit_event_id
               AND target_entity_type = 'SITE';
            """;
        command.Parameters.Add("audit_event_id", NpgsqlDbType.Uuid).Value = auditEventId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    private static AuditEventRecord Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetGuid(5),
        reader.IsDBNull(6) ? null : reader.GetGuid(6), reader.GetString(7), reader.GetString(8),
        reader.GetGuid(9), reader.IsDBNull(10) ? null : reader.GetString(10),
        new DateTimeOffset(reader.GetDateTime(11)), new DateTimeOffset(reader.GetDateTime(12)),
        reader.GetGuid(13), reader.IsDBNull(14) ? null : reader.GetGuid(14));

    private static void AddParameters(NpgsqlCommand command, AuditEventRecord record)
    {
        command.Parameters.Add("audit_event_id", NpgsqlDbType.Uuid).Value = record.AuditEventId;
        command.Parameters.Add("event_type", NpgsqlDbType.Varchar).Value = record.EventType;
        command.Parameters.Add("event_category", NpgsqlDbType.Varchar).Value = record.EventCategory;
        command.Parameters.Add("event_result", NpgsqlDbType.Varchar).Value = record.EventResult;
        command.Parameters.Add("event_reason_code", NpgsqlDbType.Varchar).Value =
            (object?)record.EventReasonCode ?? DBNull.Value;
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = record.SiteId;
        command.Parameters.Add("terminal_id", NpgsqlDbType.Uuid).Value =
            (object?)record.TerminalId ?? DBNull.Value;
        command.Parameters.Add("source_service_name", NpgsqlDbType.Varchar).Value = record.SourceServiceName;
        command.Parameters.Add("source_channel", NpgsqlDbType.Varchar).Value = record.SourceChannel;
        command.Parameters.Add("actor_service_identity_id", NpgsqlDbType.Uuid).Value = record.ActorServiceIdentityId;
        command.Parameters.Add("summary", NpgsqlDbType.Varchar).Value = (object?)record.Summary ?? DBNull.Value;
        command.Parameters.Add("occurred_at", NpgsqlDbType.TimestampTz).Value = record.OccurredAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = record.CorrelationId;
        command.Parameters.Add("causation_id", NpgsqlDbType.Uuid).Value =
            (object?)record.CausationId ?? DBNull.Value;
    }

    private static bool SameImmutableContent(AuditEventRecord left, AuditEventRecord right) =>
        left.AuditEventId == right.AuditEventId && left.EventType == right.EventType &&
        left.EventCategory == right.EventCategory && left.EventResult == right.EventResult &&
        left.EventReasonCode == right.EventReasonCode && left.SiteId == right.SiteId &&
        left.TerminalId == right.TerminalId && left.SourceServiceName == right.SourceServiceName &&
        left.SourceChannel == right.SourceChannel && left.ActorServiceIdentityId == right.ActorServiceIdentityId &&
        left.Summary == right.Summary && left.OccurredAt == right.OccurredAt &&
        left.CorrelationId == right.CorrelationId && left.CausationId == right.CausationId;
}
