namespace ExitPass.CentralPms.Application.OperatorConsole;

/// <summary>
/// Read-only service for fiscal void action audit review.
/// </summary>
public sealed class OperatorConsoleFiscalVoidActionAuditReportService : IOperatorConsoleFiscalVoidActionAuditReportService
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 200;

    private static readonly HashSet<string> ResultClasses = new(StringComparer.Ordinal)
    {
        "SUCCEEDED",
        "DENIED",
        "NOT_FOUND",
        "CONFLICT",
        "REJECTED",
        "ALREADY_VOIDED",
        "FAILED_SAFELY"
    };

    private readonly IOperatorConsoleFiscalVoidActionAuditReportRepository _repository;

    /// <summary>
    /// Creates a fiscal void action audit review service.
    /// </summary>
    public OperatorConsoleFiscalVoidActionAuditReportService(
        IOperatorConsoleFiscalVoidActionAuditReportRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    /// <inheritdoc />
    public Task<OperatorConsoleFiscalVoidActionAuditReportResult> ListAsync(
        OperatorConsoleFiscalVoidActionAuditReportQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateGuid(query.CorrelationId, nameof(query.CorrelationId));

        if (query.From.HasValue && query.To.HasValue && query.From > query.To)
        {
            throw new ArgumentException("from must be before to.", nameof(query));
        }

        var resultClass = NormalizeResultClass(query.ResultClass);
        if (resultClass is not null && !ResultClasses.Contains(resultClass))
        {
            throw new ArgumentException("resultClass is not supported.", nameof(query));
        }

        var limit = query.Limit <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);
        var offset = Math.Max(0, query.Offset);

        return _repository.ListAsync(
            query with
            {
                FiscalDocumentNumber = NormalizeOptional(query.FiscalDocumentNumber),
                ResultClass = resultClass,
                Limit = limit,
                Offset = offset
            },
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeResultClass(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static void ValidateGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
