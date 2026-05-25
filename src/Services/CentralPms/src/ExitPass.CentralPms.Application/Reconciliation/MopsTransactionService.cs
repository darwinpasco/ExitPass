namespace ExitPass.CentralPms.Application.Reconciliation;

/// <summary>
/// Coordinates MoPS continuity transaction imports.
///
/// BRD v1.2 Reference:
/// - Section 9.16 Monitoring and Administration
/// - Section 9.21 Audit and Traceability
///
/// SDD v1.2 Reference:
/// - Section 10 API Architecture
/// - Section 14.3 Distributed Tracing
/// - Section 14.4 Structured Logging
///
/// ExitPass v1.2 Invariants Enforced:
/// - MoPS records are continuity evidence and reconciliation inputs only.
/// - Importing MoPS evidence must not create PaymentAttempt, PaymentConfirmation, ExitAuthorization, or provider outcome truth.
/// </summary>
public sealed class MopsTransactionService : IMopsTransactionService
{
    private readonly IMopsTransactionRepository _repository;

    /// <summary>
    /// Creates a MoPS transaction service.
    /// </summary>
    public MopsTransactionService(IMopsTransactionRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<MopsImportResult> ImportAsync(
        ImportMopsTransactionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ValidateGuid(command.SiteId, nameof(command.SiteId));
        ValidateGuid(command.CorrelationId, nameof(command.CorrelationId));
        ValidateRequired(command.SourceSystemCode, nameof(command.SourceSystemCode));
        ValidateRequired(command.ContinuityReasonCode, nameof(command.ContinuityReasonCode));

        if (command.Amount is < 0)
        {
            throw new ArgumentException("Amount must be non-negative when supplied.", nameof(command.Amount));
        }

        if (string.IsNullOrWhiteSpace(command.SourceTransactionRef) &&
            (string.IsNullOrWhiteSpace(command.SourceBatchRef) ||
             string.IsNullOrWhiteSpace(command.CollectionReference)))
        {
            throw new ArgumentException(
                "Either SourceTransactionRef or both SourceBatchRef and CollectionReference are required.",
                nameof(command.SourceTransactionRef));
        }

        if (!string.IsNullOrWhiteSpace(command.CurrencyCode) &&
            command.CurrencyCode.Trim().Length != 3)
        {
            throw new ArgumentException("CurrencyCode must be a 3-character ISO code.", nameof(command.CurrencyCode));
        }

        if (!string.IsNullOrWhiteSpace(command.EvidenceHash) &&
            command.EvidenceHash.Trim().Length != 64)
        {
            throw new ArgumentException("EvidenceHash must be 64 characters when supplied.", nameof(command.EvidenceHash));
        }

        return _repository.ImportAsync(
            command with
            {
                SourceSystemCode = command.SourceSystemCode.Trim().ToUpperInvariant(),
                SourceTransactionRef = Normalize(command.SourceTransactionRef),
                SourceBatchRef = Normalize(command.SourceBatchRef),
                CollectionReference = Normalize(command.CollectionReference),
                CurrencyCode = Normalize(command.CurrencyCode)?.ToUpperInvariant(),
                ContinuityReasonCode = command.ContinuityReasonCode.Trim().ToUpperInvariant(),
                EvidenceHash = Normalize(command.EvidenceHash)
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MopsTransactionRecord>> ListAsync(
        ListMopsTransactionsQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 100);
        return _repository.ListAsync(query with { Limit = limit }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MopsTransactionRecord> ReadAsync(
        ReadMopsTransactionQuery query,
        CancellationToken cancellationToken)
    {
        ValidateGuid(query.MopsTransactionRecordId, nameof(query.MopsTransactionRecordId));
        return _repository.ReadAsync(query, cancellationToken);
    }

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static void ValidateRequired(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
