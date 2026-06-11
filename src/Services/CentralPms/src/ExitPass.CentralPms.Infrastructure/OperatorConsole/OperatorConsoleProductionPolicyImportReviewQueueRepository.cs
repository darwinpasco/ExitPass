using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExitPass.CentralPms.Application.OperatorConsole;
using Npgsql;
using NpgsqlTypes;

namespace ExitPass.CentralPms.Infrastructure.OperatorConsole;

/// <summary>
/// PostgreSQL-backed Operator Console production policy import review queue.
///
/// Invariants enforced:
/// - Persists only review queue, decision, history, and finding rows.
/// - Does not insert, update, activate, or enqueue production policy import execution.
/// </summary>
public sealed class OperatorConsoleProductionPolicyImportReviewQueueRepository
    : IOperatorConsoleProductionPolicyImportReviewQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _connectionString;

    public OperatorConsoleProductionPolicyImportReviewQueueRepository(string connectionString)
    {
        _connectionString = !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException("Connection string is required.", nameof(connectionString));
    }

    public async Task<ProductionPolicyImportReviewSubmission?> FindActiveByFingerprintAsync(
        Guid makerOperatorId,
        string submissionFingerprint,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT review_id
            FROM operator_console.production_policy_import_review_submissions
            WHERE maker_operator_id = @maker_operator_id
              AND submission_fingerprint = @submission_fingerprint
              AND review_status NOT IN ('REJECTED', 'CANCELLED', 'SUPERSEDED')
            ORDER BY created_at DESC, review_id DESC
            LIMIT 1;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("maker_operator_id", NpgsqlDbType.Uuid).Value = makerOperatorId;
        command.Parameters.Add("submission_fingerprint", NpgsqlDbType.Varchar).Value = submissionFingerprint;

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid reviewId
            ? await GetAsync(reviewId, cancellationToken)
            : null;
    }

    public async Task<ProductionPolicyImportReviewSubmission?> GetAsync(
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var submission = await ReadSubmissionAsync(connection, reviewId, cancellationToken);
        if (submission is null)
        {
            return null;
        }

        var decisions = await ReadDecisionsAsync(connection, reviewId, cancellationToken);
        var history = await ReadHistoryAsync(connection, reviewId, cancellationToken);
        var findings = await ReadFindingsAsync(connection, reviewId, cancellationToken);

        return submission with
        {
            ReviewerDecisions = decisions,
            History = history,
            Findings = findings
        };
    }

    public async Task<(IReadOnlyList<ProductionPolicyImportReviewSubmission> Items, int TotalCount)> ListAsync(
        ProductionPolicyImportReviewQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        const string filter = """
            FROM operator_console.production_policy_import_review_submissions s
            WHERE (@review_status IS NULL OR s.review_status = @review_status)
              AND (@maker_operator_id IS NULL OR s.maker_operator_id = @maker_operator_id)
              AND (@created_from IS NULL OR s.created_at >= @created_from)
              AND (@created_to IS NULL OR s.created_at <= @created_to)
              AND (
                    (@reviewer_operator_id IS NULL AND @reviewer_role IS NULL)
                    OR EXISTS (
                        SELECT 1
                        FROM operator_console.production_policy_import_review_decisions d
                        WHERE d.review_id = s.review_id
                          AND (@reviewer_operator_id IS NULL OR d.reviewer_operator_id = @reviewer_operator_id)
                          AND (@reviewer_role IS NULL OR d.reviewer_role = @reviewer_role)
                    )
                  )
            """;

        var countSql = $"SELECT count(*) {filter};";
        var listSql = $"""
            SELECT s.review_id
            {filter}
            ORDER BY s.updated_at DESC, s.created_at DESC, s.review_id DESC
            LIMIT @limit OFFSET @offset;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var countCommand = new NpgsqlCommand(countSql, connection);
        AddListParameters(countCommand, query, includePaging: false);
        var totalCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        await using var listCommand = new NpgsqlCommand(listSql, connection);
        AddListParameters(listCommand, query, includePaging: true);
        await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
        var reviewIds = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            reviewIds.Add(reader.GetGuid(0));
        }

        var items = new List<ProductionPolicyImportReviewSubmission>(reviewIds.Count);
        foreach (var reviewId in reviewIds)
        {
            var item = await GetAsync(reviewId, cancellationToken);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return (items, totalCount);
    }

    public async Task SaveAsync(
        ProductionPolicyImportReviewSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(submission);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await UpsertSubmissionAsync(connection, transaction, submission, cancellationToken);

            foreach (var decision in submission.ReviewerDecisions)
            {
                await InsertDecisionAsync(connection, transaction, submission.ReviewId, decision, cancellationToken);
            }

            foreach (var history in submission.History)
            {
                await InsertHistoryAsync(connection, transaction, submission.ReviewId, history, cancellationToken);
            }

            foreach (var finding in submission.Findings)
            {
                await InsertFindingAsync(connection, transaction, submission.ReviewId, finding, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<ProductionPolicyImportReviewSubmission?> ReadSubmissionAsync(
        NpgsqlConnection connection,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                review_id,
                maker_operator_id,
                file_name,
                submission_fingerprint,
                review_status,
                dry_run_result_json,
                created_at,
                updated_at,
                correlation_id
            FROM operator_console.production_policy_import_review_submissions
            WHERE review_id = @review_id
            LIMIT 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ProductionPolicyImportReviewSubmission(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            Enum.Parse<ProductionPolicyImportReviewSubmissionStatus>(reader.GetString(4), ignoreCase: false),
            JsonSerializer.Deserialize<ProductionPolicyImportDryRunResult>(reader.GetString(5), JsonOptions)
                ?? throw new InvalidOperationException("Review submission dry-run JSON could not be deserialized."),
            Array.Empty<ProductionPolicyImportReviewDecision>(),
            Array.Empty<ProductionPolicyImportReviewHistoryEntry>(),
            Array.Empty<ProductionPolicyImportReviewFinding>(),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.GetFieldValue<DateTimeOffset>(7),
            reader.GetGuid(8));
    }

    private static async Task<IReadOnlyList<ProductionPolicyImportReviewDecision>> ReadDecisionsAsync(
        NpgsqlConnection connection,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                reviewer_role,
                decision_action,
                reviewer_operator_id,
                reason,
                decided_at,
                correlation_id
            FROM operator_console.production_policy_import_review_decisions
            WHERE review_id = @review_id
            ORDER BY decided_at, review_decision_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var decisions = new List<ProductionPolicyImportReviewDecision>();
        while (await reader.ReadAsync(cancellationToken))
        {
            decisions.Add(new ProductionPolicyImportReviewDecision(
                Enum.Parse<ProductionPolicyImportReviewerRole>(reader.GetString(0), ignoreCase: false),
                Enum.Parse<ProductionPolicyImportReviewDecisionAction>(reader.GetString(1), ignoreCase: false),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetGuid(5)));
        }

        return decisions;
    }

    private static async Task<IReadOnlyList<ProductionPolicyImportReviewHistoryEntry>> ReadHistoryAsync(
        NpgsqlConnection connection,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                decision_action,
                review_status,
                actor_operator_id,
                reviewer_role,
                reason,
                occurred_at,
                correlation_id
            FROM operator_console.production_policy_import_review_history
            WHERE review_id = @review_id
            ORDER BY occurred_at, review_history_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<ProductionPolicyImportReviewHistoryEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new ProductionPolicyImportReviewHistoryEntry(
                Enum.Parse<ProductionPolicyImportReviewDecisionAction>(reader.GetString(0), ignoreCase: false),
                Enum.Parse<ProductionPolicyImportReviewSubmissionStatus>(reader.GetString(1), ignoreCase: false),
                reader.GetGuid(2),
                reader.IsDBNull(3) ? null : Enum.Parse<ProductionPolicyImportReviewerRole>(reader.GetString(3), ignoreCase: false),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetGuid(6)));
        }

        return history;
    }

    private static async Task<IReadOnlyList<ProductionPolicyImportReviewFinding>> ReadFindingsAsync(
        NpgsqlConnection connection,
        Guid reviewId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT severity, message, field_name
            FROM operator_console.production_policy_import_review_findings
            WHERE review_id = @review_id
            ORDER BY created_at, review_finding_id;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var findings = new List<ProductionPolicyImportReviewFinding>();
        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(new ProductionPolicyImportReviewFinding(
                Enum.Parse<ProductionPolicyImportFindingSeverity>(reader.GetString(0), ignoreCase: false),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return findings;
    }

    private static async Task UpsertSubmissionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ProductionPolicyImportReviewSubmission submission,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.production_policy_import_review_submissions (
                review_id,
                maker_operator_id,
                file_name,
                submission_fingerprint,
                review_status,
                dry_run_result_json,
                created_at,
                updated_at,
                correlation_id
            )
            VALUES (
                @review_id,
                @maker_operator_id,
                @file_name,
                @submission_fingerprint,
                @review_status,
                @dry_run_result_json::jsonb,
                @created_at,
                @updated_at,
                @correlation_id
            )
            ON CONFLICT (review_id)
            DO UPDATE SET
                review_status = EXCLUDED.review_status,
                dry_run_result_json = EXCLUDED.dry_run_result_json,
                updated_at = EXCLUDED.updated_at,
                correlation_id = EXCLUDED.correlation_id,
                row_version = operator_console.production_policy_import_review_submissions.row_version + 1;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = submission.ReviewId;
        command.Parameters.Add("maker_operator_id", NpgsqlDbType.Uuid).Value = submission.MakerOperatorId;
        command.Parameters.Add("file_name", NpgsqlDbType.Varchar).Value = DbValue(submission.FileName);
        command.Parameters.Add("submission_fingerprint", NpgsqlDbType.Varchar).Value = submission.SubmissionFingerprint;
        command.Parameters.Add("review_status", NpgsqlDbType.Varchar).Value = submission.Status.ToString();
        command.Parameters.Add("dry_run_result_json", NpgsqlDbType.Jsonb).Value = JsonSerializer.Serialize(submission.DryRunResult, JsonOptions);
        command.Parameters.Add("created_at", NpgsqlDbType.TimestampTz).Value = submission.CreatedAt;
        command.Parameters.Add("updated_at", NpgsqlDbType.TimestampTz).Value = submission.UpdatedAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = submission.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDecisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        ProductionPolicyImportReviewDecision decision,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.production_policy_import_review_decisions (
                review_id,
                reviewer_role,
                decision_action,
                reviewer_operator_id,
                reason,
                decided_at,
                correlation_id
            )
            VALUES (
                @review_id,
                @reviewer_role,
                @decision_action,
                @reviewer_operator_id,
                @reason,
                @decided_at,
                @correlation_id
            )
            ON CONFLICT ON CONSTRAINT uq_policy_import_review_decisions__review_role
            DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;
        command.Parameters.Add("reviewer_role", NpgsqlDbType.Varchar).Value = decision.ReviewerRole.ToString();
        command.Parameters.Add("decision_action", NpgsqlDbType.Varchar).Value = decision.Action.ToString();
        command.Parameters.Add("reviewer_operator_id", NpgsqlDbType.Uuid).Value = decision.ReviewerOperatorId;
        command.Parameters.Add("reason", NpgsqlDbType.Text).Value = DbValue(decision.Reason);
        command.Parameters.Add("decided_at", NpgsqlDbType.TimestampTz).Value = decision.DecidedAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = decision.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHistoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        ProductionPolicyImportReviewHistoryEntry history,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.production_policy_import_review_history (
                review_id,
                history_fingerprint,
                decision_action,
                review_status,
                actor_operator_id,
                reviewer_role,
                reason,
                occurred_at,
                correlation_id
            )
            VALUES (
                @review_id,
                @history_fingerprint,
                @decision_action,
                @review_status,
                @actor_operator_id,
                @reviewer_role,
                @reason,
                @occurred_at,
                @correlation_id
            )
            ON CONFLICT (review_id, history_fingerprint)
            DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;
        command.Parameters.Add("history_fingerprint", NpgsqlDbType.Varchar).Value = Fingerprint(
            reviewId,
            history.Action,
            history.Status,
            history.ActorOperatorId,
            history.ReviewerRole,
            history.Reason,
            history.OccurredAt,
            history.CorrelationId);
        command.Parameters.Add("decision_action", NpgsqlDbType.Varchar).Value = history.Action.ToString();
        command.Parameters.Add("review_status", NpgsqlDbType.Varchar).Value = history.Status.ToString();
        command.Parameters.Add("actor_operator_id", NpgsqlDbType.Uuid).Value = history.ActorOperatorId;
        command.Parameters.Add("reviewer_role", NpgsqlDbType.Varchar).Value = history.ReviewerRole?.ToString() is { } role ? role : DBNull.Value;
        command.Parameters.Add("reason", NpgsqlDbType.Text).Value = DbValue(history.Reason);
        command.Parameters.Add("occurred_at", NpgsqlDbType.TimestampTz).Value = history.OccurredAt;
        command.Parameters.Add("correlation_id", NpgsqlDbType.Uuid).Value = history.CorrelationId;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertFindingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reviewId,
        ProductionPolicyImportReviewFinding finding,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO operator_console.production_policy_import_review_findings (
                review_id,
                finding_fingerprint,
                severity,
                message,
                field_name
            )
            VALUES (
                @review_id,
                @finding_fingerprint,
                @severity,
                @message,
                @field_name
            )
            ON CONFLICT (review_id, finding_fingerprint)
            DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.Add("review_id", NpgsqlDbType.Uuid).Value = reviewId;
        command.Parameters.Add("finding_fingerprint", NpgsqlDbType.Varchar).Value = Fingerprint(
            reviewId,
            finding.Severity,
            finding.Message,
            finding.Field);
        command.Parameters.Add("severity", NpgsqlDbType.Varchar).Value = finding.Severity.ToString();
        command.Parameters.Add("message", NpgsqlDbType.Text).Value = finding.Message;
        command.Parameters.Add("field_name", NpgsqlDbType.Varchar).Value = DbValue(finding.Field);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Fingerprint(params object?[] values)
    {
        var payload = string.Join("\u001f", values.Select(value => value?.ToString() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static object DbValue(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static void AddListParameters(
        NpgsqlCommand command,
        ProductionPolicyImportReviewQuery query,
        bool includePaging)
    {
        command.Parameters.Add("review_status", NpgsqlDbType.Varchar).Value =
            query.Status?.ToString() is { } status ? status : DBNull.Value;
        command.Parameters.Add("maker_operator_id", NpgsqlDbType.Uuid).Value =
            query.MakerOperatorId.HasValue ? query.MakerOperatorId.Value : DBNull.Value;
        command.Parameters.Add("reviewer_operator_id", NpgsqlDbType.Uuid).Value =
            query.ReviewerOperatorId.HasValue ? query.ReviewerOperatorId.Value : DBNull.Value;
        command.Parameters.Add("reviewer_role", NpgsqlDbType.Varchar).Value =
            query.ReviewerRole?.ToString() is { } role ? role : DBNull.Value;
        command.Parameters.Add("created_from", NpgsqlDbType.TimestampTz).Value =
            query.CreatedFrom.HasValue ? query.CreatedFrom.Value : DBNull.Value;
        command.Parameters.Add("created_to", NpgsqlDbType.TimestampTz).Value =
            query.CreatedTo.HasValue ? query.CreatedTo.Value : DBNull.Value;

        if (includePaging)
        {
            command.Parameters.Add("limit", NpgsqlDbType.Integer).Value = query.Limit;
            command.Parameters.Add("offset", NpgsqlDbType.Integer).Value = query.Offset;
        }
    }
}
