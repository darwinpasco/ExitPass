using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for Operator Console access evaluation audit evidence.
/// </summary>
public sealed class OperatorConsoleAccessEvaluationWriter : IOperatorConsoleAccessEvaluationWriter
{
    private readonly string _connectionString;

    /// <summary>
    /// Creates an Operator Console access evaluation writer.
    /// </summary>
    public OperatorConsoleAccessEvaluationWriter(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    /// <inheritdoc />
    public async Task<OperatorConsoleAccessEvaluationResult> PersistAsync(
        OperatorConsoleAccessEvaluationResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var evaluationId = await InsertActionLogAsync(connection, result, cancellationToken);
        return result with
        {
            EvaluationId = evaluationId,
            Persisted = true
        };
    }

    private static async Task<Guid> InsertActionLogAsync(
        NpgsqlConnection connection,
        OperatorConsoleAccessEvaluationResult result,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operations.operator_action_logs (
                operator_user_id,
                action_type,
                action_reason_code,
                target_entity_type,
                target_entity_id,
                site_id,
                action_status,
                action_notes,
                performed_at,
                correlation_id,
                created_by_user_id
            )
            VALUES (
                @operator_user_id,
                'CONTROLLED_RECHECK'::operations.operator_action_type_enum,
                @action_reason_code,
                @target_entity_type,
                @target_entity_id,
                @site_id,
                @action_status::operations.operator_action_status_enum,
                @action_notes,
                @performed_at,
                @correlation_id,
                @created_by_user_id
            )
            RETURNING operator_action_log_id;
            """;

        var context = result.PersistenceContext;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = context.OperatorUserId;
        command.Parameters.Add("action_reason_code", NpgsqlDbType.Varchar).Value = context.RequestedAction;
        command.Parameters.Add("target_entity_type", NpgsqlDbType.Varchar).Value = DbValue(context.TargetEntityType);
        command.Parameters.Add("target_entity_id", NpgsqlDbType.Uuid).Value = DbValue(context.TargetEntityId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(context.SiteId);
        command.Parameters.Add("action_status", NpgsqlDbType.Text).Value = result.Allowed ? "SUCCESS" : "DENIED";
        command.Parameters.Add("action_notes", NpgsqlDbType.Text).Value = BuildDecisionSnapshot(result);
        command.Parameters.Add("performed_at", NpgsqlDbType.TimestampTz).Value = result.EvaluatedAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = result.CorrelationId;
        command.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = context.OperatorUserId;

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is Guid evaluationId
            ? evaluationId
            : throw new InvalidOperationException("Operator Console access evaluation action log insert did not return an ID.");
    }

    private static string BuildDecisionSnapshot(OperatorConsoleAccessEvaluationResult result) =>
        JsonSerializer.Serialize(new
        {
            result.Allowed,
            result.Decision,
            result.DenialReasons,
            result.EffectiveRole,
            result.DeviceTrust,
            result.ShiftContext,
            result.SiteContext,
            result.EvaluatedAt,
            result.CorrelationId,
            result.PersistenceContext.WorkflowCode,
            result.PersistenceContext.RequestedAction,
            result.PersistenceContext.TargetEntityType,
            result.PersistenceContext.TargetEntityId
        });

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
