using System.Text.Json;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed writer for Operator Console access evaluation audit evidence.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Writes are limited to operator_console.operator_access_evaluations and operator_console.operator_access_evaluation_reasons.
/// - Writes remain scoped to Operator Console evaluation evidence.
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var evaluationId = await InsertEvaluationAsync(connection, transaction, result, cancellationToken);
            await InsertReasonsAsync(connection, transaction, evaluationId, result, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return result with
            {
                EvaluationId = evaluationId,
                Persisted = true
            };
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<Guid> InsertEvaluationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OperatorConsoleAccessEvaluationResult result,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.operator_access_evaluations (
                correlation_id,
                requested_action,
                evaluation_status,
                operator_user_id,
                hr_identity_mapping_id,
                operator_device_binding_id,
                operator_shift_id,
                shift_takeover_id,
                site_group_id,
                site_id,
                target_entity_type,
                target_entity_id,
                evaluated_at,
                decision_snapshot_json,
                created_by_user_id
            )
            VALUES (
                @correlation_id,
                @requested_action,
                @evaluation_status::operator_console.access_evaluation_status_enum,
                @operator_user_id,
                @hr_identity_mapping_id,
                @operator_device_binding_id,
                @operator_shift_id,
                @shift_takeover_id,
                @site_group_id,
                @site_id,
                @target_entity_type,
                @target_entity_id,
                @evaluated_at,
                @decision_snapshot_json,
                @created_by_user_id
            )
            RETURNING operator_access_evaluation_id;
            """;

        var context = result.PersistenceContext;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = result.CorrelationId;
        command.Parameters.Add("requested_action", NpgsqlDbType.Varchar).Value = context.RequestedAction;
        command.Parameters.Add("evaluation_status", NpgsqlDbType.Text).Value = result.Decision;
        command.Parameters.Add("operator_user_id", NpgsqlDbType.Uuid).Value = context.OperatorUserId;
        command.Parameters.Add("hr_identity_mapping_id", NpgsqlDbType.Uuid).Value = DbValue(context.HrIdentityMappingId);
        command.Parameters.Add("operator_device_binding_id", NpgsqlDbType.Uuid).Value = DbValue(context.OperatorDeviceBindingId);
        command.Parameters.Add("operator_shift_id", NpgsqlDbType.Uuid).Value = DbValue(context.OperatorShiftId);
        command.Parameters.Add("shift_takeover_id", NpgsqlDbType.Uuid).Value = DbValue(context.ShiftTakeoverId);
        command.Parameters.Add("site_group_id", NpgsqlDbType.Uuid).Value = DbValue(context.SiteGroupId);
        command.Parameters.Add("site_id", NpgsqlDbType.Uuid).Value = DbValue(context.SiteId);
        command.Parameters.Add("target_entity_type", NpgsqlDbType.Varchar).Value = DbValue(context.TargetEntityType);
        command.Parameters.Add("target_entity_id", NpgsqlDbType.Uuid).Value = DbValue(context.TargetEntityId);
        command.Parameters.Add("evaluated_at", NpgsqlDbType.TimestampTz).Value = result.EvaluatedAt;
        command.Parameters.Add("decision_snapshot_json", NpgsqlDbType.Jsonb).Value = BuildDecisionSnapshot(result);
        command.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = context.OperatorUserId;

        var insertedId = await command.ExecuteScalarAsync(cancellationToken);
        return insertedId is Guid evaluationId
            ? evaluationId
            : throw new InvalidOperationException("Operator Console access evaluation insert did not return an evaluation ID.");
    }

    private static async Task InsertReasonsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid evaluationId,
        OperatorConsoleAccessEvaluationResult result,
        CancellationToken cancellationToken)
    {
        if (result.DenialReasons.Count == 0)
        {
            return;
        }

        const string sql = """
            INSERT INTO operator_console.operator_access_evaluation_reasons (
                operator_access_evaluation_id,
                reason_code,
                reason_message,
                reason_source,
                source_entity_type,
                source_entity_id,
                evaluated_fact_path,
                display_order,
                created_by_user_id
            )
            VALUES (
                @operator_access_evaluation_id,
                @reason_code,
                @reason_message,
                @reason_source,
                @source_entity_type,
                @source_entity_id,
                @evaluated_fact_path,
                @display_order,
                @created_by_user_id
            );
            """;

        for (var index = 0; index < result.DenialReasons.Count; index++)
        {
            var reason = result.DenialReasons[index];
            var source = MapSource(result.PersistenceContext, reason);

            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.Add("operator_access_evaluation_id", NpgsqlDbType.Uuid).Value = evaluationId;
            command.Parameters.Add("reason_code", NpgsqlDbType.Varchar).Value = reason;
            command.Parameters.Add("reason_message", NpgsqlDbType.Text).Value = ReasonMessage(reason);
            command.Parameters.Add("reason_source", NpgsqlDbType.Varchar).Value = "EVALUATOR_RULE";
            command.Parameters.Add("source_entity_type", NpgsqlDbType.Varchar).Value = DbValue(source.EntityType);
            command.Parameters.Add("source_entity_id", NpgsqlDbType.Uuid).Value = DbValue(source.EntityId);
            command.Parameters.Add("evaluated_fact_path", NpgsqlDbType.Varchar).Value = source.FactPath;
            command.Parameters.Add("display_order", NpgsqlDbType.Integer).Value = index;
            command.Parameters.Add("created_by_user_id", NpgsqlDbType.Uuid).Value = result.PersistenceContext.OperatorUserId;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static string BuildDecisionSnapshot(OperatorConsoleAccessEvaluationResult result)
    {
        var snapshot = new
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
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static (string? EntityType, Guid? EntityId, string FactPath) MapSource(
        OperatorConsoleAccessEvaluationPersistenceContext context,
        string reason) =>
        reason switch
        {
            "HR_IDENTITY_MAPPING_NOT_FOUND" or "HR_IDENTITY_MAPPING_INACTIVE" =>
                ("HR_IDENTITY_MAPPING", context.HrIdentityMappingId, "hrIdentityMapping.mappingStatus"),
            "DEVICE_BINDING_NOT_FOUND" or "DEVICE_BINDING_INACTIVE" or "DEVICE_NOT_TRUSTED" =>
                ("OPERATOR_DEVICE_BINDING", context.OperatorDeviceBindingId, "deviceBinding.trustLevel"),
            "DEVICE_SITE_ASSIGNMENT_NOT_FOUND" or "DEVICE_SITE_ASSIGNMENT_INVALID" =>
                ("OPERATOR_DEVICE_ASSIGNMENT", null, "deviceAssignment.assignmentStatusCode"),
            "NO_ACTIVE_SHIFT" or "SHIFT_REVOKED" =>
                ("OPERATOR_SHIFT", context.OperatorShiftId, "shift.operationalStatus"),
            "SHIFT_TAKEOVER_ACTIVE" =>
                ("SHIFT_TAKEOVER", context.ShiftTakeoverId, "shiftTakeover.takeoverStatus"),
            "WORKFLOW_NOT_SUPPORTED" =>
                (null, null, "request.workflowCode"),
            "ACTION_NOT_SUPPORTED" =>
                (null, null, "request.controlledActionCode"),
            _ =>
                (null, null, "evaluation.denialReasons")
        };

    private static string ReasonMessage(string reason) =>
        reason switch
        {
            "HR_IDENTITY_MAPPING_NOT_FOUND" => "No current HR identity mapping was found for the operator.",
            "HR_IDENTITY_MAPPING_INACTIVE" => "The operator HR identity mapping is not active for the evaluation time.",
            "DEVICE_BINDING_NOT_FOUND" => "No Operator Console device binding was found.",
            "DEVICE_BINDING_INACTIVE" => "The Operator Console device binding is not active.",
            "DEVICE_NOT_TRUSTED" => "The Operator Console device is not trusted.",
            "DEVICE_SITE_ASSIGNMENT_NOT_FOUND" => "No current device site assignment was found.",
            "DEVICE_SITE_ASSIGNMENT_INVALID" => "The device site assignment is not valid for the requested site.",
            "NO_ACTIVE_SHIFT" => "No active operator shift was found for the requested evaluation.",
            "SHIFT_REVOKED" => "The operator shift is revoked.",
            "SHIFT_TAKEOVER_ACTIVE" => "An active conflicting shift takeover exists.",
            "WORKFLOW_NOT_SUPPORTED" => "The requested workflow is not supported by the Operator Console access evaluator.",
            "ACTION_NOT_SUPPORTED" => "The requested controlled action is not supported by the Operator Console access evaluator.",
            _ => "The Operator Console access evaluator denied the request."
        };

    private static object DbValue(Guid? value) => value.HasValue ? value.Value : DBNull.Value;

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
