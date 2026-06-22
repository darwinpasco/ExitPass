using ExitPass.PaymentOrchestrator.Application.Abstractions.Persistence;
using ExitPass.PaymentOrchestrator.Application.Abstractions.Providers;
using ExitPass.PaymentOrchestrator.Application.UseCases.QueryProviderSessionStatus;
using ExitPass.PaymentOrchestrator.Contracts.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ExitPass.PaymentOrchestrator.UnitTests.Application.UseCases.QueryProviderSessionStatus;

/// <summary>
/// Unit tests for <see cref="QueryProviderSessionStatusHandler" />.
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
/// - Payment Orchestrator may query provider evidence but does not own platform finality.
/// - Status-query evidence does not create PaymentConfirmation or ExitAuthorization.
/// - Only known persisted provider sessions may be queried.
/// </summary>
public sealed class QueryProviderSessionStatusHandlerTests
{
    private static readonly Guid CorrelationId = Guid.Parse("70638d38-2f84-4c07-b708-38deebddbb34");

    [Fact]
    public async Task HandleAsync_WhenProviderSessionExists_QueriesPayMongoAdapter()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = Substitute.For<IProviderStatusQueryAdapter>();
        var expected = CreateStatusResult(
            CanonicalPaymentOutcomeStatus.Succeeded,
            isTerminal: true,
            isSuccess: true,
            retryable: false,
            reportable: true);

        adapter.ProviderCode.Returns("PAYMONGO");
        adapter.ProviderProduct.Returns("PAYMONGO_CHECKOUT_SESSION");
        adapter
            .QueryProviderSessionStatusAsync(Arg.Any<ProviderStatusQueryCommand>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord());

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.Same(expected, result);
        Assert.True(result.IsTerminal);
        Assert.True(result.IsSuccess);
        Assert.True(result.ReportableToCentralPms);

        await adapter.Received(1).QueryProviderSessionStatusAsync(
            Arg.Is<ProviderStatusQueryCommand>(command =>
                command.ProviderSessionId == "cs_status_001" &&
                command.ProviderReference == "pay_status_001" &&
                command.ExpectedAmountMinor == 10000 &&
                command.ExpectedCurrencyCode == "PHP" &&
                command.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTerminalSuccess_DoesNotDeclarePlatformFinalityOrIssueAuthorization()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.Succeeded,
            isTerminal: true,
            isSuccess: true,
            retryable: false,
            reportable: true));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord());

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.True(result.ReportableToCentralPms);
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorCode);
        Assert.DoesNotContain(
            typeof(QueryProviderSessionStatusHandler).GetConstructors().Single().GetParameters(),
            parameter => parameter.ParameterType.Name.Contains("CentralPms", StringComparison.OrdinalIgnoreCase) ||
                parameter.ParameterType.Name.Contains("PaymentConfirmation", StringComparison.OrdinalIgnoreCase) ||
                parameter.ParameterType.Name.Contains("ExitAuthorization", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(CanonicalPaymentOutcomeStatus.PendingProvider, false, false, true, false)]
    [InlineData(CanonicalPaymentOutcomeStatus.PendingProvider, false, false, false, false)]
    public async Task HandleAsync_WhenProviderEvidenceIsNonFinal_ReturnsNonReportableResult(
        CanonicalPaymentOutcomeStatus status,
        bool isTerminal,
        bool isSuccess,
        bool retryable,
        bool reportable)
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(status, isTerminal, isSuccess, retryable, reportable));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord());

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal(status, result.NormalizedStatus);
        Assert.Equal(isTerminal, result.IsTerminal);
        Assert.Equal(isSuccess, result.IsSuccess);
        Assert.Equal(retryable, result.Retryable);
        Assert.False(result.ReportableToCentralPms);
    }

    [Theory]
    [InlineData("PAYMONGO_STATUS_QUERY_TIMEOUT", true)]
    [InlineData("PAYMONGO_STATUS_QUERY_MALFORMED_RESPONSE", false)]
    [InlineData("PAYMONGO_STATUS_QUERY_UNKNOWN_STATUS", false)]
    [InlineData("PAYMONGO_STATUS_QUERY_AMOUNT_MISMATCH", false)]
    [InlineData("PAYMONGO_STATUS_QUERY_CURRENCY_MISMATCH", false)]
    [InlineData("PAYMONGO_STATUS_QUERY_PROVIDER_REFERENCE_MISMATCH", false)]
    public async Task HandleAsync_WhenAdapterReturnsRejectedEvidence_DoesNotMakeItReportable(
        string errorCode,
        bool retryable)
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateFailureResult(errorCode, retryable));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord());

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Equal(retryable, result.Retryable);
        Assert.False(result.IsTerminal);
        Assert.False(result.IsSuccess);
        Assert.False(result.ReportableToCentralPms);
    }

    [Fact]
    public async Task HandleAsync_WhenProviderSessionIsMissing_ReturnsDeterministicFailure()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.Succeeded,
            isTerminal: true,
            isSuccess: true,
            retryable: false,
            reportable: true));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns((ProviderSessionRecord?)null);

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal("PROVIDER_SESSION_NOT_FOUND", result.ErrorCode);
        Assert.False(result.ReportableToCentralPms);
        Assert.False(result.IsSuccess);

        await adapter.DidNotReceive().QueryProviderSessionStatusAsync(
            Arg.Any<ProviderStatusQueryCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenProviderIsUnsupported_ReturnsDeterministicFailure()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.Succeeded,
            isTerminal: true,
            isSuccess: true,
            retryable: false,
            reportable: true));

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(
            new QueryProviderSessionStatusCommand(
                "UNSUPPORTED",
                "UNSUPPORTED_PRODUCT",
                "provider_session_001",
                CorrelationId),
            CancellationToken.None);

        Assert.Equal("PROVIDER_STATUS_QUERY_UNSUPPORTED_PROVIDER", result.ErrorCode);
        Assert.False(result.ReportableToCentralPms);

        await repository.DidNotReceive().FindByProviderSessionIdAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPersistedProviderProductDiffers_ReturnsDeterministicFailure()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.Succeeded,
            isTerminal: true,
            isSuccess: true,
            retryable: false,
            reportable: true));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord(providerProduct: "OTHER_PRODUCT"));

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);

        Assert.Equal("PROVIDER_SESSION_PRODUCT_MISMATCH", result.ErrorCode);
        Assert.False(result.ReportableToCentralPms);

        await adapter.DidNotReceive().QueryProviderSessionStatusAsync(
            Arg.Any<ProviderStatusQueryCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_PreservesCommandCorrelationIdOverPersistedCorrelationId()
    {
        var commandCorrelationId = Guid.Parse("aaaaaaaa-aaaa-4aaa-aaaa-aaaaaaaaaaaa");
        var persistedCorrelationId = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.PendingProvider,
            isTerminal: false,
            isSuccess: false,
            retryable: true,
            reportable: false,
            correlationId: commandCorrelationId));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord(correlationId: persistedCorrelationId));

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(
            DefaultCommand() with { CorrelationId = commandCorrelationId },
            CancellationToken.None);

        Assert.Equal(commandCorrelationId, result.CorrelationId);
        await adapter.Received(1).QueryProviderSessionStatusAsync(
            Arg.Is<ProviderStatusQueryCommand>(command => command.CorrelationId == commandCorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_UsesPersistedCorrelationIdWhenCommandCorrelationIdIsMissing()
    {
        var persistedCorrelationId = Guid.Parse("bbbbbbbb-bbbb-4bbb-bbbb-bbbbbbbbbbbb");
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateStatusResult(
            CanonicalPaymentOutcomeStatus.PendingProvider,
            isTerminal: false,
            isSuccess: false,
            retryable: true,
            reportable: false,
            correlationId: persistedCorrelationId));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord(correlationId: persistedCorrelationId));

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(
            DefaultCommand() with { CorrelationId = null },
            CancellationToken.None);

        Assert.Equal(persistedCorrelationId, result.CorrelationId);
        await adapter.Received(1).QueryProviderSessionStatusAsync(
            Arg.Is<ProviderStatusQueryCommand>(command => command.CorrelationId == persistedCorrelationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_DoesNotAddSecretLikeDiagnostics()
    {
        var repository = Substitute.For<IProviderSessionRepository>();
        var adapter = CreateAdapterReturning(CreateFailureResult(
            "PAYMONGO_STATUS_QUERY_PROVIDER_REJECTED",
            retryable: false));

        repository
            .FindByProviderSessionIdAsync("PAYMONGO", "cs_status_001", Arg.Any<CancellationToken>())
            .Returns(CreateProviderSessionRecord());

        var handler = CreateHandler(repository, adapter);

        var result = await handler.HandleAsync(DefaultCommand(), CancellationToken.None);
        var diagnostics = string.Join(" ", result.Diagnostics.Values);

        Assert.DoesNotContain("sk_test", diagnostics);
        Assert.DoesNotContain("whsec", diagnostics);
        Assert.DoesNotContain("secret", diagnostics, StringComparison.OrdinalIgnoreCase);
    }

    private static QueryProviderSessionStatusHandler CreateHandler(
        IProviderSessionRepository repository,
        IProviderStatusQueryAdapter adapter)
    {
        return new QueryProviderSessionStatusHandler(
            NullLogger<QueryProviderSessionStatusHandler>.Instance,
            repository,
            new[] { adapter });
    }

    private static IProviderStatusQueryAdapter CreateAdapterReturning(ProviderStatusQueryResult result)
    {
        var adapter = Substitute.For<IProviderStatusQueryAdapter>();
        adapter.ProviderCode.Returns("PAYMONGO");
        adapter.ProviderProduct.Returns("PAYMONGO_CHECKOUT_SESSION");
        adapter
            .QueryProviderSessionStatusAsync(Arg.Any<ProviderStatusQueryCommand>(), Arg.Any<CancellationToken>())
            .Returns(result);

        return adapter;
    }

    private static QueryProviderSessionStatusCommand DefaultCommand()
    {
        return new QueryProviderSessionStatusCommand(
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_status_001",
            CorrelationId);
    }

    private static ProviderSessionRecord CreateProviderSessionRecord(
        string providerProduct = "PAYMONGO_CHECKOUT_SESSION",
        Guid? correlationId = null)
    {
        return new ProviderSessionRecord(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("be88ff8e-90a7-45a7-bb7d-3505cfce9076"),
            "PAYMONGO",
            providerProduct,
            "cs_status_001",
            "pay_status_001",
            "PENDING_PROVIDER",
            "https://checkout.paymongo.test/session",
            null,
            DateTimeOffset.Parse("2026-04-06T10:30:00Z"),
            "test-idempotency-key",
            correlationId ?? CorrelationId,
            "{}",
            "{}",
            DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            10000,
            "PHP");
    }

    private static ProviderStatusQueryResult CreateStatusResult(
        CanonicalPaymentOutcomeStatus status,
        bool isTerminal,
        bool isSuccess,
        bool retryable,
        bool reportable,
        Guid? correlationId = null)
    {
        return new ProviderStatusQueryResult(
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_status_001",
            "pay_status_001",
            status == CanonicalPaymentOutcomeStatus.Succeeded ? "paid" : "pending",
            status,
            isTerminal,
            isSuccess,
            retryable,
            reportable,
            10000,
            "PHP",
            DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            correlationId ?? CorrelationId,
            null,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout_session_id"] = "cs_status_001"
            });
    }

    private static ProviderStatusQueryResult CreateFailureResult(string errorCode, bool retryable)
    {
        return new ProviderStatusQueryResult(
            "PAYMONGO",
            "PAYMONGO_CHECKOUT_SESSION",
            "cs_status_001",
            "pay_status_001",
            null,
            CanonicalPaymentOutcomeStatus.PendingProvider,
            IsTerminal: false,
            IsSuccess: false,
            Retryable: retryable,
            ReportableToCentralPms: false,
            AmountMinor: null,
            CurrencyCode: null,
            ProviderObservedAtUtc: null,
            CorrelationId,
            ErrorCode: errorCode,
            ErrorMessage: "Safe status query diagnostic.",
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["checkout_session_id"] = "cs_status_001"
            });
    }
}
