namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read service for Operator Console statutory discount validation drafts.
///
/// ExitPass v1.2 Invariants Enforced:
/// - Reads stored validation, policy, tariff, and application metadata only.
/// - Does not resolve policy as authoritative, apply payable basis, mutate payments, call providers, open gates,
///   create coupons, create reconciliation records, or upload evidence.
/// </summary>
public sealed class OperatorConsoleStatutoryDiscountReadService : IOperatorConsoleStatutoryDiscountReadService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private const int DefaultReportLimit = 25;
    private const int MaxReportLimit = 200;

    private readonly IOperatorConsoleStatutoryDiscountReadRepository _repository;

    /// <summary>
    /// Creates an Operator Console statutory discount read service.
    /// </summary>
    public OperatorConsoleStatutoryDiscountReadService(IOperatorConsoleStatutoryDiscountReadRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<OperatorConsoleStatutoryDiscountDraftQueueResult> ListDraftsAsync(
        OperatorConsoleStatutoryDiscountDraftQueueQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateCorrelation(query.CorrelationId);

        var page = query.Page <= 0 ? DefaultPage : query.Page;
        var pageSize = query.PageSize <= 0 ? DefaultPageSize : Math.Min(query.PageSize, MaxPageSize);

        if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom > query.CreatedTo)
        {
            throw new ArgumentException("createdFrom must be before createdTo.", nameof(query));
        }

        return _repository.ListDraftsAsync(
            query with
            {
                Status = NormalizeOptional(query.Status),
                EntitlementType = NormalizeOptional(query.EntitlementType),
                Page = page,
                PageSize = pageSize
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperatorConsoleStatutoryDiscountDraftDetailResult?> GetDraftAsync(
        OperatorConsoleStatutoryDiscountDraftDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateGuid(query.DraftId, nameof(query.DraftId));
        ValidateCorrelation(query.CorrelationId);
        return _repository.GetDraftAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperatorConsoleStatutoryDiscountAuditReportResult> ListAuditReportAsync(
        OperatorConsoleStatutoryDiscountAuditReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateCorrelation(query.CorrelationId);

        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
        {
            throw new ArgumentException("from must be before to.", nameof(query));
        }

        var limit = query.Limit <= 0 ? DefaultReportLimit : Math.Min(query.Limit, MaxReportLimit);
        var offset = Math.Max(0, query.Offset);

        return _repository.ListAuditReportAsync(
            query with
            {
                ValidationStatus = NormalizeOptional(query.ValidationStatus),
                EvidenceStatus = NormalizeOptional(query.EvidenceStatus),
                AccessDecision = NormalizeOptional(query.AccessDecision),
                Limit = limit,
                Offset = offset
            },
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void ValidateCorrelation(Guid correlationId) =>
        ValidateGuid(correlationId, nameof(correlationId));

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
