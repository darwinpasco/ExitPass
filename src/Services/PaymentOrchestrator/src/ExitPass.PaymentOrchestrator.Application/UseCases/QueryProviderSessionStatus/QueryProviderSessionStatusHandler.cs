using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using Microsoft.Extensions.Logging;

namespace ExitPass.PaymentOrchestrator.Application.UseCases.QueryProviderSessionStatus;

/// <summary>
/// Controlled application use case for querying provider status for a known provider session.
///
/// BRD:
/// - 9.10 Payment Processing and Confirmation
/// - 9.13 Timeout, Retry, and Duplicate Handling
/// - 12 Payment Orchestration
///
/// SDD:
/// - 10.5.3 Report Verified Payment Outcome
/// - 10.7 Idempotency and Concurrency Rules
///
/// Invariants Enforced:
/// - Payment Orchestrator may query provider evidence but must not own platform finality.
/// - Status-query evidence does not create PaymentConfirmation or ExitAuthorization.
/// - Only a known persisted provider session may be queried.
/// </summary>
public sealed class QueryProviderSessionStatusHandler
{
    private static readonly ActivitySource ActivitySource =
        new("ExitPass.PaymentOrchestrator.Application");

    private readonly ILogger<QueryProviderSessionStatusHandler> _logger;
    private readonly IProviderSessionRepository _providerSessionRepository;
    private readonly IReadOnlyDictionary<string, IProviderStatusQueryAdapter> _statusQueryAdapters;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueryProviderSessionStatusHandler"/> class.
    /// </summary>
    /// <param name="logger">The structured logger.</param>
    /// <param name="providerSessionRepository">The provider session repository.</param>
    /// <param name="statusQueryAdapters">The registered provider status-query adapters.</param>
    public QueryProviderSessionStatusHandler(
        ILogger<QueryProviderSessionStatusHandler> logger,
        IProviderSessionRepository providerSessionRepository,
        IEnumerable<IProviderStatusQueryAdapter> statusQueryAdapters)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerSessionRepository = providerSessionRepository ?? throw new ArgumentNullException(nameof(providerSessionRepository));
        ArgumentNullException.ThrowIfNull(statusQueryAdapters);

        _statusQueryAdapters = statusQueryAdapters.ToDictionary(
            static adapter => BuildAdapterKey(adapter.ProviderCode, adapter.ProviderProduct),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Queries provider status and returns provider-neutral evidence.
    /// </summary>
    /// <param name="command">The controlled status-query command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Provider-neutral status-query evidence. This is not platform payment finality.</returns>
    public async Task<ProviderStatusQueryResult> HandleAsync(
        QueryProviderSessionStatusCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        using var activity = ActivitySource.StartActivity("QueryProviderSessionStatus");
        activity?.SetTag("provider.code", command.ProviderCode);
        activity?.SetTag("provider.product", command.ProviderProduct);
        activity?.SetTag("provider_session.id", command.ProviderSessionId);
        activity?.SetTag("correlation_id", command.CorrelationId?.ToString());

        if (string.IsNullOrWhiteSpace(command.ProviderCode) ||
            string.IsNullOrWhiteSpace(command.ProviderProduct) ||
            string.IsNullOrWhiteSpace(command.ProviderSessionId))
        {
            return CreateFailureResult(
                command.ProviderCode,
                command.ProviderProduct,
                command.ProviderSessionId,
                command.CorrelationId,
                retryable: false,
                errorCode: "PROVIDER_STATUS_QUERY_INVALID_SCOPE",
                errorMessage: "Provider code, product, and session id are required.");
        }

        var providerCode = command.ProviderCode.Trim();
        var providerProduct = command.ProviderProduct.Trim();
        var providerSessionId = command.ProviderSessionId.Trim();
        var adapterKey = BuildAdapterKey(providerCode, providerProduct);

        if (!_statusQueryAdapters.TryGetValue(adapterKey, out var adapter))
        {
            _logger.LogWarning(
                "Provider status query rejected because no status-query adapter is registered. ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, ProviderSessionId {ProviderSessionId}",
                providerCode,
                providerProduct,
                providerSessionId);

            return CreateFailureResult(
                providerCode,
                providerProduct,
                providerSessionId,
                command.CorrelationId,
                retryable: false,
                errorCode: "PROVIDER_STATUS_QUERY_UNSUPPORTED_PROVIDER",
                errorMessage: "Provider status query is not supported for the requested provider/product.");
        }

        var providerSession = await _providerSessionRepository.FindByProviderSessionIdAsync(
            providerCode,
            providerSessionId,
            cancellationToken);

        if (providerSession is null)
        {
            _logger.LogWarning(
                "Provider status query rejected because the provider session is unknown. ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, ProviderSessionId {ProviderSessionId}",
                providerCode,
                providerProduct,
                providerSessionId);

            return CreateFailureResult(
                providerCode,
                providerProduct,
                providerSessionId,
                command.CorrelationId,
                retryable: false,
                errorCode: "PROVIDER_SESSION_NOT_FOUND",
                errorMessage: "Provider session was not found.");
        }

        if (!string.Equals(providerSession.ProviderProduct, providerProduct, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Provider status query rejected because provider product does not match persisted session. ProviderCode {ProviderCode}, RequestedProduct {RequestedProduct}, PersistedProduct {PersistedProduct}, ProviderSessionId {ProviderSessionId}",
                providerCode,
                providerProduct,
                providerSession.ProviderProduct,
                providerSessionId);

            return CreateFailureResult(
                providerCode,
                providerProduct,
                providerSessionId,
                command.CorrelationId,
                retryable: false,
                errorCode: "PROVIDER_SESSION_PRODUCT_MISMATCH",
                errorMessage: "Provider product did not match the persisted provider session.");
        }

        var correlationId = command.CorrelationId ?? providerSession.CorrelationId;
        var providerCommand = new ProviderStatusQueryCommand(
            providerSession.ProviderSessionId,
            providerSession.ProviderReference,
            providerSession.AmountMinorUnits,
            providerSession.CurrencyCode,
            correlationId);

        _logger.LogInformation(
            "Querying provider session status. ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, ProviderSessionId {ProviderSessionId}",
            providerCode,
            providerProduct,
            providerSessionId);

        var result = await adapter.QueryProviderSessionStatusAsync(providerCommand, cancellationToken);

        activity?.SetTag("provider_status_query.terminal", result.IsTerminal);
        activity?.SetTag("provider_status_query.success", result.IsSuccess);
        activity?.SetTag("provider_status_query.retryable", result.Retryable);
        activity?.SetTag("provider_status_query.reportable_to_central_pms", result.ReportableToCentralPms);
        activity?.SetTag("provider_status_query.error_code", result.ErrorCode);

        _logger.LogInformation(
            "Provider session status query completed. ProviderCode {ProviderCode}, ProviderProduct {ProviderProduct}, ProviderSessionId {ProviderSessionId}, Terminal {IsTerminal}, Success {IsSuccess}, Retryable {Retryable}, ReportableToCentralPms {ReportableToCentralPms}, ErrorCode {ErrorCode}",
            result.ProviderCode,
            result.ProviderProduct,
            result.ProviderSessionId,
            result.IsTerminal,
            result.IsSuccess,
            result.Retryable,
            result.ReportableToCentralPms,
            result.ErrorCode);

        return result;
    }

    private static ProviderStatusQueryResult CreateFailureResult(
        string? providerCode,
        string? providerProduct,
        string? providerSessionId,
        Guid? correlationId,
        bool retryable,
        string errorCode,
        string errorMessage)
    {
        return new ProviderStatusQueryResult(
            providerCode?.Trim() ?? string.Empty,
            providerProduct?.Trim() ?? string.Empty,
            providerSessionId?.Trim() ?? string.Empty,
            ProviderReference: null,
            SourceStatus: null,
            CanonicalPaymentOutcomeStatus.PendingProvider,
            IsTerminal: false,
            IsSuccess: false,
            Retryable: retryable,
            ReportableToCentralPms: false,
            AmountMinor: null,
            CurrencyCode: null,
            ProviderObservedAtUtc: null,
            CorrelationId: correlationId,
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildAdapterKey(string providerCode, string providerProduct)
    {
        return $"{providerCode.Trim()}::{providerProduct.Trim()}";
    }
}
